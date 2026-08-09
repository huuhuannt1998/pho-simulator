using Unity.Netcode;
using UnityEngine;
using Pho.Domain.Multiplayer;

namespace Pho.Net
{
    /// <summary>
    /// The network face of <see cref="CarryRegistry"/>: the one object that
    /// decides who is holding which bowl, for the whole session.
    ///
    /// <b>Host-authoritative, deliberately without client prediction.</b>
    /// A client never decides it picked something up -- it asks, and waits.
    /// Predicting locally would mean visibly tearing an item back out of a
    /// player's hands whenever the host disagreed, and in a game where two
    /// people reaching for the same bowl during a rush is a normal event
    /// rather than an edge case, that correction would fire constantly. The
    /// cost of honesty here is one round-trip (~30-80ms on a friends-list
    /// P2P session), which is imperceptible for picking up a bowl and far
    /// cheaper than the alternative.
    ///
    /// All the actual rules live in the pure <see cref="CarryRegistry"/>,
    /// which is covered by the fast test suite. This class only moves
    /// messages: it should stay thin enough that nothing here needs a test
    /// of its own beyond the integration path.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class CarryAuthority : NetworkBehaviour
    {
        /// <summary>The one well-known instance for this session. Server-side only; clients see null.</summary>
        public static CarryAuthority Server { get; private set; }

        // Lives only on the server. A client that somehow reached this would
        // be answering its own questions, which is the bug this whole class
        // exists to prevent.
        readonly CarryRegistry _registry = new CarryRegistry();

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                Server = this;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                if (NetworkManager.Singleton != null)
                    NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

                if (ReferenceEquals(Server, this)) Server = null;
            }
        }

        /// <summary>
        /// A player asks to pick something up. Runs on the server no matter
        /// who called it; <paramref name="rpcParams"/> carries the caller's
        /// client id, which is what makes the claim attributable -- a client
        /// cannot claim on someone else's behalf because it doesn't get to
        /// choose that value.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void RequestClaimRpc(ulong itemId, RpcParams rpcParams = default)
        {
            var actorId = rpcParams.Receive.SenderClientId;
            bool granted = _registry.TryClaim(itemId, actorId);

            // Answer the asker either way. A silent refusal would leave a
            // client waiting forever on a pickup that is never coming.
            AnswerClaimRpc(itemId, granted, RpcTarget.Single(actorId, RpcTargetUse.Temp));

            if (granted) BroadcastHolderRpc(itemId, actorId, held: true);
        }

        /// <summary>A player puts something down. Refused silently if they weren't holding it -- see CarryRegistry.TryRelease for why that case is normal rather than an error.</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void RequestReleaseRpc(ulong itemId, RpcParams rpcParams = default)
        {
            var actorId = rpcParams.Receive.SenderClientId;
            if (_registry.TryRelease(itemId, actorId))
            {
                BroadcastHolderRpc(itemId, actorId, held: false);
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void AnswerClaimRpc(ulong itemId, bool granted, RpcParams rpcParams)
        {
            ClaimAnswered?.Invoke(itemId, granted);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void BroadcastHolderRpc(ulong itemId, ulong actorId, bool held)
        {
            HolderChanged?.Invoke(itemId, actorId, held);
        }

        /// <summary>Raised on the requesting client only: their pickup was granted or refused.</summary>
        public event System.Action<ulong, bool> ClaimAnswered;

        /// <summary>Raised on every peer: an item changed hands, so the visuals should follow. Everyone needs this, not just the two players involved.</summary>
        public event System.Action<ulong, ulong, bool> HolderChanged;

        /// <summary>
        /// Frees everything a departing player was holding.
        ///
        /// Without this, a bowl carried by someone who crashes or quits
        /// stays claimed for the rest of the session -- nobody can pick it
        /// up and nothing explains why. In four-player co-op, someone
        /// leaving mid-rush is routine, so this is a normal path, not
        /// cleanup for a rare failure.
        /// </summary>
        void OnClientDisconnected(ulong clientId)
        {
            var dropped = _registry.ReleaseAll(clientId);
            for (int i = 0; i < dropped.Count; i++)
            {
                BroadcastHolderRpc(dropped[i], clientId, held: false);
            }
        }

        /// <summary>Server-side query, for gameplay code that needs to know before acting (e.g. a station refusing to accept a bowl nobody is holding).</summary>
        public bool IsHeldBy(ulong itemId, ulong actorId) => _registry.IsHeldBy(itemId, actorId);

        /// <summary>Server-side. Call when a carryable is destroyed rather than merely put down -- a bowl a customer finished, say.</summary>
        public void ForgetItem(ulong itemId) => _registry.Forget(itemId);
    }
}
