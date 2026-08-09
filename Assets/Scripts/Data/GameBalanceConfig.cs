using System;
using System.Collections.Generic;
using Pho.Domain.Contracts;
using UnityEngine;

namespace Pho.Data
{
    /// <summary>
    /// Static tunable-balance definition. Implements the frozen
    /// <see cref="IBalanceConfig"/> contract exactly.
    ///
    /// Two authoring notes:
    /// 1. <see cref="IBalanceConfig.DefectWeight"/> is a METHOD, not a
    ///    property. Unity can't natively serialize Dictionary&lt;,&gt; in the
    ///    Inspector, so it's backed by a serialized array of (kind, weight)
    ///    pairs and a runtime Dictionary&lt;DefectKind,float&gt; lookup built in
    ///    OnEnable -- the same pattern GameDatabase uses for its lookups.
    /// 2. IBalanceConfig.RentAmount is `decimal`, which Unity's serializer
    ///    does not support at all (not just "awkward in the Inspector" --
    ///    it silently fails to persist). Stored as `double` and exposed via
    ///    an explicit decimal cast. See the "awkward for authoring" note in
    ///    the final report for why this is worth flagging upstream.
    /// </summary>
    [CreateAssetMenu(menuName = "Pho/Game Balance Config", fileName = "GameBalanceConfig")]
    public sealed class GameBalanceConfig : ScriptableObject, IBalanceConfig
    {
        [Serializable]
        public struct DefectWeightEntry
        {
            public DefectKind kind;
            public float weight;
        }

        [SerializeField] float patienceDecayRate;
        [SerializeField] float minServableAccuracy;
        [SerializeField] DefectWeightEntry[] defectWeights;
        [SerializeField] float spoilageRatePerHour;
        [SerializeField] float dayLengthSeconds;
        [SerializeField] double rentAmount;              // see class doc note 2
        [SerializeField] int rentIntervalDays;

        Dictionary<DefectKind, float> _defectWeightLookup;

        public float PatienceDecayRate => patienceDecayRate;
        public float MinServableAccuracy => minServableAccuracy;
        public float SpoilageRatePerHour => spoilageRatePerHour;
        public float DayLengthSeconds => dayLengthSeconds;
        public decimal RentAmount => (decimal)rentAmount;
        public int RentIntervalDays => rentIntervalDays;

        public float DefectWeight(DefectKind kind)
        {
            if (_defectWeightLookup == null) BuildLookup();
            return _defectWeightLookup.TryGetValue(kind, out var weight) ? weight : 0f;
        }

        void OnEnable() => BuildLookup();

        void BuildLookup()
        {
            _defectWeightLookup = new Dictionary<DefectKind, float>();
            if (defectWeights == null) return;
            foreach (var entry in defectWeights)
                _defectWeightLookup[entry.kind] = entry.weight;
        }

#if UNITY_EDITOR
        /// <summary>Editor-only initializer used exclusively by ContentGenerator. Never called at runtime.</summary>
        public void EditorInit(
            float patienceDecayRate,
            float minServableAccuracy,
            DefectWeightEntry[] defectWeights,
            float spoilageRatePerHour,
            float dayLengthSeconds,
            decimal rentAmount,
            int rentIntervalDays)
        {
            this.patienceDecayRate = patienceDecayRate;
            this.minServableAccuracy = minServableAccuracy;
            this.defectWeights = defectWeights;
            this.spoilageRatePerHour = spoilageRatePerHour;
            this.dayLengthSeconds = dayLengthSeconds;
            this.rentAmount = (double)rentAmount;
            this.rentIntervalDays = rentIntervalDays;
            BuildLookup();
        }
#endif
    }
}
