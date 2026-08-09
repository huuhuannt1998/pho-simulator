using System;
using System.Collections.Generic;
using Pho.Domain.Events;

// MOVED from Pho.Net.State at integration. This is pure, engine-free
// reconciliation logic, but living under Assets/Scripts/Net/ put it outside
// what Tools/PhoDomain.Tests.csproj globs, so its edge cases could not be
// covered by the 100ms test loop at all -- and a silently-wrong reconciler
// is exactly the kind of bug that only shows up as "a client's order board
// slowly drifts out of sync". Domain/Multiplayer/ is where CarryRegistry
// already lives for the same reason.
namespace Pho.Domain.Multiplayer
{
    /// <summary>What kind of change <see cref="OrderBoardMirror"/> detected.</summary>
    public enum OrderBoardOpKind
    {
        /// <summary>An order this board had never heard of. Maps to <c>OrderPlaced</c>.</summary>
        Placed,

        /// <summary>A known order moved state. Maps to <c>OrderStateChanged</c>.</summary>
        StateChanged,

        /// <summary>A known order vanished from the authoritative board without a terminal transition ever being observed. See the RECONCILING A VANISHED ORDER note on <see cref="OrderBoardMirror"/>.</summary>
        Removed
    }

    /// <summary>
    /// One detected change. Carries everything needed to rebuild the
    /// corresponding domain event without going back to the board.
    /// </summary>
    public readonly struct OrderBoardOp
    {
        public readonly OrderBoardOpKind Kind;
        public readonly string OrderId;
        public readonly string CustomerId;
        public readonly string RecipeId;

        /// <summary>The state this board previously believed the order was in. Meaningless for <see cref="OrderBoardOpKind.Placed"/>.</summary>
        public readonly OrderState From;

        /// <summary>The state the order is in now.</summary>
        public readonly OrderState To;

        public OrderBoardOp(OrderBoardOpKind kind, string orderId, string customerId, string recipeId, OrderState from, OrderState to)
        {
            Kind = kind;
            OrderId = orderId;
            CustomerId = customerId;
            RecipeId = recipeId;
            From = from;
            To = to;
        }
    }

    /// <summary>One live order, reduced to exactly the fields the frozen order events carry.</summary>
    public readonly struct OrderBoardEntry
    {
        public readonly string OrderId;
        public readonly string CustomerId;
        public readonly string RecipeId;
        public readonly OrderState State;

        public OrderBoardEntry(string orderId, string customerId, string recipeId, OrderState state)
        {
            OrderId = orderId;
            CustomerId = customerId;
            RecipeId = recipeId;
            State = state;
        }
    }

    /// <summary>
    /// The pure, engine-free heart of order-board replication: an
    /// insertion-ordered set of live orders that answers "what changed?"
    /// rather than "what is true?".
    ///
    /// <b>Why this is plain C# and not part of the NetworkBehaviour</b> --
    /// the same reasoning that split <c>CarryRegistry</c> out of
    /// <c>CarryAuthority</c>. Reconciling a board is a bookkeeping problem,
    /// not a transport problem, and it is the part with real edge cases: a
    /// late joiner whose board is empty, an order that moved two states
    /// while a packet was in flight, an order that disappeared entirely.
    /// Verifying those by launching two game clients and racing them by hand
    /// would be miserable; verifying them by calling a method is not.
    ///
    /// <b>Used on BOTH sides, which is the point.</b> The host runs one of
    /// these fed from its own event bus, so it always has a compact,
    /// terminal-pruned answer to "what does a joining player need to be
    /// told". Each client runs one fed from the wire, so it always knows
    /// what it has already told its local bus and never re-announces an
    /// order twice. Same class, same rules, no second implementation to
    /// drift.
    ///
    /// <b>Terminal orders are dropped, not kept.</b> An order reaching
    /// Completed/Cancelled/Expired/Refunded emits its final
    /// <see cref="OrderBoardOpKind.StateChanged"/> and is then forgotten.
    /// That mirrors what the host's <c>OrderService</c> actually does (it
    /// removes an order from its live registry on collection) and it keeps
    /// the join snapshot proportional to the orders in flight rather than to
    /// every order served since the session began. It also means both sides
    /// forget in the same place, so a later snapshot can never resurrect a
    /// finished ticket.
    ///
    /// <b>RECONCILING A VANISHED ORDER.</b> If a snapshot omits an order this
    /// board still believes is live, something was missed. There is no
    /// "OrderRemoved" domain event to replay, and inventing a state the host
    /// never reported would be exactly the kind of fabrication a replica must
    /// not commit. So <see cref="ApplySnapshot"/> reports it as
    /// <see cref="OrderBoardOpKind.Removed"/> carrying the last believed
    /// state and leaves the decision to the caller; the caller
    /// (<see cref="SharedStateReplicator"/>) documents what it does with it.
    /// The board itself simply forgets it, because continuing to claim a
    /// ticket the host does not have is strictly worse than dropping it.
    /// </summary>
    public sealed class OrderBoardMirror
    {
        readonly Dictionary<string, OrderBoardEntry> _byId = new Dictionary<string, OrderBoardEntry>(StringComparer.Ordinal);

