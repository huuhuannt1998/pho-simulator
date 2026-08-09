using System;
using System.Collections.Generic;
using Pho.Core.Economy;
using Pho.Domain.Contracts;
using Pho.Domain.Events;
using Pho.Domain.Expansion;
using Pho.Save.Dto;
using Pho.Save.Participation;
using UnityEngine;

namespace Pho.Core.Expansion
{
    /// <summary>
    /// One request to buy a unit of the complex. A struct rather than loose
    /// arguments so the co-op path has a single serialisable payload: a
    /// client sends this, the host validates and executes exactly it, and
    /// the resulting <see cref="LotPurchased"/> carries the same
    /// <see cref="RequestedBy"/> back to everyone.
    /// </summary>
    public readonly struct LotPurchaseRequest
    {
        public readonly LotId Lot;

        /// <summary>Who asked. Null/blank in single player. See ExpansionService's SHARED-BANK DECISION.</summary>
        public readonly string RequestedBy;

        public LotPurchaseRequest(LotId lot, string requestedBy = null)
        {
            Lot = lot;
            RequestedBy = requestedBy;
        }
    }

    /// <summary>
    /// Owns the restaurant's <see cref="ExpansionModel"/> and the one
    /// operation that mutates it: buying the adjacent unit. This is the
    /// "pool money and grow from one shop into a multi-unit complex"
    /// progression -- the same spend-money-own-a-thing shape as
    /// <c>ProgressionService</c> (which this file mirrors closely), scaled up
    /// from one burner to one building.
    ///
    /// Registered into GameContext as a concrete ExpansionService, no
    /// interface -- following EconomyService/ProgressionService's judgment
    /// call: nothing frozen calls for one.
    ///
    /// <b>AFFORDABILITY DECISION</b> -- identical to ProgressionService's, and
    /// for identical reasons (read EconomyService's DEBIT-GOING-NEGATIVE note
    /// first): <see cref="TryPurchase"/> DOES require sufficient funds and
    /// refuses when the bank cannot cover the price, even though
    /// <c>EconomyService.Debit</c> itself never refuses. Those two rules are
    /// consistent because they answer different questions at different
    /// layers. Debit never refuses because rent and consumed ingredients are
    /// INVOLUNTARY costs that already happened, and dropping one would break
    /// the ledger invariant (sum of deltas == Cash). A building is the
    /// opposite: voluntary, optional, and not yet real. So the gate lives
    /// HERE, above Debit -- when this declines, Debit is never called at all,
    /// its always-applies contract is untouched, and the ledger never sees a
    /// phantom entry. It matters more here than for a burner: letting a party
    /// buy a $6,000 unit on $12 of cash would put the shared bank thousands
    /// of dollars underwater on a single irreversible click and delete the
    /// "pool money for a while" half of the mechanic, which is the entire
    /// point of it.
    ///
    /// <b>SHARED-BANK DECISION (co-op, 4 players, ONE balance)</b> -- the
    /// question is who may commit an irreversible five-figure spend from
    /// money everyone earned. Three layers, decided separately:
    /// <list type="bullet">
    /// <item><b>Authority: host-only execution.</b>
    /// <see cref="HasPurchaseAuthority"/> defaults to true (single player is
    /// its own host) and the networking agent calls
    /// <see cref="SetPurchaseAuthority"/>(false) on every client. A
    /// non-authority peer's TryPurchase refuses with
    /// <see cref="LotRefusalReason.NotAuthorised"/> and never touches the
    /// model or the ledger. This is not a design opinion, it is the only
    /// correct answer for shared mutable state: four clients each debiting
    /// their local EconomyService would diverge instantly, and the model here
    /// is deterministic, so replicating the *decision* rather than the
    /// *outcome* is enough. Clients call <see cref="RequestPurchase"/>, which
    /// is a pure preflight -- it tells them what the answer will be so the
    /// prompt reads correctly, and never mutates.</item>
    /// <item><b>Policy: any player may request, no vote required.</b> The
    /// shared till is already unilaterally spendable -- anyone can order
    /// ingredients -- so gating only buildings behind a vote would be an
    /// inconsistent rule the party has to learn, and a 4-player confirmation
    /// modal is a whole feature (timeout, disconnect, absent player) that
    /// buys little in a co-writing-a-restaurant fantasy. Supermarket
    /// Together, the reference for this mechanic, is unilateral for the same
    /// reason. What replaces the vote is ACCOUNTABILITY:
    /// <see cref="LotPurchased.RequestedBy"/> is mandatory on the wire, so
    /// the toast names the buyer. Social pressure, not a modal.</item>
    /// <item><b>Escape hatch: <see cref="PurchaseApprover"/>.</b> Because
    /// "no vote" is a balance call and not an architectural one, the veto
    /// point is a hook rather than a hard-coded <c>true</c>. A later
    /// confirmation prompt, a party vote, or a host-only lobby setting
    /// installs one delegate and changes nothing else -- the model, the
    /// ledger path, and the save format are all untouched. Default approves
    /// everything, so single player never sees it.</item>
    /// </list>
    /// Networking itself is another agent's; nothing here references it. The
    /// API is merely shaped so a host-authoritative caller can drive it --
    /// every mutating entry point takes an explicit requester instead of
    /// assuming "the local player decided".
    ///
    /// <b>LEDGER CATEGORY:</b> purchases post as
    /// <see cref="LedgerCategory.Equipment"/>. That enum is frozen M1
    /// contract surface and is not this pass's to extend, so a dedicated
    /// <c>Property</c>/<c>Expansion</c> category is flagged in the report
    /// rather than added. Equipment is the least-wrong existing member --
    /// both are voluntary capital purchases -- but the daily report will
    /// bucket a $6,000 building alongside a $450 burner. Worth one enum
    /// member at the next integration pass.
    /// </summary>
    [AutoInstall]
    public sealed class ExpansionService : IInstaller, ISaveParticipant
    {
        /// <summary>
        /// Ordering constant kept LOCAL rather than appended to the shared,
        /// append-only <c>Core/InstallOrder.cs</c> -- this pass's authorized
        /// write set excludes that file (three agents are editing
        /// concurrently and architecture.md section 10 names it a conflict
        /// hotspot). <c>OrderServiceInstaller</c> set the precedent, and
        /// <c>ProgressionService</c> (350) and <c>CleanlinessService</c>
        /// (460) both followed it.
        ///
        /// <b>Slot 360</b>, between <c>ProgressionService</c> (350) and
        /// <c>InstallOrder.Inventory</c> (400):
        /// <list type="bullet">
        /// <item>AFTER Save (100) -- SaveParticipantRegistry must already
        /// exist to self-register into.</item>
        /// <item>AFTER Economy (300) -- EconomyService is resolved and cached
        /// at Install time rather than lazily, because purchases debit it
        /// directly. This is the only hard upstream dependency.</item>
        /// <item>Immediately after ProgressionService (350) because the two
        /// are the same family of thing (voluntary capital purchase, debits
        /// Economy, persists into ProgressionDto) and a reader scanning the
        /// install order should see them adjacent. Nothing requires this
        /// ordering between the two -- they never touch -- but 360 costs
        /// nothing and reads correctly.</item>
        /// <item>BEFORE Kitchen (500), DayCycle (450), Customers (600) and UI
        /// (700) -- every one of those is a plausible reader of which lots
        /// exist. A dining-room adapter in the upstairs unit that queries
        /// <see cref="IsOwned"/> while binding must find a fully-built
        /// service, not a half-installed one.</item>
        /// </list>
        /// A later integration pass may fold this into InstallOrder.cs for a
        /// single source of truth; the value would not change.
        /// </summary>
        public const int InstallSlot = 360;

