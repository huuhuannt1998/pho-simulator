using NUnit.Framework;
using Pho.Domain.DayCycle;
using Pho.Domain.Tests.Fakes;

namespace Pho.Domain.Tests
{
    [TestFixture]
    public class DayClockTests
    {
        [Test]
        public void NewClock_StartsInPrepPhase_AtTimeZero()
        {
            var clock = new DayClock(1, new FakeBalanceConfig());

            Assert.That(clock.Day, Is.EqualTo(1));
            Assert.That(clock.TimeOfDaySeconds, Is.EqualTo(0f));
            Assert.That(clock.Phase, Is.EqualTo(DayPhase.Prep));
        }

        // ---- Phase boundaries: only via explicit calls, never automatic ----

        [Test]
        public void OpenRestaurant_FromPrep_TransitionsToOpen()
        {
            var clock = new DayClock(1, new FakeBalanceConfig());
            clock.OpenRestaurant();
            Assert.That(clock.Phase, Is.EqualTo(DayPhase.Open));
        }

        [Test]
        public void OpenRestaurant_WhenAlreadyOpen_IsNoOp()
        {
            var clock = new DayClock(1, new FakeBalanceConfig());
            clock.OpenRestaurant();

            Assert.That(() => clock.OpenRestaurant(), Throws.Nothing);
            Assert.That(clock.Phase, Is.EqualTo(DayPhase.Open));
        }

        [Test]
        public void CloseRestaurant_FromOpen_TransitionsToClosed()
        {
            var clock = new DayClock(1, new FakeBalanceConfig());
            clock.OpenRestaurant();
            clock.CloseRestaurant();
            Assert.That(clock.Phase, Is.EqualTo(DayPhase.Closed));
        }

        [Test]
        public void CloseRestaurant_WhenAlreadyClosed_IsNoOp()
        {
            var clock = new DayClock(1, new FakeBalanceConfig());
            clock.OpenRestaurant();
            clock.CloseRestaurant();

            Assert.That(() => clock.CloseRestaurant(), Throws.Nothing);
            Assert.That(clock.Phase, Is.EqualTo(DayPhase.Closed));
        }

        [Test]
        public void CloseRestaurant_FromPrep_Throws_AndPhaseUnchanged()
        {
            var clock = new DayClock(1, new FakeBalanceConfig());
            Assert.That(() => clock.CloseRestaurant(), Throws.InvalidOperationException);
            Assert.That(clock.Phase, Is.EqualTo(DayPhase.Prep));
        }

        [Test]
        public void OpenRestaurant_AfterClosed_Throws_AndPhaseUnchanged()
        {
            var clock = new DayClock(1, new FakeBalanceConfig());
            clock.OpenRestaurant();
            clock.CloseRestaurant();

            Assert.That(() => clock.OpenRestaurant(), Throws.InvalidOperationException);
            Assert.That(clock.Phase, Is.EqualTo(DayPhase.Closed));
        }

        [Test]
        public void Tick_WithinTheSameDay_NeverChangesPhaseOnItsOwn()
        {
            var cfg = new FakeBalanceConfig { DayLengthSeconds = 900f };
            var clock = new DayClock(1, cfg);
            clock.OpenRestaurant();

            clock.Tick(100f, cfg);
            clock.Tick(200f, cfg);

            Assert.That(clock.Phase, Is.EqualTo(DayPhase.Open), "Tick alone must never open/close the restaurant");
            Assert.That(clock.Day, Is.EqualTo(1));
        }

        // ---- Day wraparound ----

        [Test]
        public void Tick_PastDayLength_IncrementsDay_CarriesRemainder_ResetsPhaseToPrep()
        {
            var cfg = new FakeBalanceConfig { DayLengthSeconds = 900f };
            var clock = new DayClock(1, cfg);
            clock.OpenRestaurant();
            clock.CloseRestaurant();

            clock.Tick(950f, cfg);

            Assert.That(clock.Day, Is.EqualTo(2));
            Assert.That(clock.TimeOfDaySeconds, Is.EqualTo(50f).Within(0.001f));
            Assert.That(clock.Phase, Is.EqualTo(DayPhase.Prep));
        }

        [Test]
        public void Tick_SpanningMultipleDayLengths_IncrementsDayOncePerWrap()
        {
            var cfg = new FakeBalanceConfig { DayLengthSeconds = 100f };
            var clock = new DayClock(1, cfg);

            clock.Tick(250f, cfg); // two full wraps + 50s remainder

            Assert.That(clock.Day, Is.EqualTo(3));
            Assert.That(clock.TimeOfDaySeconds, Is.EqualTo(50f).Within(0.001f));
            Assert.That(clock.Phase, Is.EqualTo(DayPhase.Prep));
        }

        [Test]
        public void Tick_ExactlyAtDayLength_WrapsToNextDay()
        {
            var cfg = new FakeBalanceConfig { DayLengthSeconds = 900f };
            var clock = new DayClock(1, cfg);

            clock.Tick(900f, cfg);

            Assert.That(clock.Day, Is.EqualTo(2));
            Assert.That(clock.TimeOfDaySeconds, Is.EqualTo(0f).Within(0.001f));
            Assert.That(clock.Phase, Is.EqualTo(DayPhase.Prep));
        }

        // ---- Time-scale independence (catches accumulation bugs) ----

        [Test]
        public void Tick_ManySmallSteps_ProducesSameEndStateAs_OneLargeTick()
        {
            var cfgA = new FakeBalanceConfig { DayLengthSeconds = 900f };
            var cfgB = new FakeBalanceConfig { DayLengthSeconds = 900f };
            var clockA = new DayClock(1, cfgA);
            var clockB = new DayClock(1, cfgB);

            const float total = 2350f; // > 2 full days
            const int steps = 1000;
            float dt = total / steps;

            for (int i = 0; i < steps; i++)
                clockA.Tick(dt, cfgA);

            clockB.Tick(total, cfgB);

            Assert.That(clockA.Day, Is.EqualTo(clockB.Day));
            Assert.That(clockA.TimeOfDaySeconds, Is.EqualTo(clockB.TimeOfDaySeconds).Within(0.01f));
            Assert.That(clockA.Phase, Is.EqualTo(clockB.Phase));
        }

        [Test]
        public void Tick_ManySmallStepsWithinASingleDay_MatchesOneEquivalentTick()
        {
            var cfgA = new FakeBalanceConfig { DayLengthSeconds = 900f };
            var cfgB = new FakeBalanceConfig { DayLengthSeconds = 900f };
            var clockA = new DayClock(1, cfgA);
            var clockB = new DayClock(1, cfgB);

            const float total = 500f;
            const int steps = 250;
            float dt = total / steps;

            for (int i = 0; i < steps; i++)
                clockA.Tick(dt, cfgA);

            clockB.Tick(total, cfgB);

            Assert.That(clockA.TimeOfDaySeconds, Is.EqualTo(clockB.TimeOfDaySeconds).Within(0.01f));
            Assert.That(clockA.Day, Is.EqualTo(clockB.Day));
            Assert.That(clockA.Phase, Is.EqualTo(clockB.Phase));
        }
    }
}
