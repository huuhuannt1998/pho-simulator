using UnityEngine;

namespace Pho.Core.Interaction
{
    /// <summary>
    /// Free-placement surface (counters, shelves, the pass). Given where the
    /// player is aiming and what they're holding, returns a snapped world
    /// pose. The M3 carry system is expected to reject overlaps at the
    /// returned pose with a Physics.CheckBox and show a ghost material for
    /// valid/invalid, per architecture.md §5 -- neither of those concerns
    /// belong to this interface itself.
    /// </summary>
    public interface IPlacementSurface
    {
        bool TryGetPlacement(Vector3 aimPoint, IHoldable item, out Pose placement);
    }
}
