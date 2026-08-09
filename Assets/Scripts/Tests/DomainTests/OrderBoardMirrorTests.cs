using System.Linq;
using NUnit.Framework;
using Pho.Domain.Events;
using Pho.Domain.Multiplayer;

namespace Pho.Domain.Tests
{
    /// <summary>
    /// Reconciliation rules for a client's copy of the order board.
    ///
    /// These matter more than they look. A client never simulates orders --
    /// it is TOLD about them -- so every wrong answer here surfaces as a
    /// board that quietly drifts out of step with the host: a ticket that
    /// never clears, a duplicate, or a state that goes backwards. None of
    /// those throw, and none would fail a compile.
    /// </summary>
    [TestFixture]
    public class OrderBoardMirrorTests
    {
        static OrderBoardEntry Entry(string id, OrderState state) =>
            new OrderBoardEntry(id, "cust." + id, "rec.pho_tai", state);

        [Test]
        public void FirstUpsert_SeedsAtCreated_ThenAdvancesToTheRealState()
        {
            var mirror = new OrderBoardMirror();

            var ops = mirror.ApplyUpsert(Entry("o1", OrderState.Waiting));

            // Two ops, deliberately: OrderBoardPresenter seeds a ticket at
            // Created on OrderPlaced and advances it via OrderStateChanged.
            // Emitting only the second would leave the client with a state
            // change for a ticket its HUD never created.
            Assert.That(ops.Count, Is.EqualTo(2));
            Assert.That(ops[0].Kind, Is.EqualTo(OrderBoardOpKind.Placed));
            Assert.That(ops[0].OrderId, Is.EqualTo("o1"));
            Assert.That(ops[1].Kind, Is.EqualTo(OrderBoardOpKind.StateChanged));
            Assert.That(ops[1].From, Is.EqualTo(OrderState.Created));
            Assert.That(ops[1].To, Is.EqualTo(OrderState.Waiting));
        }

        [Test]
        public void RepeatedUpsert_SameState_ReportsNothing()
        {
            var mirror = new OrderBoardMirror();
            mirror.ApplyUpsert(Entry("o1", OrderState.Waiting));

            // A duplicated or re-sent message must not republish OrderPlaced
            // -- the HUD would show the ticket twice.
            var ops = mirror.ApplyUpsert(Entry("o1", OrderState.Waiting));

            Assert.That(ops, Is.Empty);
        }

        [Test]
        public void Upsert_WithNewState_ReportsStateChangedCarryingBothStates()
        {
            var mirror = new OrderBoardMirror();
            mirror.ApplyUpsert(Entry("o1", OrderState.Waiting));

            var ops = mirror.ApplyUpsert(Entry("o1", OrderState.Preparing));

            Assert.That(ops.Count, Is.EqualTo(1));
            Assert.That(ops[0].Kind, Is.EqualTo(OrderBoardOpKind.StateChanged));
            Assert.That(ops[0].From, Is.EqualTo(OrderState.Waiting));
            Assert.That(ops[0].To, Is.EqualTo(OrderState.Preparing));
        }

        [Test]
        public void UnknownOrderArrivingMidFlight_ReportsPlaced_NotStateChanged()
        {
            var mirror = new OrderBoardMirror();

            // A late joiner's first sight of an order can be at any state.
            // Reporting StateChanged for something the HUD has never seen
            // would leave the board missing a ticket entirely.
            var ops = mirror.ApplyUpsert(Entry("o9", OrderState.Served));

            Assert.That(ops[0].Kind, Is.EqualTo(OrderBoardOpKind.Placed),
                "the HUD must be told the ticket exists before it is told the ticket moved");
            Assert.That(ops.Last().To, Is.EqualTo(OrderState.Served));
        }

