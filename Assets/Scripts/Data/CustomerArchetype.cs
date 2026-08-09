using System.Collections.Generic;
using Pho.Domain.Contracts;
using Pho.Domain.Identity;
using UnityEngine;

namespace Pho.Data
{
    /// <summary>
    /// Static customer archetype definition. Implements the frozen
    /// <see cref="ICustomerArchetype"/> contract exactly. No archetype content
    /// is authored in this pass (M6/M7 customer systems are later milestones)
    /// -- this class exists so GameDatabase's CustomerArchetype[] array and
    /// IGameDatabase.Archetypes are well-typed today.
    ///
    /// Class name intentionally matches the interface name minus the "I"
    /// prefix, per the architecture doc's own snippet (section 2.2).
    /// </summary>
    [CreateAssetMenu(menuName = "Pho/Customer Archetype", fileName = "NewArchetype")]
    public sealed class CustomerArchetype : ScriptableObject, ICustomerArchetype
    {
        [SerializeField] string id;                     // "arc.office_worker"
        [SerializeField] string displayName;
        [SerializeField] float spawnWeight;
        [SerializeField] FloatRange patienceSeconds;
        [SerializeField] FloatRange budget;
        [SerializeField] float qualityExpectation01;
        [SerializeField] float cleanlinessSensitivity01;
        [SerializeField] float serviceSensitivity01;
        [SerializeField] float priceSensitivity01;
        [SerializeField] float tipChance01;
        [SerializeField] float tipFraction;
        [SerializeField] float reviewChance01;
        [SerializeField] string[] preferredRecipeIds;
        [SerializeField] FloatRange eatDurationSeconds;
        [SerializeField] int visualVariantIndex;

        /// <summary>Raw serialized id string, for content-validation tooling (filename/regex checks).</summary>
        public string RawId => id;

        public ArchetypeId Id => new ArchetypeId(id);
        public string DisplayName => displayName;
        public float SpawnWeight => spawnWeight;
        public FloatRange PatienceSeconds => patienceSeconds;
        public FloatRange Budget => budget;
        public float QualityExpectation01 => qualityExpectation01;
        public float CleanlinessSensitivity01 => cleanlinessSensitivity01;
        public float ServiceSensitivity01 => serviceSensitivity01;
        public float PriceSensitivity01 => priceSensitivity01;
        public float TipChance01 => tipChance01;
        public float TipFraction => tipFraction;
        public float ReviewChance01 => reviewChance01;

        public IReadOnlyList<RecipeId> PreferredRecipeIds
        {
            get
            {
                var source = preferredRecipeIds ?? System.Array.Empty<string>();
                var list = new List<RecipeId>(source.Length);
                foreach (var s in source) list.Add(new RecipeId(s));
                return list;
            }
        }

        public FloatRange EatDurationSeconds => eatDurationSeconds;
        public int VisualVariantIndex => visualVariantIndex;

#if UNITY_EDITOR
        /// <summary>Editor-only initializer used exclusively by ContentGenerator. Never called at runtime.</summary>
        public void EditorInit(
            string id,
            string displayName,
            float spawnWeight,
            FloatRange patienceSeconds,
            FloatRange budget,
            float qualityExpectation01,
            float cleanlinessSensitivity01,
            float serviceSensitivity01,
            float priceSensitivity01,
            float tipChance01,
            float tipFraction,
            float reviewChance01,
            string[] preferredRecipeIds,
            FloatRange eatDurationSeconds,
            int visualVariantIndex)
        {
            this.id = id;
            this.displayName = displayName;
            this.spawnWeight = spawnWeight;
            this.patienceSeconds = patienceSeconds;
            this.budget = budget;
            this.qualityExpectation01 = qualityExpectation01;
            this.cleanlinessSensitivity01 = cleanlinessSensitivity01;
            this.serviceSensitivity01 = serviceSensitivity01;
            this.priceSensitivity01 = priceSensitivity01;
            this.tipChance01 = tipChance01;
            this.tipFraction = tipFraction;
            this.reviewChance01 = reviewChance01;
            this.preferredRecipeIds = preferredRecipeIds;
            this.eatDurationSeconds = eatDurationSeconds;
            this.visualVariantIndex = visualVariantIndex;
        }
#endif
    }
}
