using System;

namespace Pho.Domain.Contracts
{
    public enum IngredientCategory
    {
        Protein, Noodle, Aromatic, Herb, Broth, Condiment, Drink
    }

    public enum StorageRequirement
    {
        Ambient, Refrigerated, Frozen
    }

    public enum ComponentSlot
    {
        Noodle, Broth, Protein, Aromatic, Herb, Garnish
    }

    public enum EquipmentType
    {
        Burner, Pot, Fridge, Sink, Slicer
    }

    /// <summary>
    /// One ingredient requirement within a recipe. `[Serializable]` (plain
    /// System attribute, not UnityEngine) so Pho.Data's RecipeData
    /// ScriptableObject can expose it directly in the Inspector as
    /// `RecipeComponent[]` without a duplicate Unity-side struct.
    /// `ingredientId` is a bare string (not the typed IngredientId) because
    /// this struct is Inspector-serialized content, not runtime logic --
    /// consumers wrap it with `new IngredientId(component.ingredientId)`.
    /// </summary>
    [Serializable]
    public struct RecipeComponent
    {
        public ComponentSlot slot;
        public string ingredientId;
        public float amount;     // "units"
        public float tolerance;  // +/- before penalty
        public bool required;    // false = optional/garnish
    }

    /// <summary>Inclusive min/max, used for archetype patience/budget/eat-duration ranges.</summary>
    [Serializable]
    public struct FloatRange
    {
        public float min;
        public float max;

        public FloatRange(float min, float max)
        {
            this.min = min;
            this.max = max;
        }
    }
}
