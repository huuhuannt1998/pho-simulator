using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Pho.Data;
using UnityEditor;

namespace Pho.Data.Tests
{
    /// <summary>
    /// Tier-2 EditMode content validation (docs/architecture.md section 11,
    /// "Pho.Data.Tests/ContentValidationTests"). Loads real assets via
    /// AssetDatabase, so this suite only runs inside Unity's EditMode test
    /// runner -- it cannot run under `dotnet test` (no asset database
    /// there), which is expected and correct for this specific test, unlike
    /// the pure Domain/Save tests.
    ///
    /// Checks (per doc section 2.1 + 11):
    ///   - every SO's id matches its filename exactly
    ///   - ids are unique within their own category
    ///   - ids match ^[a-z]+\.[a-z0-9_]+$
    ///   - every recipe's ingredient references resolve to a real ingredient
    ///   - every archetype's preferred recipes resolve to a real recipe
    ///   - GameDatabase contains every generated asset (and nothing stale)
    /// </summary>
    public class ContentValidationTests
    {
        static readonly Regex IdFormat = new Regex(@"^[a-z]+\.[a-z0-9_]+$", RegexOptions.Compiled);

        [Test]
        public void IngredientIds_MatchFilenameAndFormat()
        {
            foreach (var (asset, path) in FindAll<IngredientData>())
                AssertIdMatchesFilenameAndFormat(asset.RawId, path);
        }

        [Test]
        public void RecipeIds_MatchFilenameAndFormat()
        {
            foreach (var (asset, path) in FindAll<RecipeData>())
                AssertIdMatchesFilenameAndFormat(asset.RawId, path);
        }

        [Test]
        public void EquipmentIds_MatchFilenameAndFormat()
        {
            foreach (var (asset, path) in FindAll<EquipmentData>())
                AssertIdMatchesFilenameAndFormat(asset.RawId, path);
        }

        [Test]
        public void ArchetypeIds_MatchFilenameAndFormat()
        {
            foreach (var (asset, path) in FindAll<CustomerArchetype>())
                AssertIdMatchesFilenameAndFormat(asset.RawId, path);
        }

        [Test]
        public void AllIds_AreUniqueWithinTheirCategory()
        {
            AssertUnique(CollectIds<IngredientData>(a => a.RawId), "ingredient");
            AssertUnique(CollectIds<RecipeData>(a => a.RawId), "recipe");
            AssertUnique(CollectIds<EquipmentData>(a => a.RawId), "equipment");
            AssertUnique(CollectIds<CustomerArchetype>(a => a.RawId), "archetype");
        }

        [Test]
        public void Recipes_OnlyReferenceKnownIngredients()
        {
            var knownIngredients = new HashSet<string>(CollectIds<IngredientData>(a => a.RawId));
            foreach (var (recipe, path) in FindAll<RecipeData>())
            {
                foreach (var component in recipe.Components)
                {
                    Assert.That(
                        knownIngredients.Contains(component.ingredientId),
                        $"Recipe '{path}' references unknown ingredient '{component.ingredientId}'.");
                }
            }
        }

        [Test]
        public void Archetypes_OnlyPreferKnownRecipes()
        {
            var knownRecipes = new HashSet<string>(CollectIds<RecipeData>(a => a.RawId));
            foreach (var (archetype, path) in FindAll<CustomerArchetype>())
            {
                foreach (var recipeId in archetype.PreferredRecipeIds)
                {
                    Assert.That(
                        knownRecipes.Contains(recipeId.Value),
                        $"Archetype '{path}' prefers unknown recipe '{recipeId.Value}'.");
                }
            }
        }

        [Test]
        public void GameDatabase_ContainsExactlyEveryGeneratedAsset()
        {
            var db = LoadSingleton<GameDatabase>();
            Assert.NotNull(db, "Assets/Content/GameDatabase.asset must exist (run ContentGenerator.BuildAllContent).");

            AssertSetsMatch(
                new HashSet<string>(CollectIds<IngredientData>(a => a.RawId)),
                RawIdsOf(db.IngredientAssets),
                "ingredient");
            AssertSetsMatch(
                new HashSet<string>(CollectIds<RecipeData>(a => a.RawId)),
                RawIdsOf(db.RecipeAssets),
                "recipe");
            AssertSetsMatch(
                new HashSet<string>(CollectIds<EquipmentData>(a => a.RawId)),
                RawIdsOf(db.EquipmentAssets),
                "equipment");
            AssertSetsMatch(
                new HashSet<string>(CollectIds<CustomerArchetype>(a => a.RawId)),
                RawIdsOf(db.ArchetypeAssets),
                "archetype");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        static HashSet<string> RawIdsOf(IReadOnlyList<IngredientData> assets)
        {
            var set = new HashSet<string>();
            foreach (var a in assets) set.Add(a.RawId);
            return set;
        }

        static HashSet<string> RawIdsOf(IReadOnlyList<RecipeData> assets)
        {
            var set = new HashSet<string>();
            foreach (var a in assets) set.Add(a.RawId);
            return set;
        }

        static HashSet<string> RawIdsOf(IReadOnlyList<EquipmentData> assets)
        {
            var set = new HashSet<string>();
            foreach (var a in assets) set.Add(a.RawId);
            return set;
        }

        static HashSet<string> RawIdsOf(IReadOnlyList<CustomerArchetype> assets)
        {
            var set = new HashSet<string>();
            foreach (var a in assets) set.Add(a.RawId);
            return set;
        }

        static void AssertSetsMatch(HashSet<string> expected, HashSet<string> actual, string label)
        {
            foreach (var id in expected)
                Assert.That(actual.Contains(id), $"GameDatabase is missing {label} '{id}'.");
            foreach (var id in actual)
                Assert.That(expected.Contains(id), $"GameDatabase references a stale/unknown {label} '{id}'.");
        }

        static void AssertIdMatchesFilenameAndFormat(string id, string assetPath)
        {
            var filename = Path.GetFileNameWithoutExtension(assetPath);
            Assert.AreEqual(filename, id, $"Asset '{assetPath}': id '{id}' must equal its filename.");
            Assert.IsTrue(
                IdFormat.IsMatch(id ?? string.Empty),
                $"Asset '{assetPath}': id '{id}' does not match ^[a-z]+\\.[a-z0-9_]+$.");
        }

        static void AssertUnique(IEnumerable<string> ids, string label)
        {
            var seen = new HashSet<string>();
            foreach (var id in ids)
                Assert.IsTrue(seen.Add(id), $"Duplicate {label} id '{id}'.");
        }

        static List<string> CollectIds<T>(Func<T, string> idSelector) where T : UnityEngine.Object
        {
            var result = new List<string>();
            foreach (var (asset, _) in FindAll<T>())
                result.Add(idSelector(asset));
            return result;
        }

        static IEnumerable<(T asset, string path)> FindAll<T>() where T : UnityEngine.Object
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) yield return (asset, path);
            }
        }

        static T LoadSingleton<T>() where T : UnityEngine.Object
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) return asset;
            }
            return null;
        }
    }
}
