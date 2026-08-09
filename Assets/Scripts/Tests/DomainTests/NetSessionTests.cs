using System.Collections.Generic;
using NUnit.Framework;
using Pho.Domain.Multiplayer;

namespace Pho.Domain.Tests
{
    /// <summary>
    /// The session rules that must hold before any transport is involved:
    /// the four-player cap, and the connection state machine.
    ///
    /// These exist so the netcode layer above (NetworkSession) can stay a
    /// thin adapter. If these pass, "a fifth player tried to join" and "the
    /// user pressed Leave while still connecting" are already solved, and
    /// the MonoBehaviour is only moving messages.
    /// </summary>
    [TestFixture]
    public class SessionSlotsTests
    {
        const ulong Alice = 1, Bob = 2, Carol = 3, Dave = 4, Eve = 5;

        static SessionSlots Full()
        {
            var slots = new SessionSlots();
            foreach (var id in new[] { Alice, Bob, Carol, Dave })
            {
                Assert.That(slots.TryReserve(id, out _), Is.True, $"seating {id} should succeed");
            }
            return slots;
        }

        [Test]
        public void DefaultCapacity_IsFourPlayers()
        {
            var slots = new SessionSlots();

            Assert.That(slots.MaxPlayers, Is.EqualTo(4));
            Assert.That(SessionSlots.DefaultMaxPlayers, Is.EqualTo(4));
            Assert.That(slots.Count, Is.Zero);
            Assert.That(slots.IsFull, Is.False);
            Assert.That(slots.FreeSlots, Is.EqualTo(4));
        }

        [Test]
        public void FourPlayers_AllFit()
        {
            var slots = Full();

            Assert.That(slots.Count, Is.EqualTo(4));
            Assert.That(slots.IsFull, Is.True);
            Assert.That(slots.FreeSlots, Is.Zero);
        }

        [Test]
        public void FifthPlayer_IsRejectedWithSessionFull()
        {
            var slots = Full();

            Assert.That(slots.TryReserve(Eve, out var verdict), Is.False);
            Assert.That(verdict, Is.EqualTo(SessionJoinVerdict.SessionFull));
            Assert.That(slots.Contains(Eve), Is.False);
            Assert.That(slots.Count, Is.EqualTo(4), "a refused player must not consume a slot");
        }

        [Test]
        public void FifthPlayer_RejectionCarriesAReadableReason()
        {
            // The whole point of the verdict enum: a refused player is told
            // what happened rather than silently dropped.
            var slots = Full();
            slots.TryReserve(Eve, out var verdict);

            var message = slots.Describe(verdict);

            Assert.That(message, Is.Not.Empty);
            Assert.That(message, Does.Contain("full").IgnoreCase);
            Assert.That(message, Does.Contain("4/4"));
        }

        [Test]
        public void ReReserving_AnAlreadySeatedPlayer_IsIdempotentSuccess()
        {
            // A duplicated or retried approval must never refuse a player
            // who is already legitimately in the game.
            var slots = new SessionSlots();
            slots.TryReserve(Alice, out _);

            Assert.That(slots.TryReserve(Alice, out var verdict), Is.True);
            Assert.That(verdict, Is.EqualTo(SessionJoinVerdict.AlreadySeated));
            Assert.That(slots.Count, Is.EqualTo(1), "re-reserving must not consume a second slot");
        }

        [Test]
        public void SeatedPlayer_IsStillAdmitted_WhenSessionIsFull()
        {
            var slots = Full();

            Assert.That(slots.TryReserve(Alice, out var verdict), Is.True);
            Assert.That(verdict, Is.EqualTo(SessionJoinVerdict.AlreadySeated));
        }

        [Test]
        public void Release_FreesTheSlotForSomeoneElse()
        {
            var slots = Full();

            Assert.That(slots.Release(Bob), Is.True);
            Assert.That(slots.IsFull, Is.False);
            Assert.That(slots.TryReserve(Eve, out var verdict), Is.True);
            Assert.That(verdict, Is.EqualTo(SessionJoinVerdict.Approved));
            Assert.That(slots.Count, Is.EqualTo(4));
        }

        [Test]
        public void Release_OfSomeoneNeverSeated_IsFalseAndHarmless()
        {
            // Disconnect callbacks fire for connections that were refused
            // approval and therefore never took a slot.
            var slots = Full();

            Assert.That(slots.Release(Eve), Is.False);
            Assert.That(slots.Count, Is.EqualTo(4), "a spurious release must not free somebody else's seat");
        }

