using UnityEngine;
using UnityEngine.InputSystem;
using Pho.Core.Interaction;

namespace Pho.Player
{
    /// <summary>
    /// The real M3 hand-slot carry system -- replaces PlaceholderInteractorAgent
    /// as the IInteractorAgent PlayerInteractor drives stations with. Single
    /// hand slot; held items are kinematic + reparented to HoldAnchor with a
    /// lerped local pose, no configurable joints, no physics-driven carry --
    /// architecture.md §5 "Carry system", verbatim. Attach alongside
    /// PlayerInteractor on the player GameObject.
    ///
    /// HoldAnchor single-source-of-truth: this component is the canonical
    /// owner of "where held items attach." PlayerInteractor (M2) had its own
    /// placeholder anchor, auto-created under its camera, purely so
    /// PlaceholderInteractorAgent had somewhere non-null to point at. Rather
    /// than wire two independent Transforms together through the Inspector
    /// (impossible in this pass anyway -- no Unity, no prefab access), this
    /// component pushes itself into PlayerInteractor via the already-public
    /// PlayerInteractor.BindAgent(IInteractorAgent), and the matching
    /// PlayerInteractor.cs edit makes BindAgent adopt agent.HoldAnchor into
    /// its own `holdAnchor` field. Net effect: exactly one HoldAnchor
    /// Transform exists once both components are present, sourced from here.
    /// The hookup runs in Start() (not Awake()) specifically so it doesn't
    /// depend on GameObject component execution order against
    /// PlayerInteractor.Awake() -- Unity guarantees every Awake on a
    /// GameObject runs before any Start.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHandSlot : MonoBehaviour, IInteractorAgent
    {
        [Header("Anchor")]
        [Tooltip("Where held items lerp toward. This is the single source of truth for HoldAnchor -- see class doc comment. Auto-created under 'eye' if left empty.")]
        [SerializeField] Transform holdAnchor;
        [Tooltip("Camera used to place the auto-created HoldAnchor and to aim the ghost-preview raycast/drop impulse. Auto-resolved via GetComponentInChildren<Camera>() if left empty.")]
        [SerializeField] Camera eye;

        [Header("Carry")]
        [Tooltip("Exponential lerp rate (per second) of the held item's local pose toward HoldAnchor (identity). Higher = snappier follow. This is the 'lerped local pose' architecture.md §5 calls for -- never physics-driven.")]
        [SerializeField] float followLerpSpeed = 12f;

        [Header("Drop")]
        [Tooltip("\"Drop in place\" key -- release the held item without a target IPlacementSurface. Optional per the M3 brief; included here as <Keyboard>/g.")]
        [SerializeField] float dropForwardImpulse = 2.5f;

        [Header("Ghost Preview")]
        [SerializeField] float ghostPreviewRange = 3f;
        [Tooltip("Layer(s) the ghost-preview raycast tests against. Defaults to everything since project layers aren't finalized yet; narrow once they are.")]
        [SerializeField] LayerMask ghostPreviewMask = ~0;

        IHoldable _held;
        Transform _heldTransform;
        CarryGhostPreview _ghost;
        InputAction _dropAction;

        public IHoldable Held => _held;
        public bool HasFreeHands => _held == null;
        public Transform HoldAnchor => holdAnchor;

        void Awake()
        {
            if (eye == null) eye = GetComponentInChildren<Camera>();

            if (holdAnchor == null)
            {
                var anchorGo = new GameObject("HoldAnchor");
                anchorGo.transform.SetParent(eye != null ? eye.transform : transform, false);
                holdAnchor = anchorGo.transform;
            }

            _ghost = new CarryGhostPreview(transform, ghostPreviewRange, ghostPreviewMask);

            _dropAction = new InputAction(name: "DropHeld", type: InputActionType.Button, binding: "<Keyboard>/g");
        }

        void Start()
        {
            // Self-wiring, zero scene edits -- see class doc comment.
            var interactor = GetComponent<PlayerInteractor>();
            if (interactor != null) interactor.BindAgent(this);
        }

        void OnEnable()
        {
            _dropAction?.Enable();
        }

        void OnDisable()
        {
            _dropAction?.Disable();
        }

        void OnDestroy()
        {
            _dropAction?.Dispose();
        }

        void Update()
        {
            if (_held != null && _dropAction != null && _dropAction.WasPerformedThisFrame())
            {
                DropHeld();
            }
        }

        // LateUpdate, not Update: the held item should settle relative to
        // the camera/HoldAnchor *after* look/camera movement has applied
        // this frame, or the follow will visibly lag a frame behind.
        void LateUpdate()
        {
            if (_held != null && _heldTransform != null && holdAnchor != null)
            {
                float t = 1f - Mathf.Exp(-followLerpSpeed * Time.deltaTime);
                _heldTransform.localPosition = Vector3.Lerp(_heldTransform.localPosition, Vector3.zero, t);
                _heldTransform.localRotation = Quaternion.Slerp(_heldTransform.localRotation, Quaternion.identity, t);
            }

            _ghost.Tick(_held, eye != null ? eye.transform : transform);
        }

        // ---- IInteractorAgent ----

        public bool TryTake(IHoldable item)
        {
            if (item == null || !HasFreeHands || holdAnchor == null) return false;

            item.OnPickedUp(this);

            _held = item;
            _heldTransform = item.Transform;
            _heldTransform.SetParent(holdAnchor, worldPositionStays: false);

            return true;
        }

        public bool TryGiveHeld(out IHoldable item)
        {
            if (_held == null)
            {
                item = null;
                return false;
            }

            item = _held;
            var releasedTransform = _heldTransform;

            _held = null;
            _heldTransform = null;

            if (releasedTransform != null)
                releasedTransform.SetParent(null, worldPositionStays: true);

            return true;
        }

        /// <summary>
        /// "Drop in place" (the optional G-key path noted in the M3 brief):
        /// release the held item with no target surface, re-enable its
        /// physics via IHoldable.OnDropped(), then apply architecture.md
        /// §5's "small forward impulse" -- done here, not inside
        /// IHoldable.OnDropped(), because the frozen OnDropped() signature
        /// takes no parameters and has no way to know which direction the
        /// player is facing. Only the carrier knows that.
        /// </summary>
        public bool DropHeld()
        {
            if (!TryGiveHeld(out var item)) return false;

            item.OnDropped();

            var rb = item.Transform.GetComponent<Rigidbody>();
            if (rb != null)
            {
                var forward = eye != null ? eye.transform.forward : transform.forward;
                rb.AddForce(forward * dropForwardImpulse, ForceMode.Impulse);
            }

            return true;
        }
    }
}