        /// <summary>Insertion-ordered ids (oldest first). Kept separate from the map so a snapshot goes out in the order orders were placed, which is the order a board should read in.</summary>
        readonly List<string> _order = new List<string>();

        static readonly IReadOnlyList<OrderBoardOp> NoOps = new OrderBoardOp[0];

        /// <summary>How many live (non-terminal) orders this board is tracking.</summary>
        public int Count => _order.Count;

        /// <summary>
        /// Every live order, oldest first. Materialised on call rather than
        /// cached -- this is used once per joining player, not per frame.
        /// </summary>
        public IReadOnlyList<OrderBoardEntry> Entries
        {
            get
            {
                var result = new List<OrderBoardEntry>(_order.Count);
                for (int i = 0; i < _order.Count; i++)
                {
                    if (_byId.TryGetValue(_order[i], out var entry)) result.Add(entry);
                }
                return result;
            }
        }

        public bool TryGet(string orderId, out OrderBoardEntry entry)
        {
            if (string.IsNullOrEmpty(orderId))
            {
                entry = default;
                return false;
            }
            return _byId.TryGetValue(orderId, out entry);
        }

        /// <summary>
        /// Records the authoritative state of one order and reports what
        /// changed.
        ///
        /// Returns a <see cref="OrderBoardOpKind.Placed"/> op for an unknown
        /// order, followed by a <see cref="OrderBoardOpKind.StateChanged"/>
        /// op if it did not arrive in <see cref="OrderState.Created"/> --
        /// which mirrors what <c>OrderService.CreateOrder</c> publishes
        /// locally (an <c>OrderPlaced</c>, then a Created-&gt;Waiting
        /// <c>OrderStateChanged</c>) and is what <c>OrderBoardPresenter</c>
        /// already expects: it seeds a ticket at Created on OrderPlaced and
        /// advances it from there.
        ///
        /// A repeat of a state the board already believes returns no ops at
        /// all, so a redundant snapshot after a live update is silent rather
        /// than a burst of duplicate events.
        /// </summary>
        public IReadOnlyList<OrderBoardOp> ApplyUpsert(in OrderBoardEntry entry)
        {
            if (string.IsNullOrEmpty(entry.OrderId)) return NoOps;

            var ops = new List<OrderBoardOp>(2);
            ApplyUpsertInto(entry, ops);
            return ops.Count == 0 ? NoOps : ops;
        }

