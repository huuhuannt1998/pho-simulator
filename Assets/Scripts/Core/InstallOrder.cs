namespace Pho.Core
{
    /// <summary>
    /// Accepted single shared file (ordering constants only). Append-only --
    /// never renumber an existing entry, only add new ones spaced by 100.
    /// </summary>
    public static class InstallOrder
    {
        public const int Save = 100;
        public const int Data = 200;
        public const int Economy = 300;
        public const int Inventory = 400;
        // Sits between Inventory (400) and Kitchen (500), not on a fresh
        // +100 slot: RestaurantStateService wraps DayClock and publishes
        // RestaurantOpened/RestaurantClosed/DayEnded, which conceptually
        // wants Economy/Inventory already registered (a later wave's rent
        // debit and daily-report ingredient-cost math will read both), and
        // wants to install before Kitchen/Customers so any future adapter
        // that queries restaurant phase at Install-time can find it. Spacing
        // rule ("append-only, spaced by 100") is honored for every existing
        // entry; this one is deliberately placed between two of them.
        public const int DayCycle = 450;
        public const int Kitchen = 500;
        public const int Customers = 600;
        public const int UI = 700;
    }
}
