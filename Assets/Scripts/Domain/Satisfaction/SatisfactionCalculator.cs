using Pho.Domain.Contracts;
using Pho.Domain.MathUtil;

namespace Pho.Domain.Satisfaction
{
    /// <summary>
    /// Pure function, GDD §12 direct implementation per architecture.md §7.
    ///
    /// Term weights (FoodQuality 0.40 / WaitTimeScore 0.20 / ServiceScore
    /// 0.15 / Cleanliness 0.15 / PriceValue 0.10) are the GDD §12 literal
    /// example weights, used verbatim -- not invented. <see cref="IBalanceConfig"/>
    /// does not yet carry a satisfaction-weights block (the M1 baseline only
    /// has the knobs architecture.md names explicitly), so they live here as
    /// a clearly-marked placeholder constants block.
    /// </summary>
    public static class SatisfactionCalculator
    {
        // TODO(balance): move onto IBalanceConfig once it grows a
        // satisfaction-weights block (M10). GDD §12 literal example weights.
        const float FoodQualityWeight = 0.40f;
        const float WaitTimeWeight = 0.20f;
        const float ServiceWeight = 0.15f;
        const float CleanlinessWeight = 0.15f;
        const float PriceValueWeight = 0.10f;

        // TODO(balance): reputation delta magnitude per tier. Not specified
        // by the GDD beyond "sign matches tier" -- this pass's judgment call.
        // Angry/Unhappy negative, Satisfied near-zero, Happy/Fan positive,
        // with a widening magnitude at the extremes (a Fan should move
        // reputation further than a merely-Happy customer).
        const float ReputationDeltaAngry = -5f;
        const float ReputationDeltaUnhappy = -2f;
        const float ReputationDeltaSatisfied = 0.2f;
        const float ReputationDeltaHappy = 1.5f;
        const float ReputationDeltaFan = 3f;

        // TODO(balance): tip tier multiplier -- a Fan tips more generously
        // than a customer who is merely Satisfied. Judgment call, not GDD-specified.
        const float TipMultiplierSatisfied = 1.0f;
        const float TipMultiplierHappy = 1.5f;
        const float TipMultiplierFan = 2.0f;

        public static SatisfactionResult Evaluate(in ServiceRecord r, ICustomerArchetype a, IBalanceConfig cfg)
        {
            float foodQuality01 = MathP.Clamp01(r.Quality.Overall01);

            // WaitTimeScore01: IBalanceConfig has no wait-time-score curve
            // yet, so this uses the archetype's own patience ceiling
            // (ICustomerArchetype.PatienceSeconds.max) as the "acceptable
            // wait" reference -- waiting the full patience window scores 0
            // on this term, instant service scores 1.
            float patienceCeiling = a.PatienceSeconds.max > 0f ? a.PatienceSeconds.max : 1f;
            float waitTimeScore01 = MathP.Clamp01(1f - r.WaitSeconds / patienceCeiling);

            // ServiceScore01: order accuracy stands in for "service" -- did
            // the kitchen deliver what was actually ordered.
            float serviceScore01 = MathP.Clamp01(r.Accuracy01);
            float cleanliness01 = MathP.Clamp01(r.Cleanliness01);
            float priceValue01 = ComputePriceValue01(r.PricePaid, r.ExpectedPrice);

            // Only Cleanliness/Service/Price have named archetype
            // sensitivities on ICustomerArchetype. A sensitivity of 0 makes
            // the archetype indifferent to that term (pulled to the neutral
            // midpoint 0.5); a sensitivity of 1 uses the term at full face
            // value. FoodQuality and WaitTimeScore have no corresponding
            // sensitivity field in the M1 contract, so they're applied raw.
            float serviceEffective = MathP.Lerp(0.5f, serviceScore01, a.ServiceSensitivity01);
            float cleanlinessEffective = MathP.Lerp(0.5f, cleanliness01, a.CleanlinessSensitivity01);
            float priceEffective = MathP.Lerp(0.5f, priceValue01, a.PriceSensitivity01);

            float weighted =
                FoodQualityWeight * foodQuality01 +
                WaitTimeWeight * waitTimeScore01 +
                ServiceWeight * serviceEffective +
                CleanlinessWeight * cleanlinessEffective +
                PriceValueWeight * priceEffective;

            float score0to100 = MathP.Clamp(weighted * 100f, 0f, 100f);
            var tier = TierFor(score0to100);
            var tip = ComputeTip(r.PricePaid, a, tier);
            var reputationDelta = ReputationDeltaFor(tier);
            bool willReturn = tier != SatisfactionTier.Angry && tier != SatisfactionTier.Unhappy;

            return new SatisfactionResult(score0to100, tier, tip, reputationDelta, willReturn);
        }

        static float ComputePriceValue01(decimal pricePaid, decimal expectedPrice)
        {
            if (expectedPrice <= 0m)
                return 1f;

            // 1.0 when paid <= expected; falls to 0.0 as the overrun reaches
            // 100% of the expected price (paid double or more).
            float overrun = (float)((pricePaid - expectedPrice) / expectedPrice);
            return MathP.Clamp01(1f - MathP.Clamp01(overrun));
        }

        static SatisfactionTier TierFor(float score0to100)
        {
            if (score0to100 <= 30f) return SatisfactionTier.Angry;
            if (score0to100 <= 50f) return SatisfactionTier.Unhappy;
            if (score0to100 <= 70f) return SatisfactionTier.Satisfied;
            if (score0to100 <= 90f) return SatisfactionTier.Happy;
            return SatisfactionTier.Fan;
        }

        // Deterministic stand-in for a probabilistic tip roll: IRandom is not
        // threaded into this pure function's signature (architecture.md §7),
        // so TipChance01 is treated as an expected-value multiplier rather
        // than a coin flip. No tip below Satisfied (architecture.md §11's
        // SatisfactionCalculatorTests row).
        static decimal ComputeTip(decimal pricePaid, ICustomerArchetype a, SatisfactionTier tier)
        {
            float tierMultiplier;
            switch (tier)
            {
                case SatisfactionTier.Satisfied:
                    tierMultiplier = TipMultiplierSatisfied;
                    break;
                case SatisfactionTier.Happy:
                    tierMultiplier = TipMultiplierHappy;
                    break;
                case SatisfactionTier.Fan:
                    tierMultiplier = TipMultiplierFan;
                    break;
                default:
                    return 0m;
            }

            var fraction = (decimal)MathP.Clamp01(a.TipFraction);
            var chance = (decimal)MathP.Clamp01(a.TipChance01);
            return pricePaid * fraction * chance * (decimal)tierMultiplier;
        }

        static float ReputationDeltaFor(SatisfactionTier tier)
        {
            switch (tier)
            {
                case SatisfactionTier.Angry: return ReputationDeltaAngry;
                case SatisfactionTier.Unhappy: return ReputationDeltaUnhappy;
                case SatisfactionTier.Satisfied: return ReputationDeltaSatisfied;
                case SatisfactionTier.Happy: return ReputationDeltaHappy;
                default: return ReputationDeltaFan;
            }
        }
    }
}