        [Test]
        public void DoubleRelease_DoesNotFreeAnExtraSlot()
        {
            var slots = Full();

            Assert.That(slots.Release(Bob), Is.True);
            Assert.That(slots.Release(Bob), Is.False);
            Assert.That(slots.Count, Is.EqualTo(3));
        }

        [Test]
        public void NotAcceptingJoins_RejectsNewcomersWithItsOwnReason()
        {
            var slots = new SessionSlots();
            slots.AcceptingJoins = false;

            Assert.That(slots.TryReserve(Alice, out var verdict), Is.False);
            Assert.That(verdict, Is.EqualTo(SessionJoinVerdict.NotAcceptingJoins));
            Assert.That(slots.Describe(verdict), Does.Contain("closing").IgnoreCase);
        }

        [Test]
        public void NotAcceptingJoins_StillAdmitsAlreadySeatedPlayers()
        {
            // Closing the door must not evict the people already inside.
            var slots = new SessionSlots();
            slots.TryReserve(Alice, out _);
            slots.AcceptingJoins = false;

            Assert.That(slots.TryReserve(Alice, out var verdict), Is.True);
            Assert.That(verdict, Is.EqualTo(SessionJoinVerdict.AlreadySeated));
        }

        [Test]
        public void CustomCapacity_IsHonoured_AndClampedToAtLeastOne()
        {
            var duo = new SessionSlots(2);
            duo.TryReserve(Alice, out _);
            duo.TryReserve(Bob, out _);

            Assert.That(duo.TryReserve(Carol, out var verdict), Is.False);
            Assert.That(verdict, Is.EqualTo(SessionJoinVerdict.SessionFull));

            Assert.That(new SessionSlots(0).MaxPlayers, Is.EqualTo(1));
            Assert.That(new SessionSlots(-5).MaxPlayers, Is.EqualTo(1));
        }

        [Test]
        public void Clear_EmptiesEveryone()
        {
            var slots = Full();

            slots.Clear();

            Assert.That(slots.Count, Is.Zero);
            Assert.That(slots.IsFull, Is.False);
        }
    }

    [TestFixture]
    public class SessionStateMachineTests
    {
        static readonly SessionState[] AllStates =
        {
            SessionState.Offline, SessionState.Starting, SessionState.Hosting,
            SessionState.Connecting, SessionState.Connected, SessionState.Stopping,
            SessionState.Failed
        };

        static readonly (SessionState From, SessionState To)[] LegalTransitions =
        {
            (SessionState.Offline,    SessionState.Starting),
            (SessionState.Offline,    SessionState.Connecting),
            (SessionState.Starting,   SessionState.Hosting),
            (SessionState.Starting,   SessionState.Stopping),
            (SessionState.Starting,   SessionState.Failed),
            (SessionState.Hosting,    SessionState.Stopping),
            (SessionState.Hosting,    SessionState.Failed),
            (SessionState.Connecting, SessionState.Connected),
            (SessionState.Connecting, SessionState.Stopping),
            (SessionState.Connecting, SessionState.Failed),
            (SessionState.Connected,  SessionState.Stopping),
            (SessionState.Connected,  SessionState.Failed),
            (SessionState.Stopping,   SessionState.Offline),
            (SessionState.Stopping,   SessionState.Failed),
            (SessionState.Failed,     SessionState.Offline),
            (SessionState.Failed,     SessionState.Starting),
            (SessionState.Failed,     SessionState.Connecting),
        };

        [Test]
        public void EveryLegalTransition_IsAllowed()
        {
            foreach (var (from, to) in LegalTransitions)
            {
                Assert.That(SessionStateMachine.CanTransition(from, to), Is.True, $"{from} -> {to} should be legal");
            }
        }

        [Test]
        public void EveryTransitionNotInTheTable_IsRejected()
        {
            var legal = new HashSet<(SessionState, SessionState)>(LegalTransitions);

            foreach (var from in AllStates)
            {
                foreach (var to in AllStates)
                {
                    if (legal.Contains((from, to))) continue;

                    Assert.That(SessionStateMachine.CanTransition(from, to), Is.False, $"{from} -> {to} should be illegal");
                }
            }
        }

