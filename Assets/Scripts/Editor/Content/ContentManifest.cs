using Pho.Data;
using Pho.Domain.Contracts;

namespace Pho.EditorTools.Content
{
    /// <summary>
    /// Plain-data manifest describing every Assets/Content/*.asset instance
    /// that <see cref="ContentGenerator"/> should create or update. Agents
    /// edit THIS file to add or change content -- never hand-edit the
    /// generated .asset files (docs/architecture.md section 10, "Shared
    /// files that will cause conflicts" -> "Assets/Content/*.asset").
    ///
    /// Scope for this pass: the vertical slice's first two recipes, Phở Tái
    /// and Phở Chín (docs/architecture.md section 12 scope-cut table: "Build
    /// 2 first, add Đặc Biệt once matching is proven"). Equipment and
    /// customer-archetype content are intentionally NOT authored yet -- those
    /// land with M13 and M6/M7 respectively; GameDatabase gets empty arrays
    /// for them until then.
    /// </summary>
    public static class ContentManifest
    {
        public struct IngredientEntry
        {
            public string id;
            public string displayName;
            public IngredientCategory category;
            public float basePurchasePrice;
            public float baseQuality01;
            public float shelfLifeHours;
            public StorageRequirement storage;
            public int unitsPerCrate;
        }

        public struct RecipeEntry
        {
            public string id;
            public string displayName;
            public RecipeComponent[] components;
            public float basePrice;
            public float preparationDifficulty01;
            public float targetBrothVolumeLiters;
            public float targetServeTemperatureC;
        }

        public struct BalanceConfigEntry
        {
            public float patienceDecayRate;
            public float minServableAccuracy;
            public GameBalanceConfig.DefectWeightEntry[] defectWeights;
            public float spoilageRatePerHour;
            public float dayLengthSeconds;
            public decimal rentAmount;
            public int rentIntervalDays;
        }

        // ------------------------------------------------------------------
        // Ingredients
        // ------------------------------------------------------------------
        //
        // ing.beef_brisket (raw, for Tái) and ing.beef_well_done (for Chín)
        // are modeled as two SEPARATE purchasable ingredients rather than one
        // ingredient with a "prep state" flag. Reasoning: the frozen M1
        // contracts (IIngredientDef, RecipeComponent, RecipeMatcher's
        // slot-based matching) have no notion of a cooking/doneness
        // transform -- there is no "raw -> cooked" pipeline anywhere in the
        // Domain layer, and adding one is out of scope for a content pass
        // (it would be a Domain change, which only the M1 freeze owner may
        // make). Two distinct ingredient IDs is also how real phở kitchens
        // actually work: rare beef is sliced raw to order, while chín beef
        // is a separately-prepped simmered cut with different shelf life and
        // cost. This lets RecipeMatcher correctly flag "used raw brisket in
        // a Chín bowl" as a WrongIngredient defect with zero extra plumbing.

        public static readonly IngredientEntry[] Ingredients =
        {
            new IngredientEntry
            {
                id = "ing.rice_noodles",
                displayName = "Rice Noodles (Bánh Phở)",
                category = IngredientCategory.Noodle,
                basePurchasePrice = 0.80f,
                baseQuality01 = 0.85f,
                shelfLifeHours = 96f,            // 4 days, refrigerated fresh noodles
                storage = StorageRequirement.Refrigerated,
                unitsPerCrate = 100,
            },
            new IngredientEntry
            {
                id = "ing.beef_brisket",
                displayName = "Beef Brisket (Raw, thin-sliced)",
                category = IngredientCategory.Protein,
                basePurchasePrice = 4.50f,
                baseQuality01 = 0.80f,
                shelfLifeHours = 48f,            // 2 days, raw meat
                storage = StorageRequirement.Refrigerated,
                unitsPerCrate = 40,
            },
            new IngredientEntry
            {
                id = "ing.beef_well_done",
                displayName = "Beef Well-Done (Simmered)",
                category = IngredientCategory.Protein,
                basePurchasePrice = 5.00f,
                baseQuality01 = 0.85f,
                shelfLifeHours = 36f,            // pre-cooked cut, shorter fridge life once prepped
                storage = StorageRequirement.Refrigerated,
                unitsPerCrate = 40,
            },
            new IngredientEntry
            {
                id = "ing.onion",
                displayName = "Onion (sliced)",
                category = IngredientCategory.Aromatic,
                basePurchasePrice = 0.30f,
                baseQuality01 = 0.85f,
                shelfLifeHours = 240f,           // 10 days, ambient
                storage = StorageRequirement.Ambient,
                unitsPerCrate = 150,
            },
            new IngredientEntry
            {
                id = "ing.herbs_mixed",
                displayName = "Mixed Herbs (Thai Basil, Cilantro, Sawtooth)",
                category = IngredientCategory.Herb,
                basePurchasePrice = 0.40f,
                baseQuality01 = 0.75f,
                shelfLifeHours = 48f,            // herbs wilt fast, 2 days
                storage = StorageRequirement.Refrigerated,
                unitsPerCrate = 100,
            },
            new IngredientEntry
            {
                id = "ing.broth_base",
                displayName = "Phở Broth Base (Beef Bone Stock)",
                category = IngredientCategory.Broth,
                basePurchasePrice = 2.00f,
                baseQuality01 = 0.80f,
                shelfLifeHours = 72f,            // 3 days once portioned into crates
                storage = StorageRequirement.Refrigerated,
                unitsPerCrate = 20,
            },
        };

