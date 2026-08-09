using System;
using System.Linq;
using NUnit.Framework;
using Pho.Domain.Contracts;
using Pho.Domain.Identity;
using Pho.Domain.Inventory;
using Pho.Domain.Tests.Fakes;

namespace Pho.Domain.Tests
{
    // Plain NUnit only (no UnityEngine.TestTools / [UnityTest]) -- this file
    // is compiled both by Unity's EditMode test runner and by
    // Tools/PhoDomain.Tests.csproj via `dotnet test`. Constraint-based
    // Assert.That(...) throughout, matching EventBusTests.cs style.
    [TestFixture]
    public class InventoryModelTests
    {
        static readonly IngredientId Beef = new IngredientId("ing.beef_brisket");
        static readonly IngredientId Noodle = new IngredientId("ing.rice_noodle");

        [Test]
        public void Add_ThenTotalQuantity_ReflectsAddedAmount()
        {
            var inv = new InventoryModel();

            inv.Add(Beef, 3f, 0.8f, day: 1, storage: StorageRequirement.Refrigerated);

            Assert.That(inv.TotalQuantity(Beef), Is.EqualTo(3f));
            Assert.That(inv.Lots.Count, Is.EqualTo(1));
        }

        [Test]
        public void Add_ZeroAmount_Throws()
        {
            var inv = new InventoryModel();

            Assert.That(
                () => inv.Add(Beef, 0f, 0.8f, day: 1, storage: StorageRequirement.Refrigerated),
                Throws.ArgumentException);
        }

        [Test]
        public void Add_NegativeAmount_Throws()
        {
            var inv = new InventoryModel();

            Assert.That(
                () => inv.Add(Beef, -1f, 0.8f, day: 1, storage: StorageRequirement.Refrigerated),
                Throws.ArgumentException);
        }

        [Test]
        public void Add_AcceptsAnyStorageRequirement_AndLotsReportsItBackAccurately()
        {
            var inv = new InventoryModel();

            inv.Add(Beef, 1f, 0.8f, day: 1, storage: StorageRequirement.Frozen);
            inv.Add(Noodle, 1f, 0.8f, day: 1, storage: StorageRequirement.Ambient);

            Assert.That(inv.Lots[0].StoredIn, Is.EqualTo(StorageRequirement.Frozen));
            Assert.That(inv.Lots[1].StoredIn, Is.EqualTo(StorageRequirement.Ambient));
        }

