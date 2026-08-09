using System;
using System.Collections.Generic;
using NUnit.Framework;
using Pho.Domain.Events;
using Pho.Domain.Identity;
using Pho.Domain.Orders;

namespace Pho.Domain.Tests
{
    [TestFixture]
    public class OrderStateMachineTests
    {
        static readonly OrderState[] AllStates = (OrderState[])Enum.GetValues(typeof(OrderState));

        // The full legal-transition table, matching architecture.md §6.1's
        // Allowed dictionary exactly -- every entry gets its own case.
        static IEnumerable<TestCaseData> LegalTransitionCases()
        {
            yield return new TestCaseData(OrderState.Created, OrderState.Waiting);
            yield return new TestCaseData(OrderState.Created, OrderState.Cancelled);
            yield return new TestCaseData(OrderState.Waiting, OrderState.Accepted);
            yield return new TestCaseData(OrderState.Waiting, OrderState.Cancelled);
            yield return new TestCaseData(OrderState.Waiting, OrderState.Expired);
            yield return new TestCaseData(OrderState.Accepted, OrderState.Preparing);
            yield return new TestCaseData(OrderState.Accepted, OrderState.Cancelled);
            yield return new TestCaseData(OrderState.Accepted, OrderState.Expired);
            yield return new TestCaseData(OrderState.Preparing, OrderState.Ready);
            yield return new TestCaseData(OrderState.Preparing, OrderState.Cancelled);
            yield return new TestCaseData(OrderState.Preparing, OrderState.Expired);
            yield return new TestCaseData(OrderState.Ready, OrderState.Served);
            yield return new TestCaseData(OrderState.Ready, OrderState.Expired);
            yield return new TestCaseData(OrderState.Ready, OrderState.Cancelled);
            yield return new TestCaseData(OrderState.Served, OrderState.Completed);
            yield return new TestCaseData(OrderState.Served, OrderState.Refunded);
            yield return new TestCaseData(OrderState.Expired, OrderState.Refunded);
        }

        [TestCaseSource(nameof(LegalTransitionCases))]
        public void CanTransition_LegalPair_ReturnsTrue(OrderState from, OrderState to)
        {
            Assert.That(OrderStateMachine.CanTransition(from, to), Is.True, $"{from} -> {to} should be legal");
        }

        [Test]
        public void CanTransition_EveryPairInTheFullCrossProduct_MatchesTheAllowedTable()
        {
            var legal = new HashSet<(OrderState, OrderState)>();
            foreach (var c in LegalTransitionCases())
                legal.Add(((OrderState)c.Arguments[0], (OrderState)c.Arguments[1]));

            foreach (var from in AllStates)
            {
                foreach (var to in AllStates)
                {
                    bool expected = legal.Contains((from, to));
                    Assert.That(OrderStateMachine.CanTransition(from, to), Is.EqualTo(expected), $"{from} -> {to}");
                }
            }
        }

        [Test]
        public void CanTransition_SelectedNonAdjacentIllegalPairs_ReturnFalse()
        {
            Assert.That(OrderStateMachine.CanTransition(OrderState.Created, OrderState.Served), Is.False);
            Assert.That(OrderStateMachine.CanTransition(OrderState.Created, OrderState.Completed), Is.False);
            Assert.That(OrderStateMachine.CanTransition(OrderState.Waiting, OrderState.Ready), Is.False);
            Assert.That(OrderStateMachine.CanTransition(OrderState.Preparing, OrderState.Waiting), Is.False);
            Assert.That(OrderStateMachine.CanTransition(OrderState.Ready, OrderState.Preparing), Is.False);
            Assert.That(OrderStateMachine.CanTransition(OrderState.Completed, OrderState.Refunded), Is.False);
            Assert.That(OrderStateMachine.CanTransition(OrderState.Cancelled, OrderState.Waiting), Is.False);
        }

        [Test]
        public void IsTerminal_CompletedCancelledRefunded_AreTerminal()
        {
            Assert.That(OrderStateMachine.IsTerminal(OrderState.Completed), Is.True);
            Assert.That(OrderStateMachine.IsTerminal(OrderState.Cancelled), Is.True);
            Assert.That(OrderStateMachine.IsTerminal(OrderState.Refunded), Is.True);
        }

