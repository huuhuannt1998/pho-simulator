using System;
using System.Collections.Generic;
using Pho.Core;
using Pho.Core.DayCycle;
using Pho.Core.Economy;
using Pho.Domain.DayCycle;
using Pho.Domain.Events;
using Pho.Domain.Identity;
using Pho.Domain.Restaurant;
using Unity.Netcode;
using UnityEngine;

namespace Pho.Net.State
{
    /// <summary>
    /// Makes four players share ONE restaurant: one bank balance, one order
    /// board, one clock.
    ///
    /// <b>THE CENTRAL TRICK: mirror out, re-publish in.</b> Every HUD element
    /// in this game is driven purely by events off the local
    /// <c>IEventBus</c> -- <c>CashPresenter</c> subscribes to
    /// <c>CashChanged</c>, <c>OrderBoardPresenter</c> to
    /// <c>OrderPlaced</c>/<c>OrderStateChanged</c>, <c>DaySummaryPresenter</c>
    /// to <c>DayEnded</c>. None of them knows or cares where those events came
    /// from. So this class does not build a parallel networked UI, and does
    /// not touch a single presenter. On the host it subscribes to the real
    /// bus and mirrors what it hears onto the wire; on a client it takes what
    /// arrives off the wire and <b>re-publishes it onto that client's own
    /// local bus</b>. The client's presenters then light up exactly as they
    /// would in single player, having never learned that networking exists.
    /// The entire UI layer gets multiplayer for free, and the seam is one
    /// class wide.
    ///
    /// <b>Host-authoritative, no prediction</b> -- the same stance
    /// <c>CarryAuthority</c> takes and for the same reason. A client never
    /// computes restaurant state; it displays the host's. There is no
    /// rollback path here because there is nothing to roll back.
    ///
    /// <b>WHAT IS A NetworkVariable AND WHAT IS AN RPC.</b> The rule this
    /// class follows: <i>if a late joiner needs it, it is state and belongs in
    /// a NetworkVariable; if it is a thing that happened, it is a notification
    /// and belongs in an RPC.</i> Applied field by field:
    /// <list type="bullet">
    /// <item><b>Cash balance -- NetworkVariable.</b> "How much money does the
    /// restaurant have" is the definition of current state, and Netcode
    /// delivers a NetworkVariable's current value inside the spawn payload,
    /// so a player who joins on day 3 gets $2,140 without anyone having to
    /// remember to tell them. An RPC-only design would leave that player's
    /// cash readout blank until the next sale -- which could be minutes.</item>
    /// <item><b>Cash transactions -- also an RPC, and this is not
    /// redundancy.</b> <c>CashChanged</c> carries a <i>delta</i> and a
    /// <i>category</i>, and <c>HudView</c> renders them as the "+$9.50 (Sale)"
    /// popup. Neither survives in a balance. Two consecutive balances of
    /// $2,140 and $2,149.50 do not tell you whether that was one sale or a
    /// $12 tip minus a $2.50 refund. The delta and category are one-shot
    /// facts, so they travel as a one-shot message.</item>
    /// <item><b>Day + phase -- NetworkVariable</b> (one struct, see
    /// <see cref="DayPhaseState"/>). Same argument as cash: a joiner must know
    /// it is day 3 and the restaurant is Open. Crucially, the
    /// <c>RestaurantOpened</c>/<c>RestaurantClosed</c> events are <i>derived
    /// on the client from an observed transition</i> of this variable rather
    /// than sent as their own RPCs -- one source of truth, and it makes the
    /// late-joiner behaviour automatically correct: a client that joins
    /// mid-service sees phase Open (state) but is not told the sign "just
    /// flipped" (an event that did not happen for them).</item>
    /// <item><b>Cleanliness -- NetworkVariable</b> (one struct, see
    /// <see cref="CleanlinessState"/>). <c>CleanlinessChanged</c> is unusual
    /// among the frozen events in that it is <i>purely</i> a statement of
    /// current state -- meter, dirty count, total tables, nothing
    /// historical -- so it can be reconstructed faithfully from a
    /// NetworkVariable with nothing invented. That is why cleanliness needs
    /// no companion RPC and cash does.</item>
    /// <item><b>The order board -- RPCs, with an explicit join
    /// handshake.</b> The obvious choice is <c>NetworkList</c>, and it was
    /// rejected: <c>NetworkList</c> requires unmanaged elements, so every
    /// order id (a 36-character GUID), customer id and recipe id would have
    /// to become a <c>FixedString</c>, which lives in <c>Unity.Collections</c>
    /// -- an assembly <c>Pho.Net.asmdef</c> does not reference and which this
    /// pass is not permitted to edit. Rather than smuggle in a dependency,
    /// the board uses plain-<c>string</c> RPCs (natively supported by
    /// Netcode 2.x codegen) for live changes, plus a
    /// request/begin/items/end snapshot handshake a joining client kicks off
    /// itself. That handshake is strictly better than a NetworkList in one
    /// respect anyway: the client asks when <i>it</i> is ready, so there is no
    /// race between the host noticing a connection and the client's own
    /// replicator having spawned.</item>
    /// <item><b>DayEnded -- RPC, never replayed.</b> It carries a whole
    /// <c>DailyReport</c> that cannot be derived from any current value, and
    /// it is emphatically a past event. A player joining on day 4 must not be
    /// shown day 3's takings as though the day had just ended in front of
    /// them.</item>
    /// </list>
    ///
    /// <b>THE LATE JOINER ENDS UP WITH A CORRECT HUD, NOT A BLANK ONE.</b>
    /// That is a requirement, not a side effect, and it is worth stating what
    /// the joining player sees the instant this component spawns on their
    /// machine: the real cash balance (from the NetworkVariable, republished
    /// as a delta-zero <c>CashChanged</c> so the readout populates without a
    /// fake popup -- see <see cref="PublishCashSnapshotIfNeeded"/>); the real
    /// day and phase; the real cleanliness meter; and every order currently in
    /// flight, replayed as <c>OrderPlaced</c> + <c>OrderStateChanged</c> pairs
    /// so the board draws them at the right state. What they do NOT see is
    /// history that is not theirs: no replayed day summary, no "+$9.50" popup
    /// for a sale made before they arrived, no "an order was just served"
    /// flash. Current state, yes. Other people's past, no.
    ///
    /// <b>WHAT A CLIENT CANNOT HONESTLY RECONSTRUCT.</b> The temptation in a
    /// class like this is to synthesise a plausible event whenever a value
    /// moves. That is how a HUD starts lying. Specifically:
    /// <list type="bullet">
    /// <item>A client can never derive a cash <i>delta</i> or
    /// <i>category</i> from a balance change. If it did, an inferred
    /// "+$9.50 (Other)" would appear for a change that was really a $12 sale
    /// and a $2.50 ingredient purchase in the same frame. So the only
    /// <c>CashChanged</c> a client ever publishes with a nonzero delta is one
    /// the host explicitly sent; balance-only corrections go out with
    /// <c>Delta = 0</c>, which <c>HudView</c> already renders as "no popup".</item>
    /// <item>A client cannot reconstruct <c>OrderServed</c>'s quality,
    /// accuracy or wait time, because those exist nowhere but on the event.
    /// They are forwarded live and simply absent for a late joiner -- an
    /// order in the snapshot arrives with its state correct and its served
    /// scores null, which is honest rather than zeroed.</item>
    /// <item>A client cannot reconstruct a <c>DailyReport</c> from a cash
    /// balance, so it never tries.</item>
    /// <item>A client cannot know which of two orders for the same recipe the
    /// host's FIFO matcher chose; it is told the outcome, and that is the only
    /// reason it agrees.</item>
    /// </list>
    ///
    /// <b>REPLICATION IS ONLY HALF THE FIX.</b> This class makes a client
    /// <i>see</i> the host's restaurant. It does not stop the client from also
    /// running its own -- today every service in <c>Pho.Core</c> is built
    /// locally by <c>GameBootstrap</c> and simulates unconditionally, so a
    /// client would tick its own <c>DayClock</c> and mint its own
    /// <c>OrderId</c>s alongside the replicated ones. Closing that requires
    /// editing services outside this pass's write set; <see cref="NetAuthority"/>
    /// is the seam that makes it a one-line change per service, and the exact
    /// list of edits is in this file's INTEGRATION NOTES region below.
    ///
    /// All non-trivial bookkeeping lives in the pure
    /// <see cref="OrderBoardMirror"/>, testable without a network, exactly as
    /// <c>CarryRegistry</c> is split out of <c>CarryAuthority</c>. This class
    /// only moves messages.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class SharedStateReplicator : NetworkBehaviour
    {
        // ---------------------------------------------------------------
        // Replicated current state (see the NetworkVariable-vs-RPC note on
        // the class doc comment for why each one is here rather than an RPC).
        // Write permission is Server by default, which is the whole point:
        // a client physically cannot author restaurant state.
        // ---------------------------------------------------------------

        readonly NetworkVariable<long> _cashCents = new NetworkVariable<long>(0L);
        readonly NetworkVariable<DayPhaseState> _dayPhase = new NetworkVariable<DayPhaseState>(default);
        readonly NetworkVariable<CleanlinessState> _cleanliness = new NetworkVariable<CleanlinessState>(CleanlinessState.Spotless);

        /// <summary>
        /// The live order board. On the host it is fed from the local event
        /// bus and is what a joining player gets sent; on a client it is fed
        /// from the wire and records what has already been republished
        /// locally, so nothing is announced twice.
        /// </summary>
        readonly OrderBoardMirror _board = new OrderBoardMirror();

        /// <summary>
        /// Which tables are dirty. Tracked on both sides for the same reason
        /// as <see cref="_board"/>: the host needs the set to send, the client
        /// needs it to avoid re-publishing a <c>TableDirtied</c> it already
        /// published.
        /// </summary>
        readonly HashSet<string> _dirtyTables = new HashSet<string>(StringComparer.Ordinal);

        readonly List<IDisposable> _subscriptions = new List<IDisposable>();

        /// <summary>Buffer for an in-flight join snapshot. Non-null only between Begin and End.</summary>
        List<OrderBoardEntry> _incomingOrders;
        List<string> _incomingDirtyTables;

        IEventBus _events;

        /// <summary>Host-only cache of the day-cycle service polled by <see cref="Update"/>. Null on a client and in a scene with no day cycle.</summary>
        RestaurantStateServiceBehaviour _hostDayCycle;

        /// <summary>Guards the delta-zero catch-up publish. See <see cref="PublishCashSnapshotIfNeeded"/>.</summary>
        bool _hasPublishedCash;
        long _lastPublishedCashCents;

        DayPhase _lastPhase;
        bool _hasSeenPhase;

        CleanlinessState _lastPublishedCleanliness;
        bool _hasPublishedCleanliness;

        /// <summary>A snapshot larger than this is treated as a protocol fault rather than honoured. Four players cannot generate hundreds of simultaneous orders; a number that large means something is wrong upstream, and a replica should refuse to allocate on a peer's say-so.</summary>
        const int MaxSnapshotEntries = 512;

        /// <summary>The one well-known instance for this session. Non-null on every peer once spawned (unlike <c>CarryAuthority.Server</c>, which is server-only) because a client genuinely needs to read replicated state.</summary>
        public static SharedStateReplicator Instance { get; private set; }

        // ---------------------------------------------------------------
        // Read-only views, for gameplay/UI code that wants the value rather
        // than an event. Correct on every peer, including a late joiner.
        // ---------------------------------------------------------------

        /// <summary>The restaurant's cash balance, as replicated. See <see cref="Money"/> for why this round-trips through integer cents.</summary>
        public decimal CashBalance => Money.FromCents(_cashCents.Value);

        public int Day => _dayPhase.Value.Day;

        public DayPhase Phase => _dayPhase.Value.Phase;

        public float Cleanliness01 => _cleanliness.Value.Cleanliness01;

        /// <summary>Every order currently in flight, oldest first.</summary>
        public IReadOnlyList<OrderBoardEntry> LiveOrders => _board.Entries;

        public override void OnNetworkSpawn()
        {
            Instance = this;

            _events = ResolveEventBus();
            if (_events == null)
            {
                // No composition root in this scene. Inert rather than
                // broken -- the same "degrade, don't throw" stance
                // GameBootstrap takes when a scene has no player.
                Debug.LogWarning("[SharedStateReplicator] No GameContext available (GameBootstrap.Current is null) -- shared restaurant state will not be replicated in this scene.");
                return;
            }

            if (IsServer) StartMirroringHostState();
            else StartFollowingHostState();
        }

        public override void OnNetworkDespawn()
        {
            foreach (var subscription in _subscriptions) subscription.Dispose();
            _subscriptions.Clear();

            if (!IsServer)
            {
                _dayPhase.OnValueChanged -= OnDayPhaseReplicated;
                _cleanliness.OnValueChanged -= OnCleanlinessReplicated;
                _cashCents.OnValueChanged -= OnCashBalanceReplicated;
            }

            _board.Clear();
            _dirtyTables.Clear();
            _incomingOrders = null;
            _incomingDirtyTables = null;
            _events = null;
            _hostDayCycle = null;

            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        static IEventBus ResolveEventBus() => GameBootstrap.Current?.Events;

        // =================================================================
        // HOST SIDE -- listen to the real simulation, mirror it outward.
        // =================================================================

        void StartMirroringHostState()
        {
            SeedFromLiveServices();

            _subscriptions.Add(_events.Subscribe<CashChanged>(OnHostCashChanged));
            _subscriptions.Add(_events.Subscribe<OrderPlaced>(OnHostOrderPlaced));
            _subscriptions.Add(_events.Subscribe<OrderStateChanged>(OnHostOrderStateChanged));
            _subscriptions.Add(_events.Subscribe<OrderServed>(OnHostOrderServed));
            _subscriptions.Add(_events.Subscribe<DayEnded>(OnHostDayEnded));
            _subscriptions.Add(_events.Subscribe<CleanlinessChanged>(OnHostCleanlinessChanged));
            _subscriptions.Add(_events.Subscribe<TableDirtied>(OnHostTableDirtied));
            _subscriptions.Add(_events.Subscribe<TableCleaned>(OnHostTableCleaned));
        }

        /// <summary>
        /// Publishes the CURRENT value of everything before a single event has
        /// fired.
        ///
        /// Without this the replicated cash would sit at $0 until the first
        /// sale, and the very first player to join would see a wrong balance
        /// -- events only tell you about changes, and the interesting values
        /// (a $1,500 opening float, day 1, a spotless dining room) exist
        /// before any change happens. Every lookup is optional: a
        /// boot-capable scene is allowed to lack content and therefore lack
        /// these services (see <c>GameBootstrap.RegisterContent</c>).
        /// </summary>
        void SeedFromLiveServices()
        {
            var ctx = GameBootstrap.Current;
            if (ctx == null) return;

            if (ctx.TryGet<EconomyService>(out var economy))
            {
                _cashCents.Value = Money.ToCents(economy.Cash);
            }

            if (ctx.TryGet<RestaurantStateServiceBehaviour>(out _hostDayCycle))
            {
                _dayPhase.Value = new DayPhaseState(_hostDayCycle.Day, _hostDayCycle.Phase);
            }

            if (ctx.TryGet<CleanlinessModel>(out var cleanliness))
            {
                _cleanliness.Value = new CleanlinessState(
                    cleanliness.Cleanliness01, cleanliness.DirtyTableCount, cleanliness.TotalTables);

                // A restored save repopulates the dirty set without emitting
                // per-table events (CleanlinessModel.RestoreDirtyTables says
                // so explicitly), so subscribing alone would miss them.
                foreach (var tableId in cleanliness.DirtyTableIds) _dirtyTables.Add(tableId);
            }
        }

        /// <summary>
        /// Day and phase are POLLED rather than derived from events, and that
        /// is deliberate. <c>RestaurantStateServiceBehaviour</c> publishes
        /// <c>RestaurantOpened</c>/<c>RestaurantClosed</c>/<c>DayEnded</c> for
        /// most transitions -- but its <c>Restore</c> path sets day, time and
        /// phase straight onto the <c>DayClock</c> and publishes nothing at
        /// all. An event-driven mirror would silently desync every client on
        /// every save load. Two field reads per frame on the host only is a
        /// cheap price for not having that class of bug, and nothing goes on
        /// the wire unless the value actually moved.
        ///
        /// The service is cached at spawn; the lookup below only runs while
        /// that cache is empty, which covers a scene where no day cycle exists
        /// at all (a boot-capable scene with no content registers no
        /// <c>IBalanceConfig</c>, so <c>RestaurantStateService.Install</c>
        /// creates nothing).
        /// </summary>
        void Update()
        {
            if (!IsSpawned || !IsServer || _events == null) return;

            if (_hostDayCycle == null)
            {
                var ctx = GameBootstrap.Current;
                if (ctx == null || !ctx.TryGet<RestaurantStateServiceBehaviour>(out _hostDayCycle)) return;
            }

            var current = new DayPhaseState(_hostDayCycle.Day, _hostDayCycle.Phase);
            if (!current.Equals(_dayPhase.Value)) _dayPhase.Value = current;
        }

        void OnHostCashChanged(CashChanged evt)
        {
            _cashCents.Value = Money.ToCents(evt.NewBalance);

            // The balance above is state; the delta and category below are
            // the one-shot fact, and only the host can supply them honestly.
            BroadcastCashChangedRpc(
                Money.ToCents(evt.NewBalance),
                Money.ToCents(evt.Delta),
                (byte)evt.Category);
        }

        void OnHostOrderPlaced(OrderPlaced evt)
        {
            var id = evt.Id.Value;
            if (string.IsNullOrEmpty(id)) return;

            _board.ApplyUpsert(new OrderBoardEntry(id, evt.Customer.Value, evt.Recipe.Value, OrderState.Created));
            BroadcastOrderPlacedRpc(id, evt.Customer.Value ?? string.Empty, evt.Recipe.Value ?? string.Empty);
        }

        void OnHostOrderStateChanged(OrderStateChanged evt)
        {
            var id = evt.Id.Value;
            if (string.IsNullOrEmpty(id)) return;

            // Keep the identity fields the board already has: this event
            // carries only the id, so re-deriving them is impossible and
            // guessing them would corrupt the snapshot a later joiner gets.
            _board.TryGet(id, out var known);
            _board.ApplyUpsert(new OrderBoardEntry(id, known.CustomerId, known.RecipeId, evt.To));

            BroadcastOrderStateChangedRpc(id, (byte)evt.From, (byte)evt.To);
        }

        void OnHostOrderServed(OrderServed evt)
        {
            var id = evt.Id.Value;
            if (string.IsNullOrEmpty(id)) return;

            BroadcastOrderServedRpc(id, evt.Quality01, evt.Accuracy01, evt.WaitSeconds);
        }

        void OnHostDayEnded(DayEnded evt)
        {
            var report = evt.Report;
            BroadcastDayEndedRpc(
                evt.Day,
                Money.ToCents(report?.Revenue ?? 0m),
                Money.ToCents(report?.IngredientCost ?? 0m),
                Money.ToCents(report?.Rent ?? 0m),
                Money.ToCents(report?.Utilities ?? 0m),
                Money.ToCents(report?.Profit ?? 0m));
        }

        void OnHostCleanlinessChanged(CleanlinessChanged evt) =>
            _cleanliness.Value = new CleanlinessState(evt.Cleanliness01, evt.DirtyTableCount, evt.TotalTables);

        void OnHostTableDirtied(TableDirtied evt)
        {
            if (string.IsNullOrEmpty(evt.TableId)) return;
            _dirtyTables.Add(evt.TableId);
            BroadcastTableDirtiedRpc(evt.TableId);
        }

        void OnHostTableCleaned(TableCleaned evt)
        {
            if (string.IsNullOrEmpty(evt.TableId)) return;
            _dirtyTables.Remove(evt.TableId);
            BroadcastTableCleanedRpc(evt.TableId);
        }

        // =================================================================
        // CLIENT SIDE -- take what arrives and re-publish it locally.
        // Note that a client never subscribes to the bus, only publishes to
        // it, so there is no feedback loop to guard against.
        // =================================================================

        void StartFollowingHostState()
        {
            // Netcode applies a NetworkVariable's replicated value before
            // OnNetworkSpawn runs, so reading .Value here is reading the
            // host's current truth, not a default. Seed first, subscribe
            // second: seeding records what we are about to publish so the
            // change callbacks cannot double-announce it.
            _lastPhase = _dayPhase.Value.Phase;
            _hasSeenPhase = true;

            _cashCents.OnValueChanged += OnCashBalanceReplicated;
            _dayPhase.OnValueChanged += OnDayPhaseReplicated;
            _cleanliness.OnValueChanged += OnCleanlinessReplicated;

            PublishCashSnapshotIfNeeded();
            PublishCleanlinessIfChanged(_cleanliness.Value);

            // Ask for the board rather than waiting to be told. The client
            // knows when it is ready; the host does not.
            RequestStateSnapshotRpc();
        }

        void OnCashBalanceReplicated(long previous, long current) => PublishCashSnapshotIfNeeded();

        /// <summary>
        /// Publishes a <c>CashChanged</c> carrying the correct balance and a
        /// <b>zero delta</b>, if and only if the balance we last told the
        /// local bus about disagrees with the replicated one.
        ///
        /// The zero is the honest part. A client cannot know what a balance
        /// movement was <i>made of</i> -- see the class doc comment's "what a
        /// client cannot honestly reconstruct". Publishing
        /// <c>Delta = 0, Category = Other</c> says exactly what is true ("the
        /// balance is now this; I am not claiming a transaction"), and
        /// <c>HudView</c> already treats a zero delta as "draw no popup", so
        /// the cash readout populates without a fabricated "+$0.00" appearing
        /// on screen. It is also inert for the ledger: an <c>Other</c>-category
        /// movement is explicitly ignored by <c>DayLedgerAccumulator</c>, and
        /// zero would contribute nothing even if it were not.
        ///
        /// In normal operation this fires exactly once per client, at spawn --
        /// live changes arrive as <see cref="BroadcastCashChangedRpc"/> with a
        /// real delta, which updates the same bookkeeping. A later firing
        /// means an RPC went missing and is worth knowing about, hence the
        /// log.
        /// </summary>
        void PublishCashSnapshotIfNeeded()
        {
            if (_events == null) return;

            long current = _cashCents.Value;
            if (_hasPublishedCash && _lastPublishedCashCents == current) return;

            if (_hasPublishedCash)
            {
                Debug.LogWarning($"[SharedStateReplicator] Replicated cash balance ({Money.FromCents(current)}) disagreed with the last transaction this client was told about ({Money.FromCents(_lastPublishedCashCents)}). Correcting the balance with a zero-delta CashChanged -- a transaction notification was probably dropped.");
            }

            _hasPublishedCash = true;
            _lastPublishedCashCents = current;
            _events.Publish(new CashChanged(Money.FromCents(current), 0m, LedgerCategory.Other));
        }

        /// <summary>
        /// Turns an observed phase transition into the matching frozen event.
        ///
        /// Only a transition produces an event, which is what keeps a late
        /// joiner honest: they adopt the current phase silently at spawn (see
        /// <see cref="StartFollowingHostState"/>) and are never told the sign
        /// flipped when it did not. A move to <c>Prep</c> publishes nothing --
        /// that is the day-rollover reset, and the frozen event set has no
        /// event for it.
        /// </summary>
        void OnDayPhaseReplicated(DayPhaseState previous, DayPhaseState current)
        {
            if (_events == null) return;

            var phase = current.Phase;
            if (_hasSeenPhase && _lastPhase == phase) return;

            _lastPhase = phase;
            _hasSeenPhase = true;

            if (phase == DayPhase.Open) _events.Publish(new RestaurantOpened());
            else if (phase == DayPhase.Closed) _events.Publish(new RestaurantClosed());
        }

        void OnCleanlinessReplicated(CleanlinessState previous, CleanlinessState current) =>
            PublishCleanlinessIfChanged(current);

        /// <summary>
        /// <c>CleanlinessChanged</c> is a pure statement of current state, so
        /// a replica can republish it verbatim with nothing invented -- the
        /// meter and both numbers it was derived from arrive together in one
        /// struct and stay consistent.
        /// </summary>
        void PublishCleanlinessIfChanged(CleanlinessState state)
        {
            if (_events == null) return;
            if (_hasPublishedCleanliness && _lastPublishedCleanliness.Equals(state)) return;

            _hasPublishedCleanliness = true;
            _lastPublishedCleanliness = state;
            _events.Publish(new CleanlinessChanged(state.Cleanliness01, state.DirtyTableCount, state.TotalTables));
        }

        /// <summary>
        /// Replays a set of board changes onto the local bus as the frozen
        /// order events. This is the re-publish half of the class's central
        /// trick, and it is the only place a client ever writes an order event.
        /// </summary>
        void PublishBoardOps(IReadOnlyList<OrderBoardOp> ops)
        {
            if (_events == null || ops == null) return;

            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                switch (op.Kind)
                {
                    case OrderBoardOpKind.Placed:
                        _events.Publish(new OrderPlaced(
                            new OrderId(op.OrderId),
                            new CustomerId(op.CustomerId),
                            new RecipeId(op.RecipeId)));
                        break;

                    case OrderBoardOpKind.StateChanged:
                        _events.Publish(new OrderStateChanged(new OrderId(op.OrderId), op.From, op.To));
                        break;

                    case OrderBoardOpKind.Removed:
                        // No faithful event exists for this. There is no
                        // "OrderRemoved" in the frozen set, and asserting a
                        // terminal state the host never reported would be the
                        // exact fabrication this class refuses elsewhere. The
                        // board forgets it (so it stops being replicated) and
                        // the stale ticket is left on the local presenter,
                        // which retains terminal tickets anyway. Unreachable
                        // by construction today -- a joining client's board
                        // starts empty, so a snapshot can only add -- and
                        // logged because if it ever does happen it means the
                        // live RPC stream and the snapshot disagree.
                        Debug.LogWarning($"[SharedStateReplicator] Order '{op.OrderId}' was on this client's board (state {op.From}) but absent from the host's snapshot. Dropping it locally; no order event exists to describe this faithfully.");
                        break;
                }
            }
        }

        // =================================================================
        // WIRE -- live broadcasts.
        //
        // SendTo.NotServer, never ClientsAndHost: the host already published
        // these on its own bus (that is where this class heard them), so
        // executing locally too would double every event on the machine that
        // is actually running the restaurant.
        // =================================================================

        [Rpc(SendTo.NotServer)]
        void BroadcastCashChangedRpc(long newBalanceCents, long deltaCents, byte category)
        {
            if (_events == null) return;

            _hasPublishedCash = true;
            _lastPublishedCashCents = newBalanceCents;

            _events.Publish(new CashChanged(
                Money.FromCents(newBalanceCents),
                Money.FromCents(deltaCents),
                DecodeCategory(category)));
        }

        [Rpc(SendTo.NotServer)]
        void BroadcastOrderPlacedRpc(string orderId, string customerId, string recipeId)
        {
            if (string.IsNullOrEmpty(orderId)) return;
            PublishBoardOps(_board.ApplyUpsert(new OrderBoardEntry(orderId, customerId, recipeId, OrderState.Created)));
        }

        [Rpc(SendTo.NotServer)]
        void BroadcastOrderStateChangedRpc(string orderId, byte from, byte to)
        {
            if (string.IsNullOrEmpty(orderId)) return;

            _board.TryGet(orderId, out var known);
            PublishBoardOps(_board.ApplyUpsert(
                new OrderBoardEntry(orderId, known.CustomerId, known.RecipeId, DecodeOrderState(to))));
        }

        [Rpc(SendTo.NotServer)]
        void BroadcastOrderServedRpc(string orderId, float quality01, float accuracy01, float waitSeconds)
        {
            if (_events == null || string.IsNullOrEmpty(orderId)) return;
            _events.Publish(new OrderServed(new OrderId(orderId), quality01, accuracy01, waitSeconds));
        }

        [Rpc(SendTo.NotServer)]
        void BroadcastDayEndedRpc(int day, long revenueCents, long ingredientCostCents, long rentCents, long utilitiesCents, long profitCents)
        {
            if (_events == null) return;

            // DailyReport is a mutable class on the frozen event, so it is
            // rebuilt field by field rather than serialized as a unit.
            var report = new DailyReport
            {
                Day = day,
                Revenue = Money.FromCents(revenueCents),
                IngredientCost = Money.FromCents(ingredientCostCents),
                Rent = Money.FromCents(rentCents),
                Utilities = Money.FromCents(utilitiesCents),
                Profit = Money.FromCents(profitCents)
            };

            _events.Publish(new DayEnded(day, report));
        }

        [Rpc(SendTo.NotServer)]
        void BroadcastTableDirtiedRpc(string tableId)
        {
            if (_events == null || string.IsNullOrEmpty(tableId)) return;
            if (!_dirtyTables.Add(tableId)) return;
            _events.Publish(new TableDirtied(tableId));
        }

        [Rpc(SendTo.NotServer)]
        void BroadcastTableCleanedRpc(string tableId)
        {
            if (_events == null || string.IsNullOrEmpty(tableId)) return;
            if (!_dirtyTables.Remove(tableId)) return;
            _events.Publish(new TableCleaned(tableId));
        }

        // =================================================================
        // WIRE -- the join handshake.
        //
        // A joining client asks; the host answers with Begin, one message per
        // item, then End. RPCs are reliable and ordered by default in Netcode
        // 2.x, so the client can safely buffer between the two markers.
        // One message per order rather than one array keeps every parameter a
        // plain string/byte -- the serialization surface with the least that
        // can go wrong -- at a cost of a handful of extra packets, once, per
        // player who joins.
        // =================================================================

        // InvokePermission is stated explicitly rather than left to its
        // default because it is load-bearing: this component's NetworkObject
        // is server-owned, so an owner-only permission would make it
        // impossible for the very clients that need the snapshot to ask for
        // it. (CarryAuthority expresses the same intent with the older
        // RequireOwnership = false, which Netcode 2.13 deprecates.) Letting
        // anyone ask is safe -- the request carries no payload and the answer
        // goes only to the caller, whose id comes from the transport rather
        // than from anything they can choose.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void RequestStateSnapshotRpc(RpcParams rpcParams = default)
        {
            var target = RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp);

            BeginStateSnapshotRpc(target);

            var entries = _board.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                SnapshotOrderRpc(
                    entry.OrderId,
                    entry.CustomerId ?? string.Empty,
                    entry.RecipeId ?? string.Empty,
                    (byte)entry.State,
                    target);
            }

