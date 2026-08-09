using NUnit.Framework;
using Pho.Domain.Contracts;
using Pho.Domain.Cooking;
using Pho.Domain.Identity;
using Pho.Domain.Tests.Fakes;

namespace Pho.Domain.Tests
{
    // Plain NUnit only (no UnityEngine.TestTools / [UnityTest]) -- this file
    // is compiled both by Unity's EditMode test runner and by
    // Tools/PhoDomain.Tests.csproj via `dotnet test`. Constraint-based
    // Assert.That(...) throughout, matching EventBusTests.cs style.
    //
    // Not one of section 11's named Tier-1 suites (RecipeMatcherTests /
    // QualityCalculatorTests / BrothSimulatorTests), but BowlContents is the
    // foundational type this milestone introduces and its Add/Seal/capacity
    // rules deserve direct coverage rather than only being exercised
    // incidentally through the other suites.
    [TestFixture]
    public class BowlContentsTests
    {
        static readonly IngredientId Broth = new IngredientId("ing.beef_broth");
        static readonly IngredientId Herb = new IngredientId("ing.herb_mix");

        static BowlComponent Comp(ComponentSlot slot, IngredientId ing, float amount = 1f)
            => new BowlComponent(slot, ing, amount, quality01: 0.8f, freshness01: 1f, addedAtGameSeconds: 0f);

        [Test]
        public void Add_ToEmptyBowl_Succeeds_AndIsReflectedInComponents()
        {
            var bowl = new BowlContents();

            var result = bowl.Add(Comp(ComponentSlot.Broth, Broth));

            Assert.That(result, Is.EqualTo(AddResult.Ok));
            Assert.That(bowl.Components.Count, Is.EqualTo(1));
        }

        [Test]
        public void Add_BrothComponent_SetsBrothTemperatureToPouredBaseline()
        {
            var bowl = new BowlContents();
            Assert.That(bowl.BrothTemperatureC, Is.EqualTo(0f));

            bowl.Add(Comp(ComponentSlot.Broth, Broth));

            Assert.That(bowl.BrothTemperatureC, Is.EqualTo(BowlContents.PouredBrothTemperatureC));
        }

        [Test]
        public void Add_AfterSeal_Rejected_WithAlreadySealed()
        {
            var bowl = new BowlContents();
            bowl.Seal(nowSeconds: 10f);

            var result = bowl.Add(Comp(ComponentSlot.Broth, Broth));

            Assert.That(result, Is.EqualTo(AddResult.AlreadySealed));
            Assert.That(bowl.Components, Is.Empty);
        }

        [Test]
        public void Add_BeyondSingleServingSlotCapacity_Rejected_WithSlotOverCapacity()
        {
            var bowl = new BowlContents();
            var first = bowl.Add(Comp(ComponentSlot.Broth, Broth));
            var second = bowl.Add(Comp(ComponentSlot.Broth, Broth));

            Assert.That(first, Is.EqualTo(AddResult.Ok));
            Assert.That(second, Is.EqualTo(AddResult.SlotOverCapacity));
            Assert.That(bowl.Components.Count, Is.EqualTo(1));
        }

        [Test]
        public void Add_GarnishSlot_AllowsMoreEntriesThanBrothSlot()
        {
            var bowl = new BowlContents();
            int accepted = 0;

            for (int i = 0; i < 5; i++)
            {
                if (bowl.Add(Comp(ComponentSlot.Herb, Herb)) == AddResult.Ok)
                    accepted++;
            }

            Assert.That(accepted, Is.EqualTo(5), "herb/garnish slots are documented as more generous than single-serving slots");
            Assert.That(bowl.Add(Comp(ComponentSlot.Herb, Herb)), Is.EqualTo(AddResult.SlotOverCapacity));
        }

        [Test]
        public void Seal_SetsIsSealed_AndRecordsAssembledTime()
        {
            var bowl = new BowlContents();

            bowl.Seal(nowSeconds: 42f);

            Assert.That(bowl.IsSealed, Is.True);
            Assert.That(bowl.AssembledAtGameSeconds, Is.EqualTo(42f));
        }

        [Test]
        public void CoolTowardsAmbient_MovesTemperatureTowardAmbient_NeverOvershoots()
        {
            var bowl = new BowlContents();
            var cfg = new FakeBalanceConfig();
            bowl.Add(Comp(ComponentSlot.Broth, Broth));
            float startTemp = bowl.BrothTemperatureC;

            bowl.CoolTowardsAmbient(1f, cfg);

            Assert.That(bowl.BrothTemperatureC, Is.LessThan(startTemp));
            Assert.That(bowl.BrothTemperatureC, Is.GreaterThanOrEqualTo(BowlContents.AmbientTemperatureC));
        }

        [Test]
        public void CoolTowardsAmbient_LargeDt_ConvergesToAmbient_NeverBelow()
        {
            var bowl = new BowlContents();
            var cfg = new FakeBalanceConfig();
            bowl.Add(Comp(ComponentSlot.Broth, Broth));

            bowl.CoolTowardsAmbient(10_000f, cfg);

            Assert.That(bowl.BrothTemperatureC, Is.EqualTo(BowlContents.AmbientTemperatureC).Within(1e-4f));
        }
    }
}
