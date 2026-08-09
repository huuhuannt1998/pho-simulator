using System.Collections.Generic;
using NUnit.Framework;
using Pho.Domain.Expansion;

namespace Pho.Domain.Tests
{
    // Plain NUnit only (no UnityEngine.TestTools / [UnityTest]) -- this file is
    // compiled both by Unity's EditMode runner and by
    // Tools/PhoDomain.Tests.csproj via `dotnet test`. Constraint-based
    // Assert.That(...) throughout, matching InventoryModelTests.cs style;
    // Assert.AreEqual/IsTrue live in a Legacy namespace under NUnit 4.x that
    // Unity's bundled NUnit does not ship, so they would not compile.
    [TestFixture]
    public class ExpansionTests
    {
        // A deliberately branchy block, so "adjacent" is a real constraint and
        // not just "the next one along":
        //
        //     [alley]---[start*]---[next]---[far]
        //                             |
        //                        [upstairs]
        static readonly LotId Start = new LotId("lot.start");
        static readonly LotId Alley = new LotId("lot.alley");
        static readonly LotId Next = new LotId("lot.next");
        static readonly LotId Far = new LotId("lot.far");
        static readonly LotId Upstairs = new LotId("lot.upstairs");
        static readonly LotId Nowhere = new LotId("lot.does_not_exist");

        static List<LotDef> Catalog()
        {
            return new List<LotDef>
            {
                new LotDef(Start, "Phở Shop", 0m, isStarting: true),
                new LotDef(Alley, "Back Alley", 100m, prerequisites: new[] { Start }),
                new LotDef(Next, "Unit Next Door", 500m, prerequisites: new[] { Start }),
                new LotDef(Far, "Corner Shop", 900m, prerequisites: new[] { Next }),
                new LotDef(Upstairs, "Upstairs", 700m, prerequisites: new[] { Next }),
            };
        }

        static ExpansionModel NewModel() => new ExpansionModel(new LotRegistry(Catalog()));

        // -----------------------------------------------------------------
        // Starting lot owned from the beginning
        // -----------------------------------------------------------------

        [Test]
        public void NewModel_OwnsStartingLot()
        {
            var model = NewModel();

            Assert.That(model.IsOwned(Start), Is.True);
            Assert.That(model.OwnedCount, Is.EqualTo(1));
            Assert.That(model.Owned[0], Is.EqualTo(Start));
        }

        [Test]
        public void NewModel_OwnsNothingElse()
        {
            var model = NewModel();

            Assert.That(model.IsOwned(Next), Is.False);
            Assert.That(model.IsOwned(Alley), Is.False);
            Assert.That(model.IsOwned(Far), Is.False);
        }

        [Test]
        public void StartingLot_IsAlreadyOwned_SoCannotBeRepurchased()
        {
            var model = NewModel();

            Assert.That(model.Evaluate(Start), Is.EqualTo(LotEligibility.AlreadyOwned));
            Assert.That(model.Purchase(Start), Is.False);
            Assert.That(model.OwnedCount, Is.EqualTo(1));
        }

        // -----------------------------------------------------------------
        // Adjacency enforcement -- the whole point of the model
        // -----------------------------------------------------------------

        [Test]
        public void AdjacentLot_IsEligible()
        {
            var model = NewModel();

            Assert.That(model.Evaluate(Next), Is.EqualTo(LotEligibility.Eligible));
            Assert.That(model.CanPurchase(Next), Is.True);
        }

        [Test]
        public void FarLot_IsNotAdjacent_AndPurchaseIsRefused()
        {
            var model = NewModel();

            Assert.That(model.Evaluate(Far), Is.EqualTo(LotEligibility.NotAdjacent));
            Assert.That(model.Purchase(Far), Is.False);
            Assert.That(model.IsOwned(Far), Is.False);
            Assert.That(model.OwnedCount, Is.EqualTo(1));
        }