        void ApplyUpsertInto(in OrderBoardEntry entry, List<OrderBoardOp> ops)
        {
            if (_byId.TryGetValue(entry.OrderId, out var known))
            {
                if (known.State == entry.State)
                {
                    // Keep whatever identity fields we already had: a
                    // snapshot repeating the same state tells us nothing new.
                    return;
                }

                _byId[entry.OrderId] = entry;
                ops.Add(new OrderBoardOp(
                    OrderBoardOpKind.StateChanged,
                    entry.OrderId, entry.CustomerId, entry.RecipeId,
                    known.State, entry.State));

                if (IsTerminal(entry.State)) Forget(entry.OrderId);
                return;
            }

            _byId[entry.OrderId] = entry;
            _order.Add(entry.OrderId);

            ops.Add(new OrderBoardOp(
                OrderBoardOpKind.Placed,
                entry.OrderId, entry.CustomerId, entry.RecipeId,
                OrderState.Created, OrderState.Created));

            if (entry.State != OrderState.Created)
            {
                ops.Add(new OrderBoardOp(
                    OrderBoardOpKind.StateChanged,
                    entry.OrderId, entry.CustomerId, entry.RecipeId,
                    OrderState.Created, entry.State));
            }

            if (IsTerminal(entry.State)) Forget(entry.OrderId);
        }

        /// <summary>
        /// Replaces this board's belief with the authoritative one and
        /// reports every change needed to get from here to there.
        ///
        /// This is the late-joiner path. A fresh board reconciled against a
        /// four-order snapshot emits four Placed ops (plus their state
        /// advances) and nothing else -- which, republished onto a client's
        /// local event bus, is exactly what makes the joiner's order board
        /// show four tickets instead of nothing.
        ///
        /// A null or empty snapshot is a legitimate input meaning "no orders
        /// in flight", not an error: everything currently believed is
        /// reported as <see cref="OrderBoardOpKind.Removed"/>.
        /// </summary>
        public IReadOnlyList<OrderBoardOp> ApplySnapshot(IReadOnlyList<OrderBoardEntry> authoritative)
        {
            var ops = new List<OrderBoardOp>();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (authoritative != null)
            {
                for (int i = 0; i < authoritative.Count; i++)
                {
                    var entry = authoritative[i];
                    if (string.IsNullOrEmpty(entry.OrderId)) continue;

                    seen.Add(entry.OrderId);
                    ApplyUpsertInto(entry, ops);
                }
            }

            // Anything still believed but unmentioned is gone. Snapshot the
            // id list first: Forget mutates _order.
            var believed = new List<string>(_order);
            for (int i = 0; i < believed.Count; i++)
            {
                var id = believed[i];
                if (seen.Contains(id)) continue;
                if (!_byId.TryGetValue(id, out var stale)) continue;

                ops.Add(new OrderBoardOp(
                    OrderBoardOpKind.Removed,
                    stale.OrderId, stale.CustomerId, stale.RecipeId,
                    stale.State, stale.State));

                Forget(id);
            }

            return ops.Count == 0 ? NoOps : ops;
        }

        /// <summary>Drops an order without reporting anything. For a caller that already knows (the host forgetting a collected order).</summary>
        public void Forget(string orderId)
        {
            if (string.IsNullOrEmpty(orderId)) return;
            if (!_byId.Remove(orderId)) return;
            _order.Remove(orderId);
        }

        public void Clear()
        {
            _byId.Clear();
            _order.Clear();
        }

        /// <summary>
        /// The states an order can never leave. Same set
        /// <c>Pho.UI.HudView.IsTerminal</c> filters on -- duplicated rather
        /// than shared because <c>OrderStateMachine</c>'s terminal notion
        /// lives in Pho.Domain behind a transition table this class has no
        /// business importing, and because Pho.Net must not depend on Pho.UI.
        /// If the transition table ever gains a terminal state, this list is
        /// the second place to update.
        /// </summary>
        public static bool IsTerminal(OrderState state) =>
            state == OrderState.Completed
            || state == OrderState.Cancelled
            || state == OrderState.Expired
            || state == OrderState.Refunded;
    }
}
