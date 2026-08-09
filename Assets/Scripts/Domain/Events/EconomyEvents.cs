namespace Pho.Domain.Events
{
    // Vertical-slice scope only (GDD deviations §12): no delivery fees,
    // advertising, taxes, or catering categories yet.
    public enum LedgerCategory
    {
        Sale, IngredientPurchase, Rent, Equipment, Tip, Other,

        // APPENDED, never inserted -- new members go on the end so every
        // existing ordinal keeps its value.
        //
        // Property is separate from Equipment because the expansion system
        // buys BUILDINGS: without it a $6,000 unit and a $450 burner land in
        // the same bucket, and the first thing anyone asks of a daily report
        // is "where did the money actually go". Both are capital purchases,
        // so DayLedgerAccumulator ignores this exactly as it ignores
        // Equipment -- it is a reporting distinction, not a P&L one.
        Property,
    }

    public readonly struct CashChanged : IGameEvent
    {
        public readonly decimal NewBalance;
        public readonly decimal Delta;
        public readonly LedgerCategory Category;

        public CashChanged(decimal newBalance, decimal delta, LedgerCategory category)
        {
            NewBalance = newBalance;
            Delta = delta;
            Category = category;
        }
    }

    /// <summary>
    /// Placeholder shape frozen at M1 so DayEnded's signature never has to
    /// change; fields are filled in at M11 (DayCycleService/DailyReport).
    /// </summary>
    public sealed class DailyReport
    {
        public int Day;
        public decimal Revenue;
        public decimal IngredientCost;
        public decimal Rent;
        public decimal Utilities;
        public decimal Profit;
    }

    public readonly struct DayEnded : IGameEvent
    {
        public readonly int Day;
        public readonly DailyReport Report;

        public DayEnded(int day, DailyReport report)
        {
            Day = day;
            Report = report;
        }
    }
}
