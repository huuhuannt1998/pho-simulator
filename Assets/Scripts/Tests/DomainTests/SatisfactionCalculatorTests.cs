using NUnit.Framework;
using Pho.Domain.Contracts;
using Pho.Domain.Satisfaction;
using Pho.Domain.Tests.Fakes;

namespace Pho.Domain.Tests
{
    [TestFixture]
    public class SatisfactionCalculatorTests
    {
        // Builds a ServiceRecord + archetype where every term evaluates to
        // exactly `value` (0..1) and every archetype sensitivity is 1.0 (full
        // face value, no neutral-midpoint damping). Because the five term
        // weights sum to 1.0 (0.40+0.20+0.15+0.15+0.10), this makes
        // Score0to100 == value * 100 exactly -- a simple, precise way to walk
        // the whole tier range.
        static (ServiceRecord record, FakeCustomerArchetype archetype) BuildUniform(float value)
        {
            var archetype = new FakeCustomerArchetype
            {
                CleanlinessSensitivity01 = 1f,
                ServiceSensitivity01 = 1f,
                PriceSensitivity01 = 1f,
                PatienceSeconds = new FloatRange(0f, 100f),
                TipChance01 = 1f,
                TipFraction = 0.1f,
            };

            const decimal expectedPrice = 10m;
            float waitSeconds = 100f * (1f - value);              // waitTimeScore01 == value
            decimal pricePaid = expectedPrice * (decimal)(2f - value); // priceValue01 == value

            var record = new ServiceRecord(
                overallQuality01: value,
                accuracy01: value,
                waitSeconds: waitSeconds,
                cleanliness01: value,
                pricePaid: pricePaid,
                expectedPrice: expectedPrice);

            return (record, archetype);
        }

        [TestCase(0.00f, SatisfactionTier.Angry)]
        [TestCase(0.40f, SatisfactionTier.Unhappy)]
        [TestCase(0.60f, SatisfactionTier.Satisfied)]
        [TestCase(0.80f, SatisfactionTier.Happy)]
        [TestCase(1.00f, SatisfactionTier.Fan)]
        public void Evaluate_UniformInputs_ReachEveryTier(float value, SatisfactionTier expectedTier)
        {
            var (record, archetype) = BuildUniform(value);
            var cfg = new FakeBalanceConfig();

            var result = SatisfactionCalculator.Evaluate(in record, archetype, cfg);

            Assert.That(result.Score0to100, Is.EqualTo(value * 100f).Within(0.01f));
            Assert.That(result.Tier, Is.EqualTo(expectedTier));
        }

        [Test]
        public void Evaluate_DifferentCleanlinessSensitivity_ProducesDifferentScore_ForSameServiceRecord()
        {
            // Cleanliness deliberately far from the neutral midpoint (0.5) so
            // sensitivity divergence is unmissable.
            var record = new ServiceRecord(
                overallQuality01: 0.7f,
                accuracy01: 0.7f,
                waitSeconds: 30f,
                cleanliness01: 0.1f,
                pricePaid: 10m,
                expectedPrice: 10m);
            var cfg = new FakeBalanceConfig();

            var indifferent = new FakeCustomerArchetype
            {
                CleanlinessSensitivity01 = 0f, // pulled to neutral 0.5 regardless of actual cleanliness
                ServiceSensitivity01 = 0.5f,
                PriceSensitivity01 = 0.5f,
                PatienceSeconds = new FloatRange(0f, 100f),
            };
            var sensitive = new FakeCustomerArchetype
            {
                CleanlinessSensitivity01 = 1f, // uses the poor 0.1 cleanliness at full face value
                ServiceSensitivity01 = 0.5f,
                PriceSensitivity01 = 0.5f,
                PatienceSeconds = new FloatRange(0f, 100f),
            };

            var indifferentResult = SatisfactionCalculator.Evaluate(in record, indifferent, cfg);
            var sensitiveResult = SatisfactionCalculator.Evaluate(in record, sensitive, cfg);

            Assert.That(sensitiveResult.Score0to100, Is.Not.EqualTo(indifferentResult.Score0to100));
            Assert.That(sensitiveResult.Score0to100, Is.LessThan(indifferentResult.Score0to100),
                "the archetype that actually notices poor cleanliness should score the same service lower");
        }

