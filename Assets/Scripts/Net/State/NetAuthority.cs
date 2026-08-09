using Unity.Netcode;

namespace Pho.Net.State
{
    /// <summary>
    /// The one question every simulation service in this project will
    /// eventually need to ask: <b>"am I the authority?"</b>
    ///
    /// Today every service in <c>Pho.Core</c> is built locally by
    /// <c>GameBootstrap</c> and simulates unconditionally. In single player
    /// that is correct. The moment a second player joins, it is a bug
    /// factory: four copies of <c>OrderService</c> each mint their own
    /// <c>OrderId</c>s from their own <c>Guid.NewGuid()</c>, four copies of
    /// <c>RestaurantStateServiceBehaviour</c> each tick their own
    /// <c>DayClock</c> off their own <c>Time.deltaTime</c>, four copies of
    /// <c>EconomyService</c> each credit their own till. Nothing crashes --
    /// the four restaurants simply, silently, stop being the same
    /// restaurant. <see cref="SharedStateReplicator"/> exists to push the
    /// host's version of the truth to everyone; this class exists so the
    /// other three copies can be told to stop generating a competing one.
    ///
    /// <b>Semantics.</b> <see cref="IsSimulationAuthority"/> is true when
    /// this process is allowed to RUN the restaurant simulation:
    /// <list type="bullet">
    /// <item>No <c>NetworkManager</c> in the scene at all -- a pure
    /// single-player / edit-mode / test run. True. This default matters: it
    /// is what keeps every existing single-player code path working
    /// unchanged once the guard is added.</item>
    /// <item>A <c>NetworkManager</c> exists but is not listening (nobody has
    /// hosted or joined yet -- the main menu). True, for the same
    /// reason.</item>
    /// <item>Listening as a host or dedicated server. True.</item>
    /// <item>Listening as a connected client. <b>False</b> -- this process
    /// must display the host's state, never compute its own.</item>
    /// </list>
    ///
    /// <b>How Pho.Core is supposed to consume this (it currently cannot).</b>
    /// <c>Pho.Net</c> references <c>Pho.Core</c>, so the reverse reference is
    /// impossible -- Unity refuses a circular asmdef reference, exactly as
    /// <c>GameBootstrap</c>'s doc comment already notes for
    /// <c>Pho.Player</c>. A Core-side service therefore cannot name this
    /// type. The intended seam is a tiny interface in <c>Pho.Domain</c>
    /// (which Core does reference), implemented here and handed to services
    /// through <c>GameContext</c>. See <see cref="SimulationAuthorityFlag"/>
    /// for the adapter that is already written and waiting for that
    /// interface, and the integration notes on
    /// <see cref="SharedStateReplicator"/> for the exact per-service edits.
    ///
    /// <b>Deliberately not cached.</b> Every call re-reads
    /// <c>NetworkManager.Singleton</c>. A cached answer would be wrong for
    /// the entire lifetime of a process that starts at a main menu (no
    /// session) and then hosts or joins one, which is the normal way this
    /// game will be launched. The read is a static field access plus two
    /// bools; the cost is not worth a staleness bug.
    /// </summary>
    public static class NetAuthority
    {
        /// <summary>
        /// True when this process should be running the restaurant
        /// simulation. See the class doc comment for the full truth table --
        /// in particular, the no-session case is deliberately TRUE so that
        /// adding this guard cannot break single player.
        /// </summary>
        public static bool IsSimulationAuthority
        {
            get
            {
                var manager = NetworkManager.Singleton;

                // No networking in this process at all (single player, an
                // EditMode test, a scene opened straight from the editor).
                if (manager == null) return true;

                // Networking exists but no session has begun. Whatever the
                // player is doing, they are doing it alone.
                if (!manager.IsListening) return true;

                return manager.IsServer;
            }
        }

        /// <summary>
        /// True only when this process is a connected, non-hosting client --
        /// i.e. the case where local simulation is actively harmful and
        /// replicated state is the only correct source. The exact negation of
        /// <see cref="IsSimulationAuthority"/>, named positively so guard
        /// sites read as intent ("if I'm a remote client, don't") rather than
        /// as a double negative.
        /// </summary>
        public static bool IsReplicaClient => !IsSimulationAuthority;
    }

    /// <summary>
    /// Instance-shaped adapter over <see cref="NetAuthority"/>, so the answer
    /// can be handed to <c>Pho.Core</c> services through
    /// <c>GameContext.Register</c>/<c>TryGet</c> instead of them reaching for
    /// a static in an assembly they are not allowed to reference.
    ///
    /// <b>This class is one word away from being useful and that word is not
    /// mine to write.</b> The integration owner should add to
    /// <c>Pho.Domain</c> (Core already references it):
    /// <code>
    /// // Assets/Scripts/Domain/Multiplayer/ISimulationAuthority.cs
    /// namespace Pho.Domain.Multiplayer
    /// {
    ///     public interface ISimulationAuthority { bool IsSimulationAuthority { get; } }
    /// }
    /// </code>
    /// then change this class's declaration to
    /// <c>: Pho.Domain.Multiplayer.ISimulationAuthority</c> (the member already
    /// matches the interface exactly) and register one instance into
    /// <c>GameContext</c> before the simulation installers run. It is left
    /// unimplemented here only because <c>Assets/Scripts/Domain/**</c> is
    /// outside this pass's authorized write set, not because there is
    /// anything to decide.
    /// </summary>
    public sealed class SimulationAuthorityFlag
    {
        /// <summary>See <see cref="NetAuthority.IsSimulationAuthority"/>.</summary>
        public bool IsSimulationAuthority => NetAuthority.IsSimulationAuthority;
    }
}
