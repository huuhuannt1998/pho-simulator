using System;
using System.Collections.Generic;
using Pho.Domain.Contracts;
using Pho.Domain.Identity;
using Pho.Domain.MathUtil;

namespace Pho.Domain.Cooking
{
    /// <summary>One scored discrepancy between a bowl and a recipe. See RecipeMatcher.</summary>
    public readonly struct Defect
    {
        public readonly DefectKind Kind;
        public readonly ComponentSlot Slot;
        public readonly IngredientId Ingredient;
        public readonly float Severity01;

        public Defect(DefectKind kind, ComponentSlot slot, IngredientId ingredient, float severity01)
        {
            Kind = kind;
            Slot = slot;
            Ingredient = ingredient;
            Severity01 = severity01;
        }
    }

    /// <summary>Result of matching a bowl against one recipe. See RecipeMatcher.</summary>
    public readonly struct RecipeMatch
    {
        public readonly RecipeId Recipe;
        public readonly float Accuracy01;
        public readonly IReadOnlyList<Defect> Defects;

        // Captured MinServableAccuracy at match time so IsServable doesn't need
        // to re-touch cfg later (RecipeMatch is otherwise immutable/POCO).
        readonly float _minServableAccuracy;

        // Judgment call (documented at the end of the report): section 7's
        // frozen QualityCalculator.Score signature is
        // Score(BowlContents, in RecipeMatch, in BrothState, IBalanceConfig) --
        // it does NOT take an IRecipeDef, yet the doc's own prose says
        // Temperature01 compares BrothTemperatureC against
        // recipe.TargetServeTemperatureC "(from IRecipeDef)". Since RecipeMatch
        // is a type this milestone owns outright (not a frozen upstream
        // contract), the smallest-deviation fix is to carry the matched
        // recipe's target temperature forward here -- RecipeMatcher.Match
        // already has the IRecipeDef in hand -- rather than widening Score's
        // frozen parameter list.
        public readonly float TargetServeTemperatureC;

        public RecipeMatch(
            RecipeId recipe,
            float accuracy01,
            IReadOnlyList<Defect> defects,
            float minServableAccuracy,
            float targetServeTemperatureC)
        {
            Recipe = recipe;
            Accuracy01 = accuracy01;
            Defects = defects;
            _minServableAccuracy = minServableAccuracy;
            TargetServeTemperatureC = targetServeTemperatureC;
        }

        /// <summary>
        /// architecture.md section 7 shows this as
        /// `Accuracy01 >= 0f; // >= cfg.minServableAccuracy` -- the inline
        /// comment is a placeholder showing intent (compare against config),
        /// not literal code to copy verbatim; wired to the real
        /// cfg.MinServableAccuracy captured at match time.
        /// </summary>
        public bool IsServable => Accuracy01 >= _minServableAccuracy;
    }

