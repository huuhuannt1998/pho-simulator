using Pho.Core;
using Pho.Domain.Contracts;
using Pho.Domain.Inventory;
using Pho.Save.Participation;
using UnityEngine;

namespace Pho.Core.Inventory
{
    /// <summary>
    /// Owns the ONE shared InventoryModel for the whole restaurant and
    /// registers it directly into GameContext -- not wrapped in an
    /// interface, since InventoryModel is already the concrete pure-C# type
    /// every Kitchen station's Bind(InventoryModel, ...) expects (see
    /// IngredientStation.Bind / BrothPot.Bind).
    ///
    /// STARTING STOCK (judgment call, explicitly scoped by the brief): there
    /// is no shopping/purchasing UI in this pass -- the GDD's "buy
    /// ingredients" flow is out of scope here. On Install, every ingredient
    /// in the bound IGameDatabase is seeded with <see cref="StartingUnitsPerIngredient"/>
    /// units at its base quality, dated "day 1", stored per its own
    /// StorageRequirement. That's a generous vertical-slice stand-in --
    /// enough for several bowls of every recipe -- not a real economy
    /// simulation. A later wave that builds real purchasing can either seed
    /// less (or nothing) here, or leave this seeding in place as a
    /// "restaurant already had opening stock" narrative beat; either way is
    /// a one-constant change, not a redesign.
    ///
    /// If no IGameDatabase is registered yet (a boot-capable scene without
    /// content, per GameBootstrap's own optionality note), InventoryService
    /// still registers an empty InventoryModel rather than skipping
    /// registration entirely -- Kitchen stations' Bind(...) calls expect a
    /// non-null model to exist in GameContext once installers have run.
    /// </summary>
    [AutoInstall]
    public sealed class InventoryService : IInstaller
    {
        /// <summary>
        /// Generous per-ingredient starting stock -- see class doc comment.
        /// Public/const so a later balance pass can retune without touching
        /// logic, and so tests/tools can reference the same number this
        /// service actually seeds with.
        /// </summary>
        public const float StartingUnitsPerIngredient = 50f;

        /// <summary>The game day starting stock is dated as having been purchased on.</summary>
        public const int StartingStockDay = 1;

        public int Order => InstallOrder.Inventory;

        public void Install(GameContext ctx)
        {
            var model = new InventoryModel();

            if (ctx.TryGet<IGameDatabase>(out var database) && database?.Ingredients != null)
            {
                SeedStartingStock(model, database);
            }
            else
            {
                Debug.LogWarning("[InventoryService] No IGameDatabase registered yet -- registering an empty InventoryModel. Kitchen stations will report 'out of stock' for everything until content is wired in this scene.");
            }

            ctx.Register<InventoryModel>(model);

            if (ctx.TryGet<SaveParticipantRegistry>(out var saveRegistry))
            {
                saveRegistry.Register(new InventoryModelSaveParticipant(model));
            }
        }

        static void SeedStartingStock(InventoryModel model, IGameDatabase database)
        {
            var ingredients = database.Ingredients;
            for (int i = 0; i < ingredients.Count; i++)
            {
                var def = ingredients[i];
                if (def == null) continue;

                model.Add(def.Id, StartingUnitsPerIngredient, def.BaseQuality01, StartingStockDay, def.Storage);
            }
        }

        /// <summary>
        /// Total purchase-cost value of the starting stock this service
        /// would seed for the given database -- a query hook for a future
        /// Ledger's "opening inventory value" line item (per the brief's
        /// optional ask). Deliberately just a sum, not a shopping system:
        /// no purchasing flow exists to spend against this number yet.
        /// </summary>
        public static decimal StartingStockValue(IGameDatabase database)
        {
            if (database?.Ingredients == null) return 0m;

            decimal total = 0m;
            var ingredients = database.Ingredients;
            for (int i = 0; i < ingredients.Count; i++)
            {
                var def = ingredients[i];
                if (def == null) continue;

                total += (decimal)def.BasePurchasePrice * (decimal)StartingUnitsPerIngredient;
            }
            return total;
        }
    }
}
