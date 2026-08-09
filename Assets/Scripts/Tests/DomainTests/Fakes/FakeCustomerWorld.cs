using System.Collections.Generic;
using Pho.Domain.Customers;
using Pho.Domain.Identity;
using Pho.Domain.Infra;
using Pho.Domain.Orders;

namespace Pho.Domain.Tests.Fakes
{
    /// <summary>
    /// Scriptable <see cref="ICustomerWorld"/> fake for <c>CustomerBrainTests</c>
    /// -- new file, does not touch the shared <c>Fakes.cs</c>. Every method
    /// records its calls so tests can assert on what <see cref="CustomerBrain"/>
    /// did to the world, and every "does this succeed yet" method is
    /// scriptable via an attempts-remaining counter so tests can simulate
    /// "seat becomes available after N calls" / "dish becomes ready after M
    /// calls" without any real timing.
    /// </summary>
    public sealed class FakeCustomerWorld : ICustomerWorld
    {
        public bool RestaurantIsOpen { get; set; } = true;
        public bool HasArrived { get; set; } = true;
        public Vec3 EntrancePosition { get; set; } = new Vec3(0f, 0f, 10f);
        public Vec3 ExitPosition { get; set; } = new Vec3(0f, 0f, -10f);
        public float Cleanliness01 { get; set; } = 0.8f;

        public readonly List<Vec3> MoveToCalls = new List<Vec3>();

        // TryClaimSeat scripting -- fails ClaimAttemptsRemaining times, then
        // succeeds; AllowSeatClaim = false makes it fail forever (seat never
        // becomes available). Only one seat may be claimed at a time.
        public bool AllowSeatClaim { get; set; } = true;
        public int ClaimAttemptsRemaining { get; set; } = 0;
        public SeatHandle SeatToClaim { get; set; } = new SeatHandle("tbl.1", 0);
        public int ClaimAttemptCount { get; private set; }
        public bool SeatCurrentlyClaimed { get; private set; }
        public int ReleaseCallCount { get; private set; }
        public SeatHandle? LastReleasedSeat { get; private set; }

        public readonly List<(CustomerId Id, SeatHandle Seat, RecipeId Recipe, OrderModifiers Mods)> PlaceOrderCalls =
            new List<(CustomerId, SeatHandle, RecipeId, OrderModifiers)>();
        public OrderId OrderIdToReturn { get; set; } = new OrderId("ord.fake1");

        // TryTakeServedDish scripting -- same "N failures then succeed" shape
        // as TryClaimSeat; AllowServeDish = false means food never arrives.
        public bool AllowServeDish { get; set; } = true;
        public int TakeDishAttemptsRemaining { get; set; } = 0;
        public ServedDish DishToServe { get; set; }
        public int TakeDishAttemptCount { get; private set; }

        public readonly List<(CustomerId Id, decimal Amount, decimal Tip)> PayCalls =
            new List<(CustomerId, decimal, decimal)>();

        public bool Despawned { get; private set; }

        public bool TryClaimSeat(CustomerId id, out SeatHandle seat)
        {
            ClaimAttemptCount++;

            if (!AllowSeatClaim || SeatCurrentlyClaimed)
            {
                seat = SeatHandle.None;
                return false;
            }

            if (ClaimAttemptsRemaining > 0)
            {
                ClaimAttemptsRemaining--;
                seat = SeatHandle.None;
                return false;
            }

            seat = SeatToClaim;
            SeatCurrentlyClaimed = true;
            return true;
        }

        public void ReleaseSeat(SeatHandle seat)
        {
            ReleaseCallCount++;
            LastReleasedSeat = seat;
            SeatCurrentlyClaimed = false;
        }

        public void MoveTo(Vec3 destination) => MoveToCalls.Add(destination);

        public OrderId PlaceOrder(CustomerId id, SeatHandle seat, RecipeId recipe, OrderModifiers mods)
        {
            PlaceOrderCalls.Add((id, seat, recipe, mods));
            return OrderIdToReturn;
        }

        public bool TryTakeServedDish(OrderId order, out ServedDish dish)
        {
            TakeDishAttemptCount++;

            if (!AllowServeDish)
            {
                dish = default;
                return false;
            }

            if (TakeDishAttemptsRemaining > 0)
            {
                TakeDishAttemptsRemaining--;
                dish = default;
                return false;
            }

            dish = DishToServe;
            return true;
        }

        public void Pay(CustomerId id, decimal amount, decimal tip) => PayCalls.Add((id, amount, tip));

        public void Despawn() => Despawned = true;
    }
}
