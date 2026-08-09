using Pho.Domain.Cooking;

namespace Pho.Domain.Satisfaction
{
    /// <summary>
    /// Inputs to <see cref="SatisfactionCalculator.Evaluate"/>. Exact shape
    /// from architecture.md §7's "Satisfaction -- pure function" subsection.
    ///
    /// Integration-pass note: this field was a stand-in plain float
    /// (OverallQuality01) until the Cooking milestone's DishQuality landed --
    /// both were built in the same parallel wave, and Cooking's type wasn't
    /// mergeable yet when this file was authored. Reconciled to the doc's
    /// literal `DishQuality Quality` shape immediately after both merged,
    /// before any other code could come to depend on the simplified form.
    /// </summary>
    public readonly struct ServiceRecord
    {
        public readonly DishQuality Quality;
        public readonly float Accuracy01;
        public readonly float WaitSeconds;
        public readonly float Cleanliness01;
        public readonly decimal PricePaid;
        public readonly decimal ExpectedPrice;

        public ServiceRecord(
            DishQuality quality,
            float accuracy01,
            float waitSeconds,
            float cleanliness01,
            decimal pricePaid,
            decimal expectedPrice)
        {
            Quality = quality;
            Accuracy01 = accuracy01;
            WaitSeconds = waitSeconds;
            Cleanliness01 = cleanliness01;
            PricePaid = pricePaid;
            ExpectedPrice = expectedPrice;
        }
    }
}
