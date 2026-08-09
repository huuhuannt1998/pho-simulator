using System;
using System.Collections.Generic;
using NUnit.Framework;
using Pho.Domain.Contracts;
using Pho.Domain.Cooking;
using Pho.Domain.Identity;
using Pho.Domain.Progression;
using Pho.Domain.Tests.Fakes;

namespace Pho.Domain.Tests
{
    // Plain NUnit only (no UnityEngine.TestTools / [UnityTest]) -- this file
    // is compiled both by Unity's EditMode test runner and by
    // Tools/PhoDomain.Tests.csproj via `dotnet test`. Constraint-based
    // Assert.That(...) throughout, matching InventoryModelTests.cs style.
    [TestFixture]
    public class ProgressionTests
    {
        static readonly EquipmentId CommercialBurner = new EquipmentId("eq.burner_commercial");
        static readonly EquipmentId StockBurner = new EquipmentId("eq.burner_stock");
        static readonly EquipmentId BigPot = new EquipmentId("eq.pot_large");

        /// <summary>
        /// Mirrors the real authored content -- see ContentManifest's
        /// eq.burner_commercial entry, which carries the full rationale for
        /// why this is 1.7 and not the 1.5 named illustratively in
        /// architecture.md section 7 (1.5 measures at 29.8%, just under
        /// M13's >= 30% acceptance criterion).
        /// </summary>
        const float CommercialBurnerHeatRate = 1.7f;

        const float DtStep = 0.25f;

        static FakeGameDatabase DatabaseWith(params IEquipmentDef[] defs)
        {
            var db = new FakeGameDatabase();
            foreach (var def in defs) db.EquipmentList.Add(def);
            return db;
        }

        static FakeEquipmentDef Burner(EquipmentId id, float heat, int tier = 1) => new FakeEquipmentDef
        {
            Id = id,
            EquipmentType = EquipmentType.Burner,
            Tier = tier,
            HeatRateMultiplier = heat,
            CapacityMultiplier = 1f,
        };

        /// <summary>Simulated seconds a fresh pot takes to reach Ready under the given modifiers.</summary>
        static float TimeToReady(EquipmentModifiers eq, IBalanceConfig cfg, int maxSteps = 8000)
        {
            var s = new BrothState();
            float elapsed = 0f;
            for (int i = 0; i < maxSteps && s.Phase != BrothPhase.Ready; i++)
            {
                BrothSimulator.Tick(ref s, DtStep, eq, cfg);
                elapsed += DtStep;
            }
            Assert.That(s.Phase, Is.EqualTo(BrothPhase.Ready), "broth never reached Ready within maxSteps");
            return elapsed;
        }

        // ------------------------------------------------------------------
        // Ownership
        // ------------------------------------------------------------------

        [Test]
        public void NewModel_OwnsNothing()
        {
            var owned = new OwnedEquipmentModel();

            Assert.That(owned.Count, Is.EqualTo(0));
            Assert.That(owned.Owned.Count, Is.EqualTo(0));
            Assert.That(owned.Contains(CommercialBurner), Is.False);
        }

