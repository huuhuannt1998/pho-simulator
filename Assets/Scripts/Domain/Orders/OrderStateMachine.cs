using System;
using System.Collections.Generic;
using Pho.Domain.Events;

namespace Pho.Domain.Orders
{
    /// <summary>
    /// Legal-transition table for <see cref="OrderState"/> (the enum itself
    /// is frozen in Pho.Domain.Events.OrderEvents -- M1). Exact shape from
    /// architecture.md §6.1. Pure, stateless, no allocations after the
    /// static table is built.
    /// </summary>
    public static class OrderStateMachine
    {
        static readonly Dictionary<OrderState, OrderState[]> Allowed = new Dictionary<OrderState, OrderState[]>
        {
            [OrderState.Created] = new[] { OrderState.Waiting, OrderState.Cancelled },
            [OrderState.Waiting] = new[] { OrderState.Accepted, OrderState.Cancelled, OrderState.Expired },
            [OrderState.Accepted] = new[] { OrderState.Preparing, OrderState.Cancelled, OrderState.Expired },
            [OrderState.Preparing] = new[] { OrderState.Ready, OrderState.Cancelled, OrderState.Expired },
            [OrderState.Ready] = new[] { OrderState.Served, OrderState.Expired, OrderState.Cancelled },
            [OrderState.Served] = new[] { OrderState.Completed, OrderState.Refunded },
            [OrderState.Completed] = Array.Empty<OrderState>(),
            [OrderState.Cancelled] = Array.Empty<OrderState>(),
            [OrderState.Expired] = new[] { OrderState.Refunded },
            [OrderState.Refunded] = Array.Empty<OrderState>(),
        };

        public static bool CanTransition(OrderState from, OrderState to)
        {
            return Allowed.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;
        }

        /// <summary>True when no legal transition exists out of <paramref name="s"/>.</summary>
        public static bool IsTerminal(OrderState s)
        {
            return Allowed.TryGetValue(s, out var targets) && targets.Length == 0;
        }
    }
}
