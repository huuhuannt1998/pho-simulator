using System.Collections.Generic;
using Pho.Domain.Contracts;
using Pho.Domain.Identity;
using UnityEngine;

namespace Pho.Data
{
    /// <summary>
    /// Static recipe definition. Implements the frozen <see cref="IRecipeDef"/>
    /// contract exactly. Reuses <see cref="RecipeComponent"/> as declared in
    /// Pho.Domain.Contracts.DefEnums.cs verbatim -- it is already
    /// Inspector-serializable ([Serializable], no UnityEngine types), so
    /// there is no duplicate Unity-side struct.
    /// </summary>
    [CreateAssetMenu(menuName = "Pho/Recipe", fileName = "NewRecipe")]
    public sealed class RecipeData : ScriptableObject, IRecipeDef
    {
        [SerializeField] string id;                     // "rec.pho_tai"
        [SerializeField] string displayName;
        [SerializeField] RecipeComponent[] components;
        [SerializeField] float basePrice;
        [SerializeField] float preparationDifficulty01;
        [SerializeField] float targetBrothVolumeLiters;
        [SerializeField] float targetServeTemperatureC;

        /// <summary>Raw serialized id string, for content-validation tooling (filename/regex checks).</summary>
        public string RawId => id;

        public RecipeId Id => new RecipeId(id);
        public string DisplayName => displayName;
        public IReadOnlyList<RecipeComponent> Components => components;
        public float BasePrice => basePrice;
        public float PreparationDifficulty01 => preparationDifficulty01;
        public float TargetBrothVolumeLiters => targetBrothVolumeLiters;
        public float TargetServeTemperatureC => targetServeTemperatureC;

#if UNITY_EDITOR
        /// <summary>Editor-only initializer used exclusively by ContentGenerator. Never called at runtime.</summary>
        public void EditorInit(
            string id,
            string displayName,
            RecipeComponent[] components,
            float basePrice,
            float preparationDifficulty01,
            float targetBrothVolumeLiters,
            float targetServeTemperatureC)
        {
            this.id = id;
            this.displayName = displayName;
            this.components = components;
            this.basePrice = basePrice;
            this.preparationDifficulty01 = preparationDifficulty01;
            this.targetBrothVolumeLiters = targetBrothVolumeLiters;
            this.targetServeTemperatureC = targetServeTemperatureC;
        }
#endif
    }
}
