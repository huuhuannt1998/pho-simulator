using NUnit.Framework;
using Pho.Domain.Contracts;
using Pho.Domain.Cooking;
using Pho.Domain.Identity;
using Pho.Domain.Tests.Fakes;

namespace Pho.Domain.Tests
{
    // Plain NUnit only (no UnityEngine.TestTools / [UnityTest]) -- this file
    // is compiled both by Unity's EditMode test runner and by
    // Tools/PhoDomain.Tests.csproj via `dotnet test`. Constraint-based
    // Assert.That(...) throughout, matching EventBusTests.cs style.
    [TestFixture]
    public class QualityCalculatorTests
    {
        static readonly IngredientId Broth = new IngredientId("ing.beef_broth");
        static readonly IngredientId Noodle = new IngredientId("ing.rice_noodle");
        static readonly IngredientId BeefRare = new IngredientId("ing.beef_rare");

        static FakeBalanceConfig NewConfig()
        {
            var cfg = new FakeBalanceConfig();
            cfg.DefectWeights[DefectKind.MissingRequired] = 0.9f;
            cfg.DefectWeights[DefectKind.ExtraUnwanted] = 0.05f;
            return cfg;
        }

        // Baseline recipe whose target serve temperature equals the "just
        // poured" placeholder broth temperature, so a freshly-assembled bowl
        // scores a perfect Temperature01 without needing extra cooling calls.
        static FakeRecipeDef PerfectTempRecipe()
        {
            var recipe = new FakeRecipeDef
            {
                Id = new RecipeId("rec.pho_tai"),
                TargetServeTemperatureC = BowlContents.PouredBrothTemperatureC,
            };
            recipe.Components.Add(new RecipeComponent { slot = ComponentSlot.Broth, ingredientId = Broth.Value, amount = 1f, tolerance = 0.1f, required = true });
            recipe.Components.Add(new RecipeComponent { slot = ComponentSlot.Noodle, ingredientId = Noodle.Value, amount = 1f, tolerance = 0.1f, required = true });
            recipe.Components.Add(new RecipeComponent { slot = ComponentSlot.Protein, ingredientId = BeefRare.Value, amount = 1f, tolerance = 0.2f, required = true });
            return recipe;
        }

        static BowlContents BuildBowl(float proteinQuality01, float proteinFreshness01)
        {
            var bowl = new BowlContents();
            bowl.Add(new BowlComponent(ComponentSlot.Broth, Broth, 1f, 0.9f, 0.9f, 0f));
            bowl.Add(new BowlComponent(ComponentSlot.Noodle, Noodle, 1f, 0.9f, 0.9f, 0f));
            bowl.Add(new BowlComponent(ComponentSlot.Protein, BeefRare, 1f, proteinQuality01, proteinFreshness01, 0f));
            return bowl;
        }

        static BrothState ReadyBroth(float quality01 = 0.9f)
            => new BrothState { Phase = BrothPhase.Ready, VolumeLiters = 1f, TemperatureC = 95f, SimmerSeconds = 60f, Quality01 = quality01 };

        [Test]
        public void Score_BetterIngredientQuality_NeverLowersOverall_AllElseEqual()
        {
            var cfg = NewConfig();
            var recipe = PerfectTempRecipe();
            var broth = ReadyBroth();

            var worseBowl = BuildBowl(proteinQuality01: 0.3f, proteinFreshness01: 1f);
            var betterBowl = BuildBowl(proteinQuality01: 0.9f, proteinFreshness01: 1f);

            var worseMatch = RecipeMatcher.Match(worseBowl, recipe, cfg);
            var betterMatch = RecipeMatcher.Match(betterBowl, recipe, cfg);

            var worseScore = QualityCalculator.Score(worseBowl, worseMatch, broth, cfg);
            var betterScore = QualityCalculator.Score(betterBowl, betterMatch, broth, cfg);

            Assert.That(betterScore.IngredientQuality01, Is.GreaterThan(worseScore.IngredientQuality01));
            Assert.That(betterScore.Overall01, Is.GreaterThanOrEqualTo(worseScore.Overall01),
                "strictly better ingredient quality must never produce a lower Overall01, all else equal");
        }

        [Test]
        public void Score_ColdBroth_MeasurablyLowersTemperatureAndOverall()
        {
            var cfg = NewConfig();
            var recipe = PerfectTempRecipe();
            var broth = ReadyBroth();

            var hotBowl = BuildBowl(proteinQuality01: 0.8f, proteinFreshness01: 1f);
            var hotMatch = RecipeMatcher.Match(hotBowl, recipe, cfg);
            var hotScore = QualityCalculator.Score(hotBowl, hotMatch, broth, cfg);

            var coldBowl = BuildBowl(proteinQuality01: 0.8f, proteinFreshness01: 1f);
            coldBowl.CoolTowardsAmbient(1000f, cfg); // forces BrothTemperatureC to ambient
            var coldMatch = RecipeMatcher.Match(coldBowl, recipe, cfg);
            var coldScore = QualityCalculator.Score(coldBowl, coldMatch, broth, cfg);

            Assert.That(hotScore.Temperature01, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(coldScore.Temperature01, Is.LessThan(hotScore.Temperature01));
            Assert.That(coldScore.Overall01, Is.LessThan(hotScore.Overall01));
        }

        [Test]
        public void Score_SpoiledIngredient_MeasurablyLowersFreshnessAndOverall()
        {
            var cfg = NewConfig();
            var recipe = PerfectTempRecipe();
            var broth = ReadyBroth();

            var freshBowl = BuildBowl(proteinQuality01: 0.8f, proteinFreshness01: 1f);
            var freshMatch = RecipeMatcher.Match(freshBowl, recipe, cfg);
            var freshScore = QualityCalculator.Score(freshBowl, freshMatch, broth, cfg);

            var spoiledBowl = BuildBowl(proteinQuality01: 0.8f, proteinFreshness01: 0.01f);
            var spoiledMatch = RecipeMatcher.Match(spoiledBowl, recipe, cfg);
            var spoiledScore = QualityCalculator.Score(spoiledBowl, spoiledMatch, broth, cfg);

            Assert.That(spoiledScore.Freshness01, Is.LessThan(freshScore.Freshness01));
            Assert.That(spoiledScore.Overall01, Is.LessThan(freshScore.Overall01));
        }

        [Test]
        public void Score_EmptyBowl_DoesNotThrow_AndScoresZeroIngredientAndFreshnessTerms()
        {
            var cfg = NewConfig();
            var recipe = PerfectTempRecipe();
            var broth = ReadyBroth();
            var bowl = new BowlContents();

            var match = RecipeMatcher.Match(bowl, recipe, cfg);
            DishQuality score = default;

            Assert.That(() => score = QualityCalculator.Score(bowl, match, broth, cfg), Throws.Nothing);
            Assert.That(score.IngredientQuality01, Is.EqualTo(0f));
            Assert.That(score.Freshness01, Is.EqualTo(0f));
        }

        [Test]
        public void Score_Overall01_IsAlwaysClamped01()
        {
            var cfg = NewConfig();
            var recipe = PerfectTempRecipe();
            var broth = ReadyBroth(quality01: 1f);
            var bowl = BuildBowl(proteinQuality01: 1f, proteinFreshness01: 1f);
            var match = RecipeMatcher.Match(bowl, recipe, cfg);

            var score = QualityCalculator.Score(bowl, match, broth, cfg);

            Assert.That(score.Overall01, Is.LessThanOrEqualTo(1f));
            Assert.That(score.Overall01, Is.GreaterThanOrEqualTo(0f));
        }
    }
}
