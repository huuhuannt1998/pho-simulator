using System;
using System.Collections.Generic;

namespace Pho.Domain.Expansion
{
    /// <summary>
    /// The authored catalog of every lot in the complex plus its adjacency
    /// graph -- static content only, zero runtime state. Think of it as the
    /// map of the block; <see cref="ExpansionModel"/> is the set of pins on
    /// it saying which units are ours.
    ///
    /// The constructor is where the whole "grows contiguously" property is
    /// actually guaranteed. Each <see cref="LotDef"/> can only check itself;
    /// only the registry can see the graph, so these three checks live here:
    /// <list type="bullet">
    /// <item>At least one starting lot -- the player begins with a shop, not
    /// with nothing (an empty-ownership game would have no lot with an owned
    /// neighbour and would therefore be permanently unbuyable).</item>
    /// <item>Every prerequisite resolves to a lot that actually exists. A
    /// typo'd neighbour would otherwise produce a lot that is quietly
    /// unbuyable forever.</item>
    /// <item>Every lot is REACHABLE from a starting lot by walking
    /// prerequisite edges forward. This is the strong form of the adjacency
    /// rule and it subsumes two failure modes at once: a mutually-dependent
    /// cycle (unit_5 needs unit_6 needs unit_5) and a disconnected island
    /// across the street. Both are graphs where a lot is technically
    /// well-formed but can never be bought.</item>
    /// </list>
    /// All three are authored-content errors -- programmer/designer mistakes,
    /// unreachable from player input -- so per this codebase's convention they
    /// throw rather than returning false.
    /// </summary>
    public sealed class LotRegistry
    {
        readonly Dictionary<LotId, LotDef> _byId;
        readonly List<LotDef> _all;
        readonly List<LotId> _startingLots;

        public LotRegistry(IEnumerable<LotDef> lots)
        {
            if (lots == null) throw new ArgumentNullException(nameof(lots));

            _byId = new Dictionary<LotId, LotDef>();
            _all = new List<LotDef>();
            _startingLots = new List<LotId>();

            foreach (var lot in lots)
            {
                if (lot == null)
                    throw new ArgumentException("Lot catalog contains a null entry.", nameof(lots));
                if (_byId.ContainsKey(lot.Id))
                    throw new ArgumentException($"Duplicate lot id '{lot.Id}' in catalog.", nameof(lots));

                _byId.Add(lot.Id, lot);
                _all.Add(lot);
                if (lot.IsStarting) _startingLots.Add(lot.Id);
            }

            if (_all.Count == 0)
                throw new ArgumentException("Lot catalog is empty -- a restaurant needs at least the shop it starts in.", nameof(lots));

            if (_startingLots.Count == 0)
                throw new ArgumentException("Lot catalog declares no starting lot -- the player must begin owning one shop, and nothing would ever become adjacent to an empty complex.", nameof(lots));

            ValidatePrerequisitesResolve(lots);
            ValidateEverythingReachable(lots);
        }

        /// <summary>Every authored lot, in catalog order. Starting lots included.</summary>
        public IReadOnlyList<LotDef> All => _all;

        /// <summary>Lots the player owns from the first frame. Never empty.</summary>
        public IReadOnlyList<LotId> StartingLots => _startingLots;

        public int Count => _all.Count;

        public bool Contains(LotId id) => !id.IsEmpty && _byId.ContainsKey(id);

        /// <summary>
        /// Looks up an authored lot. Returns false for an unknown or empty id
        /// -- a normal runtime outcome, because ids reach this from save files
        /// and from network messages, both of which are untrusted input
        /// (architecture.md section 4 decision 2).
        /// </summary>
        public bool TryGet(LotId id, out LotDef def)
        {
            if (id.IsEmpty)
            {
                def = null;
                return false;
            }
            return _byId.TryGetValue(id, out def);
        }

        void ValidatePrerequisitesResolve(IEnumerable<LotDef> _)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                var lot = _all[i];
                for (int p = 0; p < lot.Prerequisites.Count; p++)
                {
                    if (!_byId.ContainsKey(lot.Prerequisites[p]))
                        throw new ArgumentException($"Lot '{lot.Id}' declares prerequisite '{lot.Prerequisites[p]}', which is not in the catalog.");
                }
            }
        }

        /// <summary>
        /// Forward BFS from the starting lots over "owning X unlocks Y"
        /// edges. Anything not visited can never be purchased no matter what
        /// the player does -- see the class doc's third bullet.
        /// </summary>
        void ValidateEverythingReachable(IEnumerable<LotDef> _)
        {
            // Reverse index: prerequisite -> lots it unlocks.
            var unlockedBy = new Dictionary<LotId, List<LotId>>();
            for (int i = 0; i < _all.Count; i++)
            {
                var lot = _all[i];
                for (int p = 0; p < lot.Prerequisites.Count; p++)
                {
                    var prereq = lot.Prerequisites[p];
                    if (!unlockedBy.TryGetValue(prereq, out var dependents))
                    {
                        dependents = new List<LotId>();
                        unlockedBy.Add(prereq, dependents);
                    }
                    dependents.Add(lot.Id);
                }
            }

            var reached = new HashSet<LotId>();
            var frontier = new Queue<LotId>();
            for (int i = 0; i < _startingLots.Count; i++)
            {
                if (reached.Add(_startingLots[i])) frontier.Enqueue(_startingLots[i]);
            }

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                if (!unlockedBy.TryGetValue(current, out var dependents)) continue;

                for (int i = 0; i < dependents.Count; i++)
                {
                    if (reached.Add(dependents[i])) frontier.Enqueue(dependents[i]);
                }
            }

            if (reached.Count == _all.Count) return;

            var orphans = new List<string>();
            for (int i = 0; i < _all.Count; i++)
            {
                if (!reached.Contains(_all[i].Id)) orphans.Add(_all[i].Id.Value);
            }

            throw new ArgumentException(
                "Lot catalog contains lot(s) unreachable from any starting lot -- a disconnected island or a prerequisite cycle. Unreachable: " + string.Join(", ", orphans));
        }
    }
}