            foreach (var tableId in _dirtyTables) SnapshotDirtyTableRpc(tableId, target);

            EndStateSnapshotRpc(target);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void BeginStateSnapshotRpc(RpcParams rpcParams)
        {
            _incomingOrders = new List<OrderBoardEntry>();
            _incomingDirtyTables = new List<string>();
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void SnapshotOrderRpc(string orderId, string customerId, string recipeId, byte state, RpcParams rpcParams)
        {
            if (_incomingOrders == null || string.IsNullOrEmpty(orderId)) return;
            if (_incomingOrders.Count >= MaxSnapshotEntries) return;

            _incomingOrders.Add(new OrderBoardEntry(orderId, customerId, recipeId, DecodeOrderState(state)));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void SnapshotDirtyTableRpc(string tableId, RpcParams rpcParams)
        {
            if (_incomingDirtyTables == null || string.IsNullOrEmpty(tableId)) return;
            if (_incomingDirtyTables.Count >= MaxSnapshotEntries) return;

            _incomingDirtyTables.Add(tableId);
        }

        /// <summary>
        /// The moment a late joiner's HUD stops being blank: the buffered
        /// snapshot is reconciled against this client's (empty) board and the
        /// resulting <c>OrderPlaced</c>/<c>OrderStateChanged</c> pairs are
        /// published locally, so every in-flight ticket appears at its correct
        /// state.
        /// </summary>
        [Rpc(SendTo.SpecifiedInParams)]
        void EndStateSnapshotRpc(RpcParams rpcParams)
        {
            var orders = _incomingOrders;
            var dirtyTables = _incomingDirtyTables;
            _incomingOrders = null;
            _incomingDirtyTables = null;

            if (orders == null) return;

            PublishBoardOps(_board.ApplySnapshot(orders));

            if (dirtyTables == null || _events == null) return;

            // A JUDGMENT CALL worth naming: TableDirtied means "a customer
            // just left a mess", and for a joining player that is not
            // literally what happened. CleanlinessModel.RestoreDirtyTables
            // faces the same question on a save load and answers it the other
            // way (it publishes no per-table events). The opposite answer is
            // right here, because a loaded save has no observer who can see
            // the contradiction, whereas a joining player is looking at a
            // dining room that the host is rendering as messy. Suppressing
            // these would leave that player staring at tables that look clean
            // and refuse to be cleaned.
            for (int i = 0; i < dirtyTables.Count; i++)
            {
                var tableId = dirtyTables[i];
                if (string.IsNullOrEmpty(tableId)) continue;
                if (!_dirtyTables.Add(tableId)) continue;

                _events.Publish(new TableDirtied(tableId));
            }
        }

        // =================================================================
        // Decoding. Enums cross the wire as bytes; an unrecognised value can
        // only come from a version-mismatched peer, and is clamped to a safe
        // reading rather than cast blindly into an undefined enum value.
        // =================================================================

        static LedgerCategory DecodeCategory(byte raw) =>
            Enum.IsDefined(typeof(LedgerCategory), (int)raw) ? (LedgerCategory)raw : LedgerCategory.Other;

        static OrderState DecodeOrderState(byte raw) =>
            Enum.IsDefined(typeof(OrderState), (int)raw) ? (OrderState)raw : OrderState.Created;

        #region INTEGRATION NOTES -- making the simulation host-only
        /*
        Replication alone does not stop a client from running its own
        restaurant alongside the replicated one. These are the edits that
        close it. None of them are made here: they all touch files outside
        this pass's write set, and every one is a guard rather than a
        redesign.

        STEP 0 -- add the seam Pho.Core can actually name.
          NEW FILE: Assets/Scripts/Domain/Multiplayer/ISimulationAuthority.cs
              namespace Pho.Domain.Multiplayer
              {
                  public interface ISimulationAuthority { bool IsSimulationAuthority { get; } }
              }
          Pho.Core already references Pho.Domain, and Pho.Net references both,
          so this is the only direction that is not a circular asmdef
          reference. Then make Pho.Net.State.SimulationAuthorityFlag implement
          it (the member already matches) and register one instance into
          GameContext before the simulation installers run -- practically, an
          [AutoInstall] installer in Pho.Net at an Order below
          InstallOrder.Economy (300).

        STEP 1 -- stop clients simulating. In each service below, the guard is
        the same shape:
              if (ctx.TryGet<ISimulationAuthority>(out var authority) && !authority.IsSimulationAuthority) return;

          Core/Economy/EconomyService.cs -- Install(): still Register the
              service (a client's UI and save code expect it to exist) but do
              not let anything call Credit/Debit on a client. The cheapest
              correct version is to guard the CALLERS, since the money-moving
              call sites are what must not run twice; the service itself is
              harmless while nobody calls it. Note that its Cash property will
              then be stale on a client -- read
              SharedStateReplicator.CashBalance instead, or have the client
              apply the replicated balance.

          Core/Orders/OrderServiceInstaller.cs -- Install(): return early on a
              client. This one matters most: OrderService mints OrderIds from
              Guid.NewGuid(), so two copies produce two disjoint sets of
              orders that can never be reconciled. A client's order board must
              come entirely from this replicator.

          Core/DayCycle/RestaurantStateService.cs -- Install(): on a client,
              still create the behaviour (Pho.Save and any phase reader expect
              it in GameContext) but do not let it tick. Add a public
              `SimulateLocally` flag to RestaurantStateServiceBehaviour, set
              it false on a client, and make Update() return early when false.
              Otherwise every client runs its own DayClock off its own
              Time.deltaTime and drifts a different day boundary.

          Core/Restaurant/CleanlinessService.cs -- Install(): keep the service
              and its table registration on every peer (DirtyTable components
              are per-scene and the count must be right locally), but the
              MarkDirty/TryClean CALL SITES must be host-only. A client
              receives TableDirtied/TableCleaned from this replicator, so if it
              also marks locally it will double-publish. Guard
              Core/Restaurant/DirtyTable.cs and whatever gameplay calls
              MarkDirty.

          Customers/CustomerSpawner.cs -- spawning is simulation. Two peers
              spawning customers means two crowds. Host-only.

        STEP 2 -- wire this component up (scene/prefab work, integration owner):
          Put SharedStateReplicator on the same NetworkObject as
          CarryAuthority, or its own scene NetworkObject, so exactly one
          instance exists per session and it spawns with the session. It needs
          no inspector references -- it finds the event bus through
          GameBootstrap.Current at spawn -- but it does need GameBootstrap to
          have run first, which scene ordering already gives.

        STEP 3 -- what will still be single-authority-assuming afterwards, and
        is deliberately out of scope here: SaveCoordinator (a client should not
        write a save of a restaurant it does not own), ProgressionService, and
        InventoryService (stock is shared restaurant state and wants the same
        treatment as cash -- a second replicated field, not a second
        simulation).
        */
        #endregion
    }
}
