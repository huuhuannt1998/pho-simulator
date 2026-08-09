using UnityEngine;
using Pho.Core.Interaction;
using Pho.Domain.Contracts;
using Pho.Domain.Cooking;
using Pho.Domain.Events;

namespace Pho.Kitchen
{
    /// <summary>
    /// Where the player places a physically-assembled BowlObject to have it
    /// scored. Implements IPlacementSurface + IItemReceiver + IInteractable
    /// in the same three-way split as Pho.Player's CounterPlacementSurface
    /// (read that file's doc comment for the full placement-algorithm
    /// rationale -- clamp the aim point onto this surface's Collider bounds,
    /// snap Y to its top, reject via Physics.CheckBox, one overlap check
    /// feeding both the aim-driven Interact() path and the CanAccept/
    /// TryAccept path via the surface's own transform.position). The one
    /// deviation from CounterPlacementSurface: TryGetPlacement here only
    /// accepts a BowlObject, not any IHoldable -- a crate placed on the pass
    /// has nothing to score.
    ///
    /// Per the M5 brief: this station does NOT cook anything. It is the
    /// satisfaction of the "physical multi-station assembly, no single Cook
    /// button" acceptance criterion -- everything in the bowl already came
    /// from walking between IngredientStation/BrothPot instances; placing it
    /// here only reads and scores what's already there, then leaves the
    /// scored bowl sitting in place for a later (Customer/Order-track) wave
    /// to pick up and serve. Re-placing an already-sealed/scored bowl here
    /// again is harmless and idempotent: BowlContents.Add already rejects
    /// further ingredient adds once sealed, Seal() just re-stamps the seal
    /// time, and RecipeMatcher/QualityCalculator are pure functions of the
    /// bowl's (now-frozen) contents, so re-scoring yields the same result
    /// and simply republishes BowlAssembled.
    ///
    /// Broth quality fed into QualityCalculator.Score: Score's frozen
    /// signature takes a `in BrothState broth` purely to read
    /// `broth.Quality01` (see QualityCalculator.cs -- Temperature01 is
    /// computed from bowl.BrothTemperatureC instead, not from BrothState).
    /// The pass counter has no reference to whichever BrothPot the bowl's
    /// broth was ladled from (by design -- coupling the pass to a specific
    /// pot would be a step backwards for a kitchen with more than one pot),
    /// so it reconstructs a synthetic BrothState from the bowl's own
    /// recorded Broth-slot BowlComponent.Quality01 (the true quality that
    /// component was ladled at, exactly the same "record what actually went
    /// in" philosophy architecture.md section 7 uses for every other
    /// ingredient). A bowl with no Broth component yet (shouldn't happen for
    /// a bowl actually brought to the pass, but defended anyway) scores 0
    /// broth quality rather than throwing.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PassCounter : MonoBehaviour, IPlacementSurface, IItemReceiver, IInteractable
    {
        [SerializeField] Collider surfaceCollider;
        [Tooltip("Floor on the overlap-check box half-extents, so items with a tiny/degenerate collider still get a meaningful check.")]
        [SerializeField] Vector3 minCheckHalfExtents = new Vector3(0.05f, 0.05f, 0.05f);
        [Tooltip("Layers considered when checking for something already occupying the candidate placement.")]
        [SerializeField] LayerMask overlapMask = ~0;

        IGameDatabase _database;
        IBalanceConfig _balance;
        IEventBus _events;
        bool _bound;

        void Awake()
        {
            if (surfaceCollider == null) surfaceCollider = GetComponent<Collider>();
        }

        /// <summary>
        /// Injection seam -- see IngredientStation.Bind for the same pattern.
        /// Until called, the counter still accepts and physically seats a
        /// bowl (placement is a pure-geometry concern, always available) but
        /// skips scoring/publishing entirely rather than throwing -- inert,
        /// not broken.
        /// </summary>
        public void Bind(IGameDatabase database, IBalanceConfig balance, IEventBus events)
        {
            _database = database;
            _balance = balance;
            _events = events;
            _bound = database != null && balance != null;
        }

        // ---- IPlacementSurface ----

        public bool TryGetPlacement(Vector3 aimPoint, IHoldable item, out Pose placement)
        {
            placement = default;
            if (!(item is BowlObject) || item.Transform == null || surfaceCollider == null) return false;

            var bounds = surfaceCollider.bounds;
            var itemHalfExtents = GetItemHalfExtents(item);

            float topY = bounds.max.y + itemHalfExtents.y;
            float x = Mathf.Clamp(aimPoint.x, bounds.min.x + itemHalfExtents.x, bounds.max.x - itemHalfExtents.x);
            float z = Mathf.Clamp(aimPoint.z, bounds.min.z + itemHalfExtents.z, bounds.max.z - itemHalfExtents.z);

            var candidatePos = new Vector3(x, topY, z);
            var candidateRot = Quaternion.LookRotation(transform.forward, Vector3.up);

            var checkHalfExtents = Vector3.Max(itemHalfExtents * 0.95f, minCheckHalfExtents);

            if (Physics.CheckBox(candidatePos, checkHalfExtents, candidateRot, overlapMask, QueryTriggerInteraction.Ignore))
                return false;

            placement = new Pose(candidatePos, candidateRot);
            return true;
        }

        static Vector3 GetItemHalfExtents(IHoldable item)
        {
            var col = item.Transform.GetComponentInChildren<Collider>();
            if (col != null) return col.bounds.extents;

            var rend = item.Transform.GetComponentInChildren<Renderer>();
            if (rend != null) return rend.bounds.extents;

            return new Vector3(0.1f, 0.1f, 0.1f);
        }

        // ---- IItemReceiver ----

        public bool CanAccept(IHoldable item) => TryGetPlacement(transform.position, item, out _);

        public bool TryAccept(IHoldable item)
        {
            if (!TryGetPlacement(transform.position, item, out var placement)) return false;

            SeatAndScore((BowlObject)item, placement);
            return true;
        }

        // ---- IInteractable (placement input) ----

        public string GetInteractionText(in InteractionContext ctx)
        {
            if (ctx.Agent == null || !(ctx.Agent.Held is BowlObject)) return string.Empty;
            return TryGetPlacement(ctx.Hit.point, ctx.Agent.Held, out _) ? "Press E to place on pass" : "Can't place here";
        }

        public bool CanInteract(in InteractionContext ctx)
        {
            return ctx.Agent != null
                && ctx.Agent.Held is BowlObject
                && TryGetPlacement(ctx.Hit.point, ctx.Agent.Held, out _);
        }

        public void Interact(in InteractionContext ctx)
        {
            var agent = ctx.Agent;
            if (agent == null || !(agent.Held is BowlObject)) return;
            if (!TryGetPlacement(ctx.Hit.point, agent.Held, out var placement)) return;
            if (!agent.TryGiveHeld(out var item)) return;

            SeatAndScore((BowlObject)item, placement);
        }

        // ---- Seat + score ----

        void SeatAndScore(BowlObject bowl, in Pose placement)
        {
            bowl.Transform.SetParent(null, worldPositionStays: true);
            bowl.Transform.SetPositionAndRotation(placement.position, placement.rotation);
            bowl.OnDropped(); // re-enable physics AFTER the pose is set, matching CounterPlacementSurface.

            bowl.Seal(Time.time);

            if (!_bound) return; // inert until Bind() is called -- the bowl still physically seats, just isn't scored yet.

            var menu = _database.Recipes;
            if (menu == null || menu.Count == 0) return;

            var contents = bowl.Contents;
            var match = RecipeMatcher.BestMatch(contents, menu, _balance);

            float brothQuality01 = 0f;
            var components = contents.Components;
            for (int i = 0; i < components.Count; i++)
            {
                if (components[i].Slot == ComponentSlot.Broth)
                {
                    brothQuality01 = components[i].Quality01;
                    break;
                }
            }

            var brothState = new BrothState { Quality01 = brothQuality01 };
            var quality = QualityCalculator.Score(contents, match, brothState, _balance);

            _events?.Publish(new BowlAssembled(
                bowl.Transform.gameObject.GetInstanceID(),
                match.Recipe,
                match.Accuracy01,
                quality.Overall01));
        }
    }
}
