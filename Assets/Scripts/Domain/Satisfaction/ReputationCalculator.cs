using Pho.Domain.MathUtil;

namespace Pho.Domain.Satisfaction
{
    /// <summary>
    /// Minimal clamped reputation accumulator. Full tier-based effects (more
    /// customers, more demanding customers at higher reputation) are out of
    /// scope for this pass -- just the clamp.
    /// </summary>
    public static class ReputationCalculator
    {
        public static float Apply(float currentReputation0to100, float delta)
        {
            return MathP.Clamp(currentReputation0to100 + delta, 0f, 100f);
        }
    }
}
