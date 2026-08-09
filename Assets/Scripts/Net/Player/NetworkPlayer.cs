using System.Collections.Generic;
using Pho.Domain.Multiplayer;
using Unity.Netcode;
using UnityEngine;

namespace Pho.Net.Player
{
    /// <summary>
    /// The network face of one player: who they are, where they are, and
    /// which half of the player rig is allowed to run on this machine.
    ///
    /// <b>Owner-authoritative movement, deliberately -- and deliberately
    /// unlike <see cref="CarryAuthority"/>.</b> The two are not inconsistent;
    /// they are answers to different questions.
    ///
    /// Carrying is <i>contended</i>: two players reach for the same bowl and
    /// exactly one may have it, so somebody has to arbitrate, and that
    /// somebody is the host. Walking is <i>uncontended</i>: nobody else is
    /// trying to be standing where you are standing, so there is nothing for
    /// a host to arbitrate. Routing movement through the server anyway would
    /// buy a single thing -- resistance to a modified client teleporting --
    /// and would charge for it in the currency the game can least afford: a
    /// full round-trip of input lag on every step, felt continuously, by
    /// everyone. This is a co-op cooking game played with friends on an
    /// invite; there is no ladder, no score to defend, and no stranger to
    /// cheat against. A player who edits their client to fly can already
    /// simply be asked to stop.
    ///
    /// So the owner moves itself with the existing single-player
    /// <c>FirstPersonMotor</c> at zero added latency, publishes the result,
    /// and every other machine smooths toward it. No prediction and no
    /// reconciliation exist anywhere in this class, which keeps it consistent
    /// with CarryAuthority's stance: nothing here ever guesses at an answer
    /// it will later have to visibly take back.
    ///
    /// <b>What is NOT replicated:</b> camera pitch. Remote players are
    /// rendered as bodies; the pitch of a head nobody can see is a per-frame
    /// float sent to three peers to move nothing. Body yaw <i>is</i>
    /// replicated because it is what a remote body visibly faces, and because
    /// the interaction ray will eventually be reasoned about from it. If a
    /// head bone or a held-item pose ever needs pitch, add it then, with a
    /// visible reason.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        // Resolved by name rather than by type reference: Pho.Net.asmdef does
        // not reference Pho.Player, and adding that edge is an architecture
        // decision (architecture.md section 10: "*.asmdef reference changes
        // go through the integration agent"), not something this file gets to
        // make unilaterally. Same documented pattern as
        // KitchenBindInstaller/GameBootstrap.BindPlayer, which reach
        // Pho.Player components from Pho.Core for exactly this reason.
        static readonly string[] LocalOnlyBehaviourTypeNames =
        {
            "Pho.Player.FirstPersonMotor",
            "Pho.Player.PlayerInteractor",
        };

        [Header("Identity")]
        [Tooltip("Slot-indexed colours/models. See PlayerVisualPalette -- the model choice is authored in that asset, never as a path in code.")]
        [SerializeField] PlayerVisualPalette palette;

        [Tooltip("Body model + renderers live under here. The palette's model prefab is instantiated as a child of this transform.")]
        [SerializeField] Transform modelRoot;

        [Header("Replication")]
        [Tooltip("Seconds between owner transform publishes. 20 Hz is plenty for walking speed once remotes interpolate.")]
        [SerializeField] float sendInterval = 0.05f;

        [Tooltip("Metres the owner must move before publishing again. Stops a stationary player spending bandwidth on float noise.")]
        [SerializeField] float positionSendThreshold = 0.01f;

        [Tooltip("Degrees of yaw change before publishing again.")]
        [SerializeField] float yawSendThreshold = 0.5f;

        [Tooltip("Higher = remotes catch up faster but reproduce jitter more faithfully. This is the smoothing that stops remote players looking like they are teleporting between packets.")]
        [SerializeField] float remoteSmoothing = 14f;

