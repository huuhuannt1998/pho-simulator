using System;
using UnityEngine;
using UnityEngine.AI;
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
        [Tooltip("STAND-IN for a real RestaurantState/day-cycle open-closed signal -- no such service exists in Unity yet (later-wave work per architecture.md §9 M6/M11). A later wiring pass should replace reads of this with a real binding.")]
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

        // See the SEAT-HANDLE INTEGRATION GAP note in the class doc comment.
        TableRegistry.SeatId _claimedSeatId = TableRegistry.SeatId.Invalid;
        bool _hasClaimedSeat;

        /// <summary>Public stand-in for restaurant open/closed (see field tooltip). A later wave replaces reads of this with a real RestaurantState binding.</summary>
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
        public void Bind(TableRegistry registry, IBalanceConfig cfg, IEventBus events, ICustomerArchetype archetype, IRandom rng)
        {
            _tableRegistry = registry;
            _cfg = cfg;
            _events = events;
            _archetype = archetype;
            _rng = rng ?? new SystemRandom();

            _customerId = new CustomerId(Guid.NewGuid().ToString());
            _brain = new CustomerBrain(_customerId, _archetype, _cfg, _events, _rng);
            _bound = true;
            debugStateLabel = _brain.State.ToString();
        }

        void Awake()
        {
            if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();
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

        /// <summary>REAL (delegates to the serialized stand-in field above -- see field tooltip for why it's a stand-in, not a stub-with-log).</summary>
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

        /// <summary>REAL (once `entranceTransform` is assigned) -- returns Vec3.Zero and warns once if it isn't.</summary>
        public Vec3 EntrancePosition => ResolveAnchor(entranceTransform, ref _warnedNoEntrance, "entranceTransform");

        /// <summary>REAL (once `exitTransform` is assigned) -- returns Vec3.Zero and warns once if it isn't.</summary>
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

        /// <summary>STUBBED-WITH-LOG: no OrderService/order board exists yet (M8/M9, later wave). Logs the intended order and returns a default OrderId so the brain's state machine can proceed without a real order pipeline.</summary>
        public OrderId PlaceOrder(CustomerId id, SeatHandle seat, RecipeId recipe, OrderModifiers mods)
        {
            Debug.Log($"[CustomerAgent] STUB PlaceOrder: customer={id} recipe={recipe} noOnion={mods.NoOnion} extraMeat={mods.ExtraMeat} size={mods.Size} spice={mods.Spice}. No OrderService wired yet -- would create a real order here.");
            return default;
        }

        /// <summary>STUBBED-WITH-LOG: no kitchen-to-customer serving pipeline exists yet. Always reports "nothing ready" so WaitingForFood keeps polling harmlessly instead of fabricating a fake dish.</summary>
        public bool TryTakeServedDish(OrderId order, out ServedDish dish)
        {
            Debug.Log($"[CustomerAgent] STUB TryTakeServedDish: order={order}. No serving pipeline wired yet -- always reporting not-ready.");
            dish = default;
            return false;
        }

        /// <summary>STUBBED-WITH-LOG: no EconomyService/Ledger is reachable from Pho.Customers yet. Logs the intended cash event.</summary>
        public void Pay(CustomerId id, decimal amount, decimal tip)
        {
            Debug.Log($"[CustomerAgent] STUB Pay: customer={id} amount={amount:0.00} tip={tip:0.00}. No EconomyService wired yet -- would apply a real cash/ledger change here.");
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
