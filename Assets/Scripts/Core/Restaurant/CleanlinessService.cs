using System;
using System.Collections.Generic;
using Pho.Core;
using Pho.Domain.Contracts;
using Pho.Domain.Events;
using Pho.Domain.Restaurant;
using Pho.Save.Dto;
using Pho.Save.Participation;

namespace Pho.Core.Restaurant
{
    /// <summary>
    /// Unity-side owner of the one shared <see cref="CleanlinessModel"/> --
    /// the whole of architecture.md section 12's reduced cleanliness system
    /// ("dirty tables -&gt; single Cleanliness01 float -&gt; satisfaction
    /// term"). Follows <c>EconomyService</c>'s shape exactly: a plain-C#
    /// <c>[AutoInstall] IInstaller</c> that is ALSO the service and ALSO the
    /// <see cref="ISaveParticipant"/>, registering itself into both
    /// GameContext and SaveParticipantRegistry from its own
    /// <see cref="Install"/>. No MonoBehaviour split is needed here (unlike
    /// <c>RestaurantStateService</c>) because nothing about cleanliness
    /// needs a Unity lifecycle -- there is no per-frame tick.
    ///
    /// <b>SCENE TABLE COUNT:</b> the model's denominator comes from the
    /// scene, and this service is where that translation lives -- the Domain
    /// model deliberately knows only an <c>int</c>. Each
    /// <see cref="DirtyTable"/> in the scene calls
    /// <see cref="RegisterTable"/>/<see cref="UnregisterTable"/> as it
    /// enables/disables, and this class keeps the id set and pushes the
    /// resulting count down via <c>CleanlinessModel.SetTotalTables</c>. That
    /// keeps the count decentralised (no shared "list of tables" file for
    /// concurrent agents to fight over, and no edit to the sibling-owned
    /// <c>TableRegistry</c>), and it means an empty dining room reports
    /// 0 tables -- which the model treats as spotless, not filthy.
    /// </summary>
    [AutoInstall]
    public sealed class CleanlinessService : IInstaller, ISaveParticipant
    {
        /// <summary>
        /// Local ordering constant instead of an entry in the shared,
        /// append-only <c>Core/InstallOrder.cs</c> -- following the precedent
        /// <c>OrderServiceInstaller</c> set for exactly this reason: this
        /// pass's authorized write set is scoped to
        /// <c>Assets/Scripts/Core/Restaurant/**</c>, so InstallOrder.cs is
        /// intentionally left untouched (other agents are editing
        /// concurrently and that file is the documented conflict hotspot).
        ///
        /// <b>Slot choice (460)</b>, between <c>InstallOrder.DayCycle</c>
        /// (450) and <c>InstallOrder.Kitchen</c> (500):
        /// <list type="bullet">
        /// <item>It must run after <c>InstallOrder.Save</c> (100), or
        /// SaveParticipantRegistry won't exist to register into. Every slot
        /// here satisfies that.</item>
        /// <item>It must run before <c>InstallOrder.Customers</c> (600):
        /// cleanliness is a customer-satisfaction input
        /// (<c>ICustomerWorld.Cleanliness01</c> /
        /// <c>ServiceRecord.Cleanliness01</c>), so a customer adapter
        /// resolving it at install time must find it already
        /// registered.</item>
        /// <item>460 specifically, rather than a free +100 slot, because
        /// cleanliness is dining-room state in the same family as restaurant
        /// phase -- it belongs immediately beside DayCycle (450), and ahead
        /// of Kitchen (500) / OrderService (550) / Customers (600), all of
        /// which are plausible future readers. It has no upstream service
        /// dependency of its own, so this is about being available early,
        /// not about needing anything first.</item>
        /// </list>
        /// A later integration pass may fold this into InstallOrder.cs if a
        /// single source of truth for ordering is wanted; functionally
        /// nothing changes.
        /// </summary>
        public const int CleanlinessInstallOrder = 460;

        readonly HashSet<string> _knownTableIds = new HashSet<string>(StringComparer.Ordinal);

        CleanlinessModel _model;

        /// <summary>
        /// The pure model. Non-null from construction so this service is
        /// inert-but-usable before <see cref="Install"/> runs (it just won't
        /// publish events until it has a bus).
        /// </summary>
        public CleanlinessModel Model => _model ?? (_model = new CleanlinessModel());

        /// <summary>Convenience passthrough -- the single float the satisfaction term reads.</summary>
        public float Cleanliness01 => Model.Cleanliness01;

        public int Order => CleanlinessInstallOrder;

