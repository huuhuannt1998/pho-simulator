using UnityEngine;
using Pho.Core.Interaction;

namespace Pho.Player
{
    /// <summary>
    /// One flat placement surface (a prep counter, pass, shelf...) for the
    /// M3 vertical slice -- a single bounded rectangle derived from this
    /// object's own Collider, not a general-purpose grid/snap system (the
    /// M3 brief is explicit that this is "one surface type for the vertical
    /// slice"). TryGetPlacement clamps the aim point onto the surface's
    /// Collider bounds (XZ) and snaps Y to the surface's top, then rejects
    /// the candidate with a Physics.CheckBox sized from the item's own
    /// bounds if anything is already resting there -- per architecture.md
    /// §5: "Placement = IPlacementSurface returns a snapped pose; a
    /// Physics.CheckBox at the target rejects overlaps."
    ///
    /// Also implements IInteractable so the existing E-to-interact path
    /// (PlayerInteractor's SphereCast + hysteresis) drives placement with
    /// zero new input plumbing, per the M3 brief's suggested design:
    /// while the player's hands are full and they're aiming at this
    /// surface, "Press E to place" fires TryGetPlacement at the exact aim
    /// point from InteractionContext.Hit and, if valid, hands the item out
    /// of the agent's slot and seats it there.
    ///
    /// IItemReceiver has no aim point in its signature (CanAccept/TryAccept
    /// take only the item), so those two answer "can/does this surface
    /// accept the item at its own default slot" by feeding this object's
    /// own transform.position through the exact same TryGetPlacement/
    /// Physics.CheckBox codepath the aim-driven Interact() uses -- one
    /// overlap check, two entry points, per the M3 brief's "delegate to the
    /// same overlap check."
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class CounterPlacementSurface : MonoBehaviour, IPlacementSurface, IItemReceiver, IInteractable
    {
        [SerializeField] Collider surfaceCollider;
        [Tooltip("Floor on the overlap-check box half-extents, so items with a tiny/degenerate collider still get a meaningful check.")]
        [SerializeField] Vector3 minCheckHalfExtents = new Vector3(0.05f, 0.05f, 0.05f);
        [Tooltip("Layers considered when checking for something already occupying the candidate placement.")]
        [SerializeField] LayerMask overlapMask = ~0;

        void Awake()
        {
            if (surfaceCollider == null) surfaceCollider = GetComponent<Collider>();
        }

        // ---- IPlacementSurface ----

        public bool TryGetPlacement(Vector3 aimPoint, IHoldable item, out Pose placement)
        {
            placement = default;
            if (item == null || item.Transform == null || surfaceCollider == null) return false;

            var bounds = surfaceCollider.bounds;
            var itemHalfExtents = GetItemHalfExtents(item);

            float topY = bounds.max.y + itemHalfExtents.y;
            float x = Mathf.Clamp(aimPoint.x, bounds.min.x + itemHalfExtents.x, bounds.max.x - itemHalfExtents.x);
            float z = Mathf.Clamp(aimPoint.z, bounds.min.z + itemHalfExtents.z, bounds.max.z - itemHalfExtents.z);

            var candidatePos = new Vector3(x, topY, z);
            // Keep the item's own yaw off the surface's rotation -- flat and level, no roll/pitch inherited from wherever it was aimed from.
            var candidateRot = Quaternion.LookRotation(transform.forward, Vector3.up);

            // Shrink slightly below the item's true half-extents so a box that's
            // flush (touching, not overlapping) with the counter's own top face or a
            // flush neighbour doesn't false-reject from floating-point boundary noise.
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

            SeatItem(item, placement);
            return true;
        }

        void SeatItem(IHoldable item, in Pose placement)
        {
            // Safe even when the caller (e.g. IInteractorAgent.TryGiveHeld)
            // already unparented the item -- this is a no-op reparent in
            // that case, and a real one for any future caller that hands us
            // an item directly without going through an agent.
            item.Transform.SetParent(null, worldPositionStays: true);
            item.Transform.SetPositionAndRotation(placement.position, placement.rotation);
            item.OnDropped(); // re-enable physics AFTER the pose is set, so nothing briefly collides at the old (held/hand-slot) position.
        }

        // ---- IInteractable (placement input) ----

        public string GetInteractionText(in InteractionContext ctx)
        {
            if (ctx.Agent == null || ctx.Agent.Held == null) return string.Empty;
            return TryGetPlacement(ctx.Hit.point, ctx.Agent.Held, out _) ? "Press E to place" : "Can't place here";
        }

        public bool CanInteract(in InteractionContext ctx)
        {
            return ctx.Agent != null
                && ctx.Agent.Held != null
                && TryGetPlacement(ctx.Hit.point, ctx.Agent.Held, out _);
        }

        public void Interact(in InteractionContext ctx)
        {
            var agent = ctx.Agent;
            if (agent == null || agent.Held == null) return;
            if (!TryGetPlacement(ctx.Hit.point, agent.Held, out var placement)) return;
            if (!agent.TryGiveHeld(out var item)) return;

            SeatItem(item, placement);
        }
    }
}
