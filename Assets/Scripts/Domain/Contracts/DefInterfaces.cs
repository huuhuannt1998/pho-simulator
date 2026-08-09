using System.Collections.Generic;
using Pho.Domain.Identity;

namespace Pho.Domain.Contracts
{
    public interface IIngredientDef
    {
        IngredientId Id { get; }
        string DisplayName { get; }
        IngredientCategory Category { get; }
        float BasePurchasePrice { get; }
        float BaseQuality01 { get; }
        float ShelfLifeHours { get; }
        StorageRequirement Storage { get; }
        int UnitsPerCrate { get; }
    }

    public interface IRecipeDef
    {
        RecipeId Id { get; }
        string DisplayName { get; }
        IReadOnlyList<RecipeComponent> Components { get; }
        float BasePrice { get; }
        float PreparationDifficulty01 { get; }
        float TargetBrothVolumeLiters { get; }
        float TargetServeTemperatureC { get; }
    }

    public interface IEquipmentDef
    {
        EquipmentId Id { get; }
        string DisplayName { get; }
        EquipmentType EquipmentType { get; }
        int Tier { get; }
        float PurchaseCost { get; }
        float HeatRateMultiplier { get; }
        float CapacityMultiplier { get; }
        float QualityBonus01 { get; }
    }

    public interface ICustomerArchetype
    {
        ArchetypeId Id { get; }
        string DisplayName { get; }
        float SpawnWeight { get; }
        FloatRange PatienceSeconds { get; }
        FloatRange Budget { get; }
        float QualityExpectation01 { get; }
        float CleanlinessSensitivity01 { get; }
        float ServiceSensitivity01 { get; }
        float PriceSensitivity01 { get; }
        float TipChance01 { get; }
        float TipFraction { get; }
        float ReviewChance01 { get; }
        IReadOnlyList<RecipeId> PreferredRecipeIds { get; }
        FloatRange EatDurationSeconds { get; }
        int VisualVariantIndex { get; }
    }

    /// <summary>
    /// Grows additively as milestones land -- unlike IDs/events, new balance
    /// knobs are not save-breaking or cross-agent-contract-breaking, so this
    /// interface is not required to anticipate every future coefficient.
    /// This is the M1 baseline: only fields the architecture doc names
    /// explicitly. M4/M5/M9/M10/M11 add fields as those systems land.
    /// </summary>
    public interface IBalanceConfig
    {
        // Customers (referenced: GDD §59 patienceDecayRate example)
        float PatienceDecayRate { get; }

        // Recipe matching (referenced: §7 RecipeMatch.IsServable, defect weights)
        float MinServableAccuracy { get; }
        float DefectWeight(DefectKind kind);

        // Inventory (referenced: §2.3 InventoryModel.AdvanceSpoilage)
        float SpoilageRatePerHour { get; }

        // Day cycle / economy (referenced: M11 acceptance criteria)
        float DayLengthSeconds { get; }
        decimal RentAmount { get; }
        int RentIntervalDays { get; }
    }

    public interface IGameDatabase
    {
        bool TryGetIngredient(IngredientId id, out IIngredientDef def);
        bool TryGetRecipe(RecipeId id, out IRecipeDef def);
        bool TryGetEquipment(EquipmentId id, out IEquipmentDef def);
        bool TryGetArchetype(ArchetypeId id, out ICustomerArchetype def);

        IReadOnlyList<IIngredientDef> Ingredients { get; }
        IReadOnlyList<IRecipeDef> Recipes { get; }
        IReadOnlyList<IEquipmentDef> Equipment { get; }
        IReadOnlyList<ICustomerArchetype> Archetypes { get; }
    }

    // Forward-declared here (not in a separate file) because IBalanceConfig
    // needs it for DefectWeight(); the full Defect/RecipeMatch shape lands
    // with RecipeMatcher at M5.
    public enum DefectKind
    {
        MissingRequired, WrongIngredient, TooLittle, TooMuch, ExtraUnwanted, WrongSize, ColdBroth
    }
}