        [Test]
        public void Snapshot_AddsMissing_UpdatesChanged_AndLeavesMatchesAlone()
        {
            var mirror = new OrderBoardMirror();
            mirror.ApplyUpsert(Entry("keep", OrderState.Waiting));
            mirror.ApplyUpsert(Entry("move", OrderState.Waiting));

            var ops = mirror.ApplySnapshot(new[]
            {
                Entry("keep", OrderState.Waiting),      // unchanged -> silent
                Entry("move", OrderState.Ready),        // changed   -> StateChanged
                Entry("fresh", OrderState.Waiting),     // new       -> Placed
            });

            Assert.That(ops.Any(o => o.OrderId == "keep"), Is.False, "an unchanged order must produce no event");
            Assert.That(ops.Single(o => o.OrderId == "move").Kind, Is.EqualTo(OrderBoardOpKind.StateChanged));
            Assert.That(ops.Count(o => o.OrderId == "fresh"), Is.EqualTo(2), "a new order seeds then advances");
            Assert.That(ops.First(o => o.OrderId == "fresh").Kind, Is.EqualTo(OrderBoardOpKind.Placed));
        }

        [Test]
        public void Snapshot_ReportsOrdersTheHostNoLongerHas()
        {
            var mirror = new OrderBoardMirror();
            mirror.ApplyUpsert(Entry("ghost", OrderState.Waiting));

            var ops = mirror.ApplySnapshot(new OrderBoardEntry[0]);

            // The host is authoritative: if it doesn't have the order, the
            // client must not keep showing it forever.
            Assert.That(ops.Single().Kind, Is.EqualTo(OrderBoardOpKind.Removed));
            Assert.That(ops.Single().OrderId, Is.EqualTo("ghost"));
            Assert.That(mirror.TryGet("ghost", out _), Is.False);
        }

        [Test]
        public void Snapshot_IsIdempotent()
        {
            var mirror = new OrderBoardMirror();
            var snapshot = new[] { Entry("a", OrderState.Waiting), Entry("b", OrderState.Preparing) };

            mirror.ApplySnapshot(snapshot);
            var second = mirror.ApplySnapshot(snapshot);

            // Re-sending the same snapshot (a retried handshake) must be a
            // no-op, not a second round of Placed events.
            Assert.That(second, Is.Empty);
        }

        [Test]
        public void Forget_RemovesWithoutEmittingAnything()
        {
            var mirror = new OrderBoardMirror();
            mirror.ApplyUpsert(Entry("o1", OrderState.Waiting));

            mirror.Forget("o1");

            Assert.That(mirror.TryGet("o1", out _), Is.False);
            Assert.That(mirror.ApplyUpsert(Entry("o1", OrderState.Waiting))[0].Kind,
                Is.EqualTo(OrderBoardOpKind.Placed), "after forgetting, the order is new again");
        }

        [Test]
        public void Clear_EmptiesTheBoard()
        {
            var mirror = new OrderBoardMirror();
            mirror.ApplyUpsert(Entry("a", OrderState.Waiting));
            mirror.ApplyUpsert(Entry("b", OrderState.Waiting));

            mirror.Clear();

            Assert.That(mirror.TryGet("a", out _), Is.False);
            Assert.That(mirror.TryGet("b", out _), Is.False);
        }

        [Test]
        public void TerminalStates_AreRecognised()
        {
            Assert.That(OrderBoardMirror.IsTerminal(OrderState.Completed), Is.True);
            Assert.That(OrderBoardMirror.IsTerminal(OrderState.Cancelled), Is.True);
            Assert.That(OrderBoardMirror.IsTerminal(OrderState.Expired), Is.True);
            Assert.That(OrderBoardMirror.IsTerminal(OrderState.Refunded), Is.True);

            Assert.That(OrderBoardMirror.IsTerminal(OrderState.Waiting), Is.False);
            Assert.That(OrderBoardMirror.IsTerminal(OrderState.Served), Is.False);
        }

        [Test]
        public void TryGet_ReturnsTheStoredEntry()
        {
            var mirror = new OrderBoardMirror();
            mirror.ApplyUpsert(Entry("o1", OrderState.Preparing));

            Assert.That(mirror.TryGet("o1", out var entry), Is.True);
            Assert.That(entry.State, Is.EqualTo(OrderState.Preparing));
            Assert.That(entry.RecipeId, Is.EqualTo("rec.pho_tai"));
        }
    }
}
