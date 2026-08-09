using UnityEngine;
using Pho.Core.Interaction;

namespace Pho.Player
{
    /// <summary>
    /// M2 stand-in IInteractorAgent. PlayerInteractor needs a non-null Agent
    /// to build an InteractionContext even though M3 hasn't landed the real
    /// hand-slot carry system (CarryableObject, hand-slot pickup/drop/place
    /// -- see architecture.md §5 "Carry system" and the M3 milestone row)
    /// yet.
    ///
    /// Reports "no hands available" (HasFreeHands = false, TryTake /
    /// TryGiveHeld always fail) rather than faking success, so any station
    /// whose CanInteract/Interact gates on hand state correctly refuses
    /// instead of silently no-opping or lying about a pickup that never
    /// happened. Stations that don't touch ctx.Agent at all (most simple
    /// M2-era test interactables -- switches, levers, doors) work normally
    /// through this placeholder.
    ///
    /// Replace via PlayerInteractor.BindAgent(IInteractorAgent) once M3's
    /// real hand-slot agent exists; this class is intentionally not the
    /// place to grow that system in.
    /// </summary>
    public sealed class PlaceholderInteractorAgent : IInteractorAgent
    {
        public IHoldable Held => null;
        public bool HasFreeHands => false;
        public Transform HoldAnchor { get; }

        public PlaceholderInteractorAgent(Transform holdAnchor)
        {
            HoldAnchor = holdAnchor;
        }

        public bool TryTake(IHoldable item) => false;

        public bool TryGiveHeld(out IHoldable item)
        {
            item = null;
            return false;
        }
    }
}
