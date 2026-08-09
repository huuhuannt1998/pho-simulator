using Pho.Domain.Contracts;
using Pho.Domain.Multiplayer;

namespace Pho.Core.Orders
{
    /// <summary>
    /// Self-registers <see cref="OrderService"/> into <see cref="GameContext"/>
    /// via the reflection-discovered <c>[AutoInstall]</c> pattern
    /// (architecture.md section 10 -- "adding an installer = zero scene
    /// edits, zero shared-file edits"; see <c>GameBootstrap.DiscoverInstallers</c>).
    /// </summary>
    [AutoInstall]
    public sealed class OrderServiceInstaller : IInstaller
    {
        /// <summary>
        /// Not added to the shared, append-only <c>Core/InstallOrder.cs</c>
        /// (architecture.md section 10's "accepted single shared file"):
        /// this pass's authorized write set is scoped to
        /// <c>Assets/Scripts/Core/Orders/**</c> only, so InstallOrder.cs is
        /// intentionally left untouched. This local constant is a
        /// self-contained equivalent, spaced between the existing
        /// <c>InstallOrder.Kitchen</c> (500) and <c>InstallOrder.Customers</c>
        /// (600) entries -- OrderService is the connective service between
        /// those two waves. A later integration pass may fold this into
        /// InstallOrder.cs's append-only list if it wants a single source of
        /// truth for ordering constants; functionally it doesn't matter
        /// here, since OrderService only needs to exist (subscribed to
        /// BowlAssembled) before gameplay starts publishing events, not
        /// before any other installer runs.
        /// </summary>
        public int Order => 550;

        public void Install(GameContext ctx)
        {
            // HOST ONLY. This is the highest-priority authority guard in the
            // project: OrderService mints ids from Guid.NewGuid(), so a
            // client running its own copy produces a completely disjoint set
            // of orders that can never be reconciled with the host's -- not
            // "slightly out of sync", but two separate restaurants.
            // A missing ISimulationAuthority means single-player, which must
            // keep working exactly as before, so this only bails when
            // something explicitly says "you are a replica".
            if (ctx.TryGet<ISimulationAuthority>(out var authority) && !authority.IsSimulationAuthority)
            {
                return;
            }

            // Optional dependency -- OrderService degrades gracefully
            // (quotes $0.00, logs a warning) if no GameDatabase is bound in
            // this scene yet. See OrderService's constructor doc comment.
            ctx.TryGet<IGameDatabase>(out var database);

            var service = new OrderService(ctx.Events, database);
            ctx.Register(service);
        }
    }
}
