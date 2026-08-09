using System.Collections.Generic;

namespace Pho.Save.Dto
{
    // Pure POCOs, Newtonsoft, public fields -- architecture.md section 4.
    // Deliberately decoupled from Pho.Domain runtime/event types: only
    // stable string IDs cross the save-file boundary, and the shapes here
    // are independent of (though they mirror) their in-memory counterparts,
    // so a Domain refactor never forces a save-format migration by accident.

    public sealed class SaveFile
    {
        public int schemaVersion;          // ALWAYS first, ALWAYS present
        public string gameVersion;         // "0.1.3" -- informational only
        public long savedAtUnixSeconds;

        public WorldDto world;
        public EconomyDto economy;
        public InventoryDto inventory;
        public ProgressionDto progression;
        public RestaurantDto restaurant;
        public PlayerDto player;
    }

    public sealed class WorldDto
    {
        public int day;
        public float timeOfDaySeconds;
        public string phase; // Prep|Open|Closed
    }

    public sealed class EconomyDto
    {
        public decimal cash;
        public List<DailyReportDto> recentDays; // last 7
    }

    /// <summary>
    /// Independent save-format mirror of Pho.Domain.Events.DailyReport --
    /// deliberately not a reference to the Domain event-report type, so the
    /// save format never silently changes shape when the runtime event
    /// struct evolves.
    /// </summary>
    public sealed class DailyReportDto
    {
        public int day;
        public decimal revenue;
        public decimal ingredientCost;
        public decimal rent;
        public decimal utilities;
        public decimal profit;
    }

    public sealed class InventoryDto
    {
        public List<LotDto> lots;
    }

    public sealed class LotDto
    {
        public string ingredientId;
        public float quantity;
        public float qualityAtPurchase;
        public float freshness;
        public int purchasedOnDay;
        public string storage;
    }

    public sealed class ProgressionDto
    {
        public List<string> unlockedRecipeIds;
        public List<string> ownedEquipmentIds;
        public Dictionary<string, decimal> menuPrices; // recipeId -> price
        public float reputation;
        public Dictionary<string, bool> flags;          // tutorial/progression flags
    }

    public sealed class RestaurantDto
    {
        public float cleanliness01;
        public List<string> dirtyTableIds;
    }

    public sealed class PlayerDto
    {
        public Vec3Dto position;
        public float yaw;
    }

    /// <summary>
    /// Independent save-format mirror of Pho.Domain.Infra.Vec3 -- same
    /// shape, but a separate plain DTO so the save format doesn't couple to
    /// the Domain math type.
    /// </summary>
    public sealed class Vec3Dto
    {
        public float x, y, z;
    }
}
