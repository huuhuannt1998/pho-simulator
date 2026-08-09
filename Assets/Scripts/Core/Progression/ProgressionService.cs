using System.Collections.Generic;
using Pho.Core.Economy;
using Pho.Domain.Contracts;
using Pho.Domain.Cooking;
using Pho.Domain.Events;
using Pho.Domain.Identity;
using Pho.Domain.Progression;
using Pho.Save.Dto;
using Pho.Save.Participation;
using UnityEngine;

namespace Pho.Core.Progression
{
    /// <summary>
    /// Owns the restaurant's <see cref="OwnedEquipmentModel"/> and the one
    /// operation that mutates it: buying an upgrade. This is the vertical
    /// slice's entire progression proof (GDD §55.14 / architecture.md
    /// section 12: "Keep. Only progression proof in the slice; cheap to
    /// build") -- spend money, own a thing, that thing measurably changes
    /// gameplay.
    ///
    /// Registered into GameContext as a concrete ProgressionService, no
    /// interface -- mirroring EconomyService/InventoryService's judgment
    /// call: nothing frozen calls for one.
    ///
    /// AFFORDABILITY DECISION (deliberately NOT a contradiction of
    /// EconomyService's documented DEBIT-GOING-NEGATIVE decision -- read
    /// that first): <see cref="TryPurchase"/> DOES require sufficient funds
    /// and refuses when the player cannot pay. Those two rules are
    /// consistent because they answer different questions at different
    /// layers:
    /// <list type="bullet">
    /// <item>EconomyService.Debit never refuses because the costs it exists
    /// to post are INVOLUNTARY and already incurred -- rent falls due,
    /// ingredients were already consumed. Refusing them would break the
    /// ledger's arithmetic invariant (sum of all deltas == Cash) by
    /// dropping a transaction that really happened.</item>
    /// <item>An upgrade is the opposite: a VOLUNTARY, optional,
    /// player-initiated transaction that has not happened yet. "Can the
    /// player afford this?" is a call-site question, and EconomyService's
    /// own doc comment says exactly where it belongs -- "a future
    /// bankruptcy check should read IsInsolvent rather than Debit refusing
    /// to go there ... that keeps 'can this transaction happen' and 'is the
    /// restaurant in trouble' as two different questions."</item>
    /// <item>So the gate lives HERE, above Debit. When TryPurchase declines,
    /// Debit is never called at all -- Debit's always-applies contract is
    /// untouched, and the ledger never sees a phantom entry. Letting a
    /// player buy a $450 burner at $12 cash would also make the upgrade a
    /// free lunch and delete the "earn it" half of the progression loop,
    /// which is the only reason this milestone exists.</item>
    /// </list>
    /// </summary>
    [AutoInstall]
    public sealed class ProgressionService : IInstaller, ISaveParticipant
    {
        /// <summary>
        /// Ordering constant kept LOCAL rather than appended to the shared,
        /// append-only <c>Core/InstallOrder.cs</c> -- this pass's authorized
        /// write set excludes that file, and OrderServiceInstaller already
        /// set the precedent for exactly this situation (see its own doc
        /// comment). Slot 350 sits between <c>InstallOrder.Economy</c> (300)
        /// and <c>InstallOrder.Inventory</c> (400), which is the correct
        /// place on the merits, not just a free gap:
        /// <list type="bullet">
        /// <item>AFTER Save (100), so SaveParticipantRegistry already exists
        /// to self-register into.</item>
        /// <item>AFTER Data (200), so IGameDatabase is resolvable for
        /// equipment-def lookups.</item>
        /// <item>AFTER Economy (300), so EconomyService can be resolved and
        /// cached at Install time instead of lazily -- purchases debit it
        /// directly.</item>
        /// <item>BEFORE Kitchen (500), so any kitchen adapter that reads
        /// CurrentModifiers while binding finds a fully-built service.</item>
        /// </list>
        /// A later integration pass may fold this into InstallOrder.cs if it
        /// wants one source of truth for ordering; the value would not change.
        /// </summary>
        public const int InstallSlot = 350;

        readonly OwnedEquipmentModel _owned = new OwnedEquipmentModel();

        IGameDatabase _database;
        EconomyService _economy;
        IEventBus _events;

        public int Order => InstallSlot;

        /// <summary>ISaveParticipant. Reuses the install slot rather than inventing a second, parallel ordering policy -- same reasoning EconomyService documents.</summary>
        public int RestoreOrder => InstallSlot;

        /// <summary>Read-only view of what the restaurant owns.</summary>
        public OwnedEquipmentModel Owned => _owned;

        /// <summary>
        /// THE SEAM. The modifiers every pot in the restaurant should be
        /// ticking with right now, derived live from what is owned -- so it
        /// is automatically correct after a purchase and after a save
        /// restore, with no cached copy to invalidate. Returns
        /// <see cref="EquipmentModifiers.Default"/> (1, 1) for an
        /// unupgraded restaurant.
        /// </summary>
        public EquipmentModifiers CurrentModifiers => _owned.ComputeModifiers(_database);

        public bool IsOwned(EquipmentId id) => _owned.Contains(id);

        public void Install(GameContext ctx)
        {
            ctx.TryGet<IGameDatabase>(out var database);
            ctx.TryGet<EconomyService>(out var economy);

            Bind(database, economy, ctx.Events);
            ctx.Register<ProgressionService>(this);

            if (ctx.TryGet<SaveParticipantRegistry>(out var saveRegistry))
            {
                saveRegistry.Register(this);
            }
        }

