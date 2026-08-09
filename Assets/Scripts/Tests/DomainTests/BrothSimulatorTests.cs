using System.Collections.Generic;
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
    [TestFixture]
    public class BrothSimulatorTests
    {
        const float DtStep = 0.25f;

        static float TimeToPhase(BrothPhase target, EquipmentModifiers eq, IBalanceConfig cfg, int maxSteps = 4000)
        {
            var s = new BrothState();
            float elapsed = 0f;
            for (int i = 0; i < maxSteps && s.Phase != target; i++)
            {
                BrothSimulator.Tick(ref s, DtStep, eq, cfg);
                elapsed += DtStep;
            }
            Assert.That(s.Phase, Is.EqualTo(target), "did not reach the target phase within maxSteps");
            return elapsed;
        }

        [Test]
        public void Tick_AdvancesThroughPhases_InExpectedOrder()
        {
            var s = new BrothState(); // default(BrothPhase) == Empty
            var eq = EquipmentModifiers.Default;
            var cfg = new FakeBalanceConfig();
            var seen = new List<BrothPhase> { s.Phase };

            for (int i = 0; i < 1000 && s.Phase != BrothPhase.Ready; i++)
            {
                BrothSimulator.Tick(ref s, DtStep, eq, cfg);
                if (seen[seen.Count - 1] != s.Phase)
                    seen.Add(s.Phase);
            }

            Assert.That(seen, Is.EqualTo(new[]
            {
                BrothPhase.Empty,
                BrothPhase.Filling,
                BrothPhase.Heating,
                BrothPhase.Simmering,
                BrothPhase.Ready,
            }));
        }

        [Test]
        public void Tick_ZeroOrNegativeDt_IsANoOp()
        {
            var s = new BrothState { Phase = BrothPhase.Simmering, SimmerSeconds = 10f, TemperatureC = 95f };
            var eq = EquipmentModifiers.Default;
            var cfg = new FakeBalanceConfig();

            BrothSimulator.Tick(ref s, 0f, eq, cfg);
            Assert.That(s.SimmerSeconds, Is.EqualTo(10f));

            BrothSimulator.Tick(ref s, -1f, eq, cfg);
            Assert.That(s.SimmerSeconds, Is.EqualTo(10f));
        }

        [Test]
        public void Tick_SimmerToReady_TriggersAtExpectedThreshold()
        {
            var s = new BrothState();
            var eq = EquipmentModifiers.Default;
            var cfg = new FakeBalanceConfig();

            while (s.Phase != BrothPhase.Simmering)
                BrothSimulator.Tick(ref s, DtStep, eq, cfg);

            Assert.That(s.SimmerSeconds, Is.LessThan(BrothSimulator.SimmerDurationSecondsToReady));

            while (s.Phase == BrothPhase.Simmering)
                BrothSimulator.Tick(ref s, DtStep, eq, cfg);

            Assert.That(s.Phase, Is.EqualTo(BrothPhase.Ready));
            Assert.That(s.SimmerSeconds, Is.GreaterThanOrEqualTo(BrothSimulator.SimmerDurationSecondsToReady));
            // Should not have overshot by more than one tick step.
            Assert.That(s.SimmerSeconds, Is.LessThan(BrothSimulator.SimmerDurationSecondsToReady + DtStep + 1e-4f));
        }

        [Test]
        public void Tick_ContinuingPastReady_EventuallyReachesOvercooked()
        {
            var s = new BrothState();
            var eq = EquipmentModifiers.Default;
            var cfg = new FakeBalanceConfig();

            var reachedReadyAt = TimeToPhase(BrothPhase.Ready, eq, cfg);
            Assert.That(reachedReadyAt, Is.GreaterThan(0f));

            for (int i = 0; i < 4000 && s.Phase != BrothPhase.Overcooked; i++)
                BrothSimulator.Tick(ref s, DtStep, eq, cfg);

            Assert.That(s.Phase, Is.EqualTo(BrothPhase.Overcooked));
        }

        [Test]
        public void Tick_Overcooked_QualityDecaysTheLongerItSits()
        {
            var s = new BrothState();
            var eq = EquipmentModifiers.Default;
            var cfg = new FakeBalanceConfig();

            for (int i = 0; i < 4000 && s.Phase != BrothPhase.Overcooked; i++)
                BrothSimulator.Tick(ref s, DtStep, eq, cfg);
            Assert.That(s.Phase, Is.EqualTo(BrothPhase.Overcooked));

            float qualityJustOvercooked = s.Quality01;

            for (int i = 0; i < 200; i++)
                BrothSimulator.Tick(ref s, DtStep, eq, cfg);

            Assert.That(s.Quality01, Is.LessThan(qualityJustOvercooked));
        }

        [Test]
        public void Tick_HigherHeatRateMultiplier_ReachesReady_InMeasurablyLessSimulatedTime()
        {
            var cfg = new FakeBalanceConfig();

            float defaultTime = TimeToPhase(BrothPhase.Ready, EquipmentModifiers.Default, cfg);
            float fastTime = TimeToPhase(BrothPhase.Ready, new EquipmentModifiers(1.5f, 1f), cfg);

            Assert.That(fastTime, Is.LessThan(defaultTime));
        }

        [Test]
        public void AromaticContribution_ScalesWithAmountAndQuality_AndClampsAt1()
        {
            var cfg = new FakeBalanceConfig();
            var onion = new IngredientId("ing.onion");

            var small = new List<BowlComponent>
            {
                new BowlComponent(ComponentSlot.Aromatic, onion, 1f, 0.5f, 1f, 0f),
            };
            var large = new List<BowlComponent>
            {
                new BowlComponent(ComponentSlot.Aromatic, onion, 10f, 1f, 1f, 0f),
            };

            float smallContribution = BrothSimulator.AromaticContribution(small, cfg);
            float largeContribution = BrothSimulator.AromaticContribution(large, cfg);

            Assert.That(smallContribution, Is.GreaterThan(0f));
            Assert.That(largeContribution, Is.GreaterThan(smallContribution));
            Assert.That(largeContribution, Is.LessThanOrEqualTo(1f));
        }

        [Test]
        public void AromaticContribution_IgnoresNonAromaticSlotEntries()
        {
            var cfg = new FakeBalanceConfig();
            var beef = new IngredientId("ing.beef_rare");

            var proteinOnly = new List<BowlComponent>
            {
                new BowlComponent(ComponentSlot.Protein, beef, 5f, 1f, 1f, 0f),
            };

            Assert.That(BrothSimulator.AromaticContribution(proteinOnly, cfg), Is.EqualTo(0f));
        }
    }
}
