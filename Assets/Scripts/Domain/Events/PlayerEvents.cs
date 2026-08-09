namespace Pho.Domain.Events
{
    /// <summary>
    /// Published by PlayerInteractor (Pho.Player) whenever the raycast
    /// target changes. The UI never raycasts -- it only ever reads this.
    /// Small M1 extension, added ahead of the Wave 1 fan-out because both
    /// the Player and UI agents need the exact same frozen shape at the
    /// same time; adding it unilaterally from either agent would mean
    /// guessing the other side's contract.
    /// </summary>
    public readonly struct InteractionTargetChanged : IGameEvent
    {
        public readonly bool HasTarget;
        public readonly string PromptText; // "Press E to fill pot"; empty when !HasTarget

        public InteractionTargetChanged(bool hasTarget, string promptText)
        {
            HasTarget = hasTarget;
            PromptText = promptText ?? string.Empty;
        }
    }
}
