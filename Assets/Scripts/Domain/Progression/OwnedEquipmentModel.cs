using System;
using System.Collections.Generic;
using Pho.Domain.Contracts;
using Pho.Domain.Cooking;
using Pho.Domain.Identity;

namespace Pho.Domain.Progression
{
    /// <summary>
    /// Pure runtime record of which pieces of equipment the restaurant
    /// currently owns, plus the one thing that ownership is actually FOR:
    /// turning it into an <see cref="EquipmentModifiers"/> the broth
    /// simulator consumes (architecture.md section 7 -- "EquipmentModifiers
    /// is produced from the owned EquipmentData ... this is how the
    /// vertical-slice upgrade (commercial burner) produces a visible
    /// workflow improvement").
    ///
    /// Static definition vs. runtime state stays split per doc section 2.3:
    /// this holds only <see cref="EquipmentId"/>s. The stats live on the
    /// <see cref="IEquipmentDef"/> assets and are resolved on demand through
    /// an <see cref="IGameDatabase"/>, so nothing here goes stale when a
    /// balance pass retunes an asset.
    ///
    /// ERROR CONVENTION (mirrors InventoryModel / EconomyService): a
    /// programmer error throws; a normal runtime occurrence returns false.
    /// An empty id is a programmer error (nothing legitimately calls this
    /// with a default-constructed id). Buying something already owned is a
    /// perfectly normal thing for a player to try, so it returns false.
    /// </summary>
    public sealed class OwnedEquipmentModel
    {
        readonly List<EquipmentId> _owned = new List<EquipmentId>();

        public IReadOnlyList<EquipmentId> Owned => _owned;

        public int Count => _owned.Count;

        public bool Contains(EquipmentId id)
        {
            for (int i = 0; i < _owned.Count; i++)
            {
                if (_owned[i].Equals(id)) return true;
            }
            return false;
        }

        /// <summary>
        /// Grants ownership. Returns false if this equipment is already
        /// owned (the "can't double-buy" half of M13's acceptance criteria
        /// -- a normal runtime occurrence, not an error). Throws only on an
        /// empty id, which is a programmer error.
        /// </summary>
        public bool TryAdd(EquipmentId id)
        {
            if (id.IsEmpty)
                throw new ArgumentException("EquipmentId must not be empty.", nameof(id));

            if (Contains(id)) return false;

            _owned.Add(id);
            return true;
        }

        /// <summary>Removes every owned entry. Save/load restore point -- see <see cref="Restore"/>. Mirrors InventoryModel.Clear.</summary>
        public void Clear() => _owned.Clear();

        /// <summary>
        /// Replaces the whole owned set from a save file. Empty and
        /// duplicate ids are skipped rather than throwing: doc section 4
        /// decision 2 says "a missing ID on load is logged and skipped,
        /// never a hard crash", and the same tolerance has to apply to a
        /// malformed one -- a corrupt save must not be able to crash the
        /// restore path. Returns the number of ids actually taken, so a
        /// caller that wants to log "skipped N entries" can.
        /// </summary>
        public int Restore(IEnumerable<EquipmentId> ids)
        {
            Clear();
            if (ids == null) return 0;

            int accepted = 0;
            foreach (var id in ids)
            {
                if (id.IsEmpty) continue;
                if (Contains(id)) continue;

                _owned.Add(id);
                accepted++;
            }
            return accepted;
        }

        /// <summary>
        /// Combines everything currently owned into the modifiers the broth
        /// simulator ticks with. Returns <see cref="EquipmentModifiers.Default"/>
        /// (1, 1) when nothing is owned, when no database is available to
        /// resolve the defs against, or when no owned id resolves.
        ///
        /// COMBINATION POLICY: best-in-slot per <see cref="EquipmentType"/>,
        /// then multiplied across distinct types. Reasoning: a kitchen runs
        /// exactly one burner at a time, so owning a tier-1 AND a tier-2
        /// burner must NOT stack (a naive product across everything owned
        /// would silently hand out 1.5 x 2.0 = 3.0 the moment a second
        /// burner tier is authored -- a latent bug that costs eight lines to
        /// avoid today). Different types (burner + pot) genuinely compound,
        /// so those multiply. With exactly one upgrade authored in the
        /// vertical slice both rules collapse to "that item's multiplier".
        ///
        /// A def whose multiplier is non-positive is treated as 1 rather
        /// than allowed to zero out the whole kitchen -- an unauthored/
        /// default-initialized asset (all fields 0) is a content bug, and
        /// silently freezing every pot in the restaurant is the worst
        /// possible way to surface it.
        /// </summary>
        public EquipmentModifiers ComputeModifiers(IGameDatabase database)
        {
            if (database == null || _owned.Count == 0)
                return EquipmentModifiers.Default;

            var bestHeatPerType = new Dictionary<EquipmentType, float>();
            var bestCapacityPerType = new Dictionary<EquipmentType, float>();

            for (int i = 0; i < _owned.Count; i++)
            {
                if (!database.TryGetEquipment(_owned[i], out var def) || def == null)
                    continue;

                float heat = Sanitize(def.HeatRateMultiplier);
                float capacity = Sanitize(def.CapacityMultiplier);
                var type = def.EquipmentType;

                if (!bestHeatPerType.TryGetValue(type, out var currentHeat) || heat > currentHeat)
                    bestHeatPerType[type] = heat;

                if (!bestCapacityPerType.TryGetValue(type, out var currentCapacity) || capacity > currentCapacity)
                    bestCapacityPerType[type] = capacity;
            }

            if (bestHeatPerType.Count == 0)
                return EquipmentModifiers.Default;

            float heatProduct = 1f;
            foreach (var value in bestHeatPerType.Values) heatProduct *= value;

            float capacityProduct = 1f;
            foreach (var value in bestCapacityPerType.Values) capacityProduct *= value;

            return new EquipmentModifiers(heatProduct, capacityProduct);
        }

        static float Sanitize(float multiplier) => multiplier > 0f ? multiplier : 1f;
    }
}
