namespace Pho.Domain.Multiplayer
{
    /// <summary>
    /// "Am I the machine that actually runs the restaurant?"
    ///
    /// In co-op exactly one peer simulates: it mints order ids, ticks the
    /// day clock, spawns customers and moves money. Everyone else renders a
    /// replica of that. Without a way to ask this question, every client
    /// runs its own copy of the simulation and the four restaurants drift
    /// apart within seconds -- four different clocks, four different crowds,
    /// and order ids (minted from <c>Guid.NewGuid()</c>) that can never be
    /// reconciled because no two peers ever generate the same one.
    ///
    /// <b>Why this interface lives in Pho.Domain rather than Pho.Net:</b>
    /// the services that need to ask are in <c>Pho.Core</c>, and
    /// <c>Pho.Net</c> already references <c>Pho.Core</c> -- so Core cannot
    /// reference Net back without a circular asmdef, which Unity rejects
    /// outright. Both assemblies already depend on <c>Pho.Domain</c>, so
    /// putting the question here lets Core ask it and Net answer it.
    ///
    /// <b>Default when nothing registers one:</b> callers must treat a
    /// missing implementation as "yes, I am the authority". A single-player
    /// game has no networking and must keep simulating exactly as it always
    /// did; a guard that failed closed would silently turn the whole game
    /// off. Hence the established call shape:
    /// <code>
    /// if (ctx.TryGet&lt;ISimulationAuthority&gt;(out var a) &amp;&amp; !a.IsSimulationAuthority) return;
    /// </code>
    /// which only bails when something has explicitly said "you are a
    /// replica".
    /// </summary>
    public interface ISimulationAuthority
    {
        bool IsSimulationAuthority { get; }
    }
}
