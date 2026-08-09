using System;
using System.Collections.Generic;
using Pho.Core;
using Pho.Domain.Contracts;
using Pho.Domain.DayCycle;
using Pho.Domain.Economy;
using Pho.Domain.Events;
using Pho.Domain.Multiplayer;
using Pho.Save.Dto;
using Pho.Save.Participation;
using UnityEngine;

namespace Pho.Core.DayCycle
{
    /// <summary>
    /// MonoBehaviour host for the pure <see cref="DayClock"/>
    /// (architecture.md §6.2/M11). Ticks <c>DayClock.Tick</c> every frame,
    /// exposes <see cref="OpenRestaurant"/>/<see cref="CloseRestaurant"/>
    /// (which delegate to DayClock, then publish
    /// <see cref="RestaurantOpened"/>/<see cref="RestaurantClosed"/>), and
    /// publishes <see cref="DayEnded"/> whenever <c>DayClock.Day</c>
    /// increments (i.e. <c>TimeOfDaySeconds</c> wraps past
    /// <c>cfg.DayLengthSeconds</c> -- see DayClock.Tick).
    ///
    /// SPLIT DESIGN (why this is not itself the discovered [AutoInstall]
    /// type -- judgment call the brief explicitly asks to be documented):
    /// GameBootstrap's reflection scan builds every [AutoInstall] IInstaller
    /// via <c>Activator.CreateInstance(type)</c> on a bare parameterless
    /// constructor. That is correct for a plain C# object but WRONG for a
    /// MonoBehaviour -- Unity requires MonoBehaviours to be created via
    /// <c>GameObject.AddComponent</c>, never <c>new</c>/<c>Activator</c>,
    /// or the component silently isn't attached to anything and Unity's own
    /// lifecycle (Awake/Update/OnDestroy) never runs. So the type discovered
    /// by reflection is <see cref="RestaurantStateService"/> below -- a
    /// small plain-C# factory/registrar with no runtime state of its own --
    /// which creates a GameObject, AddComponents *this* class onto it,
    /// binds it, and registers THIS BEHAVIOUR (not itself) into
    /// GameContext. Two small types, cleanly separated concerns: one knows
    /// how to get built during bootstrap, the other knows how to run a day.
    /// Consumers fetch the running service via
    /// <c>ctx.Get&lt;RestaurantStateServiceBehaviour&gt;()</c>.
    ///
    /// DAILYREPORT: the DayEnded published here now carries a REAL report
    /// (this replaces the earlier placeholder that left every decimal field
    /// at 0m). Revenue and ingredient cost are accumulated from the live
    /// ledger: this behaviour subscribes to <see cref="CashChanged"/> --
    /// which <c>EconomyService.Apply</c> already publishes for every
    /// transaction, with its <c>LedgerCategory</c> -- and folds each delta
    /// into a pure <see cref="DayLedgerAccumulator"/>. Rent and utilities
    /// and the profit identity come from
    /// <see cref="DailyReportBuilder"/>; see that class for the day % 7 rent
    /// boundary, why rent is schedule-derived rather than ledger-derived,
    /// and the IBalanceConfig utilities gap.
    ///
    /// DAY-BOUNDARY ORDERING -- the off-by-one hazard, and why the steps in
    /// <c>Update</c> are in the order they are:
    /// <list type="bullet">
    /// <item>The report covers the day that just <b>ENDED</b>, which is the
    /// pre-tick <c>_lastSeenDay</c>, NOT the post-tick <c>_clock.Day</c>.
    /// Both <c>DailyReport.Day</c> and <c>DayEnded.Day</c> carry that ended
    /// day. (The placeholder previously published the NEW day here; using
    /// the new day would shift every rent charge one day early.)</item>
    /// <item>The accumulator is snapshotted, then reset, and only THEN is
    /// DayEnded published. Publishing last matters: a handler may itself
    /// move cash (an autosave, or the future rent auto-debit), and that
    /// belongs to the NEW day's bucket -- a reset performed after dispatch
    /// would silently erase it.</item>
    /// <item>KNOWN LIMITATION, deliberate: <c>DayClock.Tick</c>'s while-loop
    /// can cross more than one day boundary in a single call, but exactly
    /// ONE DayEnded is published per detected increment, attributed to the
    /// first ended day; intermediate days are collapsed (so a rent day
    /// jumped clean over is not billed). A single frame spanning a whole
    /// in-game day cannot occur in play -- only a test fast-forward does it
    /// -- and splitting one accumulator across N days would require
    /// inventing an attribution rule for revenue that no requirement asks
    /// for. Revisit if timed skips (sleep-to-next-day) are ever added.</item>
    /// </list>
    /// </summary>
    public sealed class RestaurantStateServiceBehaviour : MonoBehaviour, ISaveParticipant
    {
        // Subscriptions held for deterministic teardown, following the
        // established idiom (CustomerAgent._subscriptions,
        // CashPresenter._subscriptions) -- disposed in OnDestroy.
        readonly List<IDisposable> _subscriptions = new List<IDisposable>();

