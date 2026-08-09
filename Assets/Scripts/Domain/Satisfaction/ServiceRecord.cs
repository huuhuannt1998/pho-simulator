namespace Pho.Domain.Satisfaction
{
    /// <summary>
    /// Inputs to <see cref="SatisfactionCalculator.Evaluate"/>. Exact shape
    /// from architecture.md §7's "Satisfaction -- pure function" subsection,
    /// with one deliberate simplification (see <see cref="OverallQuality01"/>).
    /// </summary>
    public readonly struct ServiceRecord
    {
        /// <summary>
        /// CROSS-AGENT SIMPLIFICATION (flag for the integration pass): the
        /// architecture doc's ServiceRecord.Quality is typed as the Cooking
        /// agent's <c>DishQuality</c> struct (Assets/Scripts/Domain/Cooking/),
        /// which is not merged as of this pass and is not this agent's to
        /// create. This field stands in for <c>DishQuality.Overall01</c> as a
        /// plain float so Satisfaction stays compilable and testable in
        /// isolation. Once both land, a follow-up pass should either change
        /// this field's type to <c>DishQuality</c> (and have callers pass the
        /// whole struct) or leave it as a float and have callers pass
        /// <c>DishQuality.Overall01</c> explicitly -- integration agent's call.
        /// </summary>
        public readonly float OverallQuality01;

        public readonly float Accuracy01;
        public readonly float WaitSeconds;
        public readonly float Cleanliness01;
        public readonly decimal PricePaid;
        public readonly decimal ExpectedPrice;

        public ServiceRecord(
            float overallQuality01,
            float accuracy01,
            float waitSeconds,
            float cleanliness01,
            decimal pricePaid,
            decimal expectedPrice)
        {
            OverallQuality01 = overallQuality01;
            Accuracy01 = accuracy01;
            WaitSeconds = waitSeconds;
            Cleanliness01 = cleanliness01;
            PricePaid = pricePaid;
            ExpectedPrice = expectedPrice;
        }
    }
}
