using System;
using Pho.Core;
using Pho.Domain.Contracts;
using Pho.Domain.DayCycle;
using Pho.Domain.Events;
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
    /// DAILYREPORT PLACEHOLDER: a real DailyReport-building system
    /// (revenue, ingredient cost, rent, profit) does not exist yet -- that's
    /// explicitly a later wave's deliverable per architecture.md's M11 row.
    /// The DayEnded published here carries a DailyReport with only
    /// <c>Day</c> populated; every decimal field is left at its default
    /// (0m). This keeps DayEnded's frozen signature satisfied today without
    /// fabricating numbers that don't exist yet.
    /// </summary>
    public sealed class RestaurantStateServiceBehaviour : MonoBehaviour
    {
        DayClock _clock;
        IBalanceConfig _cfg;
        IEventBus _events;
        bool _bound;
        int _lastSeenDay;

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
            _bound = true;
        }

        void Update()
        {
            if (!_bound) return;

            _clock.Tick(Time.deltaTime, _cfg);

            if (_clock.Day != _lastSeenDay)
            {
                _lastSeenDay = _clock.Day;

                // Placeholder DailyReport -- see class doc comment's
                // DAILYREPORT PLACEHOLDER note. Money fields default to 0m.
                var report = new DailyReport { Day = _clock.Day };
                _events.Publish(new DayEnded(_clock.Day, report));
            }
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

            ctx.Register<RestaurantStateServiceBehaviour>(behaviour);
        }
    }
}