        readonly DayLedgerAccumulator _today = new DayLedgerAccumulator();

        DayClock _clock;
        IBalanceConfig _cfg;
        IEventBus _events;
        bool _bound;
        int _lastSeenDay;

        /// <summary>Net revenue booked so far on the in-progress day (0m before Bind). Exposed read-only for a live HUD/debug readout and to make the day-boundary reset observable.</summary>
        public decimal TodayRevenue => _today.Revenue;

        /// <summary>Net ingredient spend so far on the in-progress day, as a positive cost (0m before Bind).</summary>
        public decimal TodayIngredientCost => _today.IngredientCost;

        /// <summary>Prep before Bind() is called (not yet ticking).</summary>
        public DayPhase Phase => _bound ? _clock.Phase : DayPhase.Prep;

        /// <summary>0 before Bind() is called.</summary>
        public int Day => _bound ? _clock.Day : 0;

        /// <summary>
        /// Injection seam, mirroring the Kitchen station Bind(...) pattern
        /// (see IngredientStation.Bind). Called once by
        /// RestaurantStateService immediately after AddComponent.
        /// </summary>
        public void Bind(int startDay, IBalanceConfig cfg, IEventBus events)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _clock = new DayClock(startDay, _cfg);
            _lastSeenDay = _clock.Day;

            // Idempotent re-bind: drop any previous subscriptions first, so
            // a second Bind can never double-count every transaction.
            DisposeSubscriptions();
            _today.Reset();
            _subscriptions.Add(_events.Subscribe<CashChanged>(OnCashChanged));

            _bound = true;
        }

        /// <summary>
        /// Folds every cash movement into the current day's totals. The
        /// category filtering (which categories count as revenue/ingredient
        /// cost, and which are deliberately ignored) lives in the pure
        /// <see cref="DayLedgerAccumulator"/>, not here.
        /// </summary>
        void OnCashChanged(CashChanged evt) => _today.Record(evt.Delta, evt.Category);

        /// <summary>
        /// False on a replica client: the host owns the clock and
        /// replicates day/phase, so a client must NOT tick its own.
        /// Otherwise four peers advance four independent clocks off four
        /// different deltaTimes and the day rolls over at four different
        /// moments. The behaviour is still created and registered on every
        /// peer, because Save and every phase reader expect to find it in
        /// GameContext -- it just stops simulating. Defaults true so
        /// single-player is unchanged.
        /// </summary>
        public bool SimulateLocally { get; set; } = true;

        void Update()
        {
            if (!_bound || !SimulateLocally) return;

            _clock.Tick(Time.deltaTime, _cfg);

            if (_clock.Day != _lastSeenDay)
            {
                // The day that just ENDED is the pre-tick _lastSeenDay, not
                // the post-tick _clock.Day -- see the class doc comment's
                // DAY-BOUNDARY ORDERING note.
                var endedDay = _lastSeenDay;
                var report = DailyReportBuilder.Build(endedDay, _today.Snapshot(), _cfg);

                _today.Reset();
                _lastSeenDay = _clock.Day;

                // Published LAST, after the reset: a handler that moves cash
                // must land in the NEW day's bucket.
                _events.Publish(new DayEnded(endedDay, report));
            }
        }