        [Test]
        public void SelfTransitions_AreAlwaysRejected()
        {
            // Guards against a duplicated callback re-firing Changed and
            // making the UI flash.
            foreach (var state in AllStates)
            {
                Assert.That(SessionStateMachine.CanTransition(state, state), Is.False, $"{state} -> {state}");
            }
        }

        [Test]
        public void HostCannotBecomeClient_WithoutGoingOfflineFirst()
        {
            Assert.That(SessionStateMachine.CanTransition(SessionState.Hosting, SessionState.Connecting), Is.False);
            Assert.That(SessionStateMachine.CanTransition(SessionState.Connected, SessionState.Starting), Is.False);
        }

        [Test]
        public void IsInSession_IsTrueOnlyWhereTrafficCanFlow()
        {
            Assert.That(SessionStateMachine.IsInSession(SessionState.Hosting), Is.True);
            Assert.That(SessionStateMachine.IsInSession(SessionState.Connected), Is.True);

            Assert.That(SessionStateMachine.IsInSession(SessionState.Offline), Is.False);
            Assert.That(SessionStateMachine.IsInSession(SessionState.Starting), Is.False);
            Assert.That(SessionStateMachine.IsInSession(SessionState.Connecting), Is.False);
            Assert.That(SessionStateMachine.IsInSession(SessionState.Stopping), Is.False);
            Assert.That(SessionStateMachine.IsInSession(SessionState.Failed), Is.False);
        }

        [Test]
        public void IsLive_CoversEveryStateExceptOfflineAndFailed()
        {
            foreach (var state in AllStates)
            {
                var expected = state != SessionState.Offline && state != SessionState.Failed;
                Assert.That(SessionStateMachine.IsLive(state), Is.EqualTo(expected), $"IsLive({state})");
            }
        }

        [Test]
        public void IsBusy_MarksTheStatesWhereTheUiShouldSpin()
        {
            Assert.That(SessionStateMachine.IsBusy(SessionState.Starting), Is.True);
            Assert.That(SessionStateMachine.IsBusy(SessionState.Connecting), Is.True);
            Assert.That(SessionStateMachine.IsBusy(SessionState.Stopping), Is.True);

            Assert.That(SessionStateMachine.IsBusy(SessionState.Hosting), Is.False);
            Assert.That(SessionStateMachine.IsBusy(SessionState.Connected), Is.False);
            Assert.That(SessionStateMachine.IsBusy(SessionState.Offline), Is.False);
        }
    }

    [TestFixture]
    public class SessionStatusTests
    {
        [Test]
        public void StartsOffline_WithNoFailure()
        {
            var status = new SessionStatus();

            Assert.That(status.State, Is.EqualTo(SessionState.Offline));
            Assert.That(status.FailureReason, Is.Empty);
            Assert.That(status.IsInSession, Is.False);
            Assert.That(status.IsLive, Is.False);
        }

        [Test]
        public void HostHappyPath_OfflineToHostingToOffline()
        {
            var status = new SessionStatus();

            Assert.That(status.TryTransition(SessionState.Starting, out _), Is.True);
            Assert.That(status.TryTransition(SessionState.Hosting, out _), Is.True);
            Assert.That(status.IsInSession, Is.True);
            Assert.That(status.TryTransition(SessionState.Stopping, out _), Is.True);
            Assert.That(status.TryTransition(SessionState.Offline, out _), Is.True);

            Assert.That(status.State, Is.EqualTo(SessionState.Offline));
        }

        [Test]
        public void ClientHappyPath_OfflineToConnectedToOffline()
        {
            var status = new SessionStatus();

            Assert.That(status.TryTransition(SessionState.Connecting, out _), Is.True);
            Assert.That(status.TryTransition(SessionState.Connected, out _), Is.True);
            Assert.That(status.IsInSession, Is.True);
            Assert.That(status.TryTransition(SessionState.Stopping, out _), Is.True);
            Assert.That(status.TryTransition(SessionState.Offline, out _), Is.True);

            Assert.That(status.State, Is.EqualTo(SessionState.Offline));
        }

        [Test]
        public void IllegalTransition_IsRefusedAndExplained_WithoutChangingState()
        {
            var status = new SessionStatus();

            Assert.That(status.TryTransition(SessionState.Connected, out var error), Is.False);
            Assert.That(error, Does.Contain("Offline"));
            Assert.That(error, Does.Contain("Connected"));
            Assert.That(status.State, Is.EqualTo(SessionState.Offline));
        }