        // ------------------------------------------------------------------
        // Recipes
        // ------------------------------------------------------------------
        //
        // Aromatic/Herb are optional garnish slots (required = false) --
        // omitting onion/herbs never makes a bowl unservable, it just costs
        // accuracy via ExtraUnwanted/garnish-miss scoring in RecipeMatcher.
        // Noodle/Broth/Protein are the required backbone of a phở bowl.

        public static readonly RecipeEntry[] Recipes =
        {
            new RecipeEntry
            {
                id = "rec.pho_tai",
                displayName = "Phở Tái (Rare Beef)",
                components = new[]
                {
                    new RecipeComponent { slot = ComponentSlot.Noodle, ingredientId = "ing.rice_noodles", amount = 1.0f, tolerance = 0.1f, required = true },
                    new RecipeComponent { slot = ComponentSlot.Broth, ingredientId = "ing.broth_base", amount = 1.0f, tolerance = 0.1f, required = true },
                    new RecipeComponent { slot = ComponentSlot.Protein, ingredientId = "ing.beef_brisket", amount = 1.0f, tolerance = 0.1f, required = true },
                    new RecipeComponent { slot = ComponentSlot.Aromatic, ingredientId = "ing.onion", amount = 0.3f, tolerance = 0.2f, required = false },
                    new RecipeComponent { slot = ComponentSlot.Herb, ingredientId = "ing.herbs_mixed", amount = 0.3f, tolerance = 0.2f, required = false },
                },
                basePrice = 9.50f,
                preparationDifficulty01 = 0.40f,
                targetBrothVolumeLiters = 0.5f,
                targetServeTemperatureC = 70f,   // hot broth "carry-cooks" the raw brisket slices
            },
            new RecipeEntry
            {
                id = "rec.pho_chin",
                displayName = "Phở Chín (Well-Done Beef)",
                components = new[]
                {
                    new RecipeComponent { slot = ComponentSlot.Noodle, ingredientId = "ing.rice_noodles", amount = 1.0f, tolerance = 0.1f, required = true },
                    new RecipeComponent { slot = ComponentSlot.Broth, ingredientId = "ing.broth_base", amount = 1.0f, tolerance = 0.1f, required = true },
                    new RecipeComponent { slot = ComponentSlot.Protein, ingredientId = "ing.beef_well_done", amount = 1.0f, tolerance = 0.1f, required = true },
                    new RecipeComponent { slot = ComponentSlot.Aromatic, ingredientId = "ing.onion", amount = 0.3f, tolerance = 0.2f, required = false },
                    new RecipeComponent { slot = ComponentSlot.Herb, ingredientId = "ing.herbs_mixed", amount = 0.3f, tolerance = 0.2f, required = false },
                },
                basePrice = 10.50f,
                preparationDifficulty01 = 0.35f,
                targetBrothVolumeLiters = 0.5f,
                targetServeTemperatureC = 75f,   // beef already cooked; served hotter, no carry-cook need
            },
        };

        // ------------------------------------------------------------------
        // Balance config
        // ------------------------------------------------------------------
        //
        // Placeholder-but-non-degenerate defaults for every IBalanceConfig
        // field, tunable later. MissingRequired/WrongIngredient are weighted
        // heaviest (a bowl missing its protein or noodles is fundamentally
        // wrong); ExtraUnwanted is cheapest (an extra pinch of garnish is a
        // minor ding), matching the doc's "garnish slots are cheap, protein
        // slots are expensive" guidance for RecipeMatcher severity scaling.

        public static readonly BalanceConfigEntry Balance = new BalanceConfigEntry
        {
            patienceDecayRate = 1.0f,
            minServableAccuracy = 0.5f,
            defectWeights = new[]
            {
                new GameBalanceConfig.DefectWeightEntry { kind = DefectKind.MissingRequired, weight = 0.60f },
                new GameBalanceConfig.DefectWeightEntry { kind = DefectKind.WrongIngredient, weight = 0.50f },
                new GameBalanceConfig.DefectWeightEntry { kind = DefectKind.TooLittle, weight = 0.15f },
                new GameBalanceConfig.DefectWeightEntry { kind = DefectKind.TooMuch, weight = 0.10f },
                new GameBalanceConfig.DefectWeightEntry { kind = DefectKind.ExtraUnwanted, weight = 0.05f },
                new GameBalanceConfig.DefectWeightEntry { kind = DefectKind.WrongSize, weight = 0.20f },
                new GameBalanceConfig.DefectWeightEntry { kind = DefectKind.ColdBroth, weight = 0.35f },
            },
            spoilageRatePerHour = 0.015f,        // ~1.5%/hr, consistent with the 36-240hr shelf lives above
            dayLengthSeconds = 900f,             // 15 real-world minutes per game day
            rentAmount = 350m,
            rentIntervalDays = 7,
        };
    }
}
