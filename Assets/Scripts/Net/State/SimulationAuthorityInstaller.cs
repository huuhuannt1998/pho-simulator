using Pho.Core;
using Pho.Domain.Multiplayer;

namespace Pho.Net.State
{
    /// <summary>
    /// Registers the "am I the authority?" answer into <see cref="GameContext"/>
    /// before any simulation service asks the question.
    ///
    /// <b>Ordering is the whole point of this class.</b> Every guarded
    /// service resolves <see cref="ISimulationAuthority"/> during its own
    /// Install, and a missing implementation deliberately means "yes, I am
    /// the authority" (see that interface's doc comment -- failing closed
    /// would break single-player). So installing this LATE would be worse
    /// than not installing it at all: every client would sail past its
    /// guards, start its own simulation, and only then learn it was a
    /// replica. Slot 50 puts it below Save (100), which is the lowest
    /// existing entry in InstallOrder.
    ///
    /// InstallOrder.cs is deliberately not edited -- a local constant with a
    /// documented justification is the precedent set by
    /// OrderServiceInstaller and followed by ProgressionService,
    /// CleanlinessService and ExpansionService.
    /// </summary>
    [AutoInstall]
    public sealed class SimulationAuthorityInstaller : IInstaller
    {
        /// <summary>Below InstallOrder.Save (100) -- see the class doc comment for why this must run first.</summary>
        public const int InstallSlot = 50;

        public int Order => InstallSlot;

        public void Install(GameContext ctx)
        {
            ctx.Register<ISimulationAuthority>(new SimulationAuthorityFlag());
        }
    }
}