        [Tooltip("Beyond this distance a remote stops smoothing and snaps -- a long lerp across the restaurant after a hitch looks worse than a cut.")]
        [SerializeField] float remoteSnapDistance = 3f;

        /// <summary>
        /// Slot bookkeeping for the whole session. Server-side only, and
        /// static because slots are a property of the session rather than of
        /// any one player object.
        ///
        /// If a session-level roster/lobby object later wants to own this,
        /// it should -- this is the smallest thing that makes four players
        /// distinguishable today without reaching into another agent's files.
        /// </summary>
        static readonly PlayerSlotAllocator ServerSlots = new PlayerSlotAllocator();

        /// <summary>
        /// Name this machine's player wants to be called. A lobby/session UI
        /// sets this before connecting; the owner submits it to the server on
        /// spawn. Empty means "use the slot default".
        /// </summary>
        public static string LocalDisplayName { get; set; } = string.Empty;

        readonly NetworkVariable<int> _slot = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        readonly NetworkVariable<NetPlayerName> _displayName = new NetworkVariable<NetPlayerName>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Owner-write: this is where the "owner-authoritative" claim in the
        // class doc becomes a concrete permission rather than a comment.
        readonly NetworkVariable<Vector3> _netPosition = new NetworkVariable<Vector3>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<float> _netYaw = new NetworkVariable<float>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Bumped by the owner whenever it is repositioned discontinuously
        // (spawn placement). Remotes watch it so a legitimate teleport reads
        // as a cut rather than a two-second glide across the dining room.
        readonly NetworkVariable<int> _teleportCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        readonly List<Renderer> _tintTargets = new List<Renderer>();
        MaterialPropertyBlock _propertyBlock;
        GameObject _spawnedModel;

        CharacterController _controller;
        float _nextSendTime;
        int _lastSeenTeleportCount;
        bool _hasRemoteSample;
        bool _receivedAnySample;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Session slot 0..3, or -1 before the server has assigned one (or if the session was already full).</summary>
        public int Slot => _slot.Value;

        /// <summary>Replicated display name, safe to read on any peer once spawned.</summary>
        public string DisplayName => _displayName.Value.ToString();

        void Awake()
        {
            _controller = GetComponent<CharacterController>();

            // Switched OFF here rather than in OnNetworkSpawn on purpose.
            // OnNetworkSpawn runs after Awake/OnEnable, by which point
            // FirstPersonMotor.OnEnable has already enabled its input actions
            // and grabbed the cursor -- on all four player objects, on every
            // machine. Starting dark and letting only the owner switch itself
            // on means those side effects never fire on a remote body at all.
            //
            // Guarded on a NetworkManager existing so this component is inert
            // in a single-player scene that has no networking in it: no
            // NetworkManager means nobody will ever call OnNetworkSpawn to
            // turn the rig back on, and a permanently frozen player is a far
            // worse failure than a redundant component.
            if (NetworkManager.Singleton != null)
            {
                SetLocalRigEnabled(false);
            }
        }

        public override void OnNetworkSpawn()
        {
            _slot.OnValueChanged += OnSlotChanged;
            _teleportCount.OnValueChanged += OnTeleportCountChanged;
            _netPosition.OnValueChanged += OnNetPositionChanged;
            _lastSeenTeleportCount = _teleportCount.Value;

            if (IsServer) AssignSlotAndPlace();

            if (IsOwner)
            {
                // Only the owner drives input, and only the owner's camera and
                // AudioListener may be live. Four enabled cameras fight over
                // the display; four AudioListeners make Unity log a warning
                // every frame and the audio itself go strange.
                SetLocalRigEnabled(true);
                PublishTransform(force: true);
                SubmitDisplayNameRpc(NetPlayerName.From(LocalDisplayName));
            }

            ApplyIdentity(_slot.Value);
        }

