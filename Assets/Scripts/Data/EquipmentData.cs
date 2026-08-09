using Pho.Domain.Contracts;
using Pho.Domain.Identity;
using UnityEngine;

namespace Pho.Data
{
    /// <summary>
    /// Static equipment definition. Implements the frozen
    /// <see cref="IEquipmentDef"/> contract exactly. No equipment content is
    /// authored in this pass (M13 "Commercial Burner" is a later milestone) --
    /// this class exists so GameDatabase's EquipmentData[] array and
    /// IGameDatabase.Equipment are well-typed today.
    /// </summary>
    [CreateAssetMenu(menuName = "Pho/Equipment", fileName = "NewEquipment")]
    public sealed class EquipmentData : ScriptableObject, IEquipmentDef
    {
        [SerializeField] string id;                     // "eq.burner_commercial"
        [SerializeField] string displayName;
        [SerializeField] EquipmentType equipmentType;
        [SerializeField] int tier;
        [SerializeField] float purchaseCost;
        [SerializeField] float heatRateMultiplier;
        [SerializeField] float capacityMultiplier;
        [SerializeField] float qualityBonus01;

        // Unity-only, deliberately absent from IEquipmentDef -- see doc section 2.2.
        [SerializeField] GameObject prefab;

        /// <summary>Raw serialized id string, for content-validation tooling (filename/regex checks).</summary>
        public string RawId => id;

        public EquipmentId Id => new EquipmentId(id);
        public string DisplayName => displayName;
        public EquipmentType EquipmentType => equipmentType;
        public int Tier => tier;
        public float PurchaseCost => purchaseCost;
        public float HeatRateMultiplier => heatRateMultiplier;
        public float CapacityMultiplier => capacityMultiplier;
        public float QualityBonus01 => qualityBonus01;

        public GameObject Prefab => prefab;

#if UNITY_EDITOR
        /// <summary>Editor-only initializer used exclusively by ContentGenerator. Never called at runtime.</summary>
        public void EditorInit(
            string id,
            string displayName,
            EquipmentType equipmentType,
            int tier,
            float purchaseCost,
            float heatRateMultiplier,
            float capacityMultiplier,
            float qualityBonus01)
        {
            this.id = id;
            this.displayName = displayName;
            this.equipmentType = equipmentType;
            this.tier = tier;
            this.purchaseCost = purchaseCost;
            this.heatRateMultiplier = heatRateMultiplier;
            this.capacityMultiplier = capacityMultiplier;
            this.qualityBonus01 = qualityBonus01;
        }
#endif
    }
}
