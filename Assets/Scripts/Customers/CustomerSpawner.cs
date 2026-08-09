using System;
using System.Collections.Generic;
using UnityEngine;
using Pho.Core.Economy;
using Pho.Core.Orders;
using Pho.Core.Restaurant;
using Pho.Domain.Contracts;
using Pho.Domain.Events;
using Pho.Domain.Infra;
using Pho.Domain.Multiplayer;

namespace Pho.Customers
{
    /// <summary>
    /// Spawns `CustomerAgent` instances at a configurable interval and binds
    /// each one to the same `TableRegistry`/config/event bus/randomness this
    /// spawner itself was bound with (mirrors `PlayerInteractor.Bind` /
    /// the Kitchen-station injection pattern).
    ///
    /// Archetype source: the architecture doc allows either "a serialized
    /// list, or IGameDatabase.Archetypes". This class only supports the
    /// `IGameDatabase` path -- `ICustomerArchetype` is a Domain *interface*,
    /// not a concrete Unity type, so it isn't Inspector-serializable, and
    /// the concrete `Pho.Data.CustomerArchetype` ScriptableObject that
    /// implements it lives in an assembly (`Pho.Data`) that
    /// `Pho.Customers.asmdef` does not reference (by design -- not touched
    /// here). `IGameDatabase`/`ICustomerArchetype` are both pure
    /// `Pho.Domain` types, so binding a database reference needs no asmdef
    /// change.
    ///
    /// Per the Content agent's current report the generated `GameDatabase`
    /// has ZERO archetypes -- this is handled as a normal, expected,
    /// non-crashing "nothing to spawn yet" case, not an error.
    /// </summary>
    public sealed class CustomerSpawner : MonoBehaviour
    {
        [Header("Prefab (assign once a CustomerAgent prefab exists)")]
        [Tooltip("No CustomerAgent prefab exists in the project yet. Leave unassigned until scene/prefab-building work creates one -- the spawner logs a warning and spawns nothing rather than crashing.")]
        [SerializeField] CustomerAgent customerPrefab;

        [Header("Spawn point")]
        [Tooltip("Where new customers appear. Falls back to this GameObject's own position if unassigned.")]
        [SerializeField] Transform spawnPoint;

        [Header("World anchors (forwarded onto each spawned CustomerAgent)")]
        [Tooltip("Where WalkingToEntrance/Queuing walk to. Forwarded onto each spawned CustomerAgent via SetWorldAnchors, since these are per-instance and can't be baked into the shared Customer prefab.")]
        [SerializeField] Transform entranceTransform;
        [Tooltip("Where Leaving/LeavingAngry walk to before Despawn. Same forwarding as entranceTransform.")]
        [SerializeField] Transform exitTransform;

        [Header("Timing")]
        [SerializeField] float spawnIntervalSeconds = 20f;

        TableRegistry _tableRegistry;
        IBalanceConfig _cfg;
        IEventBus _events;
        IGameDatabase _database;
        IRandom _rng;
        bool _bound;
        float _timer;

        // Forwarded onto every spawned CustomerAgent -- see
        // CustomerAgent.Bind's orderService/economyService doc comments.
        // Optional/trailing for the same mergeability reason as there.
        OrderService _orderService;
        EconomyService _economyService;
        CleanlinessService _cleanlinessService;

        // Customers must only arrive while the restaurant is OPEN.
        // Without this gate the spawner ran purely on its timer, so
        // customers walked in during Prep while the player was still
        // simmering broth (~100s) -- they would burn their 45-75s patience
        // and leave angry before it was even possible to serve them, which
        // reads as a broken game rather than a hard one. Mirrors how
        // CustomerAgent already tracks the same two events.
        readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        bool _restaurantOpen;

        bool _warnedNoPrefab;
        bool _warnedNoArchetypes;

