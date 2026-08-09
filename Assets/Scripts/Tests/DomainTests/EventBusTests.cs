using System;
using System.Collections.Generic;
using NUnit.Framework;
using Pho.Domain.Events;

namespace Pho.Domain.Tests
{
    // Plain NUnit only (no UnityEngine.TestTools / [UnityTest]) -- these
    // files are compiled both by Unity's EditMode test runner and by
    // Tools/PhoDomain.Tests.csproj via `dotnet test`. Constraint-based
    // Assert.That(...) throughout (not the classic Assert.AreEqual/IsTrue
    // family) because those moved to NUnit.Framework.Legacy in NUnit 4.x
    // and Unity's bundled NUnit does not have that namespace -- Assert.That
    // is the one syntax stable across both.
    [TestFixture]
    public class EventBusTests
    {
        struct Ping : IGameEvent
        {
            public readonly int Value;
            public Ping(int value) => Value = value;
        }

        struct Pong : IGameEvent
        {
        }

        [Test]
        public void Publish_InvokesSubscribedHandler_WithCorrectData()
        {
            var bus = new EventBus();
            int received = -1;
            bus.Subscribe<Ping>(e => received = e.Value);

            bus.Publish(new Ping(42));

            Assert.That(received, Is.EqualTo(42));
        }

        [Test]
        public void Publish_WithNoSubscribers_DoesNotThrow()
        {
            var bus = new EventBus();
            Assert.That(() => bus.Publish(new Ping(1)), Throws.Nothing);
        }

        [Test]
        public void Publish_InvokesMultipleHandlers_InSubscriptionOrder()
        {
            var bus = new EventBus();
            var order = new List<int>();
            bus.Subscribe<Ping>(_ => order.Add(1));
            bus.Subscribe<Ping>(_ => order.Add(2));
            bus.Subscribe<Ping>(_ => order.Add(3));

            bus.Publish(new Ping(0));

            Assert.That(order, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void Dispose_StopsFutureInvocations()
        {
            var bus = new EventBus();
            int callCount = 0;
            var sub = bus.Subscribe<Ping>(_ => callCount++);

            bus.Publish(new Ping(0));
            sub.Dispose();
            bus.Publish(new Ping(0));

            Assert.That(callCount, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var bus = new EventBus();
            var sub = bus.Subscribe<Ping>(_ => { });
            sub.Dispose();
            Assert.That(() => sub.Dispose(), Throws.Nothing);
        }

        [Test]
        public void DistinctEventTypes_DoNotCrossTalk()
        {
            var bus = new EventBus();
            bool pingFired = false, pongFired = false;
            bus.Subscribe<Ping>(_ => pingFired = true);
            bus.Subscribe<Pong>(_ => pongFired = true);

            bus.Publish(new Pong());

            Assert.That(pingFired, Is.False);
            Assert.That(pongFired, Is.True);
        }

        [Test]
        public void HandlerException_IsIsolated_RemainingHandlersStillRun()
        {
            var exceptions = new List<Exception>();
            var bus = new EventBus(onHandlerException: ex => exceptions.Add(ex));
            bool secondHandlerRan = false;
            bus.Subscribe<Ping>(_ => throw new InvalidOperationException("boom"));
            bus.Subscribe<Ping>(_ => secondHandlerRan = true);

            Assert.That(() => bus.Publish(new Ping(0)), Throws.Nothing);

            Assert.That(secondHandlerRan, Is.True);
            Assert.That(exceptions.Count, Is.EqualTo(1));
            Assert.That(exceptions[0], Is.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void Unsubscribe_DuringDispatch_DoesNotThrow_AndDoesNotReinvokeRemovedHandler()
        {
            var bus = new EventBus();
            int handlerACalls = 0, handlerBCalls = 0;
            IDisposable subB = null;

            bus.Subscribe<Ping>(_ =>
            {
                handlerACalls++;
                subB.Dispose(); // unsubscribe another handler mid-dispatch
            });
            subB = bus.Subscribe<Ping>(_ => handlerBCalls++);

            Assert.That(() => bus.Publish(new Ping(0)), Throws.Nothing);
            // B was already dispatched-order-after-A in this same publish;
            // the important guarantee is no exception and no double-fire.
            Assert.That(handlerACalls, Is.EqualTo(1));

            bus.Publish(new Ping(0));
            Assert.That(handlerACalls, Is.EqualTo(2));
            Assert.That(handlerBCalls, Is.EqualTo(0), "B was unsubscribed before it ever ran");
        }

        [Test]
        public void Subscribe_DuringDispatch_IsDeferredToNextPublish()
        {
            var bus = new EventBus();
            int lateHandlerCalls = 0;

            bus.Subscribe<Ping>(_ =>
            {
                bus.Subscribe<Ping>(__ => lateHandlerCalls++);
            });

            bus.Publish(new Ping(0)); // registers the late handler
            Assert.That(lateHandlerCalls, Is.EqualTo(0), "handler added mid-dispatch must not fire during that same publish");

            bus.Publish(new Ping(0));
            Assert.That(lateHandlerCalls, Is.EqualTo(1), "handler added mid-dispatch must fire on the next publish");
        }

        [Test]
        public void Subscribe_NullHandler_Throws()
        {
            var bus = new EventBus();
            Assert.That(() => bus.Subscribe<Ping>(null), Throws.ArgumentNullException);
        }
    }
}
