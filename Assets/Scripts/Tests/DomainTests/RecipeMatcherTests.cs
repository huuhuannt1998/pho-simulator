using System;
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
    public class RecipeMatcherTests
    {
        static readonly IngredientId Broth = new IngredientId("ing.beef_broth");
        static readonly IngredientId Noodle = new IngredientId("ing.rice_noodle");
        static readonly IngredientId BeefRare = new IngredientId("ing.beef_rare");
        static readonly IngredientId BeefWellDone = new IngredientId("ing.beef_welldone");
        static readonly IngredientId Chicken = new IngredientId("ing.chicken");
        static readonly IngredientId Herb = new IngredientId("ing.herb_mix");

        static FakeBalanceConfig NewConfig()
        {
            var cfg = new FakeBalanceConfig { MinServableAccuracy = 0.5f };
            cfg.DefectWeights[DefectKind.MissingRequired] = 0.9f;
            cfg.DefectWeights[DefectKind.WrongIngredient] = 0.9f;
            cfg.DefectWeights[DefectKind.TooLittle] = 0.3f;
            cfg.DefectWeights[DefectKind.TooMuch] = 0.3f;
            cfg.DefectWeights[DefectKind.ExtraUnwanted] = 0.05f;
            return cfg;
        }

        static FakeRecipeDef PhoTai()
        {
            var recipe = new FakeRecipeDef { Id = new RecipeId("rec.pho_tai"), TargetServeTemperatureC = 75f };
            recipe.Components.Add(new RecipeComponent { slot = ComponentSlot.Broth, ingredientId = Broth.Value, amount = 1f, tolerance = 0.1f, required = true });
            recipe.Components.Add(new RecipeComponent { slot = ComponentSlot.Noodle, ingredientId = Noodle.Value, amount = 1f, tolerance = 0.1f, required = true });
            recipe.Components.Add(new RecipeComponent { slot = ComponentSlot.Protein, ingredientId = BeefRare.Value, amount = 1f, tolerance = 0.2f, required = true });
            recipe.Components.Add(new RecipeComponent { slot = ComponentSlot.Herb, ingredientId = Herb.Value, amount = 0.5f, tolerance = 0.5f, required = false });
            return recipe;
        }

        static FakeRecipeDef PhoChin()
        {
            var recipe = new FakeRecipeDef { Id = new RecipeId("rec.pho_chin"), TargetServeTemperatureC = 75f };
            recipe.Components.Add(new RecipeComponent { slot = ComponentSlot.Broth, ingredientId = Broth.Value, amount = 1f, tolerance = 0.1f, required = true });
            recipe.Components.Add(new RecipeComponent { slot = ComponentSlot.Noodle, ingredientId = Noodle.Value, amount = 1f, tolerance = 0.1f, required = true });
            recipe.Components.Add(new RecipeComponent { slot = ComponentSlot.Protein, ingredientId = BeefWellDone.Value, amount = 1f, tolerance = 0.2f, required = true });
            return recipe;
        }

        static BowlComponent Comp(ComponentSlot slot, IngredientId ing, float amount, float quality01 = 1f, float freshness01 = 1f)
            => new BowlComponent(slot, ing, amount, quality01, freshness01, addedAtGameSeconds: 0f);

        [Test]
        public void Match_PerfectBowl_ScoresFullAccuracy_WithNoDefects()
        {
            var bowl = new BowlContents();
            bowl.Add(Comp(ComponentSlot.Broth, Broth, 1f));
            bowl.Add(Comp(ComponentSlot.Noodle, Noodle, 1f));
            bowl.Add(Comp(ComponentSlot.Protein, BeefRare, 1f));

            var match = RecipeMatcher.Match(bowl, PhoTai(), NewConfig());

            Assert.That(match.Accuracy01, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(match.Defects, Is.Empty);
            Assert.That(match.IsServable, Is.True);
        }

        [Test]
        public void Match_MissingRequiredComponent_ScoresBelowServable()
        {
            var bowl = new BowlContents();
            bowl.Add(Comp(ComponentSlot.Broth, Broth, 1f));
            bowl.Add(Comp(ComponentSlot.Noodle, Noodle, 1f));
            // No protein at all -> empty required slot -> MissingRequired.

            var match = RecipeMatcher.Match(bowl, PhoTai(), NewConfig());

            Assert.That(match.Defects.Count, Is.EqualTo(1));
            Assert.That(match.Defects[0].Kind, Is.EqualTo(DefectKind.MissingRequired));
            Assert.That(match.Defects[0].Severity01, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(match.Accuracy01, Is.EqualTo(1f - 0.9f).Within(1e-4f));
        }

        [Test]
        public void Match_WrongIngredientInRequiredSlot_FlagsWrongIngredient_NotMissingRequired()
        {
            var bowl = new BowlContents();
            bowl.Add(Comp(ComponentSlot.Broth, Broth, 1f));
            bowl.Add(Comp(ComponentSlot.Noodle, Noodle, 1f));
            bowl.Add(Comp(ComponentSlot.Protein, Chicken, 1f)); // recipe wants BeefRare

            var match = RecipeMatcher.Match(bowl, PhoTai(), NewConfig());

            var wrongIngredientDefects = 0;
            var extraUnwantedDefects = 0;
            foreach (var d in match.Defects)
            {
                if (d.Kind == DefectKind.WrongIngredient) wrongIngredientDefects++;
                if (d.Kind == DefectKind.ExtraUnwanted) extraUnwantedDefects++;
                Assert.That(d.Kind, Is.Not.EqualTo(DefectKind.MissingRequired));
            }

            Assert.That(wrongIngredientDefects, Is.EqualTo(1));
            // The chicken is also a foreign ingredient physically in the bowl.
            Assert.That(extraUnwantedDefects, Is.EqualTo(1));
        }

        [Test]
        public void Match_TooLittleOfRequiredComponent_OutsideTolerance_FlagsTooLittle()
        {
            var bowl = new BowlContents();
            bowl.Add(Comp(ComponentSlot.Broth, Broth, 1f));
            bowl.Add(Comp(ComponentSlot.Noodle, Noodle, 1f));
            bowl.Add(Comp(ComponentSlot.Protein, BeefRare, 0.5f)); // wants 1.0 +/-0.2

            var match = RecipeMatcher.Match(bowl, PhoTai(), NewConfig());

            Assert.That(match.Defects.Count, Is.EqualTo(1));
            Assert.That(match.Defects[0].Kind, Is.EqualTo(DefectKind.TooLittle));
            Assert.That(match.Defects[0].Severity01, Is.GreaterThan(0f));
        }

        [Test]
        public void Match_TooMuchOfRequiredComponent_OutsideTolerance_FlagsTooMuch()
        {
            var bowl = new BowlContents();
            bowl.Add(Comp(ComponentSlot.Broth, Broth, 1f));
            bowl.Add(Comp(ComponentSlot.Noodle, Noodle, 1f));
            bowl.Add(Comp(ComponentSlot.Protein, BeefRare, 2f)); // wants 1.0 +/-0.2

            var match = RecipeMatcher.Match(bowl, PhoTai(), NewConfig());

            Assert.That(match.Defects.Count, Is.EqualTo(1));
            Assert.That(match.Defects[0].Kind, Is.EqualTo(DefectKind.TooMuch));
            Assert.That(match.Defects[0].Severity01, Is.GreaterThan(0f));
        }

        [Test]
        public void Match_WithinTolerance_ProducesNoAmountDefect()
        {
            var bowl = new BowlContents();
            bowl.Add(Comp(ComponentSlot.Broth, Broth, 1f));
            bowl.Add(Comp(ComponentSlot.Noodle, Noodle, 1f));
            bowl.Add(Comp(ComponentSlot.Protein, BeefRare, 1.15f)); // wants 1.0 +/-0.2, still inside band

            var match = RecipeMatcher.Match(bowl, PhoTai(), NewConfig());

            Assert.That(match.Defects, Is.Empty);
            Assert.That(match.Accuracy01, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Match_ExtraUnwantedIngredient_NotRequestedByRecipe_FlagsExtraUnwanted()
        {
            var bowl = new BowlContents();
            bowl.Add(Comp(ComponentSlot.Broth, Broth, 1f));
            bowl.Add(Comp(ComponentSlot.Noodle, Noodle, 1f));
            bowl.Add(Comp(ComponentSlot.Protein, BeefRare, 1f));
            bowl.Add(Comp(ComponentSlot.Herb, Herb, 5f)); // recipe's herb is optional, amount 0.5+/-0.5

            var match = RecipeMatcher.Match(bowl, PhoTai(), NewConfig());

            // 5.0 units is outside the optional herb's tolerance band (0 to 1.0),
            // but it's still a *recognised* recipe ingredient, so it should be
            // TooMuch, not ExtraUnwanted.
            Assert.That(match.Defects.Count, Is.EqualTo(1));
            Assert.That(match.Defects[0].Kind, Is.EqualTo(DefectKind.TooMuch));
        }

        [Test]
        public void Match_TrulyForeignIngredient_FlagsExtraUnwanted()
        {
            var bowl = new BowlContents();
            bowl.Add(Comp(ComponentSlot.Broth, Broth, 1f));
            bowl.Add(Comp(ComponentSlot.Noodle, Noodle, 1f));
            bowl.Add(Comp(ComponentSlot.Protein, BeefRare, 1f));
            bowl.Add(Comp(ComponentSlot.Garnish, Chicken, 0.2f)); // not part of the recipe at all

            var match = RecipeMatcher.Match(bowl, PhoTai(), NewConfig());

            Assert.That(match.Defects.Count, Is.EqualTo(1));
            Assert.That(match.Defects[0].Kind, Is.EqualTo(DefectKind.ExtraUnwanted));
        }

        [Test]
        public void Match_EmptyBowl_ScoresVeryLow_AllDefectsAreMissingRequired()
        {
            var bowl = new BowlContents();

            var match = RecipeMatcher.Match(bowl, PhoTai(), NewConfig());

            Assert.That(match.Defects.Count, Is.EqualTo(3)); // broth, noodle, protein are all required
            foreach (var d in match.Defects)
                Assert.That(d.Kind, Is.EqualTo(DefectKind.MissingRequired));

            Assert.That(match.Accuracy01, Is.LessThan(0.3f));
            Assert.That(match.IsServable, Is.False);
        }

        [Test]
        public void BestMatch_WellDoneBeefBowl_PicksChinOverTai()
        {
            var bowl = new BowlContents();
            bowl.Add(Comp(ComponentSlot.Broth, Broth, 1f));
            bowl.Add(Comp(ComponentSlot.Noodle, Noodle, 1f));
            bowl.Add(Comp(ComponentSlot.Protein, BeefWellDone, 1f));

            var menu = new IRecipeDef[] { PhoTai(), PhoChin() };

            var best = RecipeMatcher.BestMatch(bowl, menu, NewConfig());

            Assert.That(best.Recipe, Is.EqualTo(new RecipeId("rec.pho_chin")));
            Assert.That(best.Accuracy01, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void BestMatch_NullOrEmptyMenu_Throws()
        {
            var bowl = new BowlContents();
            var cfg = NewConfig();

            Assert.That(() => RecipeMatcher.BestMatch(bowl, null, cfg), Throws.ArgumentException);
            Assert.That(() => RecipeMatcher.BestMatch(bowl, Array.Empty<IRecipeDef>(), cfg), Throws.ArgumentException);
        }

        [Test]
        public void Match_ExtraGarnish_IsCheaperThanMissingProtein()
        {
            var cfg = NewConfig();

            var bowlMissingProtein = new BowlContents();
            bowlMissingProtein.Add(Comp(ComponentSlot.Broth, Broth, 1f));
            bowlMissingProtein.Add(Comp(ComponentSlot.Noodle, Noodle, 1f));
            var missingProteinMatch = RecipeMatcher.Match(bowlMissingProtein, PhoTai(), cfg);

            var bowlExtraGarnish = new BowlContents();
            bowlExtraGarnish.Add(Comp(ComponentSlot.Broth, Broth, 1f));
            bowlExtraGarnish.Add(Comp(ComponentSlot.Noodle, Noodle, 1f));
            bowlExtraGarnish.Add(Comp(ComponentSlot.Protein, BeefRare, 1f));
            bowlExtraGarnish.Add(Comp(ComponentSlot.Garnish, Chicken, 0.1f)); // one foreign garnish extra
            var extraGarnishMatch = RecipeMatcher.Match(bowlExtraGarnish, PhoTai(), cfg);

            Assert.That(extraGarnishMatch.Accuracy01, Is.GreaterThan(missingProteinMatch.Accuracy01),
                "an unwanted extra garnish should cost far less accuracy than an entirely missing required protein");
        }
    }
}
