// NOTE ON FILE LOCATION vs. NAMESPACE (deliberate, one-line justification
// so a reviewer doesn't read it as an accident): every IGameEvent struct in
// this codebase lives in the `Pho.Domain.Events` namespace
// (EconomyEvents.cs, KitchenEvents.cs, OrderEvents.cs, PlayerEvents.cs,
// RestaurantEvents.cs), so subscribers get all of them from a single
// `using Pho.Domain.Events;`. This file keeps that namespace but sits in
// Domain/Restaurant/ rather than Domain/Events/ because architecture.md
// section 10 rule 4 ("new event structs go in the OWNING agent's events
// file -- never a shared file") outranks folder tidiness: Domain/Events/*
// is owned by other concurrent work, and appending to RestaurantEvents.cs
// would be exactly the shared-file edit that rule forbids.

namespace Pho.Domain.Events
{
    /// <summary>
    /// Past-tense fact (architecture.md section 3's event rule): a specific
    /// table just became dirty. Carries only the table it happened to --
    /// this is the per-table signal a world/VFX layer wants ("show the mess
    /// on THAT table"). The aggregate meter reading is a separate concern;
    /// see <see cref="CleanlinessChanged"/>, which is always published
    /// alongside this one.
    ///
    /// Not published during a save restore -- "a customer just left a mess"
    /// is not a true statement about a loaded game. See
    /// <c>CleanlinessModel.RestoreDirtyTables</c>.
    /// </summary>
    public readonly struct TableDirtied : IGameEvent
    {
        public readonly string TableId;

        public TableDirtied(string tableId)
        {
            TableId = tableId;
        }
    }

    /// <summary>See <see cref="TableDirtied"/> -- the wiped-clean counterpart.</summary>
    public readonly struct TableCleaned : IGameEvent
    {
        public readonly string TableId;

        public TableCleaned(string tableId)
        {
            TableId = tableId;
        }
    }

    /// <summary>
    /// The aggregate restaurant cleanliness reading changed. Carries the
    /// derived value and both numbers it was derived from, following
    /// <see cref="CashChanged"/>'s precedent of shipping the new value on
    /// the event so a HUD never has to re-query a service (or re-derive the
    /// formula) to redraw itself.
    ///
    /// Published whenever the reading actually moves: on a table becoming
    /// dirty/clean, on a table-count change, on <c>Clear</c>, and once
    /// (without any per-table events) on a save restore.
    /// </summary>
    public readonly struct CleanlinessChanged : IGameEvent
    {
        public readonly float Cleanliness01;
        public readonly int DirtyTableCount;
        public readonly int TotalTables;

        public CleanlinessChanged(float cleanliness01, int dirtyTableCount, int totalTables)
        {
            Cleanliness01 = cleanliness01;
            DirtyTableCount = dirtyTableCount;
            TotalTables = totalTables;
        }
    }
}
