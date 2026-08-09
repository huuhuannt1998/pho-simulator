using System;
using System.Collections.Generic;
using UnityEngine;
using Pho.Domain.Contracts;
using Pho.Domain.Cooking;
using Pho.Domain.Customers;
using Pho.Domain.Events;
using Pho.Domain.Identity;
using Pho.Domain.Orders;

namespace Pho.Core.Orders
{
    /// <summary>
    /// The live registry of in-flight <see cref="OrderModel"/>s -- the M8
    /// service that connects Kitchen (which can assemble and score a bowl,
    /// publishing <see cref="BowlAssembled"/>) to Customers (which can place
    /// an order and poll for a served dish via <see cref="ICustomerWorld"/>).
    /// Neither side references the other; this service is the only thing
    /// that does.
    ///
    /// <b>FIFO-by-recipe matching (the core design simplification of this
    /// pass, read before changing anything below):</b>
    /// <see cref="BowlAssembled"/> carries a <see cref="RecipeId"/> but no
    /// <see cref="OrderId"/> -- a <c>BowlObject</c>/<c>PassCounter</c> has no
    /// idea which customer's order it is fulfilling, since Kitchen and
    /// Customers are deliberately decoupled assemblies with no reference to
    /// each other (architecture.md section 1's dependency graph). Rather
    /// than invent a "ticket" system (a real "table has a ticket, player
    /// delivers to the right table" flow is out of scope for the vertical
    /// slice), this service resolves every <see cref="BowlAssembled"/> by
    /// finding the OLDEST order still in <see cref="OrderState.Waiting"/>,
    /// <see cref="OrderState.Accepted"/>, or <see cref="OrderState.Preparing"/>
    /// whose <see cref="OrderModel.Recipe"/> equals
    /// <see cref="BowlAssembled.MatchedRecipe"/>, and fulfils THAT one. If no
    /// such order exists, the bowl's result is dropped with a warning log --
    /// a bowl assembled with nobody waiting for it (player practicing, an
    /// order was cancelled, etc.) is a legitimate, expected runtime
    /// occurrence, not an error.
    ///
    /// Consuming a served dish (<see cref="TryTakeServedDish"/>) advances the
    /// matched order to <see cref="OrderState.Completed"/> and removes it
    /// from the live registry, which also removes it from FIFO-matching
    /// consideration -- so a served-but-not-yet-collected order can never be
    /// matched a second time by a later <see cref="BowlAssembled"/> for the
    /// same recipe (its state is already past the Waiting/Accepted/Preparing
    /// window).
    ///
    /// <b>Known limitation, out of scope for this pass:</b> if a customer's
    /// patience runs out (<c>LeavingAngry</c>) AFTER their order has already
    /// been served but BEFORE <see cref="ICustomerWorld.TryTakeServedDish"/>
    /// is ever called again to collect it, that order/served-dish pair is
    /// never removed from this registry (nothing in <c>CustomerBrain</c>
    /// calls <c>TryTakeServedDish</c> once it gives up). This is a slow
    /// memory leak over a very long play session, not a functional bug in
    /// the vertical slice's timescale -- flagged here rather than silently
    /// left unmentioned; a future pass could add an expiry sweep.
    /// </summary>
    public sealed class OrderService : IDisposable
    {
        readonly IEventBus _events;
        readonly IGameDatabase _database;

        readonly Dictionary<OrderId, OrderModel> _orders = new Dictionary<OrderId, OrderModel>();

        /// <summary>
        /// Insertion-ordered (oldest first) view of every live order, used
        /// only for FIFO-by-recipe matching in <see cref="OnBowlAssembled"/>.
        /// An order is added here once, in <see cref="CreateOrder"/>, and
        /// removed once, in <see cref="TryTakeServedDish"/> -- never
        /// reordered.
        /// </summary>
        readonly List<OrderModel> _fifoByCreation = new List<OrderModel>();

        readonly Dictionary<OrderId, ServedDish> _servedDishes = new Dictionary<OrderId, ServedDish>();

        readonly IDisposable _bowlAssembledSubscription;

        /// <summary>
        /// The path walked (skipping any state already passed) to carry a
        /// FIFO-matched order from wherever it currently sits
        /// (Waiting/Accepted/Preparing) to Served, per
        /// <see cref="OrderStateMachine"/>'s legal transition table
        /// (architecture.md section 6.1): Waiting -> Accepted -> Preparing ->
        /// Ready -> Served.
        /// </summary>
        static readonly OrderState[] PathToServed =
        {
            OrderState.Accepted, OrderState.Preparing, OrderState.Ready, OrderState.Served
        };

