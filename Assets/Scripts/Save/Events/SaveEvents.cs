using Pho.Domain.Events;

namespace Pho.Save.Events
{
    /// <summary>
    /// Published by SaveCoordinator when both the primary and backup save
    /// slots fail to load (corrupt, truncated, or an unmigratable/future
    /// schema version). This is a notification of a fact that already
    /// happened -- the load attempt failed and the caller is starting a new
    /// game -- so it follows the same IGameEvent / past-tense-name
    /// convention as every other domain event, even though it is declared
    /// in Pho.Save rather than Pho.Domain/Events (IGameEvent has no
    /// assembly restriction; grouping save-specific events with the save
    /// system keeps Pho.Domain free of any dependency on save concerns).
    /// </summary>
    public readonly struct SaveCorrupted : IGameEvent
    {
        public readonly string Slot;
        public readonly string Reason;

        public SaveCorrupted(string slot, string reason)
        {
            Slot = slot;
            Reason = reason;
        }
    }
}
