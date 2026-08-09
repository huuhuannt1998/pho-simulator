using System.Collections.Generic;
using Pho.Domain.Infra;

namespace Pho.Domain.Multiplayer
{
    /// <summary>
    /// Decides WHICH SEAT AT THE TABLE each connected player gets: a stable
    /// slot index 0..<see cref="MaxPlayers"/>-1 that everything else about a
    /// player's identity is derived from -- display name, colour/variant, and
    /// the spawn offset that stops four people materialising inside each
    /// other.
    ///
    /// <b>Why this is pure C# and not part of the NetworkBehaviour</b>
    /// (same reasoning as <see cref="CarryRegistry"/>): slot assignment is a
    /// bookkeeping problem, not a transport problem. The interesting cases --
    /// a fifth player being refused, player 2 leaving and player 5 reusing
    /// slot 1, a duplicate join request not consuming two slots -- are all
    /// answerable in the sub-second <c>dotnet test</c> loop. Verifying them
    /// by launching four game clients and making people leave in a particular
    /// order would be absurd.
    ///
    /// <b>Lowest-free-slot policy, deliberately.</b> A departing player's
    /// slot is reused by the next joiner rather than handing out an
    /// ever-increasing counter. In a session where someone drops and
    /// reconnects mid-rush -- routine in four-player co-op -- a monotonic
    /// counter would run past the palette after a handful of reconnects and
    /// leave the fourth player nameless and untinted.
    ///
    /// <b>Single-authority assumption.</b> Exactly one instance exists per
    /// session, on the host. Clients are told their slot; they never compute
    /// one, because two clients computing independently would both pick slot
    /// 0 and end up identically coloured and standing in the same spot.
    ///
    /// Actor ids are opaque <see cref="ulong"/>s so this stays engine-free.
    /// In practice they are Netcode client ids, but nothing here knows that.
    /// </summary>
    public sealed class PlayerSlotAllocator
    {
        /// <summary>Design ceiling for the co-op session (architecture: 4-player shared restaurant).</summary>
        public const int MaxPlayers = 4;

        readonly Dictionary<ulong, int> _slotByActor = new Dictionary<ulong, int>();
        readonly int _capacity;

        public PlayerSlotAllocator() : this(MaxPlayers) { }

        /// <summary>Capacity is injectable so tests can prove the "session full" path with two players instead of four.</summary>
        public PlayerSlotAllocator(int capacity)
        {
            _capacity = capacity < 1 ? 1 : capacity;
        }

        public int Capacity => _capacity;

        public int AssignedCount => _slotByActor.Count;

        /// <summary>
        /// Gives <paramref name="actorId"/> the lowest free slot.
        ///
        /// Idempotent: an actor that already has a slot gets the same one
        /// back and consumes nothing, so a retried or duplicated join over an
        /// unreliable channel cannot burn two of the four seats.
        ///
        /// Returns false only when the session is genuinely full -- that is a
        /// normal "sorry, we're four" outcome, not an error.
        /// </summary>
        public bool TryAssign(ulong actorId, out int slot)
        {
            if (_slotByActor.TryGetValue(actorId, out slot)) return true;

            for (int candidate = 0; candidate < _capacity; candidate++)
            {
                if (IsSlotTaken(candidate)) continue;

                _slotByActor[actorId] = candidate;
                slot = candidate;
                return true;
            }

            slot = -1;
            return false;
        }

        /// <summary>
        /// Frees an actor's slot on disconnect. Returns false if they had
        /// none -- a duplicated disconnect callback must not free a slot that
        /// has already been handed to somebody else.
        /// </summary>
        public bool Release(ulong actorId) => _slotByActor.Remove(actorId);

        public bool TryGetSlot(ulong actorId, out int slot) => _slotByActor.TryGetValue(actorId, out slot);

        public bool HasSlot(ulong actorId) => _slotByActor.ContainsKey(actorId);

        public void Clear() => _slotByActor.Clear();

        bool IsSlotTaken(int slot)
        {
            foreach (var pair in _slotByActor)
            {
                if (pair.Value == slot) return true;
            }

            return false;
        }

        /// <summary>
        /// Deterministic spawn displacement for a slot, laid out as a square
        /// around the spawn anchor: (-,-), (+,-), (-,+), (+,+) at half
        /// <paramref name="spacing"/> in each axis.
        ///
        /// <b>Why this exists at all:</b> every player prefab spawning on one
        /// exact point means four CharacterControllers occupying the same
        /// cubic metre. Unity resolves that by ejecting them in an
        /// unpredictable direction -- sometimes through a wall. A square is
        /// used rather than a ring because it stays sane at any player count
        /// from 1 to 4 without trigonometry, and because it is trivially
        /// reproducible in a test.
        ///
        /// Y is always 0: the anchor decides the floor height, the offset
        /// only spreads players across it. Slots at or beyond capacity fall
        /// back to no offset rather than throwing -- a mis-slotted player
        /// standing on the anchor is recoverable, an exception during spawn
        /// is not.
        /// </summary>
        public static Vec3 SpawnOffset(int slot, float spacing = 1.2f)
        {
            if (slot < 0 || slot >= MaxPlayers) return Vec3.Zero;

            float half = spacing * 0.5f;
            float x = (slot & 1) == 0 ? -half : half;
            float z = (slot & 2) == 0 ? -half : half;
            return new Vec3(x, 0f, z);
        }

        /// <summary>
        /// Fallback display name for a slot, used until (or unless) a player
        /// supplies their own. One-based because "Cook 1" reads like a person
        /// and "Cook 0" reads like a bug report.
        /// </summary>
        public static string DefaultDisplayName(int slot) =>
            slot < 0 ? "Cook" : "Cook " + (slot + 1);

        /// <summary>
        /// Sanitises a player-supplied name into something safe to show and
        /// to replicate. Trims, collapses an empty/whitespace name back to
        /// the slot default, and clamps length so one player cannot blow past
        /// the fixed-size network string or push everyone else's name off the
        /// HUD.
        /// </summary>
        public static string SanitizeDisplayName(string requested, int slot, int maxLength = 16)
        {
            if (maxLength < 1) maxLength = 1;

            var trimmed = requested == null ? string.Empty : requested.Trim();
            if (trimmed.Length == 0) return DefaultDisplayName(slot);

            return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength);
        }
    }
}
