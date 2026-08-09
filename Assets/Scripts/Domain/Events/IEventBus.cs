using System;

namespace Pho.Domain.Events
{
    /// <summary>
    /// Typed instance event bus. Chosen over static events (leak across
    /// PlayMode domain reloads, untestable in isolation) and ScriptableObject
    /// event channels (one .asset per event -- MCP round-trip / merge-conflict
    /// prone, untestable in a pure assembly). Zero assets, compile-time
    /// checked, lives in Pho.Domain so it runs under `dotnet test`.
    /// </summary>
    public interface IEventBus
    {
        void Publish<T>(in T evt) where T : struct, IGameEvent;
        IDisposable Subscribe<T>(Action<T> handler) where T : struct, IGameEvent;
    }
}