        [Test]
        public void Evaluate_BelowSatisfiedTier_TipIsExactlyZero()
        {
            var cfg = new FakeBalanceConfig();

            var (angryRecord, angryArchetype) = BuildUniform(0.00f);
            var angry = SatisfactionCalculator.Evaluate(in angryRecord, angryArchetype, cfg);
            Assert.That(angry.Tier, Is.EqualTo(SatisfactionTier.Angry));
            Assert.That(angry.Tip, Is.EqualTo(0m));

            var (unhappyRecord, unhappyArchetype) = BuildUniform(0.40f);
            var unhappy = SatisfactionCalculator.Evaluate(in unhappyRecord, unhappyArchetype, cfg);
            Assert.That(unhappy.Tier, Is.EqualTo(SatisfactionTier.Unhappy));
            Assert.That(unhappy.Tip, Is.EqualTo(0m));
        }

        [Test]
        public void Evaluate_AtOrAboveSatisfiedTier_TipIsPositive()
        {
            var cfg = new FakeBalanceConfig();

            var (satisfiedRecord, satisfiedArchetype) = BuildUniform(0.60f);
            var satisfied = SatisfactionCalculator.Evaluate(in satisfiedRecord, satisfiedArchetype, cfg);
            Assert.That(satisfied.Tier, Is.EqualTo(SatisfactionTier.Satisfied));
            Assert.That(satisfied.Tip, Is.GreaterThan(0m));

            var (fanRecord, fanArchetype) = BuildUniform(1.00f);
            var fan = SatisfactionCalculator.Evaluate(in fanRecord, fanArchetype, cfg);
            Assert.That(fan.Tier, Is.EqualTo(SatisfactionTier.Fan));
            Assert.That(fan.Tip, Is.GreaterThan(0m));
        }

        [Test]
        public void Evaluate_ReputationDeltaSign_MatchesTier()
        {
            var cfg = new FakeBalanceConfig();

            SatisfactionResult Evaluate(float value)
            {
                var (record, archetype) = BuildUniform(value);
                return SatisfactionCalculator.Evaluate(in record, archetype, cfg);
            }

            Assert.That(Evaluate(0.00f).ReputationDelta, Is.LessThan(0f), "Angry");
            Assert.That(Evaluate(0.40f).ReputationDelta, Is.LessThan(0f), "Unhappy");
            Assert.That(Evaluate(0.60f).ReputationDelta, Is.EqualTo(0f).Within(1f), "Satisfied should be near-zero");
            Assert.That(Evaluate(0.80f).ReputationDelta, Is.GreaterThan(0f), "Happy");
            Assert.That(Evaluate(1.00f).ReputationDelta, Is.GreaterThan(0f), "Fan");
        }

        [Test]
        public void Evaluate_WillReturn_TrueAtOrAboveSatisfied_FalseBelow()
        {
            var cfg = new FakeBalanceConfig();

            SatisfactionResult Evaluate(float value)
            {
                var (record, archetype) = BuildUniform(value);
                return SatisfactionCalculator.Evaluate(in record, archetype, cfg);
            }

            Assert.That(Evaluate(0.00f).WillReturn, Is.False);
            Assert.That(Evaluate(0.40f).WillReturn, Is.False);
            Assert.That(Evaluate(0.60f).WillReturn, Is.True);
            Assert.That(Evaluate(0.80f).WillReturn, Is.True);
            Assert.That(Evaluate(1.00f).WillReturn, Is.True);
        }
    }

    [TestFixture]
    public class ReputationCalculatorTests
    {
        [Test]
        public void Apply_WithinRange_AddsDelta()
        {
            Assert.That(ReputationCalculator.Apply(50f, 10f), Is.EqualTo(60f).Within(0.0001f));
            Assert.That(ReputationCalculator.Apply(50f, -10f), Is.EqualTo(40f).Within(0.0001f));
        }

        [Test]
        public void Apply_AboveHundred_ClampsToHundred()
        {
            Assert.That(ReputationCalculator.Apply(95f, 20f), Is.EqualTo(100f));
        }

        [Test]
        public void Apply_BelowZero_ClampsToZero()
        {
            Assert.That(ReputationCalculator.Apply(5f, -20f), Is.EqualTo(0f));
        }
    }
}