        /// <summary>
        /// Injection seam, same pattern as `CustomerAgent.Bind` /
        /// `PlayerInteractor.Bind`. `database` is optional -- if null (or
        /// its `Archetypes` list is empty), spawning is skipped with a
        /// warning rather than throwing. `orderService`/`economyService` are
        /// forwarded verbatim to every spawned `CustomerAgent.Bind` call --
        /// see that method's doc comments for what a null value degrades to.
        /// </summary>
        public void Bind(TableRegistry registry, IBalanceConfig cfg, IEventBus events, IGameDatabase database, IRandom rng, OrderService orderService = null, EconomyService economyService = null, CleanlinessService cleanlinessService = null)
        {
            _tableRegistry = registry;
            _cfg = cfg;
            _events = events;
            _database = database;
            _rng = rng ?? new SystemRandom();
            _orderService = orderService;
            _economyService = economyService;
            _cleanlinessService = cleanlinessService;
            _bound = true;
            _timer = 0f;

            // Idempotent re-bind, same reasoning as
            // RestaurantStateServiceBehaviour.Bind: never stack duplicate
            // subscriptions.
            DisposeSubscriptions();
            if (_events != null)
            {
                _subscriptions.Add(_events.Subscribe<RestaurantOpened>(_ => OnRestaurantOpened()));
                _subscriptions.Add(_events.Subscribe<RestaurantClosed>(_ => _restaurantOpen = false));
            }
        }

        void OnRestaurantOpened()
        {
            _restaurantOpen = true;

            // Reset so the first customer arrives a full interval after
            // opening, rather than instantly because the timer had been
            // free-running through Prep.
            _timer = 0f;
        }

        void OnDestroy() => DisposeSubscriptions();

        void DisposeSubscriptions()
        {
            foreach (var sub in _subscriptions) sub.Dispose();
            _subscriptions.Clear();
        }

        /// <summary>
        /// Set false on a replica client. Customers are spawned by the host
        /// and replicated; a client that also spawned would fill the
        /// restaurant with a second, invisible-to-everyone-else crowd.
        /// Defaults true so single-player is unaffected.
        /// </summary>
        public bool SimulateLocally { get; set; } = true;

        void Update()
        {
            if (!_bound || !SimulateLocally) return;

            // No arrivals while closed -- see the _restaurantOpen field note.
            if (!_restaurantOpen) return;

            _timer += Time.deltaTime;
            if (_timer < spawnIntervalSeconds) return;

            _timer = 0f;
            TrySpawn();
        }

        void TrySpawn()
        {
            if (customerPrefab == null)
            {
                if (!_warnedNoPrefab)
                {
                    Debug.LogWarning("[CustomerSpawner] No CustomerAgent prefab assigned -- skipping spawns until one is set in the Inspector.");
                    _warnedNoPrefab = true;
                }
                return;
            }

            IReadOnlyList<ICustomerArchetype> archetypes = _database?.Archetypes;
            if (archetypes == null || archetypes.Count == 0)
            {
                if (!_warnedNoArchetypes)
                {
                    Debug.LogWarning("[CustomerSpawner] No customer archetypes available (IGameDatabase not bound, or its Archetypes list is empty -- the generated content currently has zero archetypes per the Content agent's report). Skipping spawns until archetypes exist.");
                    _warnedNoArchetypes = true;
                }
                return;
            }

            var archetype = PickArchetype(archetypes);
            var spawnPosition = spawnPoint != null ? spawnPoint.position : transform.position;
            var spawnRotation = spawnPoint != null ? spawnPoint.rotation : transform.rotation;

            var agent = Instantiate(customerPrefab, spawnPosition, spawnRotation);
            agent.SetWorldAnchors(entranceTransform, exitTransform);
            agent.Bind(_tableRegistry, _cfg, _events, archetype, _rng, _orderService, _economyService, _cleanlinessService);
        }

        /// <summary>Weighted pick by `ICustomerArchetype.SpawnWeight`. Falls back to a uniform pick if every weight is non-positive, so a bad content config never blocks spawning entirely.</summary>
        ICustomerArchetype PickArchetype(IReadOnlyList<ICustomerArchetype> archetypes)
        {
            float totalWeight = 0f;
            for (int i = 0; i < archetypes.Count; i++)
            {
                totalWeight += Mathf.Max(0f, archetypes[i].SpawnWeight);
            }

            if (totalWeight <= 0f)
            {
                return archetypes[_rng.NextInt(0, archetypes.Count)];
            }

            float roll = _rng.NextFloat(0f, totalWeight);
            float cumulative = 0f;
            for (int i = 0; i < archetypes.Count; i++)
            {
                cumulative += Mathf.Max(0f, archetypes[i].SpawnWeight);
                if (roll <= cumulative) return archetypes[i];
            }

            return archetypes[archetypes.Count - 1];
        }
    }
}
