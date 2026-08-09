using System;
using System.Collections.Generic;

namespace Pho.Domain.Multiplayer
{
    /// <summary>
    /// Where a player's multiplayer session currently is.
    ///
    /// Deliberately one enum for both host and client: from the UI's point
    /// of view "we are trying" / "we are in" / "it broke" is the same
    /// question regardless of which side you are, and a split enum would
    /// double every switch statement in the menu for no benefit.
    /// </summary>
    public enum SessionState
    {
        /// <summary>Nothing running. Single-player, or back at the menu.</summary>
        Offline,

        /// <summary>We are bringing a host up. Transport is starting.</summary>
        Starting,

        /// <summary>We are the host and accepting joins.</summary>
        Hosting,

        /// <summary>We are a client dialling someone else's host.</summary>
        Connecting,

        /// <summary>We are a client, in the session.</summary>
        Connected,

        /// <summary>Tearing down, either side.</summary>
        Stopping,

        /// <summary>The last attempt failed, or we were dropped. Carries a reason for the UI; must be acknowledged back to <see cref="Offline"/> or retried.</summary>
        Failed
    }

    /// <summary>
    /// The legal moves between <see cref="SessionState"/>s.
    ///
    /// <b>Why a table and not scattered <c>if</c>s:</b> the bugs in session
    /// lifecycle code are almost never "the happy path is wrong". They are
    /// "the user hit Leave while we were still connecting", "the transport
    /// reported failure after we already shut down", "a disconnect callback
    /// arrived twice". Every one of those is an illegal transition, and a
    /// table makes each of them a one-line test instead of a bug found by a
    /// playtester in week three.
    ///
    /// Modelled directly on <c>OrderStateMachine</c> so the two read the
    /// same way.
    /// </summary>
    public static class SessionStateMachine
    {
        static readonly Dictionary<SessionState, SessionState[]> Allowed =
            new Dictionary<SessionState, SessionState[]>
            {
                // Both entry points, plus Stopping so a shutdown request that
                // arrives with nothing running is absorbed rather than thrown.
                [SessionState.Offline] = new[] { SessionState.Starting, SessionState.Connecting },

                // Starting can fail (port in use, Steam not running) or be
                // cancelled by a user who changed their mind mid-spinner.
                [SessionState.Starting] = new[] { SessionState.Hosting, SessionState.Stopping, SessionState.Failed },

                // A host never becomes a client without going Offline first.
                [SessionState.Hosting] = new[] { SessionState.Stopping, SessionState.Failed },

                [SessionState.Connecting] = new[] { SessionState.Connected, SessionState.Stopping, SessionState.Failed },

                // Failed from Connected is the "host closed the game" /
                // "you were kicked" path -- the player needs to be told, so
                // it is not a silent drop to Offline.
                [SessionState.Connected] = new[] { SessionState.Stopping, SessionState.Failed },

                [SessionState.Stopping] = new[] { SessionState.Offline, SessionState.Failed },

                // Retry without a trip through Offline: the menu's "Try
                // again" button should not need two state changes.
                [SessionState.Failed] = new[] { SessionState.Offline, SessionState.Starting, SessionState.Connecting },
            };

        public static bool CanTransition(SessionState from, SessionState to)
        {
            if (from == to) return false;
            return Allowed.TryGetValue(from, out var next) && Array.IndexOf(next, to) >= 0;
        }

        /// <summary>True while a session exists in any form -- starting, running, or tearing down. The gameplay layer uses this to decide whether it is in "multiplayer" at all.</summary>
        public static bool IsLive(SessionState state) =>
            state == SessionState.Starting || state == SessionState.Hosting ||
            state == SessionState.Connecting || state == SessionState.Connected ||
            state == SessionState.Stopping;

        /// <summary>True once traffic can actually flow. The only state in which gameplay RPCs are meaningful.</summary>
        public static bool IsInSession(SessionState state) =>
            state == SessionState.Hosting || state == SessionState.Connected;

        /// <summary>True while an attempt is in flight -- the UI should show a spinner and disable both Host and Join.</summary>
        public static bool IsBusy(SessionState state) =>
            state == SessionState.Starting || state == SessionState.Connecting || state == SessionState.Stopping;
    }

    /// <summary>
    /// A session's current state plus the reason it last failed, with the
    /// transition rules enforced on every write.
    ///
    /// This is the whole connection-state machine as a testable object: the
    /// networking layer above owns a single instance and is not allowed to
    /// assign the state directly. An illegal transition is reported (and
    /// refused) rather than silently applied, because a session that
    /// believes it is Connected when it is not produces symptoms miles away
    /// from the cause.
    /// </summary>
    public sealed class SessionStatus
    {
        public SessionState State { get; private set; } = SessionState.Offline;

        /// <summary>Player-facing explanation for the current <see cref="SessionState.Failed"/>. Empty in every other state.</summary>
        public string FailureReason { get; private set; } = string.Empty;

        /// <summary>Raised after a successful transition, with (previous, current). The UI listens to this rather than polling.</summary>
        public event Action<SessionState, SessionState> Changed;

        public bool IsInSession => SessionStateMachine.IsInSession(State);
        public bool IsLive => SessionStateMachine.IsLive(State);
        public bool IsBusy => SessionStateMachine.IsBusy(State);

        /// <summary>
        /// Moves to <paramref name="to"/> if the table allows it.
        ///
        /// Returns false (and changes nothing) otherwise, with
        /// <paramref name="error"/> explaining which transition was refused
        /// -- callers log it rather than throw, because these arrive from
        /// asynchronous transport callbacks where an exception would tear
        /// down unrelated work.
        /// </summary>
        public bool TryTransition(SessionState to, out string error)
        {
            if (!SessionStateMachine.CanTransition(State, to))
            {
                error = $"Illegal session transition {State} -> {to}.";
                return false;
            }

            var from = State;
            State = to;
            if (to != SessionState.Failed) FailureReason = string.Empty;

            error = string.Empty;
            Changed?.Invoke(from, to);
            return true;
        }

        /// <summary>
        /// Moves to <see cref="SessionState.Failed"/> carrying a
        /// player-readable reason.
        ///
        /// Failing from a state that cannot legally fail (Offline) is a
        /// no-op returning false: a late transport error arriving after a
        /// clean shutdown must not resurrect an error banner on the main
        /// menu.
        /// </summary>
        public bool Fail(string reason)
        {
            var from = State;
            if (!SessionStateMachine.CanTransition(from, SessionState.Failed)) return false;

            State = SessionState.Failed;
            FailureReason = string.IsNullOrEmpty(reason) ? "The session ended unexpectedly." : reason;
            Changed?.Invoke(from, SessionState.Failed);
            return true;
        }

        /// <summary>Clears a failure back to <see cref="SessionState.Offline"/>. Safe to call when already Offline.</summary>
        public void Acknowledge()
        {
            if (State == SessionState.Offline) return;
            if (State != SessionState.Failed) return;

            State = SessionState.Offline;
            FailureReason = string.Empty;
            Changed?.Invoke(SessionState.Failed, SessionState.Offline);
        }

        /// <summary>
        /// Forces the state back to <see cref="SessionState.Offline"/>,
        /// bypassing the table. Reserved for hard teardown (leaving play
        /// mode, the object being destroyed) where there is nothing left to
        /// be consistent with. Not part of the normal flow.
        /// </summary>
        public void ResetToOffline()
        {
            var from = State;
            State = SessionState.Offline;
            FailureReason = string.Empty;
            if (from != SessionState.Offline) Changed?.Invoke(from, SessionState.Offline);
        }
    }
}
