using System;
using Pho.Domain.Multiplayer;
using Unity.Netcode;
using UnityEngine;

namespace Pho.Net.Session
{
    /// <summary>
    /// The one object the rest of the game talks to about multiplayer.
    /// Host, join, leave, who is here, and what went wrong.
    ///
    /// <b>Nothing outside this folder should ever touch
    /// <see cref="NetworkManager"/> directly.</b> That is the entire reason
    /// this class exists. Netcode's NetworkManager is a transport-coupled,
    /// event-heavy object with a lifecycle that does not match a game menu's;
    /// if UI code, the pause menu, and four gameplay systems each reach into
    /// it, then swapping UnityTransport for Steam -- or adding the
    /// four-player cap, or explaining a refusal to a player -- becomes a
    /// change in six places instead of one. Gameplay asks
    /// <see cref="NetworkSession"/>; NetworkSession asks
    /// <see cref="ISessionTransport"/>; the wire underneath is nobody else's
    /// business. (<c>CarryAuthority</c> is the deliberate exception: it is a
    /// NetworkBehaviour, so it is *part of* the netcode layer rather than a
    /// consumer of it.)
    ///
    /// <b>Host-authoritative, consistent with CarryAuthority.</b> The host
    /// decides who is in the session, exactly as it decides who is holding
    /// which bowl. A client never assumes it got in; it waits to be told.
    ///
    /// <b>All the rules are pure.</b> The four-player cap lives in
    /// <see cref="SessionSlots"/> and the connection lifecycle in
    /// <see cref="SessionStatus"/>, both in <c>Pho.Domain</c> and both
    /// covered by the sub-second test suite. What is left here is message
    /// plumbing, which is the correct amount of logic for a class that
    /// cannot be unit tested.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkSession : MonoBehaviour
    {
        /// <summary>The one well-known instance. Same narrow-exception reasoning as <c>GameBootstrap.Current</c>: one access point for one root object, not a general service locator.</summary>
        public static NetworkSession Instance { get; private set; }

        [Header("Wiring")]
        [Tooltip("The scene's NetworkManager. Leave empty to use NetworkManager.Singleton.")]
        [SerializeField] NetworkManager networkManager;

        [Header("Session")]
        [Tooltip("Maximum players including the host. Four, per the co-op design.")]
        [SerializeField] int maxPlayers = SessionSlots.DefaultMaxPlayers;

        [Header("Transport")]
        [Tooltip("Unity = localhost/LAN, works with zero setup. Steam = what ships to players, needs the Facepunch package and a running Steam client.")]
        [SerializeField] SessionTransportKind preferredTransport = SessionTransportKind.Unity;

        [Tooltip("If Steam is preferred but unavailable, host/join over LAN instead of failing. Keep this on: a developer without Steam running should still be able to play.")]
        [SerializeField] bool fallBackToLanIfSteamUnavailable = true;

        [Header("UnityTransport (LAN)")]
        [SerializeField] ushort port = UnityTransportSession.DefaultPort;
        [Tooltip("0.0.0.0 accepts LAN connections as well as this machine. 127.0.0.1 restricts hosting to this machine only.")]
        [SerializeField] string listenAddress = "0.0.0.0";
        [Tooltip("Where Join() goes when given no target. Localhost, so two-instance testing needs no typing.")]
        [SerializeField] string defaultJoinAddress = "127.0.0.1";

        [Header("Steam")]
        [Tooltip("480 is Spacewar, Valve's placeholder App ID for testing before a real one is issued.")]
        [SerializeField] uint steamAppId = SteamTransportSession.SpacewarAppId;

        [Header("Testing")]
        [Tooltip("Honour '-pho host' / '-pho join <target>' on the command line. Inert unless those arguments are present; this is how two local instances are started before there is any multiplayer UI.")]
        [SerializeField] bool autoStartFromCommandLine = true;

        readonly SessionStatus _status = new SessionStatus();

        SessionSlots _slots;
        ISessionTransport _transport;
        NetworkManager _boundManager;
        bool _subscribed;
        bool _ownsApprovalCallback;

        // ---- What the rest of the game reads -------------------------------

        public SessionState State => _status.State;

        /// <summary>Why the last attempt failed, in a sentence fit to show a player. Empty unless <see cref="State"/> is <see cref="SessionState.Failed"/>.</summary>
        public string FailureReason => _status.FailureReason;

        /// <summary>Fired after every state change, with (previous, current). Menus subscribe to this instead of polling.</summary>
        public event Action<SessionState, SessionState> StateChanged;

        /// <summary>A player entered the session. Fires on the host for everyone, and on a client for itself.</summary>
        public event Action<ulong> PlayerJoined;

        /// <summary>A player left the session.</summary>
        public event Action<ulong> PlayerLeft;

        /// <summary>Host-side: somebody was turned away, with the reason they were given. Wire this to a toast so the host knows a friend bounced off a full lobby.</summary>
        public event Action<ulong, string> JoinRejected;

        public bool IsInSession => _status.IsInSession;
        public bool IsBusy => _status.IsBusy;
        public bool IsHost => _boundManager != null && _boundManager.IsHost;
        public bool IsClient => _boundManager != null && _boundManager.IsClient && !_boundManager.IsHost;

        public int MaxPlayers => maxPlayers < 1 ? 1 : maxPlayers;

        /// <summary>How many players are in the session right now, host included.</summary>
        public int PlayerCount
        {
            get
            {
                if (_slots != null && _boundManager != null && _boundManager.IsServer) return _slots.Count;
                var ids = _boundManager != null ? _boundManager.ConnectedClientsIds : null;
                return ids?.Count ?? 0;
            }
        }

        /// <summary>Human-readable name of the wire currently in use, for the debug HUD.</summary>
        public string TransportName => _transport?.DisplayName ?? "none";

        /// <summary>What the host gives friends so they can join: an "ip:port" on LAN, a Steam ID on Steam.</summary>
        public string JoinTarget => _transport == null || _boundManager == null
            ? string.Empty
            : _transport.DescribeJoinTarget(_boundManager);

        // ---- Lifecycle ------------------------------------------------------

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[NetworkSession] A second NetworkSession was created; destroying it. Exactly one belongs in the boot scene.");
                Destroy(this);
                return;
            }