        /// <summary>
        /// Prefix under which owned lot ids are written into the shared
        /// <c>ProgressionDto.flags</c> dictionary -- see <see cref="Capture"/>.
        /// Equal to <see cref="LotId.Prefix"/>; named separately here so the
        /// persistence contract is greppable from the save side.
        /// </summary>
        public const string SaveFlagPrefix = LotId.Prefix;

        ExpansionModel _model;
        EconomyService _economy;
        IEventBus _events;

        public int Order => InstallSlot;

        /// <summary>ISaveParticipant. Reuses the install slot rather than inventing a second, parallel ordering policy -- same reasoning EconomyService and ProgressionService document.</summary>
        public int RestoreOrder => InstallSlot;

        /// <summary>
        /// The pure model. Non-null from first access so this service is
        /// inert-but-usable before <see cref="Install"/> runs (it just cannot
        /// spend money or publish events yet), mirroring
        /// <c>CleanlinessService.Model</c>.
        /// </summary>
        public ExpansionModel Model => _model ?? (_model = new ExpansionModel(DefaultLotCatalog.BuildRegistry()));

        public LotRegistry Registry => Model.Registry;

        /// <summary>
        /// Whether THIS peer may actually commit a purchase. True in single
        /// player and on the host; set false on clients by the networking
        /// layer. See the class doc's SHARED-BANK DECISION.
        /// </summary>
        public bool HasPurchaseAuthority { get; private set; } = true;