        [Test]
        public void IsTerminal_NonTerminalStates_AreFalse()
        {
            Assert.That(OrderStateMachine.IsTerminal(OrderState.Created), Is.False);
            Assert.That(OrderStateMachine.IsTerminal(OrderState.Waiting), Is.False);
            Assert.That(OrderStateMachine.IsTerminal(OrderState.Accepted), Is.False);
            Assert.That(OrderStateMachine.IsTerminal(OrderState.Preparing), Is.False);
            Assert.That(OrderStateMachine.IsTerminal(OrderState.Ready), Is.False);
            Assert.That(OrderStateMachine.IsTerminal(OrderState.Served), Is.False);
            // Expired is NOT terminal -- it has one legal onward transition (-> Refunded).
            Assert.That(OrderStateMachine.IsTerminal(OrderState.Expired), Is.False);
        }

        [Test]
        public void TerminalStates_AbsorbEveryOutgoingTransition()
        {
            foreach (var terminal in new[] { OrderState.Completed, OrderState.Cancelled, OrderState.Refunded })
            {
                foreach (var to in AllStates)
                {
                    Assert.That(OrderStateMachine.CanTransition(terminal, to), Is.False, $"{terminal} -> {to} must be illegal");
                }
            }
        }

        // ---- OrderModel ----

        static OrderModel MakeOrder(float createdAt = 0f)
        {
            return new OrderModel(
                new OrderId("ord.test001"),
                new CustomerId("cus.test001"),
                "table.1",
                new RecipeId("rec.pho_tai"),
                OrderModifiers.Default,
                9.5m,
                createdAt);
        }

        [Test]
        public void NewOrder_StartsInCreatedState()
        {
            var order = MakeOrder();
            Assert.That(order.State, Is.EqualTo(OrderState.Created));
            Assert.That(order.ServedAtGameSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void TryTransition_LegalTransition_UpdatesStateAndReturnsTrue()
        {
            var order = MakeOrder();
            bool ok = order.TryTransition(OrderState.Waiting, 1f, out var error);

            Assert.That(ok, Is.True);
            Assert.That(order.State, Is.EqualTo(OrderState.Waiting));
            Assert.That(error, Is.Null);
        }

        [Test]
        public void TryTransition_IllegalTransition_ReturnsFalse_DoesNotChangeState_AndDoesNotThrow()
        {
            var order = MakeOrder();
            bool ok = true;
            string error = null;

            Assert.That(() => ok = order.TryTransition(OrderState.Served, 1f, out error), Throws.Nothing);

            Assert.That(ok, Is.False);
            Assert.That(order.State, Is.EqualTo(OrderState.Created));
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void TryTransition_ToServed_StampsServedAtGameSeconds()
        {
            var order = MakeOrder();
            order.TryTransition(OrderState.Waiting, 1f, out _);
            order.TryTransition(OrderState.Accepted, 2f, out _);
            order.TryTransition(OrderState.Preparing, 3f, out _);
            order.TryTransition(OrderState.Ready, 4f, out _);

            bool ok = order.TryTransition(OrderState.Served, 10f, out var error);

            Assert.That(ok, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(order.ServedAtGameSeconds, Is.EqualTo(10f));
        }

        [Test]
        public void WaitSeconds_BeforeServed_MeasuresAgainstNow()
        {
            var order = MakeOrder(createdAt: 5f);
            Assert.That(order.WaitSeconds(20f), Is.EqualTo(15f).Within(0.0001f));
        }

        [Test]
        public void WaitSeconds_AfterServed_FreezesAtServedTime_IgnoringNow()
        {
            var order = MakeOrder(createdAt: 5f);
            order.TryTransition(OrderState.Waiting, 6f, out _);
            order.TryTransition(OrderState.Accepted, 7f, out _);
            order.TryTransition(OrderState.Preparing, 8f, out _);
            order.TryTransition(OrderState.Ready, 9f, out _);
            order.TryTransition(OrderState.Served, 10f, out _);

            Assert.That(order.WaitSeconds(500f), Is.EqualTo(5f).Within(0.0001f));
        }
    }
}
