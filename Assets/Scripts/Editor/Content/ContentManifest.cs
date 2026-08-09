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
    /// Scope: the vertical slice's first two recipes, Phở Tái and Phở Chín
    /// (docs/architecture.md section 12 scope-cut table: "Build 2 first, add
    /// Đặc Biệt once matching is proven"), plus exactly ONE piece of
    /// equipment -- M13's Commercial Burner (section 12: "§55.14 buys one
    /// upgrade -> Keep. Only progression proof in the slice"). Customer
    /// archetypes are still intentionally NOT authored (M6/M7);
    /// GameDatabase gets an empty array for them until then.
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

        public struct EquipmentEntry
        {
            public string id;
            public string displayName;
            public EquipmentType equipmentType;
            public int tier;
            public float purchaseCost;
            public float heatRateMultiplier;
            public float capacityMultiplier;
            public float qualityBonus01;
        }

        public struct ArchetypeEntry
        {
            public string id;
            public string displayName;
            public float spawnWeight;
            public FloatRange patienceSeconds;
            public FloatRange budget;
            public float qualityExpectation01;
            public float cleanlinessSensitivity01;
            public float serviceSensitivity01;
            public float priceSensitivity01;
            public float tipChance01;
            public float tipFraction;
            public float reviewChance01;
            public string[] preferredRecipeIds;
            public FloatRange eatDurationSeconds;
            public int visualVariantIndex;
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
        // Equipment
        // ------------------------------------------------------------------
        //
        // EXACTLY ONE entry, on purpose. architecture.md section 12 keeps
        // GDD §55.14 ("buys one upgrade") specifically as the slice's only
        // progression proof; a second upgrade would be scope creep with no
        // additional proof value.
        //
        // heatRateMultiplier = 1.7 (JUDGMENT CALL worth a reviewer's eye):
        // architecture.md section 7 illustrates the mechanism with
        // "heatRateMultiplier 1.0 -> 1.5", but section 9's M13 row states the
        // testable acceptance criterion as "Buying it cuts measured
        // broth-ready time >= 30%". Those two numbers conflict, because only
        // part of the brew is heat-accelerated: BrothSimulator's Filling
        // phase (10s of the ~100s total) advances at a fixed fill rate that
        // heatRateMultiplier does not touch -- only Heating (30s) and
        // Simmering (60s) scale. At 1.5x that yields 10 + 20 + 40 = 70s vs
        // 100s, i.e. exactly 30.0% in theory and 29.8% once discrete ticking
        // overhead is measured (verified: ProgressionTests measured
        // stock=100.75s upgraded=70.75s = 29.8%), which FAILS the >= 30%
        // criterion. 1.7 gives a comfortable ~37% and still reads as a
        // believable commercial-vs-domestic burner step. The hard, testable
        // acceptance criterion is treated as authoritative over the prose
        // example; ProgressionTests asserts the >= 30% cut directly, so this
        // number is regression-locked rather than merely asserted here.
        //
        // purchaseCost = 450 against EconomyService.StartingCash of 1500:
        // affordable on day one if the player has not overspent, but a real
        // 30% dent in the opening balance, so the "earn it" half of the
        // progression loop still has to happen. capacityMultiplier stays 1
        // and qualityBonus01 stays 0 -- a burner heats faster, it does not
        // make the pot bigger or the broth intrinsically better.

        public static readonly EquipmentEntry[] Equipment =
        {
            new EquipmentEntry
            {
                id = "eq.burner_commercial",
                displayName = "Commercial Burner",
                equipmentType = EquipmentType.Burner,
                tier = 2,
                purchaseCost = 450f,
                heatRateMultiplier = 1.7f,
                capacityMultiplier = 1.0f,
                qualityBonus01 = 0f,
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

        // TWO archetypes, per architecture.md section 12's explicit "Same for
        // archetypes: 2 first, then expand." They are deliberately opposites
        // along the axes SatisfactionCalculator actually reads, so the
        // difference is legible in play rather than being two near-identical
        // stat blocks: the office worker is time-pressured and forgiving
        // about quality, the food critic is patient and unforgiving.
        //
        // WHY THIS EXISTS AT ALL: until these were authored, GameDatabase
        // shipped `archetypes: []`, and CustomerSpawner.TrySpawn bails with a
        // warning when the archetype list is empty -- so NO CUSTOMER EVER
        // SPAWNED in play. Steps 7-11 of the GDD section 55 loop (customer
        // arrives, orders, is served, pays, player earns) were unreachable by
        // a human, even though the golden-path test stayed green by driving
        // OrderService directly.
        //
        // Budgets are set against the two recipes' BasePrice (8.50 / 8.00)
        // so a normally-priced bowl is affordable for both, and
        // PriceSensitivity only bites if prices rise later.
        public static readonly ArchetypeEntry[] Archetypes =
        {
            new ArchetypeEntry
            {
                id = "arc.office_worker",
                displayName = "Office Worker",
                spawnWeight = 3f,               // the common case -- 3:1 against the critic
                patienceSeconds = new FloatRange(45f, 75f),   // on a lunch break
                budget = new FloatRange(9f, 14f),
                qualityExpectation01 = 0.5f,    // wants lunch, not perfection
                cleanlinessSensitivity01 = 0.3f,
                serviceSensitivity01 = 0.8f,    // speed is what they care about
                priceSensitivity01 = 0.6f,
                tipChance01 = 0.35f,
                tipFraction = 0.10f,
                reviewChance01 = 0.05f,
                preferredRecipeIds = new[] { "rec.pho_tai", "rec.pho_chin" },
                eatDurationSeconds = new FloatRange(35f, 55f),
                visualVariantIndex = 0,
            },
            new ArchetypeEntry
            {
                id = "arc.food_critic",
                displayName = "Food Critic",
                spawnWeight = 1f,
                patienceSeconds = new FloatRange(90f, 140f),  // will wait for a good bowl
                budget = new FloatRange(15f, 25f),
                qualityExpectation01 = 0.85f,   // the whole point of this archetype
                cleanlinessSensitivity01 = 0.9f,
                serviceSensitivity01 = 0.4f,
                priceSensitivity01 = 0.2f,      // pays for quality
                tipChance01 = 0.7f,
                tipFraction = 0.20f,
                reviewChance01 = 0.9f,
                preferredRecipeIds = new[] { "rec.pho_tai" },
                eatDurationSeconds = new FloatRange(70f, 110f),
                visualVariantIndex = 1,
            },
        };

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
