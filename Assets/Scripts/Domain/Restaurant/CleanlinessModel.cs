using System;
using System.Collections.Generic;
using Pho.Domain.Events;
using Pho.Domain.MathUtil;

namespace Pho.Domain.Restaurant
{
    /// <summary>
    /// The whole of the vertical slice's cleanliness system. architecture.md
    /// section 12 reduces GDD section 15's seven dirt systems to exactly one
    /// line -- "dirty tables -&gt; single Cleanliness01 float -&gt;
    /// satisfaction term" -- and this class is that line: a set of dirty
    /// table IDs, and the float derived from it that
    /// <c>ServiceRecord.Cleanliness01</c> consumes. No dirty floors, dishes,
    /// bins, pests, grease, or health-inspection state; those are all
    /// explicitly cut.
    ///
    /// <b>DERIVATION (documented per the brief's ask):</b>
    /// <code>Cleanliness01 = clamp01(1 - dirtyTables / totalTables)</code>
    /// i.e. a straight linear fraction of the dining room that is currently
    /// messy, 1 = spotless, 0 = every table dirty. Chosen over the
    /// alternatives because:
    /// <list type="bullet">
    /// <item>It is the only derivation that is <i>readable off the screen</i>
    /// -- with 4 tables, one dirty table is visibly "a quarter of the room",
    /// and the meter says 0.75. A player can form an accurate mental model
    /// of the penalty by looking at the room, which a decaying/weighted
    /// formula would destroy.</item>
    /// <item>It is scale-free: the number means the same thing in a 4-table
    /// slice and a 20-table restaurant, so no rebalancing is needed when the
    /// dining room grows.</item>
    /// <item>Every coefficient a designer might want to tune (how much
    /// cleanliness actually costs a customer) already lives on the other
    /// side of the boundary, in <c>IBalanceConfig</c> +
    /// <c>ICustomerArchetype.CleanlinessSensitivity01</c>, which
    /// <c>SatisfactionCalculator</c> applies. Putting a second set of tuning
    /// knobs here would split "how dirty is it" from "how much do we care"
    /// across two files for no gain -- so this model deliberately has NO
    /// balance constants at all.</item>
    /// </list>
    ///
    /// <b>ZERO-TABLE EDGE CASE:</b> with <c>TotalTables &lt;= 0</c> the
    /// fraction is undefined, and this reports <b>1 (spotless)</b> rather
    /// than 0. A dining room with no tables registered is an unconfigured
    /// scene, not a filthy restaurant, and the vertical-slice scene really
    /// does start that way (see <c>TableRegistry</c>, whose seat list is
    /// empty until table prefabs exist and which degrades to "no seats
    /// available" rather than failing). Reporting 0 there would silently tax
    /// every customer's satisfaction for a content gap. The clamp also
    /// covers the inverse: more dirty IDs than tables (stale IDs restored
    /// from a save taken with a bigger dining room) floors at 0 instead of
    /// going negative.
    ///
    /// <b>TABLE IDs ARE PLAIN STRINGS,</b> matching how tables are already
    /// modelled on both sides of the save boundary --
    /// <c>TableRegistry.SeatSlot.tableId</c> ("purely a Unity-scene label --
    /// not a Domain-typed ID") and <c>RestaurantDto.dirtyTableIds</c>. A
    /// typed <c>TableId</c> would be a third, redundant identity for the
    /// same concept. Comparison is <see cref="StringComparer.Ordinal"/>,
    /// matching the typed IDs' own comparer.
    ///
    /// <b>ERROR CONVENTION</b> (verified against <c>InventoryModel</c> and
    /// <c>EconomyService</c>): programmer errors throw, normal runtime
    /// occurrences return false. A malformed table id (null/blank) or a
    /// negative table count is a programmer error and throws. Dirtying an
    /// already-dirty table, or cleaning an already-clean one, is an ordinary
    /// thing that happens in a running restaurant (two customers in a row at
    /// table 3; the player wipes a table someone else already wiped) and
    /// returns false.
    ///
    /// <b>WHY THIS PURE MODEL OWNS AN IEventBus:</b> <c>CustomerBrain</c>
    /// sets the precedent for a Domain model publishing its own events. It
    /// matters more here than usual: only this class knows whether a
    /// <see cref="MarkDirty"/>/<see cref="TryClean"/> call actually changed
    /// anything, so publishing from the Unity-side service would mean either
    /// duplicating the de-duplication logic or spamming no-op events. Taking
    /// the bus here also puts event publication inside the 1-second
    /// <c>dotnet test</c> loop. The bus is optional (null = silent), so the
    /// model is still usable bare in a test or tool.
    /// </summary>
    public sealed class CleanlinessModel
    {
        readonly HashSet<string> _dirty = new HashSet<string>(StringComparer.Ordinal);
        readonly IEventBus _events;

