using System.Reflection;
using Pho.Core.Economy;
using Pho.Core.Orders;
using Pho.Core.Restaurant;
using Pho.Domain.Contracts;
using Pho.Domain.Multiplayer;
using UnityEngine;

namespace Pho.Core.Customers
{
    /// <summary>
    /// Binds the scene's CustomerSpawner to its TableRegistry +
    /// IBalanceConfig/IGameDatabase/OrderService/EconomyService.
    ///
    /// GAP THIS CLOSES: same class as KitchenBindInstaller's own doc
    /// comment describes -- SceneBuilder only AddComponents/wires Inspector
    /// fields, it never calls CustomerSpawner.Bind(...) (an explicitly
    /// reserved "injection seam" per that class's own doc comment). Without
    /// this installer, CustomerSpawner._bound stays false forever and
    /// Update() never spawns anyone -- found the same way as the Kitchen
    /// gap, by tracing real runtime wiring while building
    /// VerticalSliceGoldenPathTest.
    ///
    /// Reflection, not a typed reference (same reason as
    /// GameBootstrap.BindPlayer / KitchenBindInstaller): Pho.Customers.asmdef
    /// already references Pho.Core, so Pho.Core cannot reference
    /// Pho.Customers back. OrderService/EconomyService themselves ARE
    /// Pho.Core types, so they're passed through with a normal typed
    /// lookup -- only the CustomerSpawner/TableRegistry side needs
    /// reflection.
    ///
    /// Runs at InstallOrder.Customers (600) -- after OrderService (550) and
    /// Kitchen (500), exactly the slot InstallOrder.cs reserves for this.
    /// </summary>
    [AutoInstall]
    public sealed class CustomerBindInstaller : IInstaller
    {
        const string TableRegistryTypeName = "Pho.Customers.TableRegistry";
        const string CustomerSpawnerTypeName = "Pho.Customers.CustomerSpawner";
        const string BindMethodName = "Bind";

        public int Order => InstallOrder.Customers;

        public void Install(GameContext ctx)
        {
            object tableRegistry = null;
            MonoBehaviour spawner = null;

            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour == null) continue;

                var typeName = behaviour.GetType().FullName;
                if (typeName == TableRegistryTypeName) tableRegistry = behaviour;
                else if (typeName == CustomerSpawnerTypeName) spawner = behaviour;
            }

            if (spawner == null) return; // no spawner in this scene -- fine (e.g. a future menu scene).

            if (tableRegistry == null)
            {
                Debug.LogWarning("[CustomerBindInstaller] No TableRegistry found in the scene -- skipping CustomerSpawner binding.");
                return;
            }

            ctx.TryGet<IBalanceConfig>(out var balance);
            ctx.TryGet<IGameDatabase>(out var database);
            ctx.TryGet<OrderService>(out var orderService);
            ctx.TryGet<EconomyService>(out var economyService);
            ctx.TryGet<CleanlinessService>(out var cleanlinessService);

            var method = spawner.GetType().GetMethod(BindMethodName, BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
            {
                Debug.LogWarning("[CustomerBindInstaller] CustomerSpawner has no public Bind(...) method -- skipped.");
                return;
            }

            method.Invoke(spawner, new object[] { tableRegistry, balance, ctx.Events, database, null, orderService, economyService, cleanlinessService });

            // Replica clients must not spawn their own crowd -- the host's
            // customers are replicated. Set by reflection for the same
            // asmdef reason the bind above uses it.
            if (ctx.TryGet<ISimulationAuthority>(out var authority) && !authority.IsSimulationAuthority)
            {
                var prop = spawner.GetType().GetProperty("SimulateLocally", BindingFlags.Public | BindingFlags.Instance);
                prop?.SetValue(spawner, false);
            }
            Debug.Log("[CustomerBindInstaller] Bound CustomerSpawner.");
        }
    }
}
