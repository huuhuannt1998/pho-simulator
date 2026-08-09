using System;
using System.Collections.Generic;

namespace Pho.Domain.Expansion
{
    /// <summary>
    /// Why a lot cannot be bought right now. Exists so the purchase prompt,
    /// the refusal toast, and (in co-op) the host's reply to a client's
    /// request can all state the SAME reason without any of them
    /// re-deriving it -- the bug class where the UI says "too expensive" and
    /// the service actually refused for adjacency.
    ///
    /// Deliberately does NOT include an "cannot afford" member: affordability
    /// is the caller's question, not the model's. See
    /// <see cref="ExpansionModel.CanPurchase"/>.
    /// </summary>
    public enum LotEligibility
    {
        /// <summary>Adjacent to something owned, not yet owned. Buy it if you can pay.</summary>
        Eligible = 0,

        /// <summary>No such lot in the catalog (typo, stale save, hostile network message).</summary>
        UnknownLot,

        /// <summary>Already part of the complex. The "no double-purchase" rule.</summary>
        AlreadyOwned,

        /// <summary>Real lot, but it does not touch anything the player owns yet. THE adjacency rule.</summary>
        NotAdjacent,
    }

    /// <summary>
    /// Runtime record of which lots the complex currently occupies, and the
    /// one rule that governs growth: you may only buy a unit ADJACENT to one
    /// you already own. Pure C#, no UnityEngine -- so the entire "buy the
    /// next building" progression is provable under <c>Tools/test.sh</c> in
    /// about a second, with no Editor (architecture.md section 1).
    ///
    /// Shaped after <c>OwnedEquipmentModel</c>: hold ids only, resolve the
    /// authored data through a catalog on demand, and never cache anything
    /// derived (so a balance pass that retunes a price can't leave this stale).
    /// The one structural difference is that ownership here is a GRAPH
    /// problem, not a flat set, which is why it carries a
    /// <see cref="LotRegistry"/>.
    ///
    /// INVARIANT, maintained at every entry point: the owned set is always
    /// contiguous -- every owned lot is either a starting lot or connected
    /// back to one through other owned lots. <see cref="Purchase"/> preserves
    /// it by construction; <see cref="Restore"/> repairs it (see its doc).
    ///
    /// ERROR CONVENTION (mirrors InventoryModel / OwnedEquipmentModel /
    /// EconomyService): programmer errors throw -- a null registry, a null
    /// eligibility argument. Everything a player can actually cause -- buying
    /// a lot twice, clicking on a far unit, an id from an edited save --
    /// returns false / a <see cref="LotEligibility"/> reason.
    /// </summary>
    public sealed class ExpansionModel
    {
        readonly LotRegistry _registry;
        readonly HashSet<LotId> _owned = new HashSet<LotId>();
        readonly List<LotId> _ownedOrder = new List<LotId>();

        public ExpansionModel(LotRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            SeedStartingLots();
        }

        /// <summary>The authored catalog this model grows across.</summary>
        public LotRegistry Registry => _registry;

        /// <summary>
        /// Owned lot ids in acquisition order (starting lots first). Ordered
        /// rather than a bare set so a save file, a UI list, and a
        /// "restaurant history" screen all read chronologically.
        /// </summary>
        public IReadOnlyList<LotId> Owned => _ownedOrder;

        /// <summary>Never zero -- the player begins owning one shop.</summary>
        public int OwnedCount => _ownedOrder.Count;

        public bool IsOwned(LotId id) => !id.IsEmpty && _owned.Contains(id);

        /// <summary>
        /// Non-mutating eligibility check. Deliberately says nothing about
        /// money: <b>affordability is the caller's decision</b>, exactly as
        /// EconomyService's DEBIT-GOING-NEGATIVE note and ProgressionService's
        /// AFFORDABILITY DECISION together establish. The cash gate belongs
        /// one layer up, in <c>ExpansionService.CanPurchase</c>, next to the
        /// EconomyService it will debit -- keeping "is this move legal" (here,
        /// pure and testable with no bank) and "can we pay for it" (there)
        /// as two different questions.
        /// </summary>
        public LotEligibility Evaluate(LotId id)
        {
            if (!_registry.TryGet(id, out var def)) return LotEligibility.UnknownLot;
            if (_owned.Contains(id)) return LotEligibility.AlreadyOwned;

            // A starting lot is always already owned, so reaching here with
            // one means Restore dropped it -- treat as not-adjacent rather
            // than special-casing; SeedStartingLots makes this unreachable.
            for (int i = 0; i < def.Prerequisites.Count; i++)
            {
                if (_owned.Contains(def.Prerequisites[i])) return LotEligibility.Eligible;
            }

            return LotEligibility.NotAdjacent;
        }

