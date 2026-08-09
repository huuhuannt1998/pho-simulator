using System.Collections.Generic;

namespace Pho.Domain.Multiplayer
{
    /// <summary>
    /// Why a would-be player was let in, or wasn't.
    ///
    /// <see cref="SessionJoinVerdict.AlreadySeated"/> is a success, not a
    /// failure -- see <see cref="SessionSlots.TryReserve"/>.
    /// </summary>
    public enum SessionJoinVerdict
    {
        Approved,
        AlreadySeated,
        SessionFull,
        NotAcceptingJoins
    }

    /// <summary>
    /// Decides WHO IS ALLOWED IN. The four-player cap for a co-op session,
    /// and the reason attached to every refusal.
    ///
    /// <b>Why this is pure C# and not part of the NetworkBehaviour:</b> the
    /// same reasoning that put <see cref="CarryRegistry"/> here. "Is there
    /// room for a fifth player, and what do we tell them if not" is a rule,
    /// not a transport concern. Keeping it pure means the cap is verified by
    /// the sub-second <c>dotnet test</c> loop rather than by launching five
    /// game clients and watching what happens to the last one.
    ///
    /// <b>Refusals are always explained.</b> Every rejection path produces a
    /// <see cref="SessionJoinVerdict"/> that <see cref="Describe"/> turns
    /// into a sentence a player can read. A silently dropped connection is
    /// indistinguishable from a crash, a firewall, or a bad build, and
    /// generates a bug report every time; "This session is full (4/4)" does
    /// not.
    ///
    /// <b>Single-authority assumption.</b> Exactly one instance exists per
    /// session, on the host, alongside the <see cref="CarryRegistry"/>.
    /// Clients never construct one.
    ///
    /// Ids are opaque <see cref="ulong"/>s so this stays engine-free. In
    /// practice they are Netcode client ids, but nothing here depends on it.
    /// </summary>
    public sealed class SessionSlots
    {
        /// <summary>Four players, per the co-op design. Passed to the constructor rather than hard-coded so tests can prove boundary behaviour at other sizes.</summary>
        public const int DefaultMaxPlayers = 4;

        readonly List<ulong> _seated = new List<ulong>();

        public SessionSlots(int maxPlayers = DefaultMaxPlayers)
        {
            MaxPlayers = maxPlayers < 1 ? 1 : maxPlayers;
        }

        public int MaxPlayers { get; }

        public int Count => _seated.Count;

        public bool IsFull => _seated.Count >= MaxPlayers;

        public int FreeSlots => MaxPlayers - _seated.Count;

        /// <summary>
        /// Whether new arrivals are being considered at all. The host closes
        /// this while shutting down so a connection that lands mid-teardown
        /// is refused with an explanation instead of being half-admitted to
        /// a session that is going away.
        /// </summary>
        public bool AcceptingJoins { get; set; } = true;

        public IReadOnlyList<ulong> Seated => _seated;

        /// <summary>
        /// Attempts to seat <paramref name="actorId"/>.
        ///
        /// Returns true if the actor occupies a slot afterwards. Reserving
        /// for someone already seated succeeds and reports
        /// <see cref="SessionJoinVerdict.AlreadySeated"/> -- idempotent for
        /// the same reason <see cref="CarryRegistry.TryClaim"/> is: a
        /// duplicated or retried approval must not be able to refuse a
        /// player who is already legitimately in the game.
        /// </summary>
        public bool TryReserve(ulong actorId, out SessionJoinVerdict verdict)
        {
            if (_seated.Contains(actorId))
            {
                verdict = SessionJoinVerdict.AlreadySeated;
                return true;
            }

            if (!AcceptingJoins)
            {
                verdict = SessionJoinVerdict.NotAcceptingJoins;
                return false;
            }

            if (IsFull)
            {
                verdict = SessionJoinVerdict.SessionFull;
                return false;
            }

            _seated.Add(actorId);
            verdict = SessionJoinVerdict.Approved;
            return true;
        }

        /// <summary>
        /// Frees an actor's slot. Returns false if they weren't seated --
        /// normal, not an error: a disconnect callback can fire for a
        /// connection that was refused approval and therefore never took a
        /// slot in the first place. Double-releasing must never free
        /// somebody else's seat.
        /// </summary>
        public bool Release(ulong actorId) => _seated.Remove(actorId);

        public bool Contains(ulong actorId) => _seated.Contains(actorId);

        public void Clear() => _seated.Clear();

        /// <summary>
        /// The sentence shown to a refused player. Written for the person
        /// staring at the screen, not for the log: it says what happened and
        /// what they can do about it.
        /// </summary>
        public string Describe(SessionJoinVerdict verdict)
        {
            switch (verdict)
            {
                case SessionJoinVerdict.Approved:
                case SessionJoinVerdict.AlreadySeated:
                    return "Joined.";
                case SessionJoinVerdict.SessionFull:
                    return $"This kitchen is full ({MaxPlayers}/{MaxPlayers} players). Ask your friend to let you know when someone leaves.";
                case SessionJoinVerdict.NotAcceptingJoins:
                    return "This session is closing and is not accepting new players.";
                default:
                    return "Could not join this session.";
            }
        }
    }
}
