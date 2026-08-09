using Pho.Domain.Cooking;
using Pho.Domain.Events;
using Pho.Domain.Identity;

namespace Pho.Domain.Progression
{
    // Namespace note: every other event file lives in Pho.Domain.Events
    // (Domain/Events/*.cs). This one deliberately does not -- architecture.md
    // section 10's rule 4 says "new event structs go in the OWNING agent's
    // events file, never a shared file", and this pass's write set is scoped
    // to Domain/Progression/**, so editing Domain/Events/ is off-limits.
    // Folder and namespace are kept consistent with each other rather than
    // declaring Pho.Domain.Events from a Progression folder; subscribers add
    // `using Pho.Domain.Progression;` alongside their existing
    // `using Pho.Domain.Events;`.

    /// <summary>
    /// Past-tense notification that the restaurant now owns a new piece of
    /// equipment and money has already left the till (doc section 3: "events
    /// are for notification of facts that already happened"; the command
    /// itself is ProgressionService.TryPurchase, not a bus message).
    ///
    /// <see cref="ModifiersAfter"/> is carried on the event so a subscriber
    /// can apply the new modifiers without having to resolve
    /// ProgressionService itself -- that makes an event-driven BrothPot
    /// integration possible with no service lookup at all.
    /// </summary>
    public readonly struct EquipmentPurchased : IGameEvent
    {
        public readonly EquipmentId Equipment;
        public readonly decimal Price;
        public readonly EquipmentModifiers ModifiersAfter;

        public EquipmentPurchased(EquipmentId equipment, decimal price, EquipmentModifiers modifiersAfter)
        {
            Equipment = equipment;
            Price = price;
            ModifiersAfter = modifiersAfter;
        }
    }
}