        [Test]
        public void FarLot_BecomesEligible_OnlyAfterTheLotBetweenIsBought()
        {
            var model = NewModel();

            Assert.That(model.CanPurchase(Far), Is.False);

            Assert.That(model.Purchase(Next), Is.True);

            Assert.That(model.CanPurchase(Far), Is.True);
            Assert.That(model.Purchase(Far), Is.True);
            Assert.That(model.OwnedCount, Is.EqualTo(3));
        }

        [Test]
        public void OneNeighbour_UnlocksEveryLotTouchingIt()
        {
            var model = NewModel();
            model.Purchase(Next);

            // ANY-prerequisite semantics: both children of Next open at once.
            Assert.That(model.Evaluate(Far), Is.EqualTo(LotEligibility.Eligible));
            Assert.That(model.Evaluate(Upstairs), Is.EqualTo(LotEligibility.Eligible));
        }

        [Test]
        public void AvailableLots_IsTheGrowthFrontier_NotTheWholeCatalog()
        {
            var model = NewModel();

            Assert.That(model.AvailableLots(), Is.EquivalentTo(new[] { Alley, Next }));

            model.Purchase(Next);

            Assert.That(model.AvailableLots(), Is.EquivalentTo(new[] { Alley, Far, Upstairs }));
        }

        [Test]
        public void UnknownLot_IsRefused_WithoutThrowing()
        {
            var model = NewModel();

            Assert.That(model.Evaluate(Nowhere), Is.EqualTo(LotEligibility.UnknownLot));
            Assert.That(model.Purchase(Nowhere), Is.False);
        }

        [Test]
        public void EmptyLotId_IsRefused_WithoutThrowing()
        {
            var model = NewModel();

            Assert.That(model.Evaluate(default(LotId)), Is.EqualTo(LotEligibility.UnknownLot));
            Assert.That(model.Purchase(default(LotId)), Is.False);
        }

        // -----------------------------------------------------------------
        // Double purchase refused
        // -----------------------------------------------------------------

        [Test]
        public void Purchase_Twice_SecondIsRefused_AndOwnedCountDoesNotGrow()
        {
            var model = NewModel();

            Assert.That(model.Purchase(Next), Is.True);
            Assert.That(model.Evaluate(Next), Is.EqualTo(LotEligibility.AlreadyOwned));
            Assert.That(model.Purchase(Next), Is.False);
            Assert.That(model.OwnedCount, Is.EqualTo(2));
        }

        [Test]
        public void Owned_IsInAcquisitionOrder_StartingLotFirst()
        {
            var model = NewModel();
            model.Purchase(Next);
            model.Purchase(Upstairs);

            Assert.That(model.Owned, Is.EqualTo(new[] { Start, Next, Upstairs }));
        }

        [Test]
        public void Reset_ReturnsToDayOne()
        {
            var model = NewModel();
            model.Purchase(Next);
            model.Purchase(Far);

            model.Reset();

            Assert.That(model.OwnedCount, Is.EqualTo(1));
            Assert.That(model.IsOwned(Start), Is.True);
            Assert.That(model.IsOwned(Far), Is.False);
        }

        // -----------------------------------------------------------------
        // Save / restore round-trip
        // -----------------------------------------------------------------

        [Test]
        public void Restore_RoundTripsOwnership()
        {
            var source = NewModel();
            source.Purchase(Next);
            source.Purchase(Upstairs);
            source.Purchase(Far);

            var captured = new List<LotId>(source.Owned);

            var restored = NewModel();
            var accepted = restored.Restore(captured);

            Assert.That(accepted, Is.EqualTo(4));
            Assert.That(restored.Owned, Is.EqualTo(source.Owned));
        }