            Instance = this;
            _status.Changed += OnStatusChanged;
        }

        void Start()
        {
            if (autoStartFromCommandLine) TryStartFromCommandLine();
        }

        void OnDestroy()
        {
            if (!ReferenceEquals(Instance, this)) return;

            _status.Changed -= OnStatusChanged;
            Unsubscribe();
            _status.ResetToOffline();
            Instance = null;
        }

        void OnStatusChanged(SessionState from, SessionState to)
        {
            StateChanged?.Invoke(from, to);
        }

        // ---- The API ---------------------------------------------------------

        /// <summary>Starts hosting. Convenience overload for buttons; the reason for a failure is on <see cref="FailureReason"/>.</summary>
        public bool StartHost() => StartHost(out _);

        /// <summary>
        /// Starts hosting a session. The host occupies the first of
        /// <see cref="MaxPlayers"/> slots.
        ///
        /// Returns false without changing anything if a session is already
        /// live -- "Host" pressed twice must not tear down the game the
        /// player is already running.
        /// </summary>
        public bool StartHost(out string error)
        {
            if (!TryBegin(SessionState.Starting, out var nm, out error)) return false;

            _transport = SelectTransport();
            if (!_transport.TryPrepareHost(nm, out error))
            {
                _status.Fail(error);
                return false;
            }

            ConfigureNetworkConfig(nm);

            // Built before StartHost, because the host approves itself
            // synchronously inside it and needs a slot to land in.
            _slots = new SessionSlots(MaxPlayers);
            Subscribe(nm);

            if (!nm.StartHost())
            {
                error = $"Could not start hosting over {_transport.DisplayName}. The port may already be in use.";
                _status.Fail(error);
                Unsubscribe();
                return false;
            }

            return true;
        }

        /// <summary>Joins the default target (localhost on LAN). Convenience for the two-instance test.</summary>
        public bool Join() => Join(string.Empty, out _);

        public bool Join(string target) => Join(target, out _);

        /// <summary>
        /// Joins somebody else's session. <paramref name="target"/> is an
        /// "ip" or "ip:port" on LAN, or a Steam ID on Steam; empty means the
        /// configured default, which is localhost.
        ///
        /// Success here means "the attempt started", not "you are in" --
        /// watch <see cref="StateChanged"/> for
        /// <see cref="SessionState.Connected"/>, or
        /// <see cref="SessionState.Failed"/> with a reason. The host decides,
        /// and it has not decided yet.
        /// </summary>
        public bool Join(string target, out string error)
        {
            if (!TryBegin(SessionState.Connecting, out var nm, out error)) return false;

            _transport = SelectTransport();
            if (!_transport.TryPrepareJoin(nm, target, out error))
            {
                _status.Fail(error);
                return false;
            }

            ConfigureNetworkConfig(nm);
            Subscribe(nm);

            if (!nm.StartClient())
            {
                error = $"Could not reach {(string.IsNullOrWhiteSpace(target) ? _transport.DescribeJoinTarget(nm) : target)}.";
                _status.Fail(error);
                Unsubscribe();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Leaves the session, hosting or joined. Safe to call when nothing
        /// is running; safe to call twice.
        ///
        /// A host leaving ends the session for everyone -- there is no host
        /// migration, deliberately: re-electing a host mid-service would have
        /// to reconstruct who was holding which bowl, which order belonged to
        /// which table and how far the broth had simmered, and getting that
        /// subtly wrong is worse for a co-op session than simply ending it.
        /// </summary>
        public void Leave()
        {
            if (_status.State == SessionState.Failed)
            {
                // Netcode normally tears itself down before reporting a
                // failure, but if anything is still listening, leaving must
                // still close it -- otherwise a socket outlives the session
                // that owned it and the next Host fails on a busy port.
                if (_boundManager != null && (_boundManager.IsListening || _boundManager.IsClient))
                {
                    _boundManager.Shutdown();
                }

                _status.Acknowledge();
                return;
            }

            if (!_status.IsLive) return;

            // Stop admitting anyone the moment we start tearing down, so a
            // connection landing mid-shutdown is refused with an explanation
            // rather than half-admitted to a session that is going away.
            if (_slots != null) _slots.AcceptingJoins = false;

            _status.TryTransition(SessionState.Stopping, out _);

            if (_boundManager != null && (_boundManager.IsListening || _boundManager.IsClient))
            {
                _boundManager.Shutdown();
            }
            else
            {
                FinalizeOffline();
            }
        }

        /// <summary>Dismisses a failure and returns to <see cref="SessionState.Offline"/>. Call this when the player closes the error dialog.</summary>
        public void AcknowledgeFailure() => _status.Acknowledge();

        // ---- Internals --------------------------------------------------------

        bool TryBegin(SessionState next, out NetworkManager nm, out string error)
        {
            nm = null;

            if (_status.IsLive)
            {
                error = "Already in a session. Leave the current one first.";
                return false;
            }

            if (!TryResolveNetworkManager(out nm, out error)) return false;

            // A retry after a failure goes straight from Failed; the state
            // machine allows it so the menu's "Try again" is one action.
            return _status.TryTransition(next, out error);
        }

        bool TryResolveNetworkManager(out NetworkManager nm, out string error)
        {
            nm = networkManager != null ? networkManager : NetworkManager.Singleton;
            if (nm == null)
            {
                error = "No NetworkManager in the scene. Multiplayer cannot start. See docs/multiplayer-setup.md.";
                Debug.LogError($"[NetworkSession] {error}");
                return false;
            }

            error = string.Empty;
            return true;
        }

        ISessionTransport SelectTransport()
        {
            if (preferredTransport == SessionTransportKind.Steam)
            {
                var steam = new SteamTransportSession(steamAppId);
                if (steam.IsAvailable(out var why)) return steam;

                Debug.LogWarning($"[NetworkSession] {why}");

                // Returning the Steam transport anyway makes the failure loud
                // and specific rather than silently changing what the player
                // asked for -- the right behaviour for a shipped Steam build.
                if (!fallBackToLanIfSteamUnavailable) return steam;
            }

            return new UnityTransportSession(port, listenAddress, defaultJoinAddress);
        }

        /// <summary>
        /// Turns on connection approval, which is what makes the player cap
        /// enforceable at all: without it Netcode admits everyone and the
        /// only way to cap a session is to kick the fifth player after they
        /// have already loaded in. Set identically on host and client,
        /// because the two must agree on the config.
        /// </summary>
        void ConfigureNetworkConfig(NetworkManager nm)
        {
            nm.NetworkConfig.ConnectionApproval = true;
        }

        void Subscribe(NetworkManager nm)
        {
            if (_subscribed && ReferenceEquals(_boundManager, nm)) return;
            Unsubscribe();

            _boundManager = nm;

            nm.OnServerStarted += HandleServerStarted;
            nm.OnServerStopped += HandleStopped;
            nm.OnClientStopped += HandleStopped;
            nm.OnClientConnectedCallback += HandleClientConnected;
            nm.OnClientDisconnectCallback += HandleClientDisconnected;
            nm.OnTransportFailure += HandleTransportFailure;

            // Netcode throws if a second approval callback is registered, so
            // we only claim the slot if nobody else has it -- and we remember
            // that we claimed it, so we only clear what we own.
            if (nm.ConnectionApprovalCallback == null)
            {
                nm.ConnectionApprovalCallback = HandleConnectionApproval;
                _ownsApprovalCallback = true;
            }
            else
            {
                Debug.LogWarning("[NetworkSession] Something else already owns ConnectionApprovalCallback; the four-player cap will NOT be enforced. Remove the other handler.");
            }

            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed || _boundManager == null)
            {
                _subscribed = false;
                return;
            }

            _boundManager.OnServerStarted -= HandleServerStarted;
            _boundManager.OnServerStopped -= HandleStopped;
            _boundManager.OnClientStopped -= HandleStopped;
            _boundManager.OnClientConnectedCallback -= HandleClientConnected;
            _boundManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            _boundManager.OnTransportFailure -= HandleTransportFailure;

            if (_ownsApprovalCallback)
            {
                _boundManager.ConnectionApprovalCallback = null;
                _ownsApprovalCallback = false;
            }

            _subscribed = false;
        }

        /// <summary>
        /// The gate. Every connection, including the host's own, passes
        /// through here, and every refusal carries a sentence explaining
        /// itself -- Netcode delivers <c>Reason</c> to the rejected client,
        /// where it lands in <c>NetworkManager.DisconnectReason</c>. Dropping
        /// a fifth player silently would be indistinguishable from a crash or
        /// a firewall.
        /// </summary>
        void HandleConnectionApproval(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            _slots ??= new SessionSlots(MaxPlayers);

            if (_slots.TryReserve(request.ClientNetworkId, out var verdict))
            {
                response.Approved = true;
                // Only ask Netcode to spawn a player object if the integrator
                // actually wired a player prefab; requesting one that does not
                // exist fails the connection for an unrelated-looking reason.
                response.CreatePlayerObject = _boundManager != null && _boundManager.NetworkConfig.PlayerPrefab != null;
                response.Reason = string.Empty;
                return;
            }

            var refusal = _slots.Describe(verdict);
            response.Approved = false;
            response.CreatePlayerObject = false;
            response.Reason = refusal;

            Debug.Log($"[NetworkSession] Refused client {request.ClientNetworkId}: {refusal}");
            JoinRejected?.Invoke(request.ClientNetworkId, refusal);
        }

        void HandleServerStarted()
        {
            _status.TryTransition(SessionState.Hosting, out _);
            Debug.Log($"[NetworkSession] Hosting over {TransportName}. Friends join with: {JoinTarget}");
        }

        void HandleClientConnected(ulong clientId)
        {
            if (_boundManager != null && !_boundManager.IsServer && clientId == _boundManager.LocalClientId)
            {
                _status.TryTransition(SessionState.Connected, out _);
                Debug.Log($"[NetworkSession] Connected to the host as client {clientId}.");
            }
            else if (_boundManager != null && _boundManager.IsServer)
            {
                // Logged because a co-op session with no UI is otherwise
                // completely silent about whether anyone actually arrived --
                // "did it connect?" was unanswerable from the player log,
                // which made the first two-instance test undiagnosable.
                Debug.Log($"[NetworkSession] Client {clientId} joined ({_boundManager.ConnectedClientsIds.Count}/{_slots?.MaxPlayers ?? SessionSlots.DefaultMaxPlayers} players).");
            }

            PlayerJoined?.Invoke(clientId);
        }

        void HandleClientDisconnected(ulong clientId)
        {
            if (_boundManager != null && _boundManager.IsServer)
            {
                // Host side: free their slot so somebody else can take it.
                // The matching cleanup for anything they were carrying lives
                // in CarryAuthority, which watches the same callback.
                _slots?.Release(clientId);
                PlayerLeft?.Invoke(clientId);
                return;
            }

            // Client side: this is us. Either we were refused, we were
            // dropped, or the host quit -- all of which the player deserves
            // to be told about, unless we are the one who pressed Leave.
            PlayerLeft?.Invoke(clientId);

            if (_status.State == SessionState.Stopping) return;

            var reason = _boundManager != null && !string.IsNullOrEmpty(_boundManager.DisconnectReason)
                ? _boundManager.DisconnectReason
                : "Disconnected from the host. They may have closed the game.";

            Debug.LogWarning($"[NetworkSession] Disconnected: {reason}");
            _status.Fail(reason);
        }

        void HandleTransportFailure()
        {
            Debug.LogError($"[NetworkSession] Transport failure on {TransportName}.");
            _status.Fail($"The network connection failed ({TransportName}).");
        }

        void HandleStopped(bool wasHost) => FinalizeOffline();

        /// <summary>
        /// Settles back to Offline once Netcode has actually stopped.
        /// Idempotent: a host receives both OnServerStopped and
        /// OnClientStopped, and both land here.
        ///
        /// A <see cref="SessionState.Failed"/> state is left alone -- the
        /// reason on screen is the whole point, and Netcode shutting down is
        /// a consequence of the failure, not news that supersedes it.
        /// </summary>
        void FinalizeOffline()
        {
            _slots?.Clear();

            if (_transport != null && _boundManager != null) _transport.Cleanup(_boundManager);
            Unsubscribe();

            if (_status.State == SessionState.Failed) return;
            if (_status.State == SessionState.Offline) return;

            if (_status.State != SessionState.Stopping) _status.TryTransition(SessionState.Stopping, out _);
            _status.TryTransition(SessionState.Offline, out _);
        }

        /// <summary>
        /// Reads "-pho host" / "-pho join [target]" from the command line.
        ///
        /// This is how two instances are started on one machine before any
        /// multiplayer UI exists, and it stays useful afterwards for
        /// automated tests. Inert when the arguments are absent, so a normal
        /// launch is unaffected.
        /// </summary>
        void TryStartFromCommandLine()
        {
            string[] args;
            try
            {
                args = Environment.GetCommandLineArgs();
            }
            catch
            {
                return;
            }

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], "-pho", StringComparison.OrdinalIgnoreCase)) continue;

                var mode = args[i + 1];
                if (string.Equals(mode, "host", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log("[NetworkSession] '-pho host' on the command line; starting a host.");
                    if (!StartHost(out var hostError)) Debug.LogError($"[NetworkSession] {hostError}");
                    return;
                }

                if (string.Equals(mode, "join", StringComparison.OrdinalIgnoreCase))
                {
                    var target = i + 2 < args.Length && !args[i + 2].StartsWith("-") ? args[i + 2] : string.Empty;
                    Debug.Log($"[NetworkSession] '-pho join' on the command line; joining '{(string.IsNullOrEmpty(target) ? "default" : target)}'.");
                    if (!Join(target, out var joinError)) Debug.LogError($"[NetworkSession] {joinError}");
                    return;
                }
            }
        }
    }
}