        void OnDestroy() => DisposeSubscriptions();

        void DisposeSubscriptions()
        {
            foreach (var sub in _subscriptions) sub.Dispose();
            _subscriptions.Clear();
        }

        /// <summary>Delegates to DayClock.OpenRestaurant(), then publishes RestaurantOpened. No-op while unbound.</summary>
        public void OpenRestaurant()
        {
            if (!_bound) return;
            _clock.OpenRestaurant();
            _events.Publish(new RestaurantOpened());
        }

        /// <summary>Delegates to DayClock.CloseRestaurant(), then publishes RestaurantClosed. No-op while unbound.</summary>
        public void CloseRestaurant()
        {
            if (!_bound) return;
            _clock.CloseRestaurant();
            _events.Publish(new RestaurantClosed());
        }

        /// <summary>ISaveParticipant. RestoreOrder mirrors InstallOrder.DayCycle.</summary>
        public int RestoreOrder => InstallOrder.DayCycle;

        public void Capture(SaveFile save)
        {
            if (!_bound) return;
            save.world.day = _clock.Day;
            save.world.timeOfDaySeconds = _clock.TimeOfDaySeconds;
            save.world.phase = _clock.Phase.ToString();
        }

        public void Restore(SaveFile save, IGameDatabase db)
        {
            if (!_bound) return;

            var phase = (DayPhase)Enum.Parse(typeof(DayPhase), save.world.phase);
            _clock.RestoreState(save.world.day, save.world.timeOfDaySeconds, phase);

            // Resync so Update() doesn't mistake "loaded a different day
            // than whatever _lastSeenDay was before this load" for a
            // same-session day-wrap and fire a spurious DayEnded.
            _lastSeenDay = _clock.Day;

            // A load is "morning of day N" (architecture.md §4 decision 1):
            // whatever the pre-load session had booked toward an in-progress
            // day is not this restored day's activity, so the accumulator
            // starts clean. This also discards the reconciliation traffic a
            // restore itself generates -- EconomyService.Restore applies
            // (savedCash - currentCash) as a transaction, which the
            // accumulator already ignores by category, but resetting here
            // means the result does not depend on participant ordering.
            _today.Reset();
        }
    }

    /// <summary>
    /// Plain-C# [AutoInstall] factory for RestaurantStateServiceBehaviour --
    /// see that class's SPLIT DESIGN doc comment for why the MonoBehaviour
    /// itself cannot be the type GameBootstrap's reflection scan discovers.
    /// </summary>
    [AutoInstall]
    public sealed class RestaurantStateService : IInstaller
    {
        /// <summary>Matches DayClock's own "a day always starts in Prep" framing -- day 1 is the vertical slice's first day.</summary>
        public const int StartDay = 1;

        public int Order => InstallOrder.DayCycle;

        public void Install(GameContext ctx)
        {
            if (!ctx.TryGet<IBalanceConfig>(out var cfg))
            {
                Debug.LogWarning("[RestaurantStateService] No IBalanceConfig registered yet -- skipping RestaurantStateServiceBehaviour creation. Day cycle will be unavailable in this scene until content is wired in.");
                return;
            }

            var host = new GameObject(nameof(RestaurantStateServiceBehaviour));
            var behaviour = host.AddComponent<RestaurantStateServiceBehaviour>();
            behaviour.Bind(StartDay, cfg, ctx.Events);

            // Registered on every peer (Save and phase readers expect it),
            // but only the authority ticks it -- see SimulateLocally.
            if (ctx.TryGet<ISimulationAuthority>(out var authority) && !authority.IsSimulationAuthority)
            {
                behaviour.SimulateLocally = false;
            }

            ctx.Register<RestaurantStateServiceBehaviour>(behaviour);

            if (ctx.TryGet<SaveParticipantRegistry>(out var saveRegistry))
            {
                saveRegistry.Register(behaviour);
            }
        }
    }
}
