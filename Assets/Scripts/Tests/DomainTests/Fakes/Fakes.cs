using System.Collections.Generic;
using System.Linq;
using Pho.Domain.Contracts;
using Pho.Domain.Identity;
using Pho.Domain.Infra;

namespace Pho.Domain.Tests.Fakes
{
    // POCO fakes for every Pho.Domain contract interface. Domain tests
    // instantiate these directly -- no Unity, no ScriptableObject.CreateInstance.

    public sealed class FakeIngredientDef : IIngredientDef
    {
        public IngredientId Id { get; set; }
        public string DisplayName { get; set; } = "Fake Ingredient";
        public IngredientCategory Category { get; set; } = IngredientCategory.Protein;
        public float BasePurchasePrice { get; set; } = 1f;
        public float BaseQuality01 { get; set; } = 0.7f;
        public float ShelfLifeHours { get; set; } = 48f;
        public StorageRequirement Storage { get; set; } = StorageRequirement.Refrigerated;
        public int UnitsPerCrate { get; set; } = 10;
    }

    public sealed class FakeRecipeDef : IRecipeDef
    {
        public RecipeId Id { get; set; }
        public string DisplayName { get; set; } = "Fake Recipe";
        public List<RecipeComponent> Components { get; set; } = new List<RecipeComponent>();
        IReadOnlyList<RecipeComponent> IRecipeDef.Components => Components;
        public float BasePrice { get; set; } = 9.5f;
        public float PreparationDifficulty01 { get; set; } = 0.3f;
        public float TargetBrothVolumeLiters { get; set; } = 0.5f;
        public float TargetServeTemperatureC { get; set; } = 70f;
    }

    public sealed class FakeEquipmentDef : IEquipmentDef
    {
        public EquipmentId Id { get; set; }
        public string DisplayName { get; set; } = "Fake Equipment";
        public EquipmentType EquipmentType { get; set; } = EquipmentType.Burner;
        public int Tier { get; set; } = 1;
        public float PurchaseCost { get; set; } = 100f;
        public float HeatRateMultiplier { get; set; } = 1f;
        public float CapacityMultiplier { get; set; } = 1f;
        public float QualityBonus01 { get; set; } = 0f;
    }

    public sealed class FakeCustomerArchetype : ICustomerArchetype
    {
        public ArchetypeId Id { get; set; }
        public string DisplayName { get; set; } = "Fake Archetype";
        public float SpawnWeight { get; set; } = 1f;
        public FloatRange PatienceSeconds { get; set; } = new FloatRange(60f, 120f);
        public FloatRange Budget { get; set; } = new FloatRange(8f, 15f);
        public float QualityExpectation01 { get; set; } = 0.5f;
        public float CleanlinessSensitivity01 { get; set; } = 0.5f;
        public float ServiceSensitivity01 { get; set; } = 0.5f;
        public float PriceSensitivity01 { get; set; } = 0.5f;
        public float TipChance01 { get; set; } = 0.3f;
        public float TipFraction { get; set; } = 0.1f;
        public float ReviewChance01 { get; set; } = 0.2f;
        public List<RecipeId> PreferredRecipeIds { get; set; } = new List<RecipeId>();
        IReadOnlyList<RecipeId> ICustomerArchetype.PreferredRecipeIds => PreferredRecipeIds;
        public FloatRange EatDurationSeconds { get; set; } = new FloatRange(30f, 60f);
        public int VisualVariantIndex { get; set; }
    }

    public sealed class FakeBalanceConfig : IBalanceConfig
    {
        public float PatienceDecayRate { get; set; } = 1f;
        public float MinServableAccuracy { get; set; } = 0.5f;
        public Dictionary<DefectKind, float> DefectWeights { get; set; } = new Dictionary<DefectKind, float>();
        public float DefectWeight(DefectKind kind) => DefectWeights.TryGetValue(kind, out var w) ? w : 0.5f;
        public float SpoilageRatePerHour { get; set; } = 0.01f;
        public float DayLengthSeconds { get; set; } = 900f;
        public decimal RentAmount { get; set; } = 100m;
        public int RentIntervalDays { get; set; } = 7;
    }

    public sealed class FakeGameDatabase : IGameDatabase
    {
        public List<IIngredientDef> IngredientsList { get; } = new List<IIngredientDef>();
        public List<IRecipeDef> RecipesList { get; } = new List<IRecipeDef>();
        public List<IEquipmentDef> EquipmentList { get; } = new List<IEquipmentDef>();
        public List<ICustomerArchetype> ArchetypesList { get; } = new List<ICustomerArchetype>();

        public IReadOnlyList<IIngredientDef> Ingredients => IngredientsList;
        public IReadOnlyList<IRecipeDef> Recipes => RecipesList;
        public IReadOnlyList<IEquipmentDef> Equipment => EquipmentList;
        public IReadOnlyList<ICustomerArchetype> Archetypes => ArchetypesList;

        public bool TryGetIngredient(IngredientId id, out IIngredientDef def)
        {
            def = IngredientsList.FirstOrDefault(i => i.Id.Equals(id));
            return def != null;
        }

        public bool TryGetRecipe(RecipeId id, out IRecipeDef def)
        {
            def = RecipesList.FirstOrDefault(r => r.Id.Equals(id));
            return def != null;
        }

        public bool TryGetEquipment(EquipmentId id, out IEquipmentDef def)
        {
            def = EquipmentList.FirstOrDefault(e => e.Id.Equals(id));
            return def != null;
        }

        public bool TryGetArchetype(ArchetypeId id, out ICustomerArchetype def)
        {
            def = ArchetypesList.FirstOrDefault(a => a.Id.Equals(id));
            return def != null;
        }
    }

    public sealed class FakeClock : IClock
    {
        public float NowSeconds { get; set; }
        public void Advance(float seconds) => NowSeconds += seconds;
    }
}
