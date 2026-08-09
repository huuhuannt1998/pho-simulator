using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Pho.Core;
using Pho.Core.Interaction;
using Pho.Domain.Events;

namespace Pho.Player
{
    /// <summary>
    /// First-person interaction probe. One SphereCast per frame from camera
    /// center (radius/range/layer mask are serialized, not hardcoded)
    /// against the "Interactable" layer, with a per-collider
    /// GetComponentInParent&lt;IInteractable&gt; cache and hysteresis so the
    /// prompt doesn't flicker at collider edges. Publishes
    /// InteractionTargetChanged for the UI -- the UI never raycasts itself.
    /// See architecture.md §5.
    ///
    /// Injection seam: GameContext.cs is frozen and outside this assembly's
    /// ownership, so this component cannot reach into a static/singleton
    /// composition root. The (not-yet-built) scene bootstrap is expected to
    /// call Bind(ctx) right after instantiating the player. Until Bind is
    /// called, target acquisition still runs and the hysteresis/highlight
    /// state is still correct -- there's just nowhere to publish the event
    /// yet, so publishing is skipped rather than throwing.
    /// </summary>
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [Header("Detection")]
        [Tooltip("Camera the SphereCast originates from. Auto-resolved via GetComponentInChildren<Camera>() if left empty.")]
        [SerializeField] Camera eye;
        [Tooltip("Physics layer(s) stations register their collider on. Project layers aren't set up yet -- set this once they are.")]
        [SerializeField] LayerMask interactableMask;
        [SerializeField] float range = 2.5f;
        [SerializeField] float sphereRadius = 0.12f;
        [SerializeField] int missFramesToClearHighlight = 3;

        [Header("Hands (M2 placeholder -- see PlaceholderInteractorAgent)")]
        [Tooltip("Where held items will attach once the M3 carry system exists. Auto-created under 'eye' if left empty.")]
        [SerializeField] Transform holdAnchor;

        IEventBus _events;
        IInteractorAgent _agent;
        InputAction _interactAction;

        readonly Dictionary<Collider, IInteractable> _colliderCache = new Dictionary<Collider, IInteractable>();
        TargetAcquisitionHysteresis<IInteractable> _hysteresis;
        RaycastHit _lastHit;

        /// <summary>
        /// Injection seam for the scene bootstrap: supplies the IEventBus
        /// this component publishes InteractionTargetChanged on. Cannot be
        /// wired through GameContext.cs itself (frozen, not ours to edit).
        /// </summary>
        public void Bind(GameContext ctx)
        {
            _events = ctx != null ? ctx.Events : null;
        }

        /// <summary>
        /// Optional replacement for the M2 PlaceholderInteractorAgent.
        /// M3's real hand-slot agent should call this once it exists.
        /// </summary>
        public void BindAgent(IInteractorAgent agent)
        {
            _agent = agent ?? new PlaceholderInteractorAgent(holdAnchor);
        }

        void Awake()
        {
            if (eye == null) eye = GetComponentInChildren<Camera>();

            if (holdAnchor == null)
            {
                var anchorGo = new GameObject("HoldAnchor (auto)");
                anchorGo.transform.SetParent(eye != null ? eye.transform : transform, false);
                holdAnchor = anchorGo.transform;
            }

            _hysteresis = new TargetAcquisitionHysteresis<IInteractable>(Mathf.Max(1, missFramesToClearHighlight));
            _agent = new PlaceholderInteractorAgent(holdAnchor);

            _interactAction = new InputAction(name: "Interact", type: InputActionType.Button, binding: "<Keyboard>/e");
        }

        void OnEnable()
        {
            _interactAction?.Enable();
        }

        void OnDisable()
        {
            _interactAction?.Disable();
        }

        void OnDestroy()
        {
            _interactAction?.Dispose();
        }

        void Update()
        {
            if (eye == null) return;

            var origin = eye.transform.position;
            var direction = eye.transform.forward;

            bool hitSomething = Physics.SphereCast(
                origin, sphereRadius, direction, out var hit, range,
                interactableMask, QueryTriggerInteraction.Ignore);

            if (hitSomething) _lastHit = hit;

            IInteractable candidate = hitSomething ? ResolveInteractable(hit.collider) : null;
            bool changed = _hysteresis.Report(candidate);

            if (changed) PublishTargetChanged();

            if (_hysteresis.Current != null && _interactAction.WasPerformedThisFrame())
            {
                TryInteract(_hysteresis.Current);
            }
        }

        IInteractable ResolveInteractable(Collider col)
        {
            if (_colliderCache.TryGetValue(col, out var cached)) return cached;
            var found = col.GetComponentInParent<IInteractable>();
            _colliderCache[col] = found;
            return found;
        }

        void PublishTargetChanged()
        {
            if (_events == null) return; // not bound yet -- nowhere to publish to.

            var target = _hysteresis.Current;
            if (target == null)
            {
                _events.Publish(new InteractionTargetChanged(false, string.Empty));
                return;
            }

            var ctx = new InteractionContext(_agent, _lastHit, InteractionVerb.Primary);
            var text = target.GetInteractionText(ctx) ?? string.Empty;
            _events.Publish(new InteractionTargetChanged(true, text));
        }

        void TryInteract(IInteractable target)
        {
            var ctx = new InteractionContext(_agent, _lastHit, InteractionVerb.Primary);
            if (!target.CanInteract(ctx)) return;
            target.Interact(ctx);
        }
    }
}
