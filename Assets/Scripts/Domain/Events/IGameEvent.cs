namespace Pho.Domain.Events
{
    /// <summary>
    /// Marker for domain events. Events notify that something already
    /// happened (past-tense names). Commands go through service interfaces,
    /// not the bus -- mixing the two is how event buses turn into spaghetti.
    /// </summary>
    public interface IGameEvent
    {
    }
}