        /// <summary>Silent model -- no events published. Convenience for tests/tools.</summary>
        public CleanlinessModel() : this(null) { }

        /// <param name="events">Optional. Null means this model publishes nothing.</param>
        public CleanlinessModel(IEventBus events)
        {
            _events = events;
        }

        /// <summary>
        /// How many tables the dining room has. Set from the scene via
        /// <see cref="SetTotalTables"/>; 0 until something tells us
        /// otherwise. See the class doc's ZERO-TABLE EDGE CASE note.
        /// </summary>
        public int TotalTables { get; private set; }

        public int DirtyTableCount => _dirty.Count;

        /// <summary>Live view of the dirty set -- iterate it, don't hold it across mutations.</summary>
        public IReadOnlyCollection<string> DirtyTableIds => _dirty;

        /// <summary>The single float the satisfaction term reads. See the class doc's DERIVATION note.</summary>
        public float Cleanliness01 =>
            TotalTables <= 0
                ? 1f
                : MathP.Clamp01(1f - (float)_dirty.Count / TotalTables);

        public bool IsDirty(string tableId) => _dirty.Contains(Validate(tableId, nameof(tableId)));

        /// <summary>
        /// Marks a table dirty. Returns false (publishing nothing) if it was
        /// already dirty -- a normal runtime occurrence, not an error. Throws
        /// only on a null/blank id.
        /// </summary>
        public bool MarkDirty(string tableId)
        {
            var id = Validate(tableId, nameof(tableId));

            if (!_dirty.Add(id)) return false;

            _events?.Publish(new TableDirtied(id));
            PublishCleanlinessChanged();
            return true;
        }

        /// <summary>
        /// Wipes a table. Returns false (publishing nothing) if it wasn't
        /// dirty in the first place -- the player walking up to an
        /// already-clean table is ordinary, so this never throws for it.
        /// Throws only on a null/blank id.
        /// </summary>
        public bool TryClean(string tableId)
        {
            var id = Validate(tableId, nameof(tableId));

            if (!_dirty.Remove(id)) return false;

            _events?.Publish(new TableCleaned(id));
            PublishCleanlinessChanged();
            return true;
        }

        /// <summary>
        /// Tells the model how big the dining room is. Zero is legal (an
        /// unconfigured scene -- see the class doc). A negative count is a
        /// programmer error and throws, mirroring
        /// <c>InventoryModel.Add</c>'s non-positive-amount throw.
        /// Idempotent: setting the same count publishes nothing.
        /// </summary>
        public void SetTotalTables(int totalTables)
        {
            if (totalTables < 0)
                throw new ArgumentOutOfRangeException(nameof(totalTables), "Total tables cannot be negative.");

            if (TotalTables == totalTables) return;

            TotalTables = totalTables;
            PublishCleanlinessChanged();
        }

        /// <summary>
        /// Everything is spotless again (end of a shift, a from-scratch new
        /// game). Clears the dirty set only -- <see cref="TotalTables"/> is a
        /// property of the scene's dining room, not of its mess, and
        /// survives, exactly as <c>InventoryModel.Clear</c> drops lots
        /// without forgetting anything structural. Publishes a single
        /// <see cref="CleanlinessChanged"/> and no per-table events.
        /// </summary>
        public void Clear()
        {
            if (_dirty.Count == 0) return;

            _dirty.Clear();
            PublishCleanlinessChanged();
        }

        /// <summary>
        /// Save/load restore point: replaces the dirty set wholesale.
        /// A null or empty list restores a spotless dining room.
        ///
        /// Publishes ONE <see cref="CleanlinessChanged"/> (so a HUD resyncs)
        /// and deliberately no <see cref="TableDirtied"/> events -- those
        /// mean "a customer just left a mess", which is false during a load.
        /// See <c>CleanlinessEvents.cs</c>. Blank/null entries in the list
        /// are skipped rather than throwing: a save file is untrusted input
        /// (architecture.md section 4 -- "a missing ID on load is logged and
        /// skipped, never a hard crash"), unlike a direct
        /// <see cref="MarkDirty"/> call from code.
        /// </summary>
        public void RestoreDirtyTables(IEnumerable<string> tableIds)
        {
            _dirty.Clear();

            if (tableIds != null)
            {
                foreach (var id in tableIds)
                {
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    _dirty.Add(id);
                }
            }

            PublishCleanlinessChanged();
        }

        void PublishCleanlinessChanged() =>
            _events?.Publish(new CleanlinessChanged(Cleanliness01, _dirty.Count, TotalTables));

        static string Validate(string tableId, string paramName)
        {
            if (string.IsNullOrWhiteSpace(tableId))
                throw new ArgumentException("Table id must be a non-blank string.", paramName);

            return tableId;
        }
    }
}