        [Test]
        public void Restore_IsOrderIndependent_BecauseItAbsorbsToAFixedPoint()
        {
            // A save file gives no ordering guarantee; the deepest lot first
            // must still restore the whole chain.
            var restored = NewModel();

            var accepted = restored.Restore(new List<LotId> { Far, Upstairs, Next });

            Assert.That(accepted, Is.EqualTo(4));
            Assert.That(restored.IsOwned(Far), Is.True);
            Assert.That(restored.IsOwned(Upstairs), Is.True);
            Assert.That(restored.IsOwned(Next), Is.True);
        }

        [Test]
        public void Restore_AlwaysReSeedsStartingLot_EvenIfTheSaveOmitsIt()
        {
            var restored = NewModel();

            restored.Restore(new List<LotId> { Alley });

            Assert.That(restored.IsOwned(Start), Is.True);
            Assert.That(restored.IsOwned(Alley), Is.True);
            Assert.That(restored.OwnedCount, Is.EqualTo(2));
        }

        [Test]
        public void Restore_DropsNonContiguousIds_KeepingTheComplexConnected()
        {
            // A hand-edited or content-drifted save claiming the corner shop
            // without the unit between it and the start.
            var restored = NewModel();

            var accepted = restored.Restore(new List<LotId> { Far });

            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(restored.IsOwned(Far), Is.False);
            Assert.That(restored.Owned, Is.EqualTo(new[] { Start }));
        }

        [Test]
        public void Restore_SkipsUnknownAndDuplicateAndEmptyIds()
        {
            var restored = NewModel();

            var accepted = restored.Restore(new List<LotId> { Next, Next, Nowhere, default(LotId) });

            Assert.That(accepted, Is.EqualTo(2));
            Assert.That(restored.Owned, Is.EqualTo(new[] { Start, Next }));
        }

        [Test]
        public void Restore_NullIds_LeavesADayOneRestaurant()
        {
            var restored = NewModel();
            restored.Purchase(Next);

            var accepted = restored.Restore(null);

            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(restored.Owned, Is.EqualTo(new[] { Start }));
        }

        [Test]
        public void Restore_ThenPurchase_ContinuesFromTheRestoredFrontier()
        {
            var restored = NewModel();
            restored.Restore(new List<LotId> { Next });

            Assert.That(restored.CanPurchase(Far), Is.True);
            Assert.That(restored.Purchase(Far), Is.True);
        }

        // -----------------------------------------------------------------
        // Authored-content errors throw (programmer error, not runtime)
        // -----------------------------------------------------------------

        [Test]
        public void LotDef_NonStartingWithNoPrerequisites_Throws()
        {
            Assert.That(
                () => new LotDef(Next, "Floating Unit", 500m),
                Throws.ArgumentException);
        }

        [Test]
        public void LotDef_StartingWithPrerequisites_Throws()
        {
            Assert.That(
                () => new LotDef(Start, "Phở Shop", 0m, isStarting: true, prerequisites: new[] { Next }),
                Throws.ArgumentException);
        }

        [Test]
        public void LotDef_StartingWithAPrice_Throws()
        {
            Assert.That(
                () => new LotDef(Start, "Phở Shop", 250m, isStarting: true),
                Throws.ArgumentException);
        }

        [Test]
        public void LotDef_SelfPrerequisite_Throws()
        {
            Assert.That(
                () => new LotDef(Next, "Unit Next Door", 500m, prerequisites: new[] { Next }),
                Throws.ArgumentException);
        }

        [Test]
        public void LotDef_EmptyId_Throws()
        {
            Assert.That(
                () => new LotDef(default(LotId), "Nameless", 100m, prerequisites: new[] { Start }),
                Throws.ArgumentException);
        }

        [Test]
        public void LotRegistry_NoStartingLot_Throws()
        {
            var lots = new List<LotDef>
            {
                new LotDef(Next, "Unit Next Door", 500m, prerequisites: new[] { Start }),
            };

            Assert.That(() => new LotRegistry(lots), Throws.ArgumentException);
        }