        /// <param name="events">Required -- the service is inert (throws) without it.</param>
        /// <param name="database">
        /// Optional. Used only to resolve a recipe's base price
        /// (<see cref="ResolveQuotedPrice"/>) when an order is created. If
        /// null (or the recipe isn't found in it), the order is quoted at
        /// $0.00 and a warning is logged -- degrade gracefully, never throw,
        /// matching this codebase's established "inert, not broken" pattern
        /// for unbound optional dependencies (see PassCounter.Bind).
        /// </param>
        public OrderService(IEventBus events, IGameDatabase database)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _database = database;

            _bowlAssembledSubscription = _events.Subscribe<BowlAssembled>(OnBowlAssembled);
        }

        /// <summary>
        /// Creates a new order for <paramref name="customer"/>, transitions
        /// it Created -&gt; Waiting, stores it, and publishes
        /// <see cref="OrderPlaced"/> followed by the Created-&gt;Waiting
        /// <see cref="OrderStateChanged"/> (matches what
        /// <c>OrderBoardPresenter</c> already expects: a ticket is seeded at
        /// <see cref="OrderState.Created"/> on <see cref="OrderPlaced"/>,
        /// then advanced by <see cref="OrderStateChanged"/>).
        /// </summary>
        /// <param name="tableId">
        /// Deliberate deviation from the task brief's "roughly" signature,
        /// which omitted a table-id parameter: <see cref="OrderModel"/>
        /// requires a non-null <c>TableId</c>, but
        /// <c>ICustomerWorld.PlaceOrder</c>'s only source for one is
        /// <c>SeatHandle.TableId</c> -- the caller (<c>CustomerAgent</c>)
        /// resolves that (with its own documented fallback for the current
        /// SeatHandle integration gap) and passes it straight through here.
        /// Never null internally -- normalized to <see cref="string.Empty"/>
        /// if the caller passes null.
        /// </param>
        /// <param name="nowSeconds">Game-clock seconds at order creation (e.g. Unity's <c>Time.time</c>).</param>
        public OrderId CreateOrder(CustomerId customer, string tableId, RecipeId recipe, OrderModifiers modifiers, float nowSeconds)
        {
            var id = new OrderId(Guid.NewGuid().ToString());
            decimal quotedPrice = ResolveQuotedPrice(recipe);

            var order = new OrderModel(id, customer, tableId ?? string.Empty, recipe, modifiers, quotedPrice, nowSeconds);

            _orders[id] = order;
            _fifoByCreation.Add(order);

            _events.Publish(new OrderPlaced(id, customer, recipe));

            // Created -> Waiting is unconditionally legal per
            // OrderStateMachine's table, so this should never fail; guarded
            // defensively rather than assumed, per this codebase's
            // "TryTransition returns false, never throws" convention.
            if (order.TryTransition(OrderState.Waiting, nowSeconds, out var error))
            {
                _events.Publish(new OrderStateChanged(id, OrderState.Created, OrderState.Waiting));
            }
            else
            {
                Debug.LogWarning($"[OrderService] New order {id} failed Created -> Waiting: {error}");
            }

            return id;
        }

        /// <summary>Looks up a live (not yet taken/removed) order by id.</summary>
        public bool TryGetOrder(OrderId id, out OrderModel order) => _orders.TryGetValue(id, out order);

        /// <summary>
        /// If a served dish is waiting for <paramref name="id"/>, hands it
        /// back and removes the order from the live registry (it is now
        /// fully consumed -- no longer eligible for FIFO matching, no longer
        /// returned by <see cref="TryGetOrder"/>). Advances the order
        /// Served -&gt; Completed first, publishing <see cref="OrderStateChanged"/>.
        /// Returns false (dish = default) if nothing has been served for
        /// this order yet -- the normal "still cooking" case, not an error.
        /// </summary>
        public bool TryTakeServedDish(OrderId id, out ServedDish dish)
        {
            if (!_servedDishes.TryGetValue(id, out dish))
            {
                dish = default;
                return false;
            }

            _servedDishes.Remove(id);

            if (_orders.TryGetValue(id, out var order))
            {
                if (order.State == OrderState.Served)
                {
                    // nowSeconds is unused by TryTransition except when
                    // transitioning *to* Served (see OrderModel.TryTransition) --
                    // reusing ServedAtGameSeconds here is safe and avoids
                    // needing a "current time" input this method's frozen
                    // ICustomerWorld-facing signature doesn't have.
                    if (order.TryTransition(OrderState.Completed, order.ServedAtGameSeconds, out var error))
                    {
                        _events.Publish(new OrderStateChanged(id, OrderState.Served, OrderState.Completed));
                    }
                    else
                    {
                        Debug.LogWarning($"[OrderService] Order {id} failed Served -> Completed on collection: {error}");
                    }
                }

                _orders.Remove(id);
                _fifoByCreation.Remove(order);
            }

            return true;
        }

