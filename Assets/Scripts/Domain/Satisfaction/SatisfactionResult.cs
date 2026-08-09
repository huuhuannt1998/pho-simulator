namespace Pho.Domain.Satisfaction
{
    /// <summary>Output of <see cref="SatisfactionCalculator.Evaluate"/>.</summary>
    public readonly struct SatisfactionResult
    {
        public readonly float Score0to100;
        public readonly SatisfactionTier Tier;
        public readonly decimal Tip;
        public readonly float ReputationDelta;
        public readonly bool WillReturn;

        public SatisfactionResult(float score0to100, SatisfactionTier tier, decimal tip, float reputationDelta, bool willReturn)
        {
            Score0to100 = score0to100;
            Tier = tier;
            Tip = tip;
            ReputationDelta = reputationDelta;
            WillReturn = willReturn;
        }
    }
}
