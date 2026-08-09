using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using Pho.Save.Dto;
using Pho.Save.Io;
using Pho.Save.Migration;
using Pho.Save.Participation;
using Pho.Save.Tests.Fakes;

namespace Pho.Save.Tests
{
    // Plain NUnit only, constraint-based Assert.That(...) -- matches
    // Pho.Domain.Tests/EventBusTests.cs style so this file compiles under
    // both `dotnet test` and the Unity EditMode runner.
    [TestFixture]
    public class SaveRoundTripTests
    {
        static SaveFile BuildRealisticSaveFile() => new SaveFile
        {
            schemaVersion = 1,
            gameVersion = "0.1.0",
            savedAtUnixSeconds = 1770500000L,
            world = new WorldDto { day = 5, timeOfDaySeconds = 32400.5f, phase = "Open" },
            economy = new EconomyDto
            {
                cash = 512.75m,
                recentDays = new List<DailyReportDto>
                {
                    new DailyReportDto { day = 4, revenue = 300.00m, ingredientCost = 80.00m, rent = 50.00m, utilities = 12.50m, profit = 157.50m },
                    new DailyReportDto { day = 5, revenue = 275.25m, ingredientCost = 70.10m, rent = 0m, utilities = 12.50m, profit = 192.65m }
                }
            },
            inventory = new InventoryDto
            {
                lots = new List<LotDto>
                {
                    new LotDto { ingredientId = "ing.beef_brisket", quantity = 6.5f, qualityAtPurchase = 0.9f, freshness = 0.7f, purchasedOnDay = 4, storage = "Refrigerated" },
                    new LotDto { ingredientId = "ing.rice_noodle", quantity = 15f, qualityAtPurchase = 0.85f, freshness = 0.99f, purchasedOnDay = 5, storage = "Ambient" }
                }
            },
            progression = new ProgressionDto
            {
                unlockedRecipeIds = new List<string> { "rec.pho_tai", "rec.pho_chin" },
                ownedEquipmentIds = new List<string> { "eq.burner_commercial" },
                menuPrices = new Dictionary<string, decimal> { ["rec.pho_tai"] = 8.5m, ["rec.pho_chin"] = 8.0m },
                reputation = 0.72f,
                flags = new Dictionary<string, bool> { ["tutorial.completed"] = true, ["seenUpgradeHint"] = false }
            },
            restaurant = new RestaurantDto
            {
                cleanliness01 = 0.85f,
                dirtyTableIds = new List<string> { "table.01", "table.03" }
            },
            player = new PlayerDto
            {
                position = new Vec3Dto { x = 2.5f, y = 0f, z = -1.25f },
                yaw = 180f
            }
        };

        static void AssertSaveFilesEqual(SaveFile expected, SaveFile actual)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual.schemaVersion, Is.EqualTo(expected.schemaVersion));
            Assert.That(actual.gameVersion, Is.EqualTo(expected.gameVersion));
            Assert.That(actual.savedAtUnixSeconds, Is.EqualTo(expected.savedAtUnixSeconds));

            Assert.That(actual.world.day, Is.EqualTo(expected.world.day));
            Assert.That(actual.world.timeOfDaySeconds, Is.EqualTo(expected.world.timeOfDaySeconds));
            Assert.That(actual.world.phase, Is.EqualTo(expected.world.phase));

            Assert.That(actual.economy.cash, Is.EqualTo(expected.economy.cash));
            Assert.That(actual.economy.recentDays.Count, Is.EqualTo(expected.economy.recentDays.Count));
            for (var i = 0; i < expected.economy.recentDays.Count; i++)
            {
                var e = expected.economy.recentDays[i];
                var a = actual.economy.recentDays[i];
                Assert.That(a.day, Is.EqualTo(e.day));
                Assert.That(a.revenue, Is.EqualTo(e.revenue));
                Assert.That(a.ingredientCost, Is.EqualTo(e.ingredientCost));
                Assert.That(a.rent, Is.EqualTo(e.rent));
                Assert.That(a.utilities, Is.EqualTo(e.utilities));
                Assert.That(a.profit, Is.EqualTo(e.profit));
            }

            Assert.That(actual.inventory.lots.Count, Is.EqualTo(expected.inventory.lots.Count));
            for (var i = 0; i < expected.inventory.lots.Count; i++)
            {
                var e = expected.inventory.lots[i];
                var a = actual.inventory.lots[i];
                Assert.That(a.ingredientId, Is.EqualTo(e.ingredientId));
                Assert.That(a.quantity, Is.EqualTo(e.quantity));
                Assert.That(a.qualityAtPurchase, Is.EqualTo(e.qualityAtPurchase));
                Assert.That(a.freshness, Is.EqualTo(e.freshness));
                Assert.That(a.purchasedOnDay, Is.EqualTo(e.purchasedOnDay));
                Assert.That(a.storage, Is.EqualTo(e.storage));
            }

            Assert.That(actual.progression.unlockedRecipeIds, Is.EqualTo(expected.progression.unlockedRecipeIds));
            Assert.That(actual.progression.ownedEquipmentIds, Is.EqualTo(expected.progression.ownedEquipmentIds));
            Assert.That(actual.progression.menuPrices, Is.EqualTo(expected.progression.menuPrices));
            Assert.That(actual.progression.reputation, Is.EqualTo(expected.progression.reputation));
            Assert.That(actual.progression.flags, Is.EqualTo(expected.progression.flags));