        /// <summary>
        /// Optional veto hook, evaluated after every other rule passes and
        /// before any money moves. Null means approve. See the class doc's
        /// SHARED-BANK DECISION, third bullet.
        /// </summary>
        public Func<LotPurchaseRequest, bool> PurchaseApprover { get; set; }

        public bool IsOwned(LotId id) => Model.IsOwned(id);

        public IReadOnlyList<LotId> OwnedLots => Model.Owned;

        /// <summary>The growth frontier -- lots adjacent to the complex, ignoring price. Drives which doors glow.</summary>
        public List<LotId> AvailableLots() => Model.AvailableLots();

        public void Install(GameContext ctx)
        {
            ctx.TryGet<EconomyService>(out var economy);

            Bind(economy, ctx.Events, null);
            ctx.Register<ExpansionService>(this);

            // Also registered under the pure Domain type, so a consumer that
            // only needs to ask "do we own this unit" can depend on
            // Pho.Domain instead of on this Pho.Core service -- the
            // lower-coupling direction, same as CleanlinessService registering
            // its CleanlinessModel. Two keys, one instance, no duplicate state.
            ctx.Register<ExpansionModel>(Model);

            if (ctx.TryGet<SaveParticipantRegistry>(out var saveRegistry))
            {
                saveRegistry.Register(this);
            }
        }

        /// <summary>
        /// Injection seam separated from <see cref="Install"/> so this can be
        /// constructed and bound without a full GameContext -- mirrors
        /// <c>EconomyService.Bind</c> / <c>ProgressionService.Bind</c>. Every
        /// dependency is optional: an unbound service is inert (purchases
        /// decline with <see cref="LotRefusalReason.ServiceUnavailable"/>,
        /// ownership still reports the starting shop) but never throws.
        ///
        /// <paramref name="catalog"/> null means the built-in
        /// <see cref="DefaultLotCatalog"/>. It exists so a future Pho.Data
        /// <c>LotData</c> ScriptableObject set, or a test, can supply its own
        /// block layout without this file changing. Passing a catalog
        /// REPLACES the model, discarding ownership -- callers rebind before
        /// restore, never mid-game.
        /// </summary>
        public void Bind(EconomyService economy, IEventBus events, IEnumerable<LotDef> catalog = null)
        {
            _economy = economy;
            _events = events;

            if (catalog != null)
            {
                _model = new ExpansionModel(new LotRegistry(catalog));
            }
        }

        /// <summary>
        /// Sets whether this peer may commit purchases. Called by the
        /// networking layer at session start (host: true, clients: false).
        /// Separate from <see cref="Bind"/> because authority can change
        /// mid-session on host migration, while the bindings do not.
        /// </summary>
        public void SetPurchaseAuthority(bool hasAuthority) => HasPurchaseAuthority = hasAuthority;

        /// <summary>Price of a lot, from its authored def. False for an unknown id.</summary>
        public bool TryGetPrice(LotId id, out decimal price)
        {
            price = 0m;
            if (!Registry.TryGet(id, out var def) || def == null) return false;

            price = def.Price;
            return true;
        }

