using System;
using System.Collections.Generic;
using Pho.Domain.Events;
using Pho.Domain.Identity;

namespace Pho.UI.Presenters
{
    /// <summary>
    /// UI-facing snapshot of one order, assembled purely from the frozen
    /// OrderPlaced / OrderStateChanged / OrderServed events. This is
    /// deliberately NOT the domain OrderModel (that's Pho.Domain, owned by
    /// Agent A at M8) -- it is a thin read-only projection for a future
    /// order-ticket view (GDD §43 "Table 3 / 2x Pho Tai / No onion").
    ///
    /// Note: OrderPlaced (as frozen at M1) carries only Id/Customer/Recipe --
    /// no table id, no modifiers (no-onion/extra-meat/size/spice), no
    /// quantity, no quoted price. Those fields exist on the *domain*
    /// OrderModel (architecture §6.1) but are not on the event, so this
    /// ticket can't show "Table 3" or "No onion" yet. Flagged in the final
    /// report rather than guessed at here.
    /// </summary>
    public sealed class OrderTicket
    {
        public OrderId Id { get; }
        public CustomerId Customer { get; }
        public RecipeId Recipe { get; }

        /// <summary>Current lifecycle state, kept in sync by OrderStateChanged.</summary>
        public OrderState State { get; internal set; }

        // Populated once OrderServed fires for this order; null until then.
        public float? ServedQuality01 { get; internal set; }
        public float? ServedAccuracy01 { get; internal set; }
        public float? ServedWaitSeconds { get; internal set; }

        internal OrderTicket(OrderId id, CustomerId customer, RecipeId recipe, OrderState state)
        {
            Id = id;
            Customer = customer;
            Recipe = recipe;
            State = state;
        }
    }

    /// <summary>
    /// Plain-C# presenter for the "Current orders" HUD element / order board
    /// (GDD §43). Subscribes to OrderPlaced/OrderStateChanged/OrderServed and
    /// maintains an in-memory, insertion-ordered ticket per OrderId.
    ///
    /// Pruning policy for terminal orders (Completed/Cancelled/Expired/
    /// Refunded) is intentionally left to a future layer: OrderStateMachine
    /// and its IsTerminal notion (architecture §6.1) are M8 domain logic, not
    /// part of the M1 freeze this pass builds against, so this presenter
    /// does not guess at when a ticket should disappear from the board -- it
    /// just tracks the latest known state and lets the view decide.
    /// </summary>
    public sealed class OrderBoardPresenter : IDisposable
    {
        readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        readonly Dictionary<OrderId, OrderTicket> _ordersById = new Dictionary<OrderId, OrderTicket>();
        readonly List<OrderTicket> _orderedTickets = new List<OrderTicket>();

        /// <summary>All orders seen this session, oldest first. Includes terminal orders -- see class remarks.</summary>
        public IReadOnlyList<OrderTicket> Orders => _orderedTickets;

        public void Bind(IEventBus events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            _subscriptions.Add(events.Subscribe<OrderPlaced>(OnOrderPlaced));
            _subscriptions.Add(events.Subscribe<OrderStateChanged>(OnOrderStateChanged));
            _subscriptions.Add(events.Subscribe<OrderServed>(OnOrderServed));
        }

        /// <summary>Convenience lookup for a single order (e.g. to bind one ticket widget).</summary>
        public bool TryGetOrder(OrderId id, out OrderTicket ticket) => _ordersById.TryGetValue(id, out ticket);

        void OnOrderPlaced(OrderPlaced evt)
        {
            if (_ordersById.ContainsKey(evt.Id))
            {
                // Defensive: a duplicate OrderPlaced for a known id should never
                // happen once OrderService (M8) exists. Ignore rather than throw --
                // this is presentation code, not the source of truth.
                return;
            }

            var ticket = new OrderTicket(evt.Id, evt.Customer, evt.Recipe, OrderState.Created);
            _ordersById[evt.Id] = ticket;
            _orderedTickets.Add(ticket);
        }

        void OnOrderStateChanged(OrderStateChanged evt)
        {
            if (_ordersById.TryGetValue(evt.Id, out var ticket))
            {
                ticket.State = evt.To;
            }
            // Unknown order id is ignored defensively (see OnOrderPlaced remark).
        }

        void OnOrderServed(OrderServed evt)
        {
            if (_ordersById.TryGetValue(evt.Id, out var ticket))
            {
                ticket.ServedQuality01 = evt.Quality01;
                ticket.ServedAccuracy01 = evt.Accuracy01;
                ticket.ServedWaitSeconds = evt.WaitSeconds;
            }
        }

        public void Dispose()
        {
            foreach (var sub in _subscriptions) sub.Dispose();
            _subscriptions.Clear();
        }
    }
}