        public override void OnNetworkDespawn()
        {
            _slot.OnValueChanged -= OnSlotChanged;
            _teleportCount.OnValueChanged -= OnTeleportCountChanged;
            _netPosition.OnValueChanged -= OnNetPositionChanged;

            // Free the seat so the next joiner -- very often the same person
            // reconnecting after a drop -- gets a slot instead of falling off
            // the end of the palette. Mirrors CarryAuthority releasing a
            // departing player's held items for the same reason.
            if (IsServer) ServerSlots.Release(OwnerClientId);
        }

        void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner) PublishTransform(force: false);
            else if (_receivedAnySample) SmoothTowardsReplicated();
            // Before the owner's first publish the replicated position is
            // still (0,0,0). Smoothing toward that would drag every remote
            // body to the world origin for a frame or two on join.
        }

        /// <summary>
        /// Turns this machine's input/camera/audio rig on or off. Public so a
        /// future pause menu or spectator mode can suppress the local player
        /// without knowing which components make up the rig.
        ///
        /// Components are found by type NAME, not type reference -- see the
        /// LocalOnlyBehaviourTypeNames comment for why the asmdef edge that
        /// would allow a typed reference is not this file's to add.
        /// </summary>
        public void SetLocalRigEnabled(bool enabledState)
        {
            foreach (var behaviour in GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            {
                if (behaviour == null || ReferenceEquals(behaviour, this)) continue;

                var typeName = behaviour.GetType().FullName;
                for (int i = 0; i < LocalOnlyBehaviourTypeNames.Length; i++)
                {
                    if (typeName == LocalOnlyBehaviourTypeNames[i])
                    {
                        behaviour.enabled = enabledState;
                        break;
                    }
                }
            }

            foreach (var cam in GetComponentsInChildren<Camera>(includeInactive: true))
            {
                cam.enabled = enabledState;
            }

            foreach (var listener in GetComponentsInChildren<AudioListener>(includeInactive: true))
            {
                listener.enabled = enabledState;
            }
        }

        // ---------------------------------------------------------------
        // Identity
        // ---------------------------------------------------------------

        void AssignSlotAndPlace()
        {
            if (!ServerSlots.TryAssign(OwnerClientId, out var slot))
            {
                // Refusing the connection outright belongs to the session
                // layer, not here. All this class can do is decline to invent
                // a fifth identity: the player joins looking generic rather
                // than as a duplicate of somebody else.
                Debug.LogWarning($"[NetworkPlayer] Session already has {PlayerSlotAllocator.MaxPlayers} players; client {OwnerClientId} gets no slot.");
                _slot.Value = -1;
                return;
            }

            _slot.Value = slot;
            _displayName.Value = NetPlayerName.From(PlayerSlotAllocator.DefaultDisplayName(slot));

            PlayerSpawnAnchor.ResolvePose(slot, transform.position, transform.eulerAngles.y,
                out var position, out var yaw);

            // The server decides WHERE, the owner performs the move: position
            // is an owner-written variable, so the server writing it directly
            // would be the one place in this class that contradicts its own
            // authority model.
            PlaceAtRpc(position, yaw, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void PlaceAtRpc(Vector3 position, float yaw, RpcParams rpcParams)
        {
            Teleport(position, yaw);
        }

        /// <summary>
        /// Moves the local (owned) player discontinuously and tells remotes to
        /// cut rather than glide. Public because spawn placement is not the
        /// only legitimate teleport a co-op restaurant will ever want.
        /// </summary>
        public void Teleport(Vector3 position, float yaw)
        {
            if (!IsOwner) return;

            // CharacterController caches its own position and will drag the
            // transform back on its next Move() unless it is off across the
            // assignment.
            bool hadController = _controller != null && _controller.enabled;
            if (hadController) _controller.enabled = false;

            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            if (hadController) _controller.enabled = true;

            _teleportCount.Value++;
            PublishTransform(force: true);
        }

        [Rpc(SendTo.Server)]
        void SubmitDisplayNameRpc(NetPlayerName requested, RpcParams rpcParams = default)
        {
            // Attributed by the sender id the client does not get to choose,
            // the same trick CarryAuthority uses: without this check any
            // client could invoke this RPC on somebody else's NetworkPlayer
            // and rename them. Cheap enough to be worth having even in a
            // trusted friends-only session.
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;

            // Sanitised server-side, never client-side: a client is the one
            // party that has an interest in sending a 400-character name.
            var clean = PlayerSlotAllocator.SanitizeDisplayName(requested.ToString(), _slot.Value);
            _displayName.Value = NetPlayerName.From(clean);
        }

        void OnSlotChanged(int previous, int current) => ApplyIdentity(current);

        void ApplyIdentity(int slot)
        {
            if (palette == null || !palette.TryGet(slot, out var entry))
            {
                // No palette wired: leave the prefab looking however it was
                // authored. Silent rather than warning-per-player, because a
                // scene mid-integration will hit this on all four.
                return;
            }

            var root = modelRoot != null ? modelRoot : transform;

            if (entry.modelPrefab != null)
            {
                if (_spawnedModel != null) Destroy(_spawnedModel);
                _spawnedModel = Instantiate(entry.modelPrefab, root);
                _spawnedModel.transform.localPosition = Vector3.zero;
                _spawnedModel.transform.localRotation = Quaternion.identity;
            }

            ApplyTint(root, entry.tint);
        }

        void ApplyTint(Transform root, Color tint)
        {
            if (tint.a <= 0f) return; // an unset palette entry is transparent black; don't paint players invisible.

            _propertyBlock ??= new MaterialPropertyBlock();
            _tintTargets.Clear();
            root.GetComponentsInChildren(includeInactive: true, _tintTargets);

            for (int i = 0; i < _tintTargets.Count; i++)
            {
                var renderer = _tintTargets[i];
                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorId, tint); // URP Lit
                _propertyBlock.SetColor(ColorId, tint);     // built-in / unlit fallback
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        // ---------------------------------------------------------------
        // Transform replication
        // ---------------------------------------------------------------

        void PublishTransform(bool force)
        {
            if (!force)
            {
                if (Time.time < _nextSendTime) return;

                bool moved = (transform.position - _netPosition.Value).sqrMagnitude
                             > positionSendThreshold * positionSendThreshold;
                bool turned = Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, _netYaw.Value)) > yawSendThreshold;
                if (!moved && !turned) return;
            }

            _nextSendTime = Time.time + sendInterval;
            _netPosition.Value = transform.position;
            _netYaw.Value = transform.eulerAngles.y;
        }

        /// <summary>
        /// Remote bodies are smoothed toward the last replicated pose rather
        /// than snapped to it. Without this they visibly step once per packet
        /// (20 Hz against a 60+ FPS render) which reads as stuttering even on
        /// a flawless connection -- the interpolation, not the send rate, is
        /// what makes other players look like they are walking.
        ///
        /// Exponential smoothing (not a fixed Lerp factor) so the result does
        /// not change with framerate.
        /// </summary>
        void SmoothTowardsReplicated()
        {
            var target = _netPosition.Value;
            float targetYaw = _netYaw.Value;

            if (!_hasRemoteSample || (transform.position - target).sqrMagnitude > remoteSnapDistance * remoteSnapDistance)
            {
                SnapTo(target, targetYaw);
                return;
            }

            float t = 1f - Mathf.Exp(-remoteSmoothing * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, target, t);

            float yaw = Mathf.LerpAngle(transform.eulerAngles.y, targetYaw, t);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        void OnNetPositionChanged(Vector3 previous, Vector3 current) => _receivedAnySample = true;

        void OnTeleportCountChanged(int previous, int current)
        {
            if (IsOwner || current == _lastSeenTeleportCount) return;

            _lastSeenTeleportCount = current;
            SnapTo(_netPosition.Value, _netYaw.Value);
        }

        void SnapTo(Vector3 position, float yaw)
        {
            bool hadController = _controller != null && _controller.enabled;
            if (hadController) _controller.enabled = false;

            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            if (hadController) _controller.enabled = true;
            _hasRemoteSample = true;
        }
    }
}