            Assert.That(actual.restaurant.cleanliness01, Is.EqualTo(expected.restaurant.cleanliness01));
            Assert.That(actual.restaurant.dirtyTableIds, Is.EqualTo(expected.restaurant.dirtyTableIds));

            Assert.That(actual.player.position.x, Is.EqualTo(expected.player.position.x));
            Assert.That(actual.player.position.y, Is.EqualTo(expected.player.position.y));
            Assert.That(actual.player.position.z, Is.EqualTo(expected.player.position.z));
            Assert.That(actual.player.yaw, Is.EqualTo(expected.player.yaw));
        }

        [Test]
        public void SerializeThenDeserialize_EveryFieldSurvives_WithNonEmptyCollections()
        {
            var original = BuildRealisticSaveFile();

            var json = JsonConvert.SerializeObject(original);
            var restored = JsonConvert.DeserializeObject<SaveFile>(json);

            AssertSaveFilesEqual(original, restored);
        }

        [Test]
        public void SerializeThenDeserialize_MoneyFieldsStayExactDecimal_NoFloatDrift()
        {
            var original = BuildRealisticSaveFile();
            original.economy.cash = 9.499999999999999m; // classic float-drift trap the doc calls out

            var json = JsonConvert.SerializeObject(original);
            var restored = JsonConvert.DeserializeObject<SaveFile>(json);

            Assert.That(restored.economy.cash, Is.EqualTo(9.499999999999999m));
        }

        [Test]
        public void EmptyCollections_RoundTripAsEmpty_NotNull()
        {
            var original = new SaveFile
            {
                schemaVersion = 1,
                gameVersion = "0.1.0",
                savedAtUnixSeconds = 0,
                world = new WorldDto(),
                economy = new EconomyDto { recentDays = new List<DailyReportDto>() },
                inventory = new InventoryDto { lots = new List<LotDto>() },
                progression = new ProgressionDto
                {
                    unlockedRecipeIds = new List<string>(),
                    ownedEquipmentIds = new List<string>(),
                    menuPrices = new Dictionary<string, decimal>(),
                    flags = new Dictionary<string, bool>()
                },
                restaurant = new RestaurantDto { dirtyTableIds = new List<string>() },
                player = new PlayerDto { position = new Vec3Dto() }
            };

            var json = JsonConvert.SerializeObject(original);
            var restored = JsonConvert.DeserializeObject<SaveFile>(json);

            Assert.That(restored.economy.recentDays, Is.Empty);
            Assert.That(restored.inventory.lots, Is.Empty);
            Assert.That(restored.progression.unlockedRecipeIds, Is.Empty);
            Assert.That(restored.progression.menuPrices, Is.Empty);
            Assert.That(restored.restaurant.dirtyTableIds, Is.Empty);
        }

        [Test]
        public void SaveCoordinator_CaptureSerializeDeserializeRestore_RoundTripsThroughParticipants()
        {
            var store = new InMemorySaveStore();
            var migrator = new SaveMigrator(System.Array.Empty<ISaveMigration>());
            var callLog = new List<string>();

            var inventoryParticipant = new FakeSaveParticipant(
                "inventory",
                restoreOrder: 0,
                callLog: callLog,
                onCapture: save => save.inventory.lots.Add(new LotDto
                {
                    ingredientId = "ing.beef_brisket",
                    quantity = 4f,
                    qualityAtPurchase = 0.8f,
                    freshness = 0.6f,
                    purchasedOnDay = 2,
                    storage = "Refrigerated"
                }),
                onRestore: (save, db) => Assert.That(save.inventory.lots, Has.Count.EqualTo(1)));

            var economyParticipant = new FakeSaveParticipant(
                "economy",
                restoreOrder: 10,
                callLog: callLog,
                onCapture: save => save.economy.cash = 321.55m,
                onRestore: (save, db) => Assert.That(save.economy.cash, Is.EqualTo(321.55m)));

            var coordinator = new SaveCoordinator(
                store,
                migrator,
                new ISaveParticipant[] { economyParticipant, inventoryParticipant }, // deliberately out of order
                eventBus: null,
                gameVersion: "0.9.9");

            var written = coordinator.Save("slot0", savedAtUnixSeconds: 1700000000L);
            Assert.That(written.economy.cash, Is.EqualTo(321.55m));
            Assert.That(written.inventory.lots, Has.Count.EqualTo(1));

            var loaded = coordinator.TryLoad("slot0", new FakeGameDatabase(), out var restored);

            Assert.That(loaded, Is.True);
            Assert.That(restored.gameVersion, Is.EqualTo("0.9.9"));
            Assert.That(restored.economy.cash, Is.EqualTo(321.55m));
            Assert.That(restored.inventory.lots[0].ingredientId, Is.EqualTo("ing.beef_brisket"));

            // SaveCoordinator sorts all participants by RestoreOrder once
            // (inventory=0 before economy=10) and uses that same order for
            // both Capture and Restore.
            Assert.That(callLog, Is.EqualTo(new[] { "capture:inventory", "capture:economy", "restore:inventory", "restore:economy" }));
        }
    }
}
