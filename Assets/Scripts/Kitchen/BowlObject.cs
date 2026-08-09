using UnityEngine;
using Pho.Core.Interaction;
using Pho.Domain.Contracts;
using Pho.Domain.Cooking;
using Pho.Domain.Identity;
using Pho.Domain.Inventory;

namespace Pho.Kitchen
{
    /// <summary>
    /// The physical, carryable bowl -- the Kitchen-side counterpart to
    /// Pho.Player's CarryableObject, wrapping a Pho.Domain.Cooking.BowlContents
    /// (pure model, architecture.md section 7) as its internal state.
    ///
    /// Why this implements IHoldable fresh instead of subclassing
    /// CarryableObject: Pho.Kitchen.asmdef references only Pho.Domain and
    /// Pho.Core (not Pho.Player), and Pho.Player already depends on Pho.Core
    /// -- a Kitchen -> Player reference would be a new asmdef edge, which
    /// architecture.md section 10 explicitly reserves for the integration
    /// agent ("Anything requiring a new asmdef reference ... stop and
    /// escalate"). Since IHoldable is a 5-member interface, duplicating the
    /// handful of physics-toggle lines below is a smaller, safer footprint
    /// than opening that asmdef question. The OnPickedUp/OnDropped bodies
    /// below intentionally mirror CarryableObject.cs line for line (same
    /// kinematic-not-disabled Rigidbody approach, same "disable colliders
    /// outright, not trigger-ify" reasoning) so the two carry implementations
    /// stay behaviourally identical even though they don't share code.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BowlObject : MonoBehaviour, IHoldable, IInteractable
    {
        [Header("Carry")]
        [SerializeField] HoldPose pose = HoldPose.TwoHandedLow;
        [SerializeField] bool allowsSprint = true;
        [Tooltip("Prompt name, e.g. \"bowl\" -> \"Press E to pick up bowl\".")]
        [SerializeField] string displayName = "bowl";

        [Header("Physics (auto-resolved if left empty)")]
        [SerializeField] Rigidbody body;
        [SerializeField] Collider[] colliders;

        // The pure model this component wraps -- see architecture.md section 7.
        // One BowlContents per physical bowl instance, created once and never
        // replaced for the object's lifetime.
        readonly BowlContents _contents = new BowlContents();

        bool _isHeld;

        public Transform Transform => transform;
        public HoldPose Pose => pose;
        public bool AllowsSprint => allowsSprint;
        public bool IsHeld => _isHeld;

        /// <summary>Read access for RecipeMatcher/QualityCalculator/PassCounter -- never mutated directly from outside, only via AddIngredient/Seal below.</summary>
        public BowlContents Contents => _contents;

        void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (colliders == null || colliders.Length == 0)
                colliders = GetComponentsInChildren<Collider>(true);
        }

        // ---- IHoldable ----

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

        // ---- IInteractable (world pickup, e.g. off a shelf/stack) ----

        public string GetInteractionText(in InteractionContext ctx)
        {
            if (_isHeld) return string.Empty;
            var name = string.IsNullOrEmpty(displayName) ? "bowl" : displayName;
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

        // ---- Cooking-model bridge ----

        /// <summary>
        /// Primary path for stations drawing from the shared InventoryModel:
        /// translates a ConsumedIngredient (InventoryModel.TryConsume's
        /// output) into a BowlComponent and forwards it to BowlContents.Add.
        /// See IngredientStation.
        /// </summary>
        public AddResult AddIngredient(ComponentSlot slot, ConsumedIngredient consumed, float nowSeconds)
        {
            return AddIngredient(slot, consumed.Id, consumed.Amount, consumed.Quality01, consumed.Freshness01, nowSeconds);
        }

        /// <summary>
        /// Lower-level overload for a station whose ingredient does NOT come
        /// from InventoryModel.TryConsume -- specifically BrothPot, which
        /// ladles from a simulated BrothState (BrothSimulator.cs), not a
        /// FIFO ingredient lot. Both overloads bottom out in the same
        /// BowlContents.Add call, so BowlContents' AlreadySealed / slot
        /// over-capacity rejection is honoured identically no matter which
        /// station is calling.
        /// </summary>
        public AddResult AddIngredient(ComponentSlot slot, IngredientId ingredient, float amount, float quality01, float freshness01, float nowSeconds)
        {
            var component = new BowlComponent(slot, ingredient, amount, quality01, freshness01, nowSeconds);
            return _contents.Add(component);
        }

        /// <summary>Seals the underlying BowlContents -- called by PassCounter once the bowl is placed for scoring.</summary>
        public void Seal(float nowSeconds)
        {
            _contents.Seal(nowSeconds);
        }
    }
}