        /// <summary>
        /// Non-mutating preflight -- the exact rules <see cref="TryPurchase"/>
        /// applies, in the same order, so the interaction prompt and the
        /// purchase itself can never disagree. This is also what a co-op
        /// CLIENT calls: it is safe on a non-authority peer because it only
        /// reads, and it deliberately does NOT report
        /// <see cref="LotRefusalReason.NotAuthorised"/>, so a client's prompt
        /// says "Buy Unit 2 -- $2,500" rather than the meaningless "you are
        /// not the host". The authority check belongs on the commit path
        /// only. All decline paths are normal runtime occurrences; nothing
        /// here throws.
        /// </summary>
        public bool RequestPurchase(in LotPurchaseRequest request, out LotRefusalReason reason)
        {
            reason = LotRefusalReason.UnknownLot;

            if (_economy == null)
            {
                reason = LotRefusalReason.ServiceUnavailable;
                return false;
            }

            switch (Model.Evaluate(request.Lot))
            {
                case LotEligibility.UnknownLot:
                    reason = LotRefusalReason.UnknownLot;
                    return false;
                case LotEligibility.AlreadyOwned:
                    reason = LotRefusalReason.AlreadyOwned;
                    return false;
                case LotEligibility.NotAdjacent:
                    reason = LotRefusalReason.NotAdjacent;
                    return false;
            }

            if (!TryGetPrice(request.Lot, out var price))
            {
                reason = LotRefusalReason.UnknownLot;
                return false;
            }

            // See the AFFORDABILITY DECISION in the class doc.
            if (_economy.Cash < price)
            {
                reason = LotRefusalReason.InsufficientFunds;
                return false;
            }

            var approver = PurchaseApprover;
            if (approver != null && !approver(request))
            {
                reason = LotRefusalReason.Vetoed;
                return false;
            }

            return true;
        }

        /// <summary>Convenience overload for UI that only needs yes/no.</summary>
        public bool CanPurchase(LotId id) => RequestPurchase(new LotPurchaseRequest(id), out _);

        /// <summary>
        /// COMMIT. Grants the lot, debits the shared bank, publishes
        /// <see cref="LotPurchased"/>. Returns false without touching the
        /// ledger for every decline path, publishing
        /// <see cref="LotPurchaseRefused"/> so the requesting player -- who in
        /// co-op is not necessarily on this machine -- finds out why.
        ///
        /// Only the authority may reach the mutating part; see the class
        /// doc's SHARED-BANK DECISION. A host drives this on behalf of
        /// whoever asked, which is why the requester is a parameter and not
        /// something this class looks up.
        /// </summary>
        public bool TryPurchase(in LotPurchaseRequest request)
        {
            if (!HasPurchaseAuthority)
            {
                Refuse(request, LotRefusalReason.NotAuthorised);
                return false;
            }

            if (!RequestPurchase(request, out var reason))
            {
                Refuse(request, reason);
                return false;
            }

            if (!TryGetPrice(request.Lot, out var price))
            {
                Refuse(request, LotRefusalReason.UnknownLot);
                return false;
            }

            // Ownership is granted BEFORE the debit and before the event, so
            // every subscriber (and OwnedLotCount on the event) already sees
            // the purchase, and there is no window where the money is gone
            // but the unit is not owned. Same ordering ProgressionService uses.
            if (!Model.Purchase(request.Lot))
            {
                Refuse(request, LotRefusalReason.NotAdjacent);
                return false;
            }

            if (price > 0m)
            {
                _economy.Debit(price, LedgerCategory.Equipment);
            }

            _events?.Publish(new LotPurchased(request.Lot, price, request.RequestedBy, Model.OwnedCount));
            return true;
        }

        /// <summary>Convenience overload for single-player / editor callers with no requester identity.</summary>
        public bool TryPurchase(LotId id) => TryPurchase(new LotPurchaseRequest(id));

        void Refuse(in LotPurchaseRequest request, LotRefusalReason reason)
        {
            _events?.Publish(new LotPurchaseRefused(request.Lot, reason, request.RequestedBy));
        }

        // ------------------------------------------------------------------
        // ISaveParticipant
        // ------------------------------------------------------------------

