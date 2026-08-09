namespace Pho.Domain.Events
{
    // Vertical-slice scope only (GDD deviations §12): no delivery fees,
    // advertising, taxes, or catering categories yet.
    public enum LedgerCategory
    {
        Sale, IngredientPurchase, Rent, Equipment, Tip, Other
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