        [Test]
        public void TryConsume_SingleLotCoversAmount_ReturnsTrueWithLotQualityAndFreshness()
        {
            var inv = new InventoryModel();
            inv.Add(Beef, 5f, 0.9f, day: 1, storage: StorageRequirement.Refrigerated);

            var ok = inv.TryConsume(Beef, 2f, out var consumed);

            Assert.That(ok, Is.True);
            Assert.That(consumed.Id, Is.EqualTo(Beef));
            Assert.That(consumed.Amount, Is.EqualTo(2f));
            Assert.That(consumed.Quality01, Is.EqualTo(0.9f).Within(1e-5f));
            Assert.That(consumed.Freshness01, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(inv.TotalQuantity(Beef), Is.EqualTo(3f).Within(1e-5f));
        }

        [Test]
        public void TryConsume_DrawsFromOldestLotFirst_FIFO()
        {
            var inv = new InventoryModel();
            // Oldest lot (day 1) is low quality; newest (day 3) is high quality.
            inv.Add(Beef, 2f, 0.3f, day: 1, storage: StorageRequirement.Refrigerated);
            inv.Add(Beef, 2f, 0.9f, day: 3, storage: StorageRequirement.Refrigerated);

            var ok = inv.TryConsume(Beef, 1f, out var consumed);

            Assert.That(ok, Is.True);
            Assert.That(consumed.Quality01, Is.EqualTo(0.3f).Within(1e-5f), "should have drawn from the day-1 (oldest) lot, not the fresher day-3 lot");
            Assert.That(inv.TotalQuantity(Beef), Is.EqualTo(3f).Within(1e-5f));

            // The oldest lot should now have 1 unit left, newest lot untouched.
            var oldest = inv.Lots.FirstOrDefault(l => l.PurchasedOnDay == 1);
            var newest = inv.Lots.FirstOrDefault(l => l.PurchasedOnDay == 3);
            Assert.That(oldest.Quantity, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(newest.Quantity, Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void TryConsume_ExhaustsOldestLot_RemovesItFromLots()
        {
            var inv = new InventoryModel();
            inv.Add(Beef, 1f, 0.5f, day: 1, storage: StorageRequirement.Refrigerated);
            inv.Add(Beef, 1f, 0.5f, day: 2, storage: StorageRequirement.Refrigerated);

            inv.TryConsume(Beef, 1f, out _);

            Assert.That(inv.Lots.Count, Is.EqualTo(1));
            Assert.That(inv.Lots[0].PurchasedOnDay, Is.EqualTo(2));
        }

        [Test]
        public void TryConsume_AcrossMultipleLots_BlendsQualityAndFreshnessWeightedByAmount()
        {
            var inv = new InventoryModel();
            // Oldest lot: 1.5kg @ quality 0.4. Next lot: 1.0kg @ quality 1.0.
            inv.Add(Beef, 1.5f, 0.4f, day: 1, storage: StorageRequirement.Refrigerated);
            inv.Add(Beef, 1.0f, 1.0f, day: 2, storage: StorageRequirement.Refrigerated);

            // Requesting 2kg must drain the oldest lot (1.5kg) and take 0.5kg
            // from the second lot, blending quality/freshness by contribution.
            var ok = inv.TryConsume(Beef, 2f, out var consumed);

            Assert.That(ok, Is.True);
            Assert.That(consumed.Amount, Is.EqualTo(2f).Within(1e-5f));

            float expectedQuality = (1.5f * 0.4f + 0.5f * 1.0f) / 2f; // = 0.55
            Assert.That(consumed.Quality01, Is.EqualTo(expectedQuality).Within(1e-4f));

            // Both lots were freshly added (Freshness01 == 1), so the blended
            // freshness should also be 1.
            Assert.That(consumed.Freshness01, Is.EqualTo(1f).Within(1e-5f));

            // Oldest lot fully drained and removed; second lot has 0.5kg left.
            Assert.That(inv.Lots.Count, Is.EqualTo(1));
            Assert.That(inv.Lots[0].PurchasedOnDay, Is.EqualTo(2));
            Assert.That(inv.Lots[0].Quantity, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void TryConsume_InsufficientQuantity_ReturnsFalse_AndDoesNotPartiallyConsume()
        {
            var inv = new InventoryModel();
            inv.Add(Beef, 1f, 0.7f, day: 1, storage: StorageRequirement.Refrigerated);

            var ok = inv.TryConsume(Beef, 5f, out var consumed);

            Assert.That(ok, Is.False);
            Assert.That(consumed, Is.EqualTo(default(ConsumedIngredient)));
            Assert.That(inv.TotalQuantity(Beef), Is.EqualTo(1f), "a failed consume must not touch existing stock");
            Assert.That(inv.Lots.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryConsume_UnknownIngredient_ReturnsFalse()
        {
            var inv = new InventoryModel();
            inv.Add(Beef, 5f, 0.7f, day: 1, storage: StorageRequirement.Refrigerated);

            var ok = inv.TryConsume(Noodle, 1f, out _);

            Assert.That(ok, Is.False);
        }

        [Test]
        public void TryConsume_ZeroAmount_ReturnsFalse_DoesNotThrow()
        {
            var inv = new InventoryModel();
            inv.Add(Beef, 5f, 0.7f, day: 1, storage: StorageRequirement.Refrigerated);

            bool ok = true;
            ConsumedIngredient consumed = default;
            Assert.That(() => ok = inv.TryConsume(Beef, 0f, out consumed), Throws.Nothing);

            Assert.That(ok, Is.False);
            Assert.That(inv.TotalQuantity(Beef), Is.EqualTo(5f));
        }

        [Test]
        public void TryConsume_NegativeAmount_ReturnsFalse_DoesNotThrow()
        {
            var inv = new InventoryModel();
            inv.Add(Beef, 5f, 0.7f, day: 1, storage: StorageRequirement.Refrigerated);

            bool ok = true;
            Assert.That(() => ok = inv.TryConsume(Beef, -2f, out _), Throws.Nothing);

            Assert.That(ok, Is.False);
            Assert.That(inv.TotalQuantity(Beef), Is.EqualTo(5f));
        }

        [Test]
        public void AdvanceSpoilage_ReducesFreshnessByExpectedAmount()
        {
            var inv = new InventoryModel();
            inv.Add(Beef, 1f, 0.8f, day: 1, storage: StorageRequirement.Refrigerated);
            var cfg = new FakeBalanceConfig { SpoilageRatePerHour = 0.1f };

            inv.AdvanceSpoilage(3f, cfg); // 3 hours * 0.1/hr = 0.3 decay

            Assert.That(inv.Lots[0].Freshness01, Is.EqualTo(0.7f).Within(1e-5f));
        }

        [Test]
        public void AdvanceSpoilage_ClampsAtZero_NeverGoesNegative()
        {
            var inv = new InventoryModel();
            inv.Add(Beef, 1f, 0.8f, day: 1, storage: StorageRequirement.Refrigerated);
            var cfg = new FakeBalanceConfig { SpoilageRatePerHour = 0.5f };

            inv.AdvanceSpoilage(100f, cfg); // way more decay than 1.0 available

            Assert.That(inv.Lots[0].Freshness01, Is.EqualTo(0f));
        }

        [Test]
        public void Clear_RemovesAllLots()
        {
            var inv = new InventoryModel();
            inv.Add(Beef, 1f, 0.8f, day: 1, storage: StorageRequirement.Refrigerated);
            inv.Add(Noodle, 1f, 0.8f, day: 1, storage: StorageRequirement.Ambient);

            inv.Clear();

            Assert.That(inv.Lots.Count, Is.EqualTo(0));
        }

        [Test]
        public void AddLot_PreservesExplicitFreshness_UnlikeAdd()
        {
            var inv = new InventoryModel();

            inv.AddLot(Beef, 2f, 0.8f, freshness01: 0.4f, day: 1, storage: StorageRequirement.Refrigerated);

            Assert.That(inv.Lots[0].Freshness01, Is.EqualTo(0.4f).Within(1e-5f), "AddLot must round-trip freshness exactly, not reset it to 1 like Add does");
            Assert.That(inv.Lots[0].Quantity, Is.EqualTo(2f));
            Assert.That(inv.Lots[0].QualityAtPurchase01, Is.EqualTo(0.8f));
            Assert.That(inv.Lots[0].PurchasedOnDay, Is.EqualTo(1));
            Assert.That(inv.Lots[0].StoredIn, Is.EqualTo(StorageRequirement.Refrigerated));
        }

        [Test]
        public void AddLot_ClampsFreshnessTo01()
        {
            var inv = new InventoryModel();

            inv.AddLot(Beef, 1f, 0.8f, freshness01: 1.5f, day: 1, storage: StorageRequirement.Ambient);
            inv.AddLot(Noodle, 1f, 0.8f, freshness01: -0.5f, day: 1, storage: StorageRequirement.Ambient);

            Assert.That(inv.Lots[0].Freshness01, Is.EqualTo(1f));
            Assert.That(inv.Lots[1].Freshness01, Is.EqualTo(0f));
        }

        [Test]
        public void AddLot_ZeroOrNegativeAmount_Throws()
        {
            var inv = new InventoryModel();

            Assert.That(
                () => inv.AddLot(Beef, 0f, 0.8f, 1f, day: 1, storage: StorageRequirement.Ambient),
                Throws.ArgumentException);
        }

        [Test]
        public void AdvanceSpoilage_AffectsAllLots_AcrossDifferentIngredients()
        {
            var inv = new InventoryModel();
            inv.Add(Beef, 1f, 0.8f, day: 1, storage: StorageRequirement.Refrigerated);
            inv.Add(Noodle, 1f, 0.8f, day: 1, storage: StorageRequirement.Ambient);
            var cfg = new FakeBalanceConfig { SpoilageRatePerHour = 0.2f };

            inv.AdvanceSpoilage(1f, cfg);

            foreach (var lot in inv.Lots)
            {
                Assert.That(lot.Freshness01, Is.EqualTo(0.8f).Within(1e-5f));
            }
        }
    }
}
