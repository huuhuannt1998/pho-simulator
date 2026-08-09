namespace Pho.Core.Interaction
{
    /// <summary>Counters, stations, tables, sinks -- anything an IHoldable can be placed into/onto by name rather than by free placement.</summary>
    public interface IItemReceiver
    {
        bool CanAccept(IHoldable item);
        bool TryAccept(IHoldable item);
    }
}
