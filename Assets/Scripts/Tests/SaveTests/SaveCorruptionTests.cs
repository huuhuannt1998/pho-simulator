using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using Pho.Domain.Events;
using Pho.Save.Dto;
using Pho.Save.Events;
using Pho.Save.Io;
using Pho.Save.Migration;
using Pho.Save.Participation;
using Pho.Save.Tests.Fakes;

namespace Pho.Save.Tests
{
    [TestFixture]
    public class SaveCorruptionTests
    {
        const string Slot = "slot0";

        static string ValidJson(int day = 1, decimal cash = 100m) => JsonConvert.SerializeObject(new SaveFile
        {
            schemaVersion = 1,
            gameVersion = "0.1.0",
            savedAtUnixSeconds = 1L,
            world = new WorldDto { day = day, timeOfDaySeconds = 0f, phase = "Prep" },
            economy = new EconomyDto { cash = cash, recentDays = new List<DailyReportDto>() },
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
        });

        static SaveCoordinator NewCoordinator(InMemorySaveStore store, IEventBus bus = null) =>
            new SaveCoordinator(store, new SaveMigrator(System.Array.Empty<ISaveMigration>()), System.Array.Empty<ISaveParticipant>(), bus);

        [Test]
        public void TruncatedPrimaryJson_FallsBackToValidBackup()
        {
            var store = new InMemorySaveStore();
            store.SetRaw(Slot, "{ \"schemaVersion\": 1, \"world\": { \"day\": 9,"); // truncated / invalid JSON
            store.SetRawBackup(Slot, ValidJson(day: 4, cash: 55m));

            var bus = new EventBus();
            var corruptions = new List<SaveCorrupted>();
            bus.Subscribe<SaveCorrupted>(corruptions.Add);

            var coordinator = NewCoordinator(store, bus);

            var ok = coordinator.TryLoad(Slot, new FakeGameDatabase(), out var save);

            Assert.That(ok, Is.True);
            Assert.That(save.world.day, Is.EqualTo(4));
            Assert.That(save.economy.cash, Is.EqualTo(55m));
            Assert.That(corruptions, Is.Empty, "falling back to a good backup is a successful load, not a total-corruption event");
        }

        [Test]
        public void MissingSchemaVersion_PrimaryOnly_RejectedGracefully_NoThrow()
        {
            var store = new InMemorySaveStore();
            store.SetRaw(Slot, "{ \"world\": { \"day\": 2 } }"); // no schemaVersion field, no backup

            var bus = new EventBus();
            var corruptions = new List<SaveCorrupted>();
            bus.Subscribe<SaveCorrupted>(corruptions.Add);

            var coordinator = NewCoordinator(store, bus);

            bool ok = false;
            SaveFile save = null;
            Assert.That(() => ok = coordinator.TryLoad(Slot, new FakeGameDatabase(), out save), Throws.Nothing);

            Assert.That(ok, Is.False);
            Assert.That(save, Is.Null);
            Assert.That(corruptions, Has.Count.EqualTo(1));
            Assert.That(corruptions[0].Slot, Is.EqualTo(Slot));
        }

        [Test]
        public void MissingSchemaVersion_WithGoodBackup_FallsBackSuccessfully()
        {
            var store = new InMemorySaveStore();
            store.SetRaw(Slot, "{ \"world\": { \"day\": 2 } }"); // no schemaVersion
            store.SetRawBackup(Slot, ValidJson(day: 7, cash: 20m));

            var coordinator = NewCoordinator(store);
            var ok = coordinator.TryLoad(Slot, new FakeGameDatabase(), out var save);

            Assert.That(ok, Is.True);
            Assert.That(save.world.day, Is.EqualTo(7));
        }

        [Test]
        public void FutureSchemaVersion_NoGoodBackup_RejectedGracefully_NoThrow()
        {
            var store = new InMemorySaveStore();
            store.SetRaw(Slot, "{ \"schemaVersion\": 99, \"world\": { \"day\": 1 } }");

            var bus = new EventBus();
            var corruptions = new List<SaveCorrupted>();
            bus.Subscribe<SaveCorrupted>(corruptions.Add);

            var coordinator = NewCoordinator(store, bus);

            bool ok = false;
            SaveFile save = null;
            Assert.That(() => ok = coordinator.TryLoad(Slot, new FakeGameDatabase(), out save), Throws.Nothing);

            Assert.That(ok, Is.False);
            Assert.That(save, Is.Null);
            Assert.That(corruptions, Has.Count.EqualTo(1));
        }

        [Test]
        public void FutureSchemaVersion_WithGoodBackup_FallsBackSuccessfully_NotMisMigrated()
        {
            var store = new InMemorySaveStore();
            store.SetRaw(Slot, "{ \"schemaVersion\": 99, \"world\": { \"day\": 1 } }");
            store.SetRawBackup(Slot, ValidJson(day: 3, cash: 42m));

            var coordinator = NewCoordinator(store);
            var ok = coordinator.TryLoad(Slot, new FakeGameDatabase(), out var save);

            Assert.That(ok, Is.True);
            Assert.That(save.world.day, Is.EqualTo(3));
            Assert.That(save.economy.cash, Is.EqualTo(42m));
        }

        [Test]
        public void PrimaryAndBackupBothMissing_ReturnsNoValidSave_WithoutThrowing()
        {
            var store = new InMemorySaveStore(); // nothing written at all

            var bus = new EventBus();
            var corruptions = new List<SaveCorrupted>();
            bus.Subscribe<SaveCorrupted>(corruptions.Add);

            var coordinator = NewCoordinator(store, bus);

            bool ok = false;
            SaveFile save = null;
            Assert.That(() => ok = coordinator.TryLoad(Slot, new FakeGameDatabase(), out save), Throws.Nothing);

            Assert.That(ok, Is.False);
            Assert.That(save, Is.Null);
            Assert.That(corruptions, Has.Count.EqualTo(1));
        }

        [Test]
        public void PrimaryAndBackupBothCorrupt_ReturnsNoValidSave_WithoutThrowing()
        {
            var store = new InMemorySaveStore();
            store.SetRaw(Slot, "not json at all");
            store.SetRawBackup(Slot, "{ also not json");

            var bus = new EventBus();
            var corruptions = new List<SaveCorrupted>();
            bus.Subscribe<SaveCorrupted>(corruptions.Add);

            var coordinator = NewCoordinator(store, bus);

            bool ok = false;
            SaveFile save = null;
            Assert.That(() => ok = coordinator.TryLoad(Slot, new FakeGameDatabase(), out save), Throws.Nothing);

            Assert.That(ok, Is.False);
            Assert.That(save, Is.Null);
            Assert.That(corruptions, Has.Count.EqualTo(1));
        }

        [Test]
        public void TryLoad_WithNoEventBus_DoesNotThrow_OnTotalFailure()
        {
            var store = new InMemorySaveStore(); // empty
            var coordinator = NewCoordinator(store, bus: null);

            bool ok = false;
            SaveFile save = null;
            Assert.That(() => ok = coordinator.TryLoad(Slot, new FakeGameDatabase(), out save), Throws.Nothing);

            Assert.That(ok, Is.False);
            Assert.That(save, Is.Null);
        }
    }
}
