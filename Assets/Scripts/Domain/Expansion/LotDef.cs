using System;
using System.Collections.Generic;

namespace Pho.Domain.Expansion
{
    /// <summary>
    /// Static, authored definition of one purchasable unit of the complex --
    /// the shop next door, the unit upstairs, the back alley storeroom.
    /// Runtime ownership state lives in <see cref="ExpansionModel"/>; this
    /// type is immutable, exactly the static-def / runtime-state split
    /// architecture.md section 2.3 mandates.
    ///
    /// Unlike ingredients/recipes/equipment this is a plain immutable POCO
    /// rather than an <c>I...Def</c> interface implemented by a
    /// ScriptableObject. Reason: an interface only earns its keep when a
    /// Unity asset has to supply Unity-only fields alongside the data (an
    /// icon, a world prefab). A lot's *content* is four scalars and an
    /// adjacency list; its Unity side is a scene GameObject that a
    /// <c>LotSection</c> component already points at by id. Making this a
    /// POCO keeps the whole catalog constructible in a pure unit test with
    /// no fakes, and a future Pho.Data <c>LotData</c> ScriptableObject can
    /// still project into this type in one line.
    ///
    /// ADJACENCY IS THE INTERESTING CONSTRAINT. <see cref="Prerequisites"/>
    /// is the set of lots physically touching this one. Eligibility is
    /// <b>ANY</b>, not ALL: owning any single neighbour opens this lot up.
    /// That is what makes it an adjacency rule rather than a tech tree --
    /// a corner unit touching both unit_2 and unit_3 must be reachable from
    /// either side, and requiring both would silently turn a physical
    /// neighbourhood into a linear unlock chain. The complex therefore always
    /// grows contiguously outward from the shop the player started in, and
    /// can never contain a floating island across the street.
    /// </summary>
    public sealed class LotDef
    {
        static readonly LotId[] NoPrerequisites = new LotId[0];

        /// <summary>Stable content id, e.g. <c>lot.unit_2</c>.</summary>
        public LotId Id { get; }

        /// <summary>Human-facing name for the purchase prompt and the toast, e.g. "Unit 2 -- Next Door".</summary>
        public string DisplayName { get; }

        /// <summary>Purchase price. <c>decimal</c>, never float -- architecture.md section 4 decision 3.</summary>
        public decimal Price { get; }

        /// <summary>
        /// True for the unit the player already runs on day 1. A starting lot
        /// is owned from the first frame and has no prerequisites -- see
        /// <see cref="ExpansionModel"/>.
        /// </summary>
        public bool IsStarting { get; }

        /// <summary>
        /// Lots physically adjacent to this one. Owning ANY of them makes
        /// this lot eligible. Empty only for a starting lot.
        /// </summary>
        public IReadOnlyList<LotId> Prerequisites { get; }

        /// <summary>
        /// ERROR CONVENTION (mirrors InventoryModel / OwnedEquipmentModel /
        /// EconomyService): a programmer error throws, a normal runtime
        /// outcome returns false. Everything this constructor rejects is
        /// malformed *authored content* -- a programmer/designer error that
        /// can never arise from anything a player does -- so it throws.
        /// Nothing here is reachable from a save file: restore goes through
        /// <see cref="ExpansionModel.Restore"/>, which is deliberately
        /// tolerant instead.
        /// </summary>
        public LotDef(LotId id, string displayName, decimal price, bool isStarting = false, IReadOnlyList<LotId> prerequisites = null)
        {
            if (id.IsEmpty)
                throw new ArgumentException("LotId must not be empty.", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("Lot displayName must not be blank.", nameof(displayName));
            if (price < 0m)
                throw new ArgumentException("Lot price must not be negative.", nameof(price));

            var prereqs = prerequisites ?? NoPrerequisites;

            if (isStarting)
            {
                // A starting lot is owned before any transaction exists, so a
                // prerequisite on it could never be satisfied and would be a
                // silent unreachability bug rather than a rule.
                if (prereqs.Count > 0)
                    throw new ArgumentException($"Starting lot '{id}' must not declare prerequisites.", nameof(prerequisites));
                if (price != 0m)
                    throw new ArgumentException($"Starting lot '{id}' must be free -- it is granted, not bought.", nameof(price));
            }
            else if (prereqs.Count == 0)
            {
                // THE adjacency invariant, enforced at authoring time. A
                // non-starting lot with no neighbours would be buyable from
                // anywhere on the map, which is precisely the "buy a far
                // building" case this model exists to forbid.
                throw new ArgumentException(
                    $"Non-starting lot '{id}' must declare at least one adjacent prerequisite lot, otherwise it could be bought out of nowhere.",
                    nameof(prerequisites));
            }

            var copy = new List<LotId>(prereqs.Count);
            for (int i = 0; i < prereqs.Count; i++)
            {
                var prereq = prereqs[i];
                if (prereq.IsEmpty)
                    throw new ArgumentException($"Lot '{id}' declares an empty prerequisite id.", nameof(prerequisites));
                if (prereq.Equals(id))
                    throw new ArgumentException($"Lot '{id}' cannot be its own prerequisite.", nameof(prerequisites));
                if (copy.Contains(prereq))
                    throw new ArgumentException($"Lot '{id}' declares prerequisite '{prereq}' twice.", nameof(prerequisites));

                copy.Add(prereq);
            }

            Id = id;
            DisplayName = displayName;
            Price = price;
            IsStarting = isStarting;
            Prerequisites = copy;
        }

        public override string ToString() => $"{DisplayName} ({Id})";
    }
}
