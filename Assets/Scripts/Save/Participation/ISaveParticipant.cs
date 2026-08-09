using Pho.Domain.Contracts;
using Pho.Save.Dto;

namespace Pho.Save.Participation
{
    /// <summary>
    /// Implemented by each service that owns a slice of save data
    /// (InventoryService, EconomyService, ProgressionService,
    /// DayCycleService, ...). Each service self-registers via its own
    /// installer -- there is deliberately no shared save-wiring file, so
    /// agents building different services never touch the same file.
    /// </summary>
    public interface ISaveParticipant
    {
        void Capture(SaveFile save);
        void Restore(SaveFile save, IGameDatabase db);

        /// <summary>Restore ordering -- e.g. inventory before kitchen.</summary>
        int RestoreOrder { get; }
    }
}
