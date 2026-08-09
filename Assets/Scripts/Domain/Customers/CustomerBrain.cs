using System;
using Pho.Domain.Contracts;
using Pho.Domain.Cooking;
using Pho.Domain.Events;
using Pho.Domain.Identity;
using Pho.Domain.Infra;
using Pho.Domain.Orders;
using Pho.Domain.Satisfaction;

namespace Pho.Domain.Customers
{
    /// <summary>
    /// Pure customer lifecycle state machine. Drives an injected
    /// <see cref="ICustomerWorld"/> through <see cref="CustomerState"/>;
    /// zero NavMesh, zero scene, zero frame timing -- fully unit-testable
    /// with a scripted fake world. Exact public shape (constructor, `State`,
    /// `Tick`, `Result`) frozen by architecture.md section 6.2.
    ///
    /// <b>WalkingToSeat position gap (load-bearing interpretation):</b>
    /// <see cref="ICustomerWorld"/> has no `GetSeatPosition(SeatHandle)`
    /// member -- the frozen interface cannot be patched here without risking
    /// a mismatch with the sibling agent's independently-authored
    /// `CustomerAgent`. Resolution taken: <see cref="ICustomerWorld.TryClaimSeat"/>
    /// is assumed to ALSO kick off the walk to the seat as a side effect on
    /// the real (Unity-side) implementation. `CustomerBrain` therefore does
    /// NOT call `MoveTo` again when entering `WalkingToSeat` -- it only polls
    /// `world.HasArrived`. `WalkingToEntrance` is the only state where
    /// `CustomerBrain` itself calls `MoveTo`, for `EntrancePosition`; the
    /// `Leaving`/`LeavingAngry` states call it again for `ExitPosition`.
    /// </summary>
    public sealed class CustomerBrain
    {
        // TODO(balance): architecture.md section 6.2 leaves menu-browsing
        // timing unspecified ("a real menu-browsing minigame is out of
        // scope"). IBalanceConfig has no field for this; short fixed
        // placeholder delays, same convention as other pure-logic
        // TODO(balance) constants in this codebase (SatisfactionCalculator,
        // QualityCalculator).
        const float SittingDurationSeconds = 1.0f;
        const float BrowsingMenuDurationSeconds = 2.0f;

        readonly CustomerId _id;
        readonly ICustomerArchetype _archetype;
        readonly IBalanceConfig _cfg;
        readonly IEventBus _bus;
        readonly IRandom _rng;

        float _stateTimer;
        bool _justEntered;

        float _patienceRemaining;

        bool _hasSeat;
        SeatHandle _seat;

        OrderId _orderId;

        ServedDish _servedDish;
        float _waitSeconds;

        float _eatDurationSeconds;

        public CustomerState State { get; private set; }

        /// <summary>Set once, on entering Paying (happy path) or LeavingAngry (patience exhausted).</summary>
        public SatisfactionResult? Result { get; private set; }

        public CustomerBrain(CustomerId id, ICustomerArchetype archetype, IBalanceConfig cfg, IEventBus bus, IRandom rng)
        {
            _id = id;
            _archetype = archetype ?? throw new ArgumentNullException(nameof(archetype));
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));

