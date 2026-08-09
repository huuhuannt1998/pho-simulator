using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Pho.Save.Dto;
using Pho.Save.Migration;

namespace Pho.Save.Tests
{
    [TestFixture]
    public class SaveMigrationTests
    {
        // --- Golden fixture: Fixtures/v1_typical.json ---
        // No real migrations exist yet (schema version 1 is the first
        // version), so this exercises the no-op chain: schemaVersion 1 in,
        // MigrateToCurrent(root, 1) out, with every field intact.

        [Test]
        public void GoldenFixture_V1Typical_LoadsAtCurrentSchema_WithExpectedValues()
        {
            var path = TestPaths.FixturePath("v1_typical.json");
            Assert.That(File.Exists(path), Is.True, $"golden fixture not found at {path}");

            var root = JObject.Parse(File.ReadAllText(path));
            var migrator = new SaveMigrator(System.Array.Empty<ISaveMigration>());

            var migrated = migrator.MigrateToCurrent(root, SaveCoordinator.CurrentSchemaVersion);
            var save = migrated.ToObject<SaveFile>();

            Assert.That((int)migrated["schemaVersion"], Is.EqualTo(SaveCoordinator.CurrentSchemaVersion));

            Assert.That(save.schemaVersion, Is.EqualTo(1));
            Assert.That(save.gameVersion, Is.EqualTo("0.1.0"));
            Assert.That(save.savedAtUnixSeconds, Is.EqualTo(1770500000L));

            Assert.That(save.world.day, Is.EqualTo(3));
            Assert.That(save.world.phase, Is.EqualTo("Prep"));

            Assert.That(save.economy.cash, Is.EqualTo(482.50m));
            Assert.That(save.economy.recentDays, Has.Count.EqualTo(2));
            Assert.That(save.economy.recentDays[1].profit, Is.EqualTo(173.50m));

            Assert.That(save.inventory.lots, Has.Count.EqualTo(2));
            Assert.That(save.inventory.lots[0].ingredientId, Is.EqualTo("ing.beef_brisket"));
            Assert.That(save.inventory.lots[0].storage, Is.EqualTo("Refrigerated"));

            Assert.That(save.progression.unlockedRecipeIds, Is.EqualTo(new[] { "rec.pho_tai", "rec.pho_chin" }));
            Assert.That(save.progression.menuPrices["rec.pho_tai"], Is.EqualTo(8.50m));
            Assert.That(save.progression.flags["tutorial.completed"], Is.True);
            Assert.That(save.progression.flags["seenMenuBoard"], Is.False);
            Assert.That(save.progression.reputation, Is.EqualTo(0.63f));

            Assert.That(save.restaurant.cleanliness01, Is.EqualTo(0.9f));
            Assert.That(save.restaurant.dirtyTableIds, Is.EqualTo(new[] { "table.02" }));

            Assert.That(save.player.position.x, Is.EqualTo(1.5f));
            Assert.That(save.player.position.z, Is.EqualTo(-3.25f));
            Assert.That(save.player.yaw, Is.EqualTo(90f));
        }

        // --- Synthetic chain mechanism proof ---
        // These migrations exist only in this test file -- there is nothing
        // real to migrate from yet -- and exist purely to prove
        // SaveMigrator applies a multi-step chain in order and stamps the
        // final schemaVersion.

        sealed class V1ToV2Migration : ISaveMigration
        {
            public int FromVersion => 1;

            public void Migrate(JObject root)
            {
                // Simulate a realistic migration shape: rename a legacy
                // top-level field into its new nested home.
                var legacyCash = root["legacyCash"];
                if (legacyCash != null)
                {
                    var economy = root["economy"] as JObject ?? new JObject();
                    economy["cash"] = legacyCash;
                    root["economy"] = economy;
                    root.Remove("legacyCash");
                }

                root["migratedThroughV2"] = true;
            }
        }

        sealed class V2ToV3Migration : ISaveMigration
        {
            public int FromVersion => 2;

            public void Migrate(JObject root)
            {
                root["migratedThroughV3"] = true;
            }
        }

        [Test]
        public void MigrateToCurrent_AppliesMultiStepChainInOrder_AndSetsFinalSchemaVersion()
        {
            var root = new JObject
            {
                ["schemaVersion"] = 1,
                ["legacyCash"] = 100.25m
            };

            var migrator = new SaveMigrator(new ISaveMigration[]
            {
                new V2ToV3Migration(), // registered out of order on purpose
                new V1ToV2Migration()
            });

            var result = migrator.MigrateToCurrent(root, 3);

            Assert.That((int)result["schemaVersion"], Is.EqualTo(3));
            Assert.That((bool)result["migratedThroughV2"], Is.True);
            Assert.That((bool)result["migratedThroughV3"], Is.True);
            Assert.That(result["legacyCash"], Is.Null);
            Assert.That((decimal)result["economy"]["cash"], Is.EqualTo(100.25m));
        }

        [Test]
        public void MigrateToCurrent_AlreadyAtCurrentVersion_IsNoOp()
        {
            var root = new JObject
            {
                ["schemaVersion"] = 1,
                ["world"] = new JObject { ["day"] = 7 }
            };

            var migrator = new SaveMigrator(System.Array.Empty<ISaveMigration>());
            var result = migrator.MigrateToCurrent(root, 1);

            Assert.That((int)result["schemaVersion"], Is.EqualTo(1));
            Assert.That((int)result["world"]["day"], Is.EqualTo(7));
        }

        [Test]
        public void MigrateToCurrent_MissingSchemaVersion_TreatedAsVersionZero()
        {
            // Documented decision (see SaveMigrator class doc): a missing
            // schemaVersion is treated as version 0, not silently accepted
            // as "already current". With no migration registered for
            // FromVersion 0, this fails cleanly rather than guessing.
            var root = new JObject { ["world"] = new JObject { ["day"] = 1 } };
            var migrator = new SaveMigrator(System.Array.Empty<ISaveMigration>());

            Assert.That(() => migrator.MigrateToCurrent(root, 1), Throws.TypeOf<SaveMigrationMissingException>());
        }

        [Test]
        public void MigrateToCurrent_FutureSchemaVersion_ThrowsRatherThanMisMigrating()
        {
            var root = new JObject { ["schemaVersion"] = 99 };
            var migrator = new SaveMigrator(System.Array.Empty<ISaveMigration>());

            Assert.That(() => migrator.MigrateToCurrent(root, 1), Throws.TypeOf<SaveVersionTooNewException>());
        }

        [Test]
        public void MigrateToCurrent_NoMigrationForIntermediateVersion_ThrowsMissingMigration()
        {
            var root = new JObject { ["schemaVersion"] = 1 };
            // Chain needs 1->2 and 2->3; only 1->2 is registered.
            var migrator = new SaveMigrator(new List<ISaveMigration> { new V1ToV2Migration() });

            Assert.That(() => migrator.MigrateToCurrent(root, 3), Throws.TypeOf<SaveMigrationMissingException>());
        }
    }
}