        [Test]
        public void LotRegistry_DanglingPrerequisite_Throws()
        {
            var lots = new List<LotDef>
            {
                new LotDef(Start, "Phở Shop", 0m, isStarting: true),
                new LotDef(Next, "Unit Next Door", 500m, prerequisites: new[] { Nowhere }),
            };

            Assert.That(() => new LotRegistry(lots), Throws.ArgumentException);
        }

        [Test]
        public void LotRegistry_DuplicateId_Throws()
        {
            var lots = new List<LotDef>
            {
                new LotDef(Start, "Phở Shop", 0m, isStarting: true),
                new LotDef(Next, "Unit Next Door", 500m, prerequisites: new[] { Start }),
                new LotDef(Next, "Unit Next Door (again)", 500m, prerequisites: new[] { Start }),
            };

            Assert.That(() => new LotRegistry(lots), Throws.ArgumentException);
        }

        [Test]
        public void LotRegistry_UnreachableCycle_Throws()
        {
            // far <-> upstairs depend on each other and on nothing owned:
            // well-formed defs, permanently unbuyable graph.
            var lots = new List<LotDef>
            {
                new LotDef(Start, "Phở Shop", 0m, isStarting: true),
                new LotDef(Far, "Corner Shop", 900m, prerequisites: new[] { Upstairs }),
                new LotDef(Upstairs, "Upstairs", 700m, prerequisites: new[] { Far }),
            };

            Assert.That(() => new LotRegistry(lots), Throws.ArgumentException);
        }

        [Test]
        public void LotRegistry_EmptyCatalog_Throws()
        {
            Assert.That(() => new LotRegistry(new List<LotDef>()), Throws.ArgumentException);
        }

        [Test]
        public void ExpansionModel_NullRegistry_Throws()
        {
            Assert.That(() => new ExpansionModel(null), Throws.ArgumentNullException);
        }

        // -----------------------------------------------------------------
        // The shipped catalog must itself be valid and growable
        // -----------------------------------------------------------------

        [Test]
        public void DefaultCatalog_IsValid_AndStartsWithExactlyOneShop()
        {
            var registry = DefaultLotCatalog.BuildRegistry();

            Assert.That(registry.StartingLots.Count, Is.EqualTo(1));
            Assert.That(registry.StartingLots[0], Is.EqualTo(DefaultLotCatalog.Unit1));
        }

        [Test]
        public void DefaultCatalog_EveryLotIsPurchasableInSomeOrder()
        {
            var model = new ExpansionModel(DefaultLotCatalog.BuildRegistry());
            var registry = model.Registry;

            // Greedily absorb the frontier until nothing new opens up. A
            // reachable catalog ends with everything owned.
            var frontier = model.AvailableLots();
            while (frontier.Count > 0)
            {
                for (int i = 0; i < frontier.Count; i++) model.Purchase(frontier[i]);
                frontier = model.AvailableLots();
            }

            Assert.That(model.OwnedCount, Is.EqualTo(registry.Count));
        }

        [Test]
        public void DefaultCatalog_CornerShopIsNotBuyableFromTheStartingShop()
        {
            var model = new ExpansionModel(DefaultLotCatalog.BuildRegistry());

            Assert.That(model.Evaluate(DefaultLotCatalog.Unit3), Is.EqualTo(LotEligibility.NotAdjacent));
            Assert.That(model.Evaluate(DefaultLotCatalog.Upstairs), Is.EqualTo(LotEligibility.NotAdjacent));
            Assert.That(model.Evaluate(DefaultLotCatalog.Unit2), Is.EqualTo(LotEligibility.Eligible));
        }

        [Test]
        public void DefaultCatalog_EveryLotIdCarriesTheLotPrefix()
        {
            var registry = DefaultLotCatalog.BuildRegistry();

            foreach (var lot in registry.All)
            {
                Assert.That(lot.Id.Value, Does.StartWith(LotId.Prefix));
            }
        }
    }
}
