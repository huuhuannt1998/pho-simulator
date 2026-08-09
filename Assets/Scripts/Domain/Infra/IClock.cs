namespace Pho.Domain.Infra
{
    /// <summary>
    /// Game-time abstraction. Never read real wall-clock time from domain
    /// logic -- inject this instead, so tests fast-forward through a day
    /// instead of waiting on it. `Pho.Core` wires a real implementation
    /// driven by scaled `Time.deltaTime`; tests use a manually-advanced fake.
    /// </summary>
    public interface IClock
    {
        float NowSeconds { get; }
    }
}
