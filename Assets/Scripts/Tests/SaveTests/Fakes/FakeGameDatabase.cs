using System;
using System.Collections.Generic;
using Pho.Domain.Contracts;
using Pho.Domain.Identity;

namespace Pho.Save.Tests.Fakes
{
    /// <summary>Trivial IGameDatabase stand-in -- SaveTests never needs real content lookups.</summary>
    sealed class FakeGameDatabase : IGameDatabase
    {
        public bool TryGetIngredient(IngredientId id, out IIngredientDef def) { def = null; return false; }
        public bool TryGetRecipe(RecipeId id, out IRecipeDef def) { def = null; return false; }
        public bool TryGetEquipment(EquipmentId id, out IEquipmentDef def) { def = null; return false; }
        public bool TryGetArchetype(ArchetypeId id, out ICustomerArchetype def) { def = null; return false; }

        public IReadOnlyList<IIngredientDef> Ingredients => Array.Empty<IIngredientDef>();
        public IReadOnlyList<IRecipeDef> Recipes => Array.Empty<IRecipeDef>();
        public IReadOnlyList<IEquipmentDef> Equipment => Array.Empty<IEquipmentDef>();
        public IReadOnlyList<ICustomerArchetype> Archetypes => Array.Empty<ICustomerArchetype>();
    }
}