        public void Install(GameContext ctx)
        {
            Bind(ctx.Events);

            ctx.Register<CleanlinessService>(this);

            // Also registered under the pure Domain type so a consumer that
            // only needs the reading (a future CustomerAgent implementing
            // ICustomerWorld.Cleanliness01) can depend on Pho.Domain rather
            // than on this Pho.Core service -- the lower-coupling direction.
            // Two keys, one instance; no duplicate state.
            ctx.Register<CleanlinessModel>(Model);

            if (ctx.TryGet<SaveParticipantRegistry>(out var saveRegistry))
            {
                saveRegistry.Register(this);
            }
        }

        /// <summary>
        /// Injection seam separated from <see cref="Install"/> so this can be
        /// constructed and bound without a full GameContext, mirroring
        /// <c>EconomyService.Bind</c>. Re-binding carries the existing dirty
        /// set and table count over to the new (event-publishing) model, so
        /// anything that touched the service before Install isn't lost.
        /// </summary>
        public void Bind(IEventBus events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));

            var previous = _model;
            _model = new CleanlinessModel(events);

            if (previous != null)
            {
                _model.SetTotalTables(previous.TotalTables);
                _model.RestoreDirtyTables(previous.DirtyTableIds);
            }
            else
            {
                _model.SetTotalTables(_knownTableIds.Count);
            }
        }

        // -----------------------------------------------------------------
        // Scene table registration -- see the class doc's SCENE TABLE COUNT.
        // -----------------------------------------------------------------

        /// <summary>
        /// A table exists in the dining room. Returns false (a normal
        /// runtime occurrence, never a throw) for a blank id or a table
        /// already registered -- <see cref="DirtyTable"/> retries
        /// registration lazily, so a duplicate call is expected, not a bug.
        /// </summary>
        public bool RegisterTable(string tableId)
        {
            if (string.IsNullOrWhiteSpace(tableId)) return false;
            if (!_knownTableIds.Add(tableId)) return false;

            Model.SetTotalTables(_knownTableIds.Count);
            return true;
        }

        /// <summary>
        /// A table left the dining room (destroyed/disabled). Also wipes it
        /// clean: leaving a departed table's id in the dirty set would
        /// permanently depress cleanliness against a table the player can no
        /// longer walk up to and clean. Returns false if it wasn't
        /// registered.
        /// </summary>
        public bool UnregisterTable(string tableId)
        {
            if (string.IsNullOrWhiteSpace(tableId)) return false;
            if (!_knownTableIds.Remove(tableId)) return false;

            Model.TryClean(tableId);
            Model.SetTotalTables(_knownTableIds.Count);
            return true;
        }

        public bool IsKnownTable(string tableId) =>
            !string.IsNullOrWhiteSpace(tableId) && _knownTableIds.Contains(tableId);

        // -----------------------------------------------------------------
        // Gameplay passthroughs (the model owns the actual rules).
        // -----------------------------------------------------------------

        public bool MarkDirty(string tableId) => Model.MarkDirty(tableId);

        public bool TryClean(string tableId) => Model.TryClean(tableId);

        public bool IsDirty(string tableId) => Model.IsDirty(tableId);

        /// <summary>Everything spotless again (end of shift / new game).</summary>
        public void ClearAll() => Model.Clear();

        // -----------------------------------------------------------------
        // ISaveParticipant
        // -----------------------------------------------------------------

        /// <summary>Mirrors <see cref="Order"/> -- one ordering policy, not two (same reasoning as EconomyService.RestoreOrder).</summary>
        public int RestoreOrder => CleanlinessInstallOrder;

        /// <summary>
        /// Writes both DTO fields. <c>dirtyTableIds</c> is the authoritative
        /// one; <c>cleanliness01</c> is a DERIVED mirror, written because the
        /// frozen <c>RestaurantDto</c> has the field and a human reading the
        /// JSON should see the meter -- but it is deliberately NOT read back
        /// on <see cref="Restore"/>. Recomputing it from the restored ids and
        /// the CURRENT scene's table count is the only way the two can't
        /// disagree (a save taken in a 4-table room loaded into a 6-table one
        /// would otherwise restore a stale float).
        /// </summary>
        public void Capture(SaveFile save)
        {
            if (save?.restaurant == null) return;

            save.restaurant.cleanliness01 = Model.Cleanliness01;
            save.restaurant.dirtyTableIds = new List<string>(Model.DirtyTableIds);
        }

        /// <summary>
        /// Restores the dirty set; the float re-derives itself. A null
        /// <c>restaurant</c> block or id list restores a spotless dining
        /// room rather than throwing -- a save file is untrusted input
        /// (architecture.md section 4).
        /// </summary>
        public void Restore(SaveFile save, IGameDatabase db)
        {
            Model.RestoreDirtyTables(save?.restaurant?.dirtyTableIds);
        }
    }
}