    /// <summary>
    /// Pure function: how well a bowl matches a recipe. Deterministic, no
    /// allocations beyond the returned Defect list. See architecture.md
    /// section 7 for the algorithm this implements step for step.
    /// </summary>
    public static class RecipeMatcher
    {
        public static RecipeMatch Match(BowlContents bowl, IRecipeDef recipe, IBalanceConfig cfg)
        {
            var bowlComponents = bowl.Components;
            var recipeComponents = recipe.Components;
            var defects = new List<Defect>();

            // Steps 1-2: for each recipe requirement, sum the bowl's matching
            // (same slot, same ingredient) amount and classify the outcome.
            // "Group bowl components by ComponentSlot" (section 7 step 1) is
            // achieved here via a direct per-requirement scan rather than
            // building an intermediate Dictionary<ComponentSlot, ...> --
            // recipes have a handful of components and bowls a handful of
            // items, so this stays allocation-free beyond the defect list
            // while producing an identical grouping result.
            for (int r = 0; r < recipeComponents.Count; r++)
            {
                var rc = recipeComponents[r];
                var wantedIngredient = new IngredientId(rc.ingredientId);

                float totalAmount = 0f;
                bool foundCorrectIngredient = false;
                bool foundOtherIngredientInSlot = false;

                for (int b = 0; b < bowlComponents.Count; b++)
                {
                    var bc = bowlComponents[b];
                    if (bc.Slot != rc.slot)
                        continue;

                    if (bc.Ingredient.Equals(wantedIngredient))
                    {
                        totalAmount += bc.Amount;
                        foundCorrectIngredient = true;
                    }
                    else
                    {
                        foundOtherIngredientInSlot = true;
                    }
                }

                if (!foundCorrectIngredient)
                {
                    if (rc.required)
                    {
                        // Judgment call: if some other ingredient occupies the
                        // required slot, that's WrongIngredient; only an
                        // entirely empty required slot is MissingRequired. A
                        // wrong-ingredient bowl item still fails the separate
                        // "matches no recipe component" check in step 3 below
                        // and picks up its own ExtraUnwanted defect there too --
                        // both facts are real (the requirement is unmet AND
                        // there's a foreign ingredient physically in the bowl).
                        var kind = foundOtherIngredientInSlot
                            ? DefectKind.WrongIngredient
                            : DefectKind.MissingRequired;
                        defects.Add(new Defect(kind, rc.slot, wantedIngredient, 1f));
                    }
                    // Optional (garnish) requirement, entirely absent: no defect.
                }
                else
                {
                    float diff = totalAmount - rc.amount;
                    float scale = rc.amount > 0f ? rc.amount : 1f;

                    if (diff < -rc.tolerance)
                    {
                        float severity = MathP.Clamp01((-diff - rc.tolerance) / scale);
                        defects.Add(new Defect(DefectKind.TooLittle, rc.slot, wantedIngredient, severity));
                    }
                    else if (diff > rc.tolerance)
                    {
                        float severity = MathP.Clamp01((diff - rc.tolerance) / scale);
                        defects.Add(new Defect(DefectKind.TooMuch, rc.slot, wantedIngredient, severity));
                    }
                }
            }

            // Step 3: any bowl component whose (slot, ingredient) pair is not
            // requested by any recipe component is an unwanted extra.
            for (int b = 0; b < bowlComponents.Count; b++)
            {
                var bc = bowlComponents[b];
                bool wanted = false;

                for (int r = 0; r < recipeComponents.Count; r++)
                {
                    var rc = recipeComponents[r];
                    if (rc.slot == bc.Slot && string.Equals(rc.ingredientId, bc.Ingredient.Value, StringComparison.Ordinal))
                    {
                        wanted = true;
                        break;
                    }
                }

                if (!wanted)
                    defects.Add(new Defect(DefectKind.ExtraUnwanted, bc.Slot, bc.Ingredient, 1f));
            }

            // Step 4: Accuracy01 = clamp01(1 - sum(severity * defectWeight(kind))).
            float penalty = 0f;
            for (int i = 0; i < defects.Count; i++)
                penalty += defects[i].Severity01 * cfg.DefectWeight(defects[i].Kind);

            float accuracy = MathP.Clamp01(1f - penalty);

            return new RecipeMatch(recipe.Id, accuracy, defects, cfg.MinServableAccuracy, recipe.TargetServeTemperatureC);
        }

        /// <summary>
        /// Runs Match over the whole menu, returns the highest-accuracy
        /// result -- how the player finds out they made a Phở Chín when the
        /// customer wanted Phở Tái (section 7 step 5). A null/empty menu is a
        /// programming error (nothing to compare against), not a legitimate
        /// runtime "no match" outcome, so it throws rather than returning a
        /// degenerate default RecipeMatch.
        /// </summary>
        public static RecipeMatch BestMatch(BowlContents bowl, IReadOnlyList<IRecipeDef> menu, IBalanceConfig cfg)
        {
            if (menu == null || menu.Count == 0)
                throw new ArgumentException("Menu must contain at least one recipe.", nameof(menu));

            RecipeMatch best = Match(bowl, menu[0], cfg);
            for (int i = 1; i < menu.Count; i++)
            {
                var candidate = Match(bowl, menu[i], cfg);
                if (candidate.Accuracy01 > best.Accuracy01)
                    best = candidate;
            }

            return best;
        }
    }
}
