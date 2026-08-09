using UnityEngine;
using Pho.Core.Interaction;

namespace Pho.Player
{
    /// <summary>
    /// Generic pickupable world object (bowl, crate, tray, ...) -- the
    /// concrete IHoldable the M3 carry system moves in and out of the hand
    /// slot. Also implements IInteractable so a CarryableObject sitting in
    /// the world is itself the thing PlayerInteractor's existing E-press
    /// path acquires and picks up: CanInteract gates on free hands,
    /// Interact hands off to IInteractorAgent.TryTake(this) (per
    /// architecture.md §5's IInteractorAgent contract and the M3 milestone
    /// row's "pickup/drop/place").
    ///
    /// Physics-disable approach while held (a judgment call the "Carry
    /// system" paragraph leaves to the implementation, saying only
    /// "kinematic + reparented ... no physics-driven carry"): the
    /// Rigidbody is set kinematic (not disabled as a component -- a
    /// disabled Rigidbody would make the GameObject fall back to
    /// non-physics static-collider semantics, which is not what we want:
    /// we still want OnDropped() to be able to flip isKinematic back off
    /// and immediately have a live, simulated Rigidbody again) and every
    /// Collider found under this object is disabled outright (not left as
    /// triggers) for the duration of the hold. Disabling beats
    /// trigger-ifying here because it (a) fully removes the held item from
    /// physics queries -- including the CounterPlacementSurface overlap
    /// check that runs *while the item is still held*, so the item can
    /// never self-intersect its own placement candidate -- and (b) needs no
    /// layer-collision-matrix bookkeeping to keep the carried item from
    /// shoving the player's own CharacterController.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarryableObject : MonoBehaviour, IHoldable, IInteractable
    {
        [Header("Carry")]
        [SerializeField] HoldPose pose = HoldPose.OneHandedFront;
        [SerializeField] bool allowsSprint = true;
        [Tooltip("Prompt name, e.g. \"bowl\" -> \"Press E to pick up bowl\". Falls back to a generic phrase if left empty.")]
        [SerializeField] string displayName;

        [Header("Physics (auto-resolved if left empty)")]
        [SerializeField] Rigidbody body;
        [SerializeField] Collider[] colliders;

        bool _isHeld;

        public Transform Transform => transform;
        public HoldPose Pose => pose;
        public bool AllowsSprint => allowsSprint;
        public bool IsHeld => _isHeld;

        void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (colliders == null || colliders.Length == 0)
                colliders = GetComponentsInChildren<Collider>(true);
        }

        // ---- IHoldable ----

        /// <summary>
        /// Disables physics for the duration of the hold. Does NOT touch
        /// this object's Transform/parenting -- that is PlayerHandSlot's
        /// job (it owns the hand slot and the HoldAnchor lerp), per the
        /// IHoldable doc comment's split of responsibility.
        /// </summary>
        public void OnPickedUp(IInteractorAgent agent)
        {
            _isHeld = true;

            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            SetCollidersEnabled(false);
        }

        /// <summary>
        /// Re-enables physics. Deliberately does NOT apply the "small
        /// forward impulse" architecture.md §5 describes for a drop --
        /// OnDropped() is parameterless per the frozen IHoldable contract
        /// and has no drop direction to work with. That impulse is the
        /// carrier's responsibility: PlayerHandSlot.DropHeld() calls
        /// OnDropped() first (to get physics back on) and then applies
        /// Rigidbody.AddForce itself, since only the carrier knows which
        /// way the player is facing. Placement (CounterPlacementSurface)
        /// also calls OnDropped() but never applies an impulse -- a placed
        /// item should snap and stay, not go flying.
        /// </summary>
        public void OnDropped()
        {
            _isHeld = false;

            SetCollidersEnabled(true);

            if (body != null)
            {
                body.isKinematic = false;
            }
        }

        void SetCollidersEnabled(bool value)
        {
            if (colliders == null) return;
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null) colliders[i].enabled = value;
            }
        }

        // ---- IInteractable (world pickup) ----

        public string GetInteractionText(in InteractionContext ctx)
        {
            if (_isHeld) return string.Empty;
            var name = string.IsNullOrEmpty(displayName) ? "item" : displayName;
            return $"Press E to pick up {name}";
        }

        public bool CanInteract(in InteractionContext ctx)
        {
            return !_isHeld && ctx.Agent != null && ctx.Agent.HasFreeHands;
        }

        public void Interact(in InteractionContext ctx)
        {
            ctx.Agent?.TryTake(this);
        }
    }
}
