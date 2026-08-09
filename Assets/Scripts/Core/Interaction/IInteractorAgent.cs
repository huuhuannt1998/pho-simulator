using UnityEngine;

namespace Pho.Core.Interaction
{
    /// <summary>
    /// What a station is allowed to know about the actor interacting with
    /// it -- today the player, later (unchanged) an employee. Stations in
    /// Pho.Kitchen / Pho.Customers depend only on this interface, never on
    /// Pho.Player, which is what keeps those assemblies decoupled from the
    /// concrete player implementation.
    /// </summary>
    public interface IInteractorAgent
    {
        IHoldable Held { get; }
        bool HasFreeHands { get; }

        /// <summary>Station -> hands. Returns false if hands are already full or the item refuses.</summary>
        bool TryTake(IHoldable item);

        /// <summary>Hands -> station. Returns false if nothing is held.</summary>
        bool TryGiveHeld(out IHoldable item);

        Transform HoldAnchor { get; }
    }
}