        [Test]
        public void LeavingWhileConnecting_IsLegal()
        {
            // The user pressed Leave while the spinner was still up. This is
            // the transition that gets forgotten and then hangs the menu.
            var status = new SessionStatus();
            status.TryTransition(SessionState.Connecting, out _);

            Assert.That(status.TryTransition(SessionState.Stopping, out _), Is.True);
        }

        [Test]
        public void Fail_RecordsAReason_AndIsReachableFromEveryLiveState()
        {
            foreach (var live in new[] { SessionState.Starting, SessionState.Connecting, SessionState.Stopping })
            {
                var status = new SessionStatus();
                status.TryTransition(live == SessionState.Starting ? SessionState.Starting : SessionState.Connecting, out _);
                if (live == SessionState.Stopping) status.TryTransition(SessionState.Stopping, out _);

                Assert.That(status.Fail("port already in use"), Is.True);
                Assert.That(status.State, Is.EqualTo(SessionState.Failed));
                Assert.That(status.FailureReason, Is.EqualTo("port already in use"));
            }
        }

        [Test]
        public void Fail_FromOffline_IsIgnored()
        {
            // A late transport error arriving after a clean shutdown must
            // not put an error banner back on the main menu.
            var status = new SessionStatus();

            Assert.That(status.Fail("stale error"), Is.False);
            Assert.That(status.State, Is.EqualTo(SessionState.Offline));
            Assert.That(status.FailureReason, Is.Empty);
        }

        [Test]
        public void Fail_WithNoReason_StillProducesSomethingShowable()
        {
            var status = new SessionStatus();
            status.TryTransition(SessionState.Starting, out _);

            status.Fail(null);

            Assert.That(status.FailureReason, Is.Not.Empty);
        }

        [Test]
        public void Acknowledge_ClearsAFailureBackToOffline()
        {
            var status = new SessionStatus();
            status.TryTransition(SessionState.Connecting, out _);
            status.Fail("host refused");

            status.Acknowledge();

            Assert.That(status.State, Is.EqualTo(SessionState.Offline));
            Assert.That(status.FailureReason, Is.Empty);
        }

        [Test]
        public void Acknowledge_DoesNothingOutsideOfFailed()
        {
            var status = new SessionStatus();
            status.TryTransition(SessionState.Starting, out _);
            status.TryTransition(SessionState.Hosting, out _);

            status.Acknowledge();

            Assert.That(status.State, Is.EqualTo(SessionState.Hosting), "Acknowledge must not kick a running host offline");
        }

        [Test]
        public void RetryAfterFailure_GoesStraightBackToStartingOrConnecting()
        {
            var status = new SessionStatus();
            status.TryTransition(SessionState.Starting, out _);
            status.Fail("port in use");

            Assert.That(status.TryTransition(SessionState.Starting, out _), Is.True);
            Assert.That(status.FailureReason, Is.Empty, "a successful retry must clear the stale reason");
        }

        [Test]
        public void Changed_FiresWithPreviousAndCurrent()
        {
            var status = new SessionStatus();
            var seen = new List<(SessionState, SessionState)>();
            status.Changed += (from, to) => seen.Add((from, to));

            status.TryTransition(SessionState.Starting, out _);
            status.TryTransition(SessionState.Hosting, out _);

            Assert.That(seen, Is.EqualTo(new[]
            {
                (SessionState.Offline, SessionState.Starting),
                (SessionState.Starting, SessionState.Hosting)
            }));
        }

        [Test]
        public void Changed_DoesNotFireOnARefusedTransition()
        {
            var status = new SessionStatus();
            var fired = 0;
            status.Changed += (_, __) => fired++;

            status.TryTransition(SessionState.Connected, out _);

            Assert.That(fired, Is.Zero);
        }

        [Test]
        public void ResetToOffline_WorksFromAnyState_AndAnnouncesTheChange()
        {
            var status = new SessionStatus();
            status.TryTransition(SessionState.Connecting, out _);
            status.TryTransition(SessionState.Connected, out _);
            var fired = 0;
            status.Changed += (_, __) => fired++;

            status.ResetToOffline();

            Assert.That(status.State, Is.EqualTo(SessionState.Offline));
            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void ResetToOffline_WhenAlreadyOffline_IsSilent()
        {
            var status = new SessionStatus();
            var fired = 0;
            status.Changed += (_, __) => fired++;

            status.ResetToOffline();

            Assert.That(fired, Is.Zero);
        }
    }
}
