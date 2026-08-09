using System.Collections.Generic;
using System.Linq;
using Pho.Domain.Contracts;
using Pho.Domain.Identity;
using UnityEngine;

namespace Pho.Data
{
    /// <summary>
    /// The single content root. Implements the frozen <see cref="IGameDatabase"/>
    /// contract exactly. Wires the four content arrays together and builds
    /// Dictionary&lt;string,T&gt; lookups in OnEnable for the TryGetX methods,
    /// keyed by each typed ID's .Value (a bare string), per doc section 2.2.
    ///
    /// Referenced by GameBootstrap in Boot.unity -- deliberately NOT loaded
    /// from a Resources/ folder (doc section 12, deviation #4).
    /// </summary>
    [CreateAssetMenu(menuName = "Pho/Game Database", fileName = "GameDatabase")]
    public sealed class GameDatabase : ScriptableObject, IGameDatabase
    {
        [SerializeField] IngredientData[] ingredients;
        [SerializeField] RecipeData[] recipes;
        [SerializeField] EquipmentData[] equipment;
        [SerializeField] CustomerArchetype[] archetypes;

        Dictionary<string, IngredientData> _ingredientLookup;
        Dictionary<string, RecipeData> _recipeLookup;
        Dictionary<string, EquipmentData> _equipmentLookup;
        Dictionary<string, CustomerArchetype> _archetypeLookup;

        IReadOnlyList<IIngredientDef> _ingredientsView;
        IReadOnlyList<IRecipeDef> _recipesView;
        IReadOnlyList<IEquipmentDef> _equipmentView;
        IReadOnlyList<ICustomerArchetype> _archetypesView;

        // Concrete-typed views (in addition to the interface-typed ones the
        // frozen contract requires) so Pho.Data.Tests/ContentValidationTests
        // can enumerate the actual assets (filenames, RawId) without needing
        // Unity Editor-only APIs baked into this runtime-safe assembly.
        public IReadOnlyList<IngredientData> IngredientAssets => ingredients ?? System.Array.Empty<IngredientData>();
        public IReadOnlyList<RecipeData> RecipeAssets => recipes ?? System.Array.Empty<RecipeData>();
        public IReadOnlyList<EquipmentData> EquipmentAssets => equipment ?? System.Array.Empty<EquipmentData>();
        public IReadOnlyList<CustomerArchetype> ArchetypeAssets => archetypes ?? System.Array.Empty<CustomerArchetype>();

        public IReadOnlyList<IIngredientDef> Ingredients => _ingredientsView;
        public IReadOnlyList<IRecipeDef> Recipes => _recipesView;
        public IReadOnlyList<IEquipmentDef> Equipment => _equipmentView;
        public IReadOnlyList<ICustomerArchetype> Archetypes => _archetypesView;

        void OnEnable() => BuildLookups();

        void BuildLookups()
        {
            ingredients ??= System.Array.Empty<IngredientData>();
            recipes ??= System.Array.Empty<RecipeData>();
            equipment ??= System.Array.Empty<EquipmentData>();
            archetypes ??= System.Array.Empty<CustomerArchetype>();

            _ingredientLookup = ingredients.Where(i => i != null).ToDictionary(i => i.Id.Value);
            _recipeLookup = recipes.Where(r => r != null).ToDictionary(r => r.Id.Value);
            _equipmentLookup = equipment.Where(e => e != null).ToDictionary(e => e.Id.Value);
            _archetypeLookup = archetypes.Where(a => a != null).ToDictionary(a => a.Id.Value);

            _ingredientsView = ingredients.Where(i => i != null).Cast<IIngredientDef>().ToList();
            _recipesView = recipes.Where(r => r != null).Cast<IRecipeDef>().ToList();
            _equipmentView = equipment.Where(e => e != null).Cast<IEquipmentDef>().ToList();
            _archetypesView = archetypes.Where(a => a != null).Cast<ICustomerArchetype>().ToList();
        }

        public bool TryGetIngredient(IngredientId id, out IIngredientDef def)
        {
            if (_ingredientLookup == null) BuildLookups();
            if (_ingredientLookup.TryGetValue(id.Value, out var data)) { def = data; return true; }
            def = null;
            return false;
        }

        public bool TryGetRecipe(RecipeId id, out IRecipeDef def)
        {
            if (_recipeLookup == null) BuildLookups();
            if (_recipeLookup.TryGetValue(id.Value, out var data)) { def = data; return true; }
            def = null;
            return false;
        }

        public bool TryGetEquipment(EquipmentId id, out IEquipmentDef def)
        {
            if (_equipmentLookup == null) BuildLookups();
            if (_equipmentLookup.TryGetValue(id.Value, out var data)) { def = data; return true; }
            def = null;
            return false;
        }

        public bool TryGetArchetype(ArchetypeId id, out ICustomerArchetype def)
        {
            if (_archetypeLookup == null) BuildLookups();
            if (_archetypeLookup.TryGetValue(id.Value, out var data)) { def = data; return true; }
            def = null;
            return false;
        }

#if UNITY_EDITOR
        /// <summary>Editor-only initializer used exclusively by ContentGenerator. Never called at runtime.</summary>
        public void EditorInit(
            IngredientData[] ingredients,
            RecipeData[] recipes,
            EquipmentData[] equipment,
            CustomerArchetype[] archetypes)
        {
            this.ingredients = ingredients;
            this.recipes = recipes;
            this.equipment = equipment;
            this.archetypes = archetypes;
            BuildLookups();
        }
#endif
    }
}
