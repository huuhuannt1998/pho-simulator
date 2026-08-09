using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Pho.Core.Economy;
using Pho.Core.Orders;
using Pho.Domain.Contracts;
using Pho.Domain.Customers;
using Pho.Domain.Events;
using Pho.Domain.Identity;
using Pho.Domain.Infra;
using Pho.Domain.Orders;

namespace Pho.Customers
{
    /// <summary>
    /// Unity-side adapter for the pure `CustomerBrain` (architecture.md
    /// §6.2). Owns a `NavMeshAgent`, ticks the brain every frame, and
    /// implements every `ICustomerWorld` member the brain drives the world
    /// through. `CustomerBrain`/`ICustomerWorld`/`SeatHandle`/`ServedDish`/
    /// `CustomerState` are being authored concurrently by a sibling agent in
    /// a different worktree per the frozen §6.2 spec -- this file is written
    /// against that exact spec's member names/signatures but cannot see the
    /// sibling's actual source, so it will not compile until that lands and
    /// is merged. A separate integration pass is expected to do that
    /// compile/wire/playtest step.
    ///
    /// NAMESPACE ASSUMPTION: the `using Pho.Domain.Customers;` above follows
    /// this codebase's established one-namespace-per-Domain-folder
    /// convention (Domain/Orders -&gt; Pho.Domain.Orders, Domain/Satisfaction
    /// -&gt; Pho.Domain.Satisfaction, etc.) applied to architecture.md's
    /// "Customer state machine" section. If the sibling agent lands the
    /// types under a different namespace, this is a one-line `using` fix
    /// for the integration pass, not a design problem.
    ///
    /// SEAT-HANDLE INTEGRATION GAP (read before touching TryClaimSeat /
    /// ReleaseSeat / PlaceOrder): `SeatHandle`'s fields/constructor are
    /// owned by the sibling agent and not visible here, so this class
    /// cannot construct a real `SeatHandle` value. `TryClaimSeat` below
    /// assigns `seat = default;` as a placeholder and documents this
    /// in-line. Because of that, this class does NOT trust the `SeatHandle`
    /// value it receives back on `ReleaseSeat`/`PlaceOrder` to identify
    /// which seat is being released/ordered against -- `default(SeatHandle)`
    /// looks the same for every customer. Instead, `CustomerAgent` tracks
    /// its own currently-claimed `TableRegistry.SeatId` as private instance
    /// state and uses THAT to release the seat. This is safe under the
    /// architecture's stated M7 simplification "one customer per seat, no
    /// groups" -- each `CustomerAgent` instance represents exactly one
    /// customer holding at most one seat at a time, so there's no ambiguity
    /// to resolve. Flagged prominently again in the final report.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class CustomerAgent : MonoBehaviour, ICustomerWorld
    {
        [Header("Navigation")]
        [Tooltip("Auto-resolved via GetComponent<NavMeshAgent>() in Awake if left empty.")]
        [SerializeField] NavMeshAgent navAgent;

        [Header("World anchors (assign in Inspector once the dining-room scene exists)")]
        [Tooltip("STAND-IN: where WalkingToEntrance/Queuing walk to. No dining-room scene content exists yet -- assign once it does.")]
        [SerializeField] Transform entranceTransform;
        [Tooltip("STAND-IN: where Leaving/LeavingAngry walk to before Despawn.")]
        [SerializeField] Transform exitTransform;

        [Header("Stand-ins for services that don't exist yet (later waves)")]
        [Tooltip("Backing field for the restaurant open/closed signal. Now kept in sync with the real RestaurantOpened/RestaurantClosed events (see Bind's subscription below) once bound; still serialized as the initial value / manual-override seam for a customer spawned before the day-cycle service publishes its first event, or for testing without one.")]
        [SerializeField] bool simulateRestaurantOpen = true;
        [Tooltip("STAND-IN for a real cleanliness system (architecture.md §6.2/§12 reduces cleanliness to a single float, but no producer of that float exists in Unity yet). Serialized so it can be tuned per-instance for now.")]
        [SerializeField, Range(0f, 1f)] float cleanliness01Stub = 1f;

        [Header("Debug")]
        [Tooltip("Updated every Tick with the brain's current CustomerState. \"Expose debug state text over each customer while developing\" -- a plain string field, not a floating UI widget, per this pass's scope.")]
        [SerializeField] string debugStateLabel = "(unbound)";

        CustomerBrain _brain;
        TableRegistry _tableRegistry;
        IBalanceConfig _cfg;
        IEventBus _events;
        ICustomerArchetype _archetype;
        IRandom _rng;
        CustomerId _customerId;
        bool _bound;

        // Injected by Bind(...) -- see the OrderService parameter's doc
        // comment there. Null-checked at every call site below so an
        // unbound customer degrades gracefully instead of NRE-ing.
        OrderService _orderService;

        // Injected by Bind(...). Same null-safe convention as _orderService
        // above. Wired here (integration pass) now that EconomyService
        // exists -- see Pay() below, previously a documented stub pending
        // exactly this.
        EconomyService _economyService;

        // See the SEAT-HANDLE INTEGRATION GAP note in the class doc comment.
        TableRegistry.SeatId _claimedSeatId = TableRegistry.SeatId.Invalid;
        bool _hasClaimedSeat;

        // Restaurant-open-state event subscriptions (RestaurantOpened/
        // RestaurantClosed, see Bind below), disposed in OnDestroy. Follows
        // the same IDisposable-subscription-list idiom as UI/Presenters/*
        // (e.g. CashPresenter) elsewhere in this codebase.
        readonly List<IDisposable> _subscriptions = new List<IDisposable>();

        /// <summary>Manual override / initial-value seam for restaurant open-closed (see field tooltip). Kept for tests and for a customer spawned before the first real RestaurantOpened/RestaurantClosed event -- once Bind() runs, real events keep this in sync going forward, but this setter still works as a manual override in between events.</summary>
        public bool SimulateRestaurantOpen
        {
            get => simulateRestaurantOpen;
            set => simulateRestaurantOpen = value;
        }

        public string DebugStateLabel => debugStateLabel;

        /// <summary>
        /// Injection seam, mirroring `PlayerInteractor.Bind`/the Kitchen
        /// station pattern: constructs the `CustomerBrain` with a
        /// freshly-minted `CustomerId` and stores everything `ICustomerWorld`
        /// needs. Safe to call once per spawn (typically right after
        /// `Instantiate`, by `CustomerSpawner`).
        /// </summary>
        /// <param name="orderService">
        /// Optional (M8) -- reaches the real `Pho.Core.Orders.OrderService`
        /// so `PlaceOrder`/`TryTakeServedDish` are backed by a real order
        /// pipeline instead of the old stub-with-log bodies. Appended as the
        /// last parameter (rather than inserted mid-list) to keep this
        /// signature change additive/mergeable if another agent is
        /// concurrently adding its own trailing Bind parameter in this same
        /// wave. Null-safe: an unbound customer just logs and no-ops, same
        /// degrade-gracefully convention as every other stand-in in this
        /// class.
        /// </param>
        /// <param name="economyService">
        /// Optional (integration pass, M8/M9) -- reaches the real
        /// `Pho.Core.Economy.EconomyService` so `Pay` credits real cash
        /// instead of only logging. Trailing/optional for the same
        /// mergeability reason as `orderService`. Null-safe.
        /// </param>
        public void Bind(TableRegistry registry, IBalanceConfig cfg, IEventBus events, ICustomerArchetype archetype, IRandom rng, OrderService orderService = null, EconomyService economyService = null)
        {
            _tableRegistry = registry;
            _cfg = cfg;
            _events = events;
            _archetype = archetype;
            _rng = rng ?? new SystemRandom();
            _orderService = orderService;
            _economyService = economyService;

            _customerId = new CustomerId(Guid.NewGuid().ToString());
            _brain = new CustomerBrain(_customerId, _archetype, _cfg, _events, _rng);
            _bound = true;
            debugStateLabel = _brain.State.ToString();

            // Real restaurant open/closed signal: replaces reads of the
            // simulateRestaurantOpen stand-in with the actual
            // RestaurantStateServiceBehaviour-published events (Core/DayCycle).
            // Extends this existing Bind(...) rather than adding a separate
            // BindRestaurantState(IEventBus) -- `events` is already a
            // parameter here, so this is the smaller, non-duplicating edit.
            _subscriptions.Add(events.Subscribe<RestaurantOpened>(_ => simulateRestaurantOpen = true));
            _subscriptions.Add(events.Subscribe<RestaurantClosed>(_ => simulateRestaurantOpen = false));
        }

        void Awake()
        {
            if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();
        }

        /// <summary>Unsubscribes every event subscription this instance registered (currently just RestaurantOpened/RestaurantClosed from Bind). Mirrors the IDisposable-subscription-list teardown idiom used by UI/Presenters/*.</summary>
        void OnDestroy()
        {
            foreach (var sub in _subscriptions) sub.Dispose();
            _subscriptions.Clear();
        }

        void Update()
        {
            if (!_bound || _brain == null) return;

            _brain.Tick(Time.deltaTime, this);
            debugStateLabel = _brain.State.ToString();
        }

        // ---------------------------------------------------------------
        // ICustomerWorld
        // ---------------------------------------------------------------

        /// <summary>REAL: delegates to simulateRestaurantOpen, which Bind() now keeps in sync with the real RestaurantOpened/RestaurantClosed events (Core/DayCycle's RestaurantStateServiceBehaviour) -- see field tooltip.</summary>
        public bool RestaurantIsOpen => simulateRestaurantOpen;

        /// <summary>REAL plumbing to TableRegistry, with a documented placeholder for the SeatHandle value itself -- see the class doc comment's SEAT-HANDLE INTEGRATION GAP section.</summary>
        public bool TryClaimSeat(CustomerId id, out SeatHandle seat)
        {
            if (_tableRegistry == null)
            {
                Debug.LogWarning($"[CustomerAgent] TryClaimSeat({id}) called with no TableRegistry bound -- treating as 'no seats available'.");
                seat = default;
                return false;
            }

            if (_hasClaimedSeat)
            {
                // The §6.2 state machine only ever holds one seat per
                // customer at a time; a second claim attempt while one is
                // already held would indicate a brain/world desync. Guard
                // rather than silently double-claim.
                Debug.LogWarning($"[CustomerAgent] TryClaimSeat({id}) called while a seat is already held -- refusing a second claim.");
                seat = default;
                return false;
            }

            if (!_tableRegistry.TryClaimSeat(out var seatId, out var worldPosition))
            {
                seat = default;
                return false;
            }

            _claimedSeatId = seatId;
            _hasClaimedSeat = true;

            // Load-bearing behavioral requirement from architecture.md §6.2:
            // a successful claim is expected to ALSO kick off the NavMesh
            // walk to the seat as a side effect -- CustomerBrain.Tick polls
            // HasArrived afterward without calling MoveTo again for this
            // specific WalkingToSeat transition.
            MoveTo(new Vec3(worldPosition.x, worldPosition.y, worldPosition.z));

            // INTEGRATION TODO: cannot construct a real Domain SeatHandle --
            // its constructor/fields are owned by a sibling agent's
            // concurrent, unseen work. `default` is a deliberate placeholder;
            // see the class doc comment's SEAT-HANDLE INTEGRATION GAP for why
            // this is still safe (this class never trusts the SeatHandle
            // value it gets back -- it tracks `_claimedSeatId` itself).
            // Replace once the real SeatHandle type is merged, if it turns
            // out to need to carry information this class doesn't already
            // have from `_claimedSeatId`.
            seat = default;
            return true;
        }

        /// <summary>REAL plumbing to TableRegistry -- ignores the (placeholder) `seat` value per the SEAT-HANDLE INTEGRATION GAP; releases whatever seat THIS customer is actually holding.</summary>
        public void ReleaseSeat(SeatHandle seat)
        {
            if (!_hasClaimedSeat) return;

            _tableRegistry?.ReleaseSeat(_claimedSeatId);
            _hasClaimedSeat = false;
            _claimedSeatId = TableRegistry.SeatId.Invalid;
        }

        /// <summary>REAL: converts the pure Vec3 to a Unity Vector3 and drives the NavMeshAgent.</summary>
        public void MoveTo(Vec3 destination)
        {
            if (navAgent == null)
            {
                Debug.LogWarning("[CustomerAgent] MoveTo called with no NavMeshAgent.");
                return;
            }

            var dest = new Vector3(destination.X, destination.Y, destination.Z);

            if (!navAgent.isOnNavMesh)
            {
                Debug.LogWarning($"[CustomerAgent] MoveTo({dest}) called but the NavMeshAgent isn't on a baked NavMesh yet -- ignoring.");
                return;
            }

            navAgent.SetDestination(dest);
        }

        /// <summary>
        /// REAL. Guards the standard Unity gotcha: `remainingDistance` is
        /// unreliable for one frame right after `SetDestination` while the
        /// path is still being computed, so `pathPending` is checked first.
        /// </summary>
        public bool HasArrived
        {
            get
            {
                if (navAgent == null) return true; // nothing to wait on -- don't block the state machine on a missing agent.
                if (!navAgent.isOnNavMesh) return false;
                if (navAgent.pathPending) return false;
                return navAgent.remainingDistance <= navAgent.stoppingDistance;
            }
        }

        /// <summary>
        /// Runtime wiring for entranceTransform/exitTransform -- these are
        /// per-instance scene Transforms (see the field tooltips) that
        /// cannot be baked into the shared Customer prefab, so a spawner
        /// (CustomerSpawner) is expected to call this right after
        /// Instantiate. Either argument may be null (falls back to the
        /// existing Vec3.Zero + warn-once behavior in ResolveAnchor).
        /// </summary>
        public void SetWorldAnchors(Transform entrance, Transform exit)
        {
            entranceTransform = entrance;
            exitTransform = exit;
        }

        /// <summary>REAL (once `entranceTransform` is assigned, e.g. via SetWorldAnchors) -- returns Vec3.Zero and warns once if it isn't.</summary>
        public Vec3 EntrancePosition => ResolveAnchor(entranceTransform, ref _warnedNoEntrance, "entranceTransform");

        /// <summary>REAL (once `exitTransform` is assigned, e.g. via SetWorldAnchors) -- returns Vec3.Zero and warns once if it isn't.</summary>
        public Vec3 ExitPosition => ResolveAnchor(exitTransform, ref _warnedNoExit, "exitTransform");

        bool _warnedNoEntrance, _warnedNoExit;

        Vec3 ResolveAnchor(Transform t, ref bool warned, string fieldName)
        {
            if (t != null) return new Vec3(t.position.x, t.position.y, t.position.z);

            if (!warned)
            {
                Debug.LogWarning($"[CustomerAgent] '{fieldName}' is unassigned -- no dining-room scene content exists yet. Returning Vec3.Zero until it's assigned in the Inspector.");
                warned = true;
            }

            return Vec3.Zero;
        }

        /// <summary>STAND-IN: no cleanliness system exists yet (architecture.md §12 reduces it to one float, but nothing produces it in Unity yet). Returns the serialized stub field.</summary>
        public float Cleanliness01 => cleanliness01Stub;

        /// <summary>
        /// REAL (M8): delegates to `Pho.Core.Orders.OrderService.CreateOrder`.
        /// Falls back to a logged no-op (returns `default`) if no
        /// OrderService is bound, same degrade-gracefully convention as the
        /// rest of this class's stand-ins.
        /// </summary>
        public OrderId PlaceOrder(CustomerId id, SeatHandle seat, RecipeId recipe, OrderModifiers mods)
        {
            if (_orderService == null)
            {
                Debug.LogWarning($"[CustomerAgent] PlaceOrder({id}, recipe={recipe}) called with no OrderService bound -- no order created.");
                return default;
            }

            // SEAT-HANDLE INTEGRATION GAP (see class doc comment): `seat` is
            // currently always `default` because TryClaimSeat above cannot
            // construct a real Domain SeatHandle. Falls back to this
            // customer's own tracked TableRegistry seat -- see
            // ResolveOwnTableId -- so the order still carries a meaningful
            // table id while that gap persists; a real, non-empty
            // `seat.TableId` (once the gap closes) takes priority.
            string tableId = !seat.IsEmpty ? seat.TableId : ResolveOwnTableId();

            return _orderService.CreateOrder(id, tableId, recipe, mods, Time.time);
        }

        /// <summary>Best-effort table id for the order while the SEAT-HANDLE INTEGRATION GAP persists (see PlaceOrder). Falls back to a placeholder string, never null.</summary>
        string ResolveOwnTableId()
        {
            if (_hasClaimedSeat && _tableRegistry != null && _tableRegistry.TryGetTableId(_claimedSeatId, out var tableId))
                return tableId;

            return "table.unknown";
        }

        /// <summary>REAL (M8): delegates to `Pho.Core.Orders.OrderService.TryTakeServedDish`. Reports not-ready (never throws) if no OrderService is bound.</summary>
        public bool TryTakeServedDish(OrderId order, out ServedDish dish)
        {
            if (_orderService == null)
            {
                dish = default;
                return false;
            }

            return _orderService.TryTakeServedDish(order, out dish);
        }

        /// <summary>
        /// REAL (integration pass): credits the bill + tip into the real
        /// `Pho.Core.Economy.EconomyService` as one `LedgerCategory.Sale`
        /// transaction. Was a documented stub pending exactly this wiring --
        /// `OrderService` (this file's other consumer) and `EconomyService`
        /// were built concurrently by two agents that couldn't see each
        /// other's code, so this connection was deferred to the integration
        /// step, per both classes' doc comments. Degrades to a log (never
        /// throws) if no `EconomyService` is bound, or if there's nothing to
        /// credit (amount + tip &lt;= 0 -- `EconomyService.Credit` itself
        /// rejects a non-positive amount as a programmer error, so this
        /// guards that instead of letting a legitimate "customer left
        /// angry, paid nothing" case throw).
        /// </summary>
        public void Pay(CustomerId id, decimal amount, decimal tip)
        {
            decimal total = amount + tip;

            if (_economyService == null)
            {
                Debug.LogWarning($"[CustomerAgent] Pay({id}, amount={amount:0.00}, tip={tip:0.00}) called with no EconomyService bound -- no cash credited.");
                return;
            }

            if (total <= 0m)
            {
                Debug.Log($"[CustomerAgent] Pay({id}): nothing to credit (amount={amount:0.00}, tip={tip:0.00}).");
                return;
            }

            _economyService.Credit(total, LedgerCategory.Sale);
        }

        /// <summary>REAL-ish: releases any held seat, then deactivates the GameObject. Not real pooling (out of scope for this pass) -- forward-thinking enough to make pooling a drop-in later, per architecture.md §12's `ICustomerFactory` note.</summary>
        public void Despawn()
        {
            if (_hasClaimedSeat)
            {
                _tableRegistry?.ReleaseSeat(_claimedSeatId);
                _hasClaimedSeat = false;
                _claimedSeatId = TableRegistry.SeatId.Invalid;
            }

            Debug.Log($"[CustomerAgent] Despawning customer {_customerId}.");
            gameObject.SetActive(false);
        }
    }
}