        /// <summary>
        /// Injection seam separated from Install so this can be constructed
        /// and bound without a full GameContext -- mirrors
        /// EconomyService.Bind and the Kitchen stations' Bind(...) pattern.
        /// Every dependency is optional: an unbound service is inert
        /// (purchases decline, CurrentModifiers reports Default) but never
        /// throws.
        /// </summary>
        public void Bind(IGameDatabase database, EconomyService economy, IEventBus events)
        {
            _database = database;
            _economy = economy;
            _events = events;
        }

        /// <summary>
        /// Static convenience for MonoBehaviour consumers that have no
        /// Bind(...) seam of their own, resolving through the one deliberate
        /// GameBootstrap.Current singleton exception (see GameBootstrap's
        /// class doc comment; RestaurantSign uses the same convention).
        /// Returns <see cref="EquipmentModifiers.Default"/> whenever the
        /// context or the service is unavailable, so a caller in a scene
        /// that never booted behaves exactly like an unupgraded restaurant
        /// instead of throwing.
        ///
        /// Prefer <see cref="CurrentModifiers"/> on an injected instance
        /// wherever a Bind(...) already exists -- this exists so a component
        /// like BrothPot, which is created by a scene builder long before
        /// GameBootstrap.Awake() runs, can pick up owned equipment in one
        /// line and with zero wiring.
        /// </summary>
        public static EquipmentModifiers CurrentModifiersOrDefault()
        {
            var ctx = GameBootstrap.Current;
            if (ctx == null) return EquipmentModifiers.Default;

            return ctx.TryGet<ProgressionService>(out var service)
                ? service.CurrentModifiers
                : EquipmentModifiers.Default;
        }

        /// <summary>
        /// Price of a piece of equipment, from its authored def. False if
        /// unbound or the id is unknown.
        /// </summary>
        public bool TryGetCost(EquipmentId id, out decimal cost)
        {
            cost = 0m;
            if (_database == null || id.IsEmpty) return false;
            if (!_database.TryGetEquipment(id, out var def) || def == null) return false;

            cost = (decimal)def.PurchaseCost;
            return true;
        }

        /// <summary>
        /// Non-mutating preflight -- same rules <see cref="TryPurchase"/>
        /// applies, so UI/prompt text and the purchase itself can never
        /// disagree. All four decline paths are normal runtime occurrences
        /// (nothing here throws).
        /// </summary>
        public bool CanPurchase(EquipmentId id)
        {
            if (_economy == null) return false;          // unbound / no economy in this scene
            if (_owned.Contains(id)) return false;        // no double-buy (M13 acceptance criterion)
            if (!TryGetCost(id, out var cost)) return false; // unknown equipment
            return _economy.Cash >= cost;                 // see the AFFORDABILITY DECISION above
        }

        /// <summary>
        /// Debits the cost, grants ownership, and publishes
        /// <see cref="EquipmentPurchased"/>. Returns false without touching
        /// the ledger for any of the decline paths in
        /// <see cref="CanPurchase"/> -- unbound, already owned, unknown id,
        /// or not enough cash.
        /// </summary>
        public bool TryPurchase(EquipmentId id)
        {
            if (!CanPurchase(id)) return false;
            if (!TryGetCost(id, out var cost)) return false;

            // Ownership is granted BEFORE the event is published so that
            // CurrentModifiers (carried on the event) already reflects the
            // purchase for every subscriber.
            if (!_owned.TryAdd(id)) return false;

            // Only debit once the grant has actually landed -- ordering it
            // this way means there is no window where money is gone but the
            // equipment is not owned.
            if (cost > 0m)
            {
                _economy.Debit(cost, LedgerCategory.Equipment);
            }

            _events?.Publish(new EquipmentPurchased(id, cost, CurrentModifiers));
            return true;
        }

        // ------------------------------------------------------------------
        // ISaveParticipant
        // ------------------------------------------------------------------

        /// <summary>
        /// Writes into <c>save.progression.ownedEquipmentIds</c> -- the field
        /// already declared in SaveFile.cs and unused until now. Assumes a
        /// non-null progression DTO, exactly like
        /// InventoryModelSaveParticipant.Capture assumes save.inventory
        /// (SaveCoordinator.NewEmptySaveFile always populates it).
        /// </summary>
        public void Capture(SaveFile save)
        {
            var ids = new List<string>(_owned.Count);
            for (int i = 0; i < _owned.Owned.Count; i++)
            {
                ids.Add(_owned.Owned[i].Value);
            }

            save.progression.ownedEquipmentIds = ids;
        }

        /// <summary>
        /// Null-tolerant on the way in (a save written before this field was
        /// used, or a corrupt one, must not crash the restore path -- doc
        /// section 4 decision 2). Also adopts the database handed to Restore
        /// if this service was bound without one, so a restore in a
        /// content-less boot still resolves modifiers correctly afterwards.
        /// </summary>
        public void Restore(SaveFile save, IGameDatabase db)
        {
            if (_database == null && db != null) _database = db;

            var raw = save?.progression?.ownedEquipmentIds;
            if (raw == null)
            {
                _owned.Clear();
                return;
            }

            var ids = new List<EquipmentId>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                ids.Add(new EquipmentId(raw[i]));
            }

            var accepted = _owned.Restore(ids);
            if (accepted != raw.Count)
            {
                Debug.LogWarning($"[ProgressionService] Restored {accepted} of {raw.Count} owned equipment id(s) -- the rest were empty or duplicated in the save file.");
            }
        }
    }
}