        void OnBowlAssembled(BowlAssembled evt)
        {
            OrderModel match = null;

            // _fifoByCreation is insertion-ordered oldest-first and never
            // reordered, so the first eligible match found IS the oldest --
            // see the FIFO-by-recipe design note on the class doc comment.
            for (int i = 0; i < _fifoByCreation.Count; i++)
            {
                var candidate = _fifoByCreation[i];
                if (!(candidate.Recipe == evt.MatchedRecipe)) continue;
                if (!IsAwaitingKitchen(candidate.State)) continue;

                match = candidate;
                break;
            }

            if (match == null)
            {
                Debug.LogWarning($"[OrderService] BowlAssembled (bowl #{evt.BowlInstanceId}, recipe '{evt.MatchedRecipe}') matched no order currently waiting for that recipe -- dropping. This is expected (not an error) when nobody is waiting: a practice bowl, an already-fulfilled/cancelled order, etc.");
                return;
            }

            AdvanceToServed(match, evt);
        }

        static bool IsAwaitingKitchen(OrderState state) =>
            state == OrderState.Waiting || state == OrderState.Accepted || state == OrderState.Preparing;

        void AdvanceToServed(OrderModel order, in BowlAssembled evt)
        {
            float now = Time.time;

            foreach (var target in PathToServed)
            {
                // Skips any state already behind `order` (e.g. a match found
                // in Preparing skips straight past Accepted/Preparing to
                // Ready) -- CanTransition(from, from) is always false since
                // no state lists itself as a legal target, so this loop
                // never re-enters the current state.
                if (!OrderStateMachine.CanTransition(order.State, target)) continue;

                var from = order.State;
                if (!order.TryTransition(target, now, out var error))
                {
                    Debug.LogWarning($"[OrderService] Order {order.Id} failed transition {from} -> {target} while fulfilling BowlAssembled #{evt.BowlInstanceId}: {error}");
                    return;
                }

                _events.Publish(new OrderStateChanged(order.Id, from, target));
            }

            if (order.State != OrderState.Served)
            {
                Debug.LogWarning($"[OrderService] Order {order.Id} did not reach Served (stuck at {order.State}) while fulfilling BowlAssembled #{evt.BowlInstanceId} -- no served dish produced.");
                return;
            }

            var quality = ApproximateDishQuality(evt.Quality01);
            _servedDishes[order.Id] = new ServedDish(evt.MatchedRecipe, quality, evt.Accuracy01, order.QuotedPrice);

            _events.Publish(new OrderServed(order.Id, evt.Quality01, evt.Accuracy01, order.WaitSeconds(now)));
        }

        /// <summary>
        /// <see cref="BowlAssembled"/> (frozen, KitchenEvents.cs) only carries
        /// a single flattened <c>float Quality01</c> -- the full
        /// <see cref="DishQuality"/> struct <c>QualityCalculator.Score</c>
        /// originally produced inside <c>PassCounter.SeatAndScore</c> is not
        /// on the event, and widening that frozen event shape is out of
        /// scope for this pass. Documented, honest simplification: every
        /// sub-score (<see cref="DishQuality.IngredientQuality01"/>,
        /// <see cref="DishQuality.BrothQuality01"/>,
        /// <see cref="DishQuality.Accuracy01"/>,
        /// <see cref="DishQuality.Temperature01"/>,
        /// <see cref="DishQuality.Freshness01"/>) is approximated as the same
        /// flattened <see cref="DishQuality.Overall01"/> value -- no
        /// fabricated variance, and harmless downstream since
        /// <c>SatisfactionCalculator</c>/<c>CustomerBrain.TickPaying</c>
        /// consume <see cref="ServedDish.Quality"/> primarily through
        /// <c>Overall01</c>-derived terms.
        /// </summary>
        static DishQuality ApproximateDishQuality(float quality01)
        {
            return new DishQuality(
                overall01: quality01,
                ingredientQuality01: quality01,
                brothQuality01: quality01,
                accuracy01: quality01,
                temperature01: quality01,
                freshness01: quality01);
        }

        decimal ResolveQuotedPrice(RecipeId recipe)
        {
            if (_database != null && _database.TryGetRecipe(recipe, out var def))
            {
                return (decimal)def.BasePrice;
            }

            Debug.LogWarning($"[OrderService] Could not resolve a base price for recipe '{recipe}' (no GameDatabase bound, or recipe not found) -- quoting $0.00.");
            return 0m;
        }

        public void Dispose()
        {
            _bowlAssembledSubscription?.Dispose();
        }
    }
}