        [Test]
        public void TryAdd_GrantsOwnership_AndReturnsTrue()
        {
            var owned = new OwnedEquipmentModel();

            Assert.That(owned.TryAdd(CommercialBurner), Is.True);
            Assert.That(owned.Contains(CommercialBurner), Is.True);
            Assert.That(owned.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryAdd_AlreadyOwned_ReturnsFalse_AndDoesNotDuplicate()
        {
            var owned = new OwnedEquipmentModel();
            owned.TryAdd(CommercialBurner);

            bool second = true;
            Assert.That(() => second = owned.TryAdd(CommercialBurner), Throws.Nothing,
                "a double-buy attempt is a normal runtime occurrence, not a programmer error");

            Assert.That(second, Is.False);
            Assert.That(owned.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryAdd_EmptyId_Throws()
        {
            var owned = new OwnedEquipmentModel();

            Assert.That(() => owned.TryAdd(default(EquipmentId)), Throws.ArgumentException);
            Assert.That(() => owned.TryAdd(new EquipmentId("")), Throws.ArgumentException);
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            var owned = new OwnedEquipmentModel();
            owned.TryAdd(CommercialBurner);
            owned.TryAdd(BigPot);

            owned.Clear();

            Assert.That(owned.Count, Is.EqualTo(0));
            Assert.That(owned.Contains(CommercialBurner), Is.False);
        }

        // ------------------------------------------------------------------
        // Save/load restore
        // ------------------------------------------------------------------

        [Test]
        public void Restore_ReplacesTheWholeSet()
        {
            var owned = new OwnedEquipmentModel();
            owned.TryAdd(StockBurner);

            var accepted = owned.Restore(new[] { CommercialBurner, BigPot });

            Assert.That(accepted, Is.EqualTo(2));
            Assert.That(owned.Count, Is.EqualTo(2));
            Assert.That(owned.Contains(CommercialBurner), Is.True);
            Assert.That(owned.Contains(BigPot), Is.True);
            Assert.That(owned.Contains(StockBurner), Is.False, "Restore must replace, not merge");
        }

        [Test]
        public void Restore_Null_ClearsAndReportsZero()
        {
            var owned = new OwnedEquipmentModel();
            owned.TryAdd(CommercialBurner);

            int accepted = -1;
            Assert.That(() => accepted = owned.Restore(null), Throws.Nothing);

            Assert.That(accepted, Is.EqualTo(0));
            Assert.That(owned.Count, Is.EqualTo(0));
        }

        [Test]
        public void Restore_SkipsEmptyAndDuplicateIds_WithoutThrowing()
        {
            var owned = new OwnedEquipmentModel();

            int accepted = -1;
            Assert.That(
                () => accepted = owned.Restore(new[]
                {
                    CommercialBurner,
                    default(EquipmentId),
                    CommercialBurner,
                    BigPot,
                }),
                Throws.Nothing,
                "a corrupt save must never crash the restore path");

            Assert.That(accepted, Is.EqualTo(2));
            Assert.That(owned.Count, Is.EqualTo(2));
        }

        [Test]
        public void OwnershipSurvivesACaptureRestoreRoundTrip()
        {
            var before = new OwnedEquipmentModel();
            before.TryAdd(CommercialBurner);

            // What ProgressionService.Capture writes into
            // save.progression.ownedEquipmentIds, and Restore reads back.
            var serialized = new List<string>();
            foreach (var id in before.Owned) serialized.Add(id.Value);

            var after = new OwnedEquipmentModel();
            var rehydrated = new List<EquipmentId>();
            foreach (var raw in serialized) rehydrated.Add(new EquipmentId(raw));
            after.Restore(rehydrated);

            Assert.That(after.Count, Is.EqualTo(before.Count));
            Assert.That(after.Contains(CommercialBurner), Is.True);
        }

        // ------------------------------------------------------------------
        // Owned equipment -> EquipmentModifiers
        // ------------------------------------------------------------------

        [Test]
        public void ComputeModifiers_NothingOwned_IsDefault()
        {
            var owned = new OwnedEquipmentModel();
            var db = DatabaseWith(Burner(CommercialBurner, CommercialBurnerHeatRate, tier: 2));

            var mods = owned.ComputeModifiers(db);

            Assert.That(mods.HeatRateMultiplier, Is.EqualTo(EquipmentModifiers.Default.HeatRateMultiplier));
            Assert.That(mods.CapacityMultiplier, Is.EqualTo(EquipmentModifiers.Default.CapacityMultiplier));
            Assert.That(mods.HeatRateMultiplier, Is.EqualTo(1f));
            Assert.That(mods.CapacityMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void ComputeModifiers_NullDatabase_IsDefault_DoesNotThrow()
        {
            var owned = new OwnedEquipmentModel();
            owned.TryAdd(CommercialBurner);

            EquipmentModifiers mods = default;
            Assert.That(() => mods = owned.ComputeModifiers(null), Throws.Nothing);

            Assert.That(mods.HeatRateMultiplier, Is.EqualTo(1f));
            Assert.That(mods.CapacityMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void ComputeModifiers_OwnedBurner_ReflectsItsMultipliers()
        {
            var owned = new OwnedEquipmentModel();
            owned.TryAdd(CommercialBurner);
            var db = DatabaseWith(Burner(CommercialBurner, CommercialBurnerHeatRate, tier: 2));

            var mods = owned.ComputeModifiers(db);

            Assert.That(mods.HeatRateMultiplier, Is.EqualTo(CommercialBurnerHeatRate).Within(1e-5f));
            Assert.That(mods.HeatRateMultiplier, Is.GreaterThan(EquipmentModifiers.Default.HeatRateMultiplier));
        }

        [Test]
        public void ComputeModifiers_UnknownId_IsSkipped_NotAnError()
        {
            var owned = new OwnedEquipmentModel();
            owned.TryAdd(new EquipmentId("eq.deleted_in_a_later_patch"));
            var db = DatabaseWith(Burner(CommercialBurner, CommercialBurnerHeatRate));

            EquipmentModifiers mods = default;
            Assert.That(() => mods = owned.ComputeModifiers(db), Throws.Nothing);

            Assert.That(mods.HeatRateMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void ComputeModifiers_TwoBurners_DoNotStack_BestInSlotWins()
        {
            var owned = new OwnedEquipmentModel();
            owned.TryAdd(StockBurner);
            owned.TryAdd(CommercialBurner);
            var db = DatabaseWith(
                Burner(StockBurner, 1.2f, tier: 1),
                Burner(CommercialBurner, CommercialBurnerHeatRate, tier: 2));

            var mods = owned.ComputeModifiers(db);

            Assert.That(mods.HeatRateMultiplier, Is.EqualTo(CommercialBurnerHeatRate).Within(1e-5f),
                "only one burner runs at a time -- 1.2 * 1.5 would be a stacking bug");
        }

        [Test]
        public void ComputeModifiers_DifferentTypes_Compound()
        {
            var owned = new OwnedEquipmentModel();
            owned.TryAdd(CommercialBurner);
            owned.TryAdd(BigPot);
            var db = DatabaseWith(
                Burner(CommercialBurner, CommercialBurnerHeatRate, tier: 2),
                new FakeEquipmentDef
                {
                    Id = BigPot,
                    EquipmentType = EquipmentType.Pot,
                    HeatRateMultiplier = 1f,
                    CapacityMultiplier = 2f,
                });

            var mods = owned.ComputeModifiers(db);

            Assert.That(mods.HeatRateMultiplier, Is.EqualTo(CommercialBurnerHeatRate).Within(1e-5f));
            Assert.That(mods.CapacityMultiplier, Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void ComputeModifiers_DegenerateZeroedDef_TreatedAsNeutral_NeverFreezesThePot()
        {
            var owned = new OwnedEquipmentModel();
            owned.TryAdd(CommercialBurner);
            // An unauthored / default-initialized asset: every float is 0.
            var db = DatabaseWith(new FakeEquipmentDef
            {
                Id = CommercialBurner,
                EquipmentType = EquipmentType.Burner,
                HeatRateMultiplier = 0f,
                CapacityMultiplier = 0f,
            });

            var mods = owned.ComputeModifiers(db);

            Assert.That(mods.HeatRateMultiplier, Is.EqualTo(1f));
            Assert.That(mods.CapacityMultiplier, Is.EqualTo(1f));
        }

        // ------------------------------------------------------------------
        // The whole point: owning the upgrade measurably changes gameplay.
        // ------------------------------------------------------------------

        [Test]
        public void OwningTheCommercialBurner_MeasurablyShortensTimeToReady()
        {
            var cfg = new FakeBalanceConfig();
            var db = DatabaseWith(Burner(CommercialBurner, CommercialBurnerHeatRate, tier: 2));

            var stock = new OwnedEquipmentModel();
            var upgraded = new OwnedEquipmentModel();
            upgraded.TryAdd(CommercialBurner);

            float stockTime = TimeToReady(stock.ComputeModifiers(db), cfg);
            float upgradedTime = TimeToReady(upgraded.ComputeModifiers(db), cfg);

            Assert.That(upgradedTime, Is.LessThan(stockTime));

            // M13's acceptance criterion: "Buying it cuts measured
            // broth-ready time >= 30%."
            float reduction = (stockTime - upgradedTime) / stockTime;
            Assert.That(reduction, Is.GreaterThanOrEqualTo(0.30f),
                $"stock={stockTime}s upgraded={upgradedTime}s reduction={reduction:P1}");
        }

        [Test]
        public void UnownedRestaurant_TicksAtExactlyTheDefaultRate()
        {
            var cfg = new FakeBalanceConfig();
            var db = DatabaseWith(Burner(CommercialBurner, CommercialBurnerHeatRate, tier: 2));
            var stock = new OwnedEquipmentModel();

            float viaModel = TimeToReady(stock.ComputeModifiers(db), cfg);
            float viaDefault = TimeToReady(EquipmentModifiers.Default, cfg);

            Assert.That(viaModel, Is.EqualTo(viaDefault),
                "an unupgraded restaurant must behave identically to EquipmentModifiers.Default");
        }
    }
}
