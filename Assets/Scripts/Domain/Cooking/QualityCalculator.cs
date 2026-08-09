using Pho.Domain.Contracts;
using Pho.Domain.MathUtil;

namespace Pho.Domain.Cooking
{
    /// <summary>Scored breakdown of a served (or about-to-be-served) dish. See QualityCalculator.</summary>
    public readonly struct DishQuality
    {
        public readonly float Overall01;
        public readonly float IngredientQuality01;
        public readonly float BrothQuality01;
        public readonly float Accuracy01;
        public readonly float Temperature01;
        public readonly float Freshness01;

        public DishQuality(
            float overall01,
            float ingredientQuality01,
            float brothQuality01,
            float accuracy01,
            float temperature01,
            float freshness01)
        {
            Overall01 = overall01;
            IngredientQuality01 = ingredientQuality01;
            BrothQuality01 = brothQuality01;
            Accuracy01 = accuracy01;
            Temperature01 = temperature01;
            Freshness01 = freshness01;
        }
    }

    /// <summary>
    /// Pure function: scores a served dish. See architecture.md section 7.
    /// </summary>
    public static class QualityCalculator
    {
        // TODO(balance): IBalanceConfig (Domain/Contracts/DefInterfaces.cs) has
        // no QualityWeights section today -- it is frozen for this pass and
        // not something this milestone may edit. Per the task's explicit
        // guidance, these are clearly-marked placeholder constants (not a
        // silently hardcoded balance value) pending a real config surface;
        // equal-weighted (0.2 each, sum to 1.0) is the least-arbitrary default
        // until GDD balance data exists.
        const float WeightIngredientQuality = 0.2f;
        const float WeightBrothQuality = 0.2f;
        const float WeightAccuracy = 0.2f;
        const float WeightTemperature = 0.2f;
        const float WeightFreshness = 0.2f;

        // TODO(balance): also pending a real config surface. Temperature01 is
        // 1.0 within +/-ToleranceBandC of the recipe's target serve
        // temperature, falls off linearly to 0.0 at +/-FullPenaltyBandC, and
        // stays clamped at 0 beyond that -- a generous "still hot/still cold
        // enough" band before harshly penalising, then a hard floor once the
        // bowl is clearly stone cold or scalding.
        public const float TemperatureToleranceBandC = 5f;
        public const float TemperatureFullPenaltyBandC = 25f;

        public static DishQuality Score(BowlContents bowl, in RecipeMatch match, in BrothState broth, IBalanceConfig cfg)
        {
            var components = bowl.Components;

            float qualityWeightedSum = 0f;
            float freshnessWeightedSum = 0f;
            float amountTotal = 0f;

            for (int i = 0; i < components.Count; i++)
            {
                var c = components[i];
                qualityWeightedSum += c.Quality01 * c.Amount;
                freshnessWeightedSum += c.Freshness01 * c.Amount;
                amountTotal += c.Amount;
            }

            float ingredientQuality = amountTotal > 0f ? MathP.Clamp01(qualityWeightedSum / amountTotal) : 0f;
            float freshness = amountTotal > 0f ? MathP.Clamp01(freshnessWeightedSum / amountTotal) : 0f;
            float brothQuality = MathP.Clamp01(broth.Quality01);
            float accuracy = MathP.Clamp01(match.Accuracy01);
            float temperature = TemperatureScore(bowl.BrothTemperatureC, match.TargetServeTemperatureC);

            float overall =
                WeightIngredientQuality * ingredientQuality +
                WeightBrothQuality * brothQuality +
                WeightAccuracy * accuracy +
                WeightTemperature * temperature +
                WeightFreshness * freshness;

            return new DishQuality(
                MathP.Clamp01(overall),
                ingredientQuality,
                brothQuality,
                accuracy,
                temperature,
                freshness);
        }

        static float TemperatureScore(float actualC, float targetC)
        {
            float distance = actualC > targetC ? actualC - targetC : targetC - actualC;
            if (distance <= TemperatureToleranceBandC)
                return 1f;

            return 1f - MathP.InverseLerp(TemperatureToleranceBandC, TemperatureFullPenaltyBandC, distance);
        }
    }
}
