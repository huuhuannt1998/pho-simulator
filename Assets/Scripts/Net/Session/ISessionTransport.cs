using Unity.Netcode;
using UnityEngine;

namespace Pho.Net.Session
{
    /// <summary>Which wire a session runs over. See <see cref="ISessionTransport"/> for why this is a choice at all.</summary>
    public enum SessionTransportKind
    {
        /// <summary>
        /// UnityTransport (UDP over localhost/LAN). Ships inside Netcode for
        /// GameObjects, needs no accounts, no external service and no
        /// network connection. This is the default precisely because it has
        /// zero prerequisites: the game must always be testable by launching
        /// two builds on one machine.
        /// </summary>
        Unity,

        /// <summary>
        /// Steam P2P via the Facepunch transport and the Steam relay
        /// network. What ships to players -- no port forwarding, joinable
        /// from the friends list. Requires the Steam client running and the
        /// community transport package installed.
        /// </summary>
        Steam
    }

    /// <summary>
    /// The seam between "we are in a co-op session" and "we are talking over
    /// a specific wire".
    ///
    /// <b>Why this interface exists.</b> The transport is the single most
    /// likely thing in this game's networking to be replaced: UnityTransport
    /// for developing on one machine, Steam relay for the Steam release, and
    /// plausibly something else entirely if the game ever ships elsewhere.
    /// Every one of those swaps must be invisible to gameplay code. So
    /// gameplay talks to <see cref="NetworkSession"/>, NetworkSession talks
    /// to this, and nothing else in the project references a transport type
    /// by name.
    ///
    /// <b>Implementations must never throw for "not available".</b> A
    /// missing Steam client is an ordinary Tuesday, not an exceptional
    /// condition; it is reported through <see cref="IsAvailable"/> and the
    /// <c>out string error</c> parameters so the session can fall back or
    /// explain itself. An exception here would take the main menu down with
    /// it.
    /// </summary>
    public interface ISessionTransport
    {
        SessionTransportKind Kind { get; }

        /// <summary>Short name for logs and the debug HUD, e.g. "UnityTransport (LAN)".</summary>
        string DisplayName { get; }

        /// <summary>
        /// Whether this transport can be used right now, and if not, a
        /// sentence explaining why that a player could act on ("Steam is not
        /// running").
        /// </summary>
        bool IsAvailable(out string reason);

        /// <summary>
        /// Attaches and configures the transport on the NetworkManager for
        /// hosting, and assigns it to <c>NetworkConfig.NetworkTransport</c>.
        /// Must be called before <c>StartHost</c>.
        /// </summary>
        bool TryPrepareHost(NetworkManager networkManager, out string error);

        /// <summary>
        /// Same, for joining. <paramref name="target"/> is whatever this
        /// transport's <see cref="DescribeJoinTarget"/> produced on the host
        /// -- an "ip:port" for UnityTransport, a Steam ID for Steam. An
        /// empty target means "use the configured default", which is how
        /// localhost testing avoids making the tester type anything.
        /// </summary>
        bool TryPrepareJoin(NetworkManager networkManager, string target, out string error);

        /// <summary>
        /// What the host hands to friends so they can join. Only meaningful
        /// once hosting has started.
        /// </summary>
        string DescribeJoinTarget(NetworkManager networkManager);

        /// <summary>Called after shutdown. Releases anything the transport held open; must be safe to call when nothing was ever started.</summary>
        void Cleanup(NetworkManager networkManager);
    }

    /// <summary>Shared helpers for transports that live as components on the NetworkManager's GameObject.</summary>
    internal static class SessionTransportUtil
    {
        /// <summary>
        /// Finds or adds a transport component on the NetworkManager and
        /// makes it the active one.
        ///
        /// Reusing an existing component matters: a scene that already has a
        /// UnityTransport wired in the inspector must keep the values the
        /// integrator set there rather than silently getting a second
        /// component with defaults.
        /// </summary>
        internal static T GetOrAdd<T>(NetworkManager networkManager) where T : NetworkTransport
        {
            var existing = networkManager.GetComponent<T>();
            return existing != null ? existing : networkManager.gameObject.AddComponent<T>();
        }

        internal static void MakeActive(NetworkManager networkManager, NetworkTransport transport)
        {
            networkManager.NetworkConfig.NetworkTransport = transport;
        }

        /// <summary>Disables every transport component except the active one, so a stale transport cannot poll events for a session it isn't part of.</summary>
        internal static void DisableOtherTransports(NetworkManager networkManager, NetworkTransport keep)
        {
            foreach (var transport in networkManager.GetComponents<NetworkTransport>())
            {
                if (transport == null || transport == keep) continue;
                transport.enabled = false;
            }

            if (keep != null) keep.enabled = true;
        }
    }
}
