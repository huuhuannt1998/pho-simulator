using Pho.Domain.Identity;

namespace Pho.Domain.Events
{
    // The transition table (OrderStateMachine, M8) is not part of the M1
    // freeze, but the enum itself is: OrderStateChanged is a frozen event
    // and must carry a concrete type for From/To.
    public enum OrderState
    {
        Created, Waiting, Accepted, Preparing, Ready, Served, Completed, Cancelled, Expired, Refunded
    }

    public readonly struct OrderPlaced : IGameEvent
    {
        public readonly OrderId Id;
        public readonly CustomerId Customer;
        public readonly RecipeId Recipe;

        public OrderPlaced(OrderId id, CustomerId customer, RecipeId recipe)
        {
            Id = id;
            Customer = customer;
            Recipe = recipe;
        }
    }

    public readonly struct OrderStateChanged : IGameEvent
    {
        public readonly OrderId Id;
        public readonly OrderState From;
        public readonly OrderState To;

        public OrderStateChanged(OrderId id, OrderState from, OrderState to)
        {
            Id = id;
            From = from;
            To = to;
        }
    }

    public readonly struct OrderServed : IGameEvent
    {
        public readonly OrderId Id;
        public readonly float Quality01;
        public readonly float Accuracy01;
        public readonly float WaitSeconds;

        public OrderServed(OrderId id, float quality01, float accuracy01, float waitSeconds)
        {
            Id = id;
            Quality01 = quality01;
            Accuracy01 = accuracy01;
            WaitSeconds = waitSeconds;
        }
    }
}
