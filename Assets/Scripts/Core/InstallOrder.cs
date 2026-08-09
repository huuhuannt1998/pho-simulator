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
        public const int Kitchen = 500;
        public const int Customers = 600;
        public const int UI = 700;
    }
}