        /// <summary>Convenience over <see cref="Evaluate"/>. True only for <see cref="LotEligibility.Eligible"/>.</summary>
        public bool CanPurchase(LotId id) => Evaluate(id) == LotEligibility.Eligible;

        /// <summary>
        /// Adds the lot to the complex. Returns false -- never throws -- for
        /// every ineligible case, because all of them are things a player or
        /// an untrusted network message can legitimately ask for. Payment is
        /// NOT handled here; the caller debits.
        /// </summary>
        public bool Purchase(LotId id)
        {
            if (!CanPurchase(id)) return false;

            _owned.Add(id);
            _ownedOrder.Add(id);
            return true;
        }

        /// <summary>
        /// Every lot that could be bought right now, in catalog order -- the
        /// growth frontier. Drives "which doors are glowing" in the scene and
        /// the expansion UI list without either of them re-implementing the
        /// adjacency walk.
        /// </summary>
        public List<LotId> AvailableLots()
        {
            var result = new List<LotId>();
            var all = _registry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (Evaluate(all[i].Id) == LotEligibility.Eligible) result.Add(all[i].Id);
            }
            return result;
        }

        /// <summary>Back to day one: starting lots only. New game / test reset.</summary>
        public void Reset()
        {
            _owned.Clear();
            _ownedOrder.Clear();
            SeedStartingLots();
        }

        /// <summary>
        /// Replaces ownership from a save file. Untrusted input, so nothing
        /// here throws (architecture.md section 4 decision 2 -- "a missing ID
        /// on load is logged and skipped, never a hard crash"):
        /// <list type="bullet">
        /// <item>Starting lots are re-seeded unconditionally, before anything
        /// else. A save that somehow lacks the shop the player started in
        /// still loads into a playable restaurant.</item>
        /// <item>Unknown and duplicate ids are skipped.</item>
        /// <item>Ids that would violate contiguity are dropped. This is the
        /// interesting case: a save written when <c>lot.unit_3</c> was
        /// adjacent to <c>lot.unit_2</c>, loaded after a content change that
        /// re-parented it, would otherwise install a floating island that
        /// <see cref="Purchase"/> could never have produced and that
        /// <see cref="AvailableLots"/> would then grow outward from. Repairing
        /// on load keeps the invariant true for every consumer instead of
        /// making each one defend against it. The repair runs to a fixed
        /// point, so order in the save file does not matter.</item>
        /// </list>
        /// Returns the number of ids accepted, so the caller can log
        /// "skipped N" -- same contract as <c>OwnedEquipmentModel.Restore</c>.
        /// </summary>
        public int Restore(IEnumerable<LotId> ids)
        {
            Reset();
            if (ids == null) return _ownedOrder.Count;

            // Pass 1: everything that names a real, not-yet-owned lot.
            var pending = new List<LotId>();
            foreach (var id in ids)
            {
                if (id.IsEmpty) continue;
                if (!_registry.Contains(id)) continue;
                if (_owned.Contains(id)) continue;      // starting lots, or a duplicate row
                if (pending.Contains(id)) continue;

                pending.Add(id);
            }

            // Pass 2: absorb whatever is currently adjacent to the owned set,
            // repeatedly, until a full sweep adds nothing. Anything still
            // pending after that is disconnected and is dropped.
            // Swept FORWARD, so among equally-eligible ids the save file's own
            // order wins -- Owned then reads back in the same acquisition
            // order it was captured in, and a save/restore round-trip is
            // exactly identity rather than merely set-equal.
            var absorbed = new bool[pending.Count];
            bool progressed = true;
            while (progressed)
            {
                progressed = false;
                for (int i = 0; i < pending.Count; i++)
                {
                    if (absorbed[i]) continue;
                    if (Evaluate(pending[i]) != LotEligibility.Eligible) continue;

                    _owned.Add(pending[i]);
                    _ownedOrder.Add(pending[i]);
                    absorbed[i] = true;
                    progressed = true;
                }
            }

            return _ownedOrder.Count;
        }

        void SeedStartingLots()
        {
            var starting = _registry.StartingLots;
            for (int i = 0; i < starting.Count; i++)
            {
                if (_owned.Add(starting[i])) _ownedOrder.Add(starting[i]);
            }
        }
    }
}
