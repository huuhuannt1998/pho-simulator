namespace Pho.Core.Interaction
{
    /// <summary>
    /// Anything the player (or, later, an employee) can aim at and act on --
    /// stations, doors, crates, the register, etc. Implementations live in
    /// the owning system's assembly (Pho.Kitchen, Pho.Customers, ...); this
    /// interface is the only thing Pho.Player needs to know about them.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>UI prompt text, e.g. "Press E to fill pot". PlayerInteractor calls this on acquisition; the UI never raycasts.</summary>
        string GetInteractionText(in InteractionContext ctx);

        bool CanInteract(in InteractionContext ctx);

        void Interact(in InteractionContext ctx);
    }
}
