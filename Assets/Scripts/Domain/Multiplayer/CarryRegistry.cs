using System.Collections.Generic;

namespace Pho.Domain.Multiplayer
{
    /// <summary>
    /// Decides WHO IS HOLDING WHAT. The authority for every carryable in the
    /// restaurant, and the answer to the hardest question in this genre's
    /// netcode: two players reaching for the same bowl on the same frame.
    ///
    /// <b>Why this is pure C# and not a NetworkBehaviour:</b> contention is
    /// a logic problem, not a transport problem. Keeping it here means the
    /// rule "first claim wins" is verified by the 100ms <c>dotnet test</c>
    /// loop instead of by launching two game clients and racing them by
    /// hand. The networking layer above becomes a thin adapter: a client
    /// asks, the host calls <see cref="TryClaim"/>, and the boolean it
    /// returns is the whole decision.
    ///
    /// <b>Single-authority assumption.</b> Exactly one instance of this
    /// exists per session, on the host. Clients never construct one and
    /// never guess the answer locally -- a client that predicted a
    /// successful pickup would have to visibly yank the bowl back out of
    /// the player's hands when the host disagreed, which is far worse than
    /// the few frames of delay that honesty costs.
    ///
    /// Ids are opaque <see cref="ulong"/>s so this stays engine-free. In
    /// practice the item id is a Netcode <c>NetworkObjectId</c> and the
    /// actor id is a client id, but nothing here depends on that.
    /// </summary>
    public sealed class CarryRegistry
    {
        readonly Dictionary<ulong, ulong> _holderByItem = new Dictionary<ulong, ulong>();

        /// <summary>How many items are currently held, across all actors.</summary>
        public int HeldCount => _holderByItem.Count;

        /// <summary>
        /// Attempts to give <paramref name="itemId"/> to
        /// <paramref name="actorId"/>.
        ///
        /// Returns true if the actor holds it afterwards. Re-claiming
        /// something you already hold succeeds (idempotent) so a duplicated
        /// or retried request over an unreliable channel can't fail
        /// spuriously. Claiming something someone else holds returns false
        /// -- that is the normal contention outcome, not an error.
        /// </summary>
        public bool TryClaim(ulong itemId, ulong actorId)
        {
            if (_holderByItem.TryGetValue(itemId, out var holder))
            {
                return holder == actorId;
            }

            _holderByItem[itemId] = actorId;
            return true;
        }

        /// <summary>
        /// Releases <paramref name="itemId"/>, but only if
        /// <paramref name="actorId"/> is actually holding it. Returns false
        /// when they aren't -- a late or duplicated release from a player
        /// who already put the bowl down must never knock it out of the
        /// hands of whoever picked it up next.
        /// </summary>
        public bool TryRelease(ulong itemId, ulong actorId)
        {
            if (!_holderByItem.TryGetValue(itemId, out var holder) || holder != actorId)
            {
                return false;
            }

            _holderByItem.Remove(itemId);
            return true;
        }

        public bool TryGetHolder(ulong itemId, out ulong actorId) => _holderByItem.TryGetValue(itemId, out actorId);

        public bool IsHeld(ulong itemId) => _holderByItem.ContainsKey(itemId);

        public bool IsHeldBy(ulong itemId, ulong actorId) =>
            _holderByItem.TryGetValue(itemId, out var holder) && holder == actorId;

        /// <summary>
        /// Drops everything an actor was holding.
        ///
        /// <b>This is the disconnect path and it is not optional.</b> A
        /// player who quits, crashes or times out while carrying a bowl
        /// would otherwise hold that bowl for the rest of the session --
        /// nobody could pick it up and nothing would explain why. In a
        /// four-player co-op game where one person rage-quitting mid-rush is
        /// entirely normal, a leaked claim is a permanently broken object.
        ///
        /// Returns the item ids that were released so the caller can put
        /// those objects back into the world.
        /// </summary>
        public IReadOnlyList<ulong> ReleaseAll(ulong actorId)
        {
            var released = new List<ulong>();
            foreach (var pair in _holderByItem)
            {
                if (pair.Value == actorId) released.Add(pair.Key);
            }

            for (int i = 0; i < released.Count; i++)
            {
                _holderByItem.Remove(released[i]);
            }

            return released;
        }

        /// <summary>Forgets an item entirely -- for when the object itself is destroyed (a bowl consumed by a customer), as opposed to merely put down.</summary>
        public void Forget(ulong itemId) => _holderByItem.Remove(itemId);

        public void Clear() => _holderByItem.Clear();
    }
}
