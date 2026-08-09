using Pho.Domain.Events;
using Pho.Domain.Identity;

namespace Pho.Domain.Orders
{
    /// <summary>
    /// Runtime order state. Exact shape from architecture.md §6.1.
    ///
    /// Deliberate deviation from GDD §62 (documented there too):
    /// <see cref="Customer"/> is a typed <see cref="CustomerId"/>, never a
    /// live object reference -- keeps the order serializable, testable, and
    /// safe to hold onto after a customer despawns.
    /// </summary>
    public sealed class OrderModel
    {
        public readonly OrderId Id;
        public readonly CustomerId Customer;
        public readonly string TableId;
        public readonly RecipeId Recipe;
        public readonly OrderModifiers Modifiers;
        public readonly decimal QuotedPrice;

        public OrderState State { get; private set; }
        public float CreatedAtGameSeconds { get; }
        public float ServedAtGameSeconds { get; private set; }

        public OrderModel(
            OrderId id,
            CustomerId customer,
            string tableId,
            RecipeId recipe,
            OrderModifiers modifiers,
            decimal quotedPrice,
            float createdAtGameSeconds)
        {
            Id = id;
            Customer = customer;
            TableId = tableId;
            Recipe = recipe;
            Modifiers = modifiers;
            QuotedPrice = quotedPrice;
            State = OrderState.Created;
            CreatedAtGameSeconds = createdAtGameSeconds;
            ServedAtGameSeconds = 0f;
        }

        /// <summary>
        /// Attempts the transition via <see cref="OrderStateMachine.CanTransition"/>.
        /// An illegal transition (e.g. a UI double-click race re-firing
        /// "serve" on an already-served order) is a normal runtime occurrence,
        /// not a programming error -- this returns false with a descriptive
        /// message rather than throwing. On a legal transition to
        /// <see cref="OrderState.Served"/>, <see cref="ServedAtGameSeconds"/>
        /// is stamped with <paramref name="nowSeconds"/>.
        /// </summary>
        public bool TryTransition(OrderState to, float nowSeconds, out string error)
        {
            if (!OrderStateMachine.CanTransition(State, to))
            {
                error = $"Illegal order transition: {State} -> {to}.";
                return false;
            }

            State = to;
            if (to == OrderState.Served)
                ServedAtGameSeconds = nowSeconds;

            error = null;
            return true;
        }

        /// <summary>
        /// Seconds elapsed since the order was created. Freezes at the
        /// moment of serving (uses <see cref="ServedAtGameSeconds"/> instead
        /// of <paramref name="now"/> once served) so a served order's wait
        /// time doesn't keep climbing while it sits in "Completed".
        /// </summary>
        public float WaitSeconds(float now) =>
            (ServedAtGameSeconds > 0f ? ServedAtGameSeconds : now) - CreatedAtGameSeconds;
    }
}