            State = CustomerState.Spawning;
            _patienceRemaining = _rng.NextFloat(_archetype.PatienceSeconds.min, _archetype.PatienceSeconds.max);
        }

        public void Tick(float dt, ICustomerWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            switch (State)
            {
                case CustomerState.Spawning:
                    SetState(CustomerState.WalkingToEntrance);
                    break;

                case CustomerState.WalkingToEntrance:
                    TickWalkingToEntrance(world);
                    break;

                case CustomerState.Queuing:
                    TickQueuing(dt, world);
                    break;

                case CustomerState.WalkingToSeat:
                    TickWalkingToSeat(world);
                    break;

                case CustomerState.Sitting:
                    TickSitting(dt);
                    break;

                case CustomerState.BrowsingMenu:
                    TickBrowsingMenu(dt);
                    break;

                case CustomerState.ReadyToOrder:
                    TickReadyToOrder(world);
                    break;

                case CustomerState.WaitingForFood:
                    TickWaitingForFood(dt, world);
                    break;

                case CustomerState.Eating:
                    TickEating(dt);
                    break;

                case CustomerState.Paying:
                    TickPaying(world);
                    break;

                case CustomerState.Leaving:
                case CustomerState.LeavingAngry:
                    TickLeaving(world);
                    break;

                case CustomerState.Despawned:
                    // Terminal. No further ticking.
                    break;
            }
        }

        void TickWalkingToEntrance(ICustomerWorld world)
        {
            if (_justEntered)
            {
                _justEntered = false;

                // Closed restaurant: park in the queue instead of walking to
                // a door that won't let anyone in yet (architecture.md
                // section 6.2's ordering judgment call).
                if (!world.RestaurantIsOpen)
                {
                    SetState(CustomerState.Queuing);
                    return;
                }

                world.MoveTo(world.EntrancePosition);
            }

            if (!world.HasArrived)
                return;

            if (world.TryClaimSeat(_id, out var seat))
            {
                _seat = seat;
                _hasSeat = true;
                SetState(CustomerState.WalkingToSeat);
            }
            else
            {
                SetState(CustomerState.Queuing);
            }
        }

        void TickQueuing(float dt, ICustomerWorld world)
        {
            _stateTimer += dt;
            _patienceRemaining -= dt * _cfg.PatienceDecayRate;

            if (_patienceRemaining <= 0f)
            {
                float waitSeconds = _stateTimer;
                SetState(CustomerState.LeavingAngry);
                FinalizeAngryResult(world, waitSeconds);
                return;
            }

            if (!world.RestaurantIsOpen)
                return;

            if (world.TryClaimSeat(_id, out var seat))
            {
                _seat = seat;
                _hasSeat = true;
                SetState(CustomerState.WalkingToSeat);
            }
        }

        void TickWalkingToSeat(ICustomerWorld world)
        {
            // Deliberately no MoveTo call here -- see the class-level doc
            // comment on the WalkingToSeat position gap. TryClaimSeat is
            // assumed to have already kicked off the walk on the real world.
            if (!world.HasArrived)
                return;

            SetState(CustomerState.Sitting);
        }

        void TickSitting(float dt)
        {
            _stateTimer += dt;
            if (_stateTimer >= SittingDurationSeconds)
                SetState(CustomerState.BrowsingMenu);
        }

        void TickBrowsingMenu(float dt)
        {
            _stateTimer += dt;
            if (_stateTimer >= BrowsingMenuDurationSeconds)
                SetState(CustomerState.ReadyToOrder);
        }

        void TickReadyToOrder(ICustomerWorld world)
        {
            var preferred = _archetype.PreferredRecipeIds;
            if (preferred == null || preferred.Count == 0)
            {
                // Known limitation, not a crash: an archetype with no
                // preferred recipes is a content/data problem. Graceful
                // degrade -- leave without ordering.
                SetState(CustomerState.Leaving);
                return;
            }

            var recipe = preferred[_rng.NextInt(0, preferred.Count)];
            _orderId = world.PlaceOrder(_id, _seat, recipe, OrderModifiers.Default);
            SetState(CustomerState.WaitingForFood);
        }

        void TickWaitingForFood(float dt, ICustomerWorld world)
        {
            _stateTimer += dt;
            _patienceRemaining -= dt * _cfg.PatienceDecayRate;

            if (world.TryTakeServedDish(_orderId, out var dish))
            {
                _servedDish = dish;
                _waitSeconds = _stateTimer;
                SetState(CustomerState.Eating);
                return;
            }

            if (_patienceRemaining <= 0f)
            {
                float waitSeconds = _stateTimer;
                SetState(CustomerState.LeavingAngry);
                FinalizeAngryResult(world, waitSeconds);
            }
        }

        void TickEating(float dt)
        {
            if (_justEntered)
            {
                _justEntered = false;
                _eatDurationSeconds = _rng.NextFloat(_archetype.EatDurationSeconds.min, _archetype.EatDurationSeconds.max);
            }

            _stateTimer += dt;
            if (_stateTimer >= _eatDurationSeconds)
                SetState(CustomerState.Paying);
        }

        void TickPaying(ICustomerWorld world)
        {
            // ExpectedPrice: the order's own quoted price isn't visible to
            // CustomerBrain (ICustomerWorld.PlaceOrder returns only an
            // OrderId) -- the archetype's Budget midpoint is the closest
            // available proxy for "what this customer expected to pay",
            // consistent with ICustomerArchetype.Budget's role elsewhere.
            decimal expectedPrice = (decimal)((_archetype.Budget.min + _archetype.Budget.max) * 0.5f);

            var record = new ServiceRecord(
                quality: _servedDish.Quality,
                accuracy01: _servedDish.Accuracy01,
                waitSeconds: _waitSeconds,
                cleanliness01: world.Cleanliness01,
                pricePaid: _servedDish.QuotedPrice,
                expectedPrice: expectedPrice);

            var result = SatisfactionCalculator.Evaluate(in record, _archetype, _cfg);
            Result = result;

            world.Pay(_id, _servedDish.QuotedPrice, result.Tip);
            ReleaseSeatIfHeld(world);

            SetState(CustomerState.Leaving);
        }

        void TickLeaving(ICustomerWorld world)
        {
            if (_justEntered)
            {
                _justEntered = false;
                world.MoveTo(world.ExitPosition);
            }

            if (!world.HasArrived)
                return;

            world.Despawn();
            State = CustomerState.Despawned;
        }

        /// <summary>
        /// A customer who ran out of patience (queuing or waiting for food)
        /// never received a real dish, so there is no real DishQuality to
        /// score. Chosen resolution (architecture.md section 6.2's explicit
        /// either/or): build a synthetic worst-case ServiceRecord (zero
        /// DishQuality, zero accuracy, zero price paid) and still route it
        /// through SatisfactionCalculator.Evaluate, rather than a bypassed/
        /// hardcoded result -- keeps Result always produced by the same
        /// code path (correct tier/tip/reputation-delta sign for "Angry",
        /// consistent with the rest of the pipeline) instead of a second,
        /// parallel way of computing satisfaction.
        /// </summary>
        void FinalizeAngryResult(ICustomerWorld world, float waitSeconds)
        {
            ReleaseSeatIfHeld(world);

            decimal expectedPrice = (decimal)((_archetype.Budget.min + _archetype.Budget.max) * 0.5f);

            var record = new ServiceRecord(
                quality: default(DishQuality),
                accuracy01: 0f,
                waitSeconds: waitSeconds,
                cleanliness01: world.Cleanliness01,
                pricePaid: 0m,
                expectedPrice: expectedPrice);

            Result = SatisfactionCalculator.Evaluate(in record, _archetype, _cfg);
        }

        void ReleaseSeatIfHeld(ICustomerWorld world)
        {
            if (!_hasSeat)
                return;

            world.ReleaseSeat(_seat);
            _hasSeat = false;
        }

        void SetState(CustomerState next)
        {
            State = next;
            _stateTimer = 0f;
            _justEntered = true;
        }
    }
}
