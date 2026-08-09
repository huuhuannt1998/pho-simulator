using System.Collections.Generic;
using System.Linq;
using Pho.Data;
using UnityEditor;
using UnityEngine;

namespace Pho.EditorTools.Content
{
    /// <summary>
    /// Generates/refreshes Assets/Content/**/*.asset from
    /// <see cref="ContentManifest"/>. This is the ONLY code path allowed to
    /// create or modify those assets -- agents edit the manifest, never the
    /// generated .asset files (docs/architecture.md section 10).
    ///
    /// Idempotent by construction: <see cref="LoadOrCreate{T}"/> looks up an
    /// existing asset by its deterministic path first and reuses it if found,
    /// so running BuildAllContent twice updates the same assets in place
    /// rather than creating duplicates or leaving orphans. This mirrors the
    /// SceneBuilderTests idempotency philosophy in doc section 11.
    ///
    /// Invoked headless via:
    ///   Unity -batchmode -nographics -projectPath . \
    ///     -executeMethod Pho.EditorTools.Content.ContentGenerator.BuildAllContent -quit
    ///
    /// This script itself never launches Unity or runs in batch mode -- it is
    /// only ever invoked BY Unity, by a separate integration pass.
    /// </summary>
    public static class ContentGenerator
    {
        const string RootFolder = "Assets/Content";
        const string IngredientsFolder = RootFolder + "/Ingredients";
        const string RecipesFolder = RootFolder + "/Recipes";
        const string EquipmentFolder = RootFolder + "/Equipment";
        const string ArchetypesFolder = RootFolder + "/Archetypes";

        [MenuItem("Pho/Content/Build All Content")]
        public static void BuildAllContent()
        {
            EnsureFolders();

            var ingredients = BuildIngredients();
            var recipes = BuildRecipes(ingredients);
            var equipment = BuildEquipment();
            var balance = BuildBalanceConfig();

            // No archetype content authored yet -- see ContentManifest's
            // scope note. The folder is still created so a later agent has a
            // place to drop generated assets without touching this script.
            var archetypes = System.Array.Empty<CustomerArchetype>();

            BuildGameDatabase(ingredients, recipes, equipment, archetypes);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[ContentGenerator] Built {ingredients.Length} ingredient(s), {recipes.Length} recipe(s), " +
                $"{equipment.Length} equipment, {archetypes.Length} archetype(s), 1 balance config, 1 database. " +
                $"(balance='{balance.name}')");
        }

        static void EnsureFolders()
        {
            EnsureFolder("Assets", "Content");
            EnsureFolder(RootFolder, "Ingredients");
            EnsureFolder(RootFolder, "Recipes");
            EnsureFolder(RootFolder, "Equipment");
            EnsureFolder(RootFolder, "Archetypes");
        }

        static void EnsureFolder(string parent, string name)
        {
            var path = $"{parent}/{name}";
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, name);
        }

        static IngredientData[] BuildIngredients()
        {
            var results = new List<IngredientData>(ContentManifest.Ingredients.Length);
            foreach (var e in ContentManifest.Ingredients)
            {
                var asset = LoadOrCreate<IngredientData>($"{IngredientsFolder}/{e.id}.asset");
                asset.EditorInit(
                    e.id, e.displayName, e.category, e.basePurchasePrice, e.baseQuality01,
                    e.shelfLifeHours, e.storage, e.unitsPerCrate);
                EditorUtility.SetDirty(asset);
                results.Add(asset);
            }
            return results.ToArray();
        }

        static RecipeData[] BuildRecipes(IngredientData[] ingredients)
        {
            var knownIngredientIds = new HashSet<string>(ingredients.Select(i => i.RawId));
            var results = new List<RecipeData>(ContentManifest.Recipes.Length);

            foreach (var e in ContentManifest.Recipes)
            {
                foreach (var component in e.components)
                {
                    if (!knownIngredientIds.Contains(component.ingredientId))
                    {
                        Debug.LogError(
                            $"[ContentGenerator] Recipe '{e.id}' references unknown ingredient " +
                            $"'{component.ingredientId}'. Fix ContentManifest before re-running.");
                    }
                }

                var asset = LoadOrCreate<RecipeData>($"{RecipesFolder}/{e.id}.asset");
                asset.EditorInit(
                    e.id, e.displayName, e.components, e.basePrice,
                    e.preparationDifficulty01, e.targetBrothVolumeLiters, e.targetServeTemperatureC);
                EditorUtility.SetDirty(asset);
                results.Add(asset);
            }
            return results.ToArray();
        }

        /// <summary>
        /// Same deterministic-path + LoadOrCreate shape as
        /// <see cref="BuildIngredients"/>, so re-running BuildAllContent
        /// updates eq.burner_commercial.asset in place and its GUID (and any
        /// scene/prefab reference to it) survives.
        /// </summary>
        static EquipmentData[] BuildEquipment()
        {
            var results = new List<EquipmentData>(ContentManifest.Equipment.Length);
            foreach (var e in ContentManifest.Equipment)
            {
                var asset = LoadOrCreate<EquipmentData>($"{EquipmentFolder}/{e.id}.asset");
                asset.EditorInit(
                    e.id, e.displayName, e.equipmentType, e.tier, e.purchaseCost,
                    e.heatRateMultiplier, e.capacityMultiplier, e.qualityBonus01);
                EditorUtility.SetDirty(asset);
                results.Add(asset);
            }
            return results.ToArray();
        }

        static GameBalanceConfig BuildBalanceConfig()
        {
            var asset = LoadOrCreate<GameBalanceConfig>($"{RootFolder}/GameBalanceConfig.asset");
            var b = ContentManifest.Balance;
            asset.EditorInit(
                b.patienceDecayRate, b.minServableAccuracy, b.defectWeights,
                b.spoilageRatePerHour, b.dayLengthSeconds, b.rentAmount, b.rentIntervalDays);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        static void BuildGameDatabase(
            IngredientData[] ingredients, RecipeData[] recipes,
            EquipmentData[] equipment, CustomerArchetype[] archetypes)
        {
            var asset = LoadOrCreate<GameDatabase>($"{RootFolder}/GameDatabase.asset");
            asset.EditorInit(ingredients, recipes, equipment, archetypes);
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// Idempotency primitive: reuse the asset already at this path if one
        /// exists (preserves its GUID/meta so cross-references stay stable
        /// across re-runs), otherwise create a fresh instance there.
        /// </summary>
        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var instance = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(instance, path);
            return instance;
        }
    }
}
