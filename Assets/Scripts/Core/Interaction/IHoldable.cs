using UnityEngine;

namespace Pho.Core.Interaction
{
    /// <summary>
    /// A carryable world object (bowl, crate, tray, ...). The M3 carry
    /// system kinematically reparents held items to IInteractorAgent.HoldAnchor
    /// with a lerped local pose -- no physics-driven carry (see
    /// architecture.md §5, "physics carry ... produces a week of jitter
    /// bugs"). OnPickedUp/OnDropped are where an implementation should
    /// toggle its own Rigidbody/collider state; the agent does not do this
    /// for it.
    /// </summary>
    public interface IHoldable
    {
        Transform Transform { get; }
        HoldPose Pose { get; }
        bool AllowsSprint { get; }

        void OnPickedUp(IInteractorAgent agent);
        void OnDropped();
    }
}