        /// <summary>
        /// Writes owned lot ids into <c>save.progression.flags</c> as
        /// <c>{"lot.unit_2": true}</c>.
        ///
        /// WHY flags AND NOT A NEW DTO FIELD: <c>Save/Dto/SaveFile.cs</c> is
        /// outside this pass's authorized write set, and
        /// <c>ProgressionDto.flags</c> is a
        /// <c>Dictionary&lt;string,bool&gt;</c> that already exists for
        /// exactly this shape of data ("tutorial/progression flags") and is
        /// currently unused by any service. Owned-lot ids are already
        /// namespaced by the mandatory <c>lot.</c> prefix, so they cannot
        /// collide with another agent's flags -- and this method deliberately
        /// REWRITES only <c>lot.</c>-prefixed keys, preserving everything
        /// else in the dictionary, so a concurrent participant writing
        /// <c>tutorial.completed</c> is never clobbered regardless of
        /// participant order. <c>ownedEquipmentIds</c> is already taken by
        /// ProgressionService and must not be shared. A later integration
        /// pass that adds a dedicated <c>ExpansionDto</c> can migrate these
        /// keys in a single <c>ISaveMigration</c>.
        ///
        /// Null-tolerant on both the progression block and the dictionary --
        /// a save file is untrusted input on the way out as well as in
        /// (SaveCoordinator.NewEmptySaveFile populates both, but a test or a
        /// future partial-save path may not).
        /// </summary>
        public void Capture(SaveFile save)
        {
            var progression = save?.progression;
            if (progression == null) return;

            if (progression.flags == null)
            {
                progression.flags = new Dictionary<string, bool>(StringComparer.Ordinal);
            }
            else
            {
                var stale = new List<string>();
                foreach (var key in progression.flags.Keys)
                {
                    if (key != null && key.StartsWith(SaveFlagPrefix, StringComparison.Ordinal)) stale.Add(key);
                }
                for (int i = 0; i < stale.Count; i++) progression.flags.Remove(stale[i]);
            }

            var owned = Model.Owned;
            for (int i = 0; i < owned.Count; i++)
            {
                progression.flags[owned[i].Value] = true;
            }
        }

        /// <summary>
        /// Null-tolerant on the way in: a save written before this field was
        /// used, or a corrupt one, must restore a day-one restaurant (the
        /// starting shop, owned) rather than crash the restore path --
        /// architecture.md section 4 decision 2. A <c>lot.</c> key explicitly
        /// set to <c>false</c> is treated as not-owned, which is what a
        /// hand-edited save would mean by it.
        ///
        /// <see cref="ExpansionModel.Restore"/> re-seeds the starting lot and
        /// drops any id that would leave the complex non-contiguous; a
        /// mismatch between requested and accepted counts is logged, matching
        /// ProgressionService.Restore's behaviour.
        /// </summary>
        public void Restore(SaveFile save, IGameDatabase db)
        {
            var flags = save?.progression?.flags;
            if (flags == null)
            {
                Model.Reset();
                return;
            }

            var ids = new List<LotId>();
            foreach (var pair in flags)
            {
                if (!pair.Value) continue;
                if (pair.Key == null || !pair.Key.StartsWith(SaveFlagPrefix, StringComparison.Ordinal)) continue;

                ids.Add(new LotId(pair.Key));
            }

            var beforeStarting = Registry.StartingLots.Count;
            var accepted = Model.Restore(ids);

            // Starting lots are granted, not restored, so they are expected
            // on top of whatever the save asked for -- only warn when fewer
            // landed than the save+grant should have produced.
            var expected = CountDistinctExpected(ids, beforeStarting);
            if (accepted < expected)
            {
                Debug.LogWarning($"[ExpansionService] Restored {accepted} lot(s); the save asked for {expected}. The rest were unknown, duplicated, or would have left the complex non-contiguous.");
            }
        }

        int CountDistinctExpected(List<LotId> ids, int startingCount)
        {
            var distinct = new HashSet<LotId>();
            var starting = Registry.StartingLots;
            for (int i = 0; i < starting.Count; i++) distinct.Add(starting[i]);
            for (int i = 0; i < ids.Count; i++) distinct.Add(ids[i]);

            // startingCount is already folded in above; the parameter exists
            // to document that starting lots are part of the expectation.
            _ = startingCount;
            return distinct.Count;
        }
    }
}
