using System;
using Pho.Domain.DayCycle;
using Unity.Netcode;

namespace Pho.Net.State
{
    /// <summary>
    /// Money on the wire.
    ///
    /// The project's money type is <c>decimal</c> everywhere and deliberately
    /// so -- architecture.md section 4 decision 3, "float money produces
    /// $9.499999 in the daily report". Netcode cannot serialize
    /// <c>decimal</c>: it is a 16-byte managed-friendly type with no
    /// registered serializer, and forcing one by memcpy would ship its
    /// internal representation across a version boundary.
    ///
    /// So money travels as integer CENTS in a <c>long</c>. That is exact for
    /// every value this game produces (prices come from
    /// <c>RecipeData.BasePrice</c> and tips are computed from them; nothing
    /// deals in fractions of a cent), it is 8 bytes, and it round-trips
    /// without the accumulating error the decimal convention exists to
    /// prevent. A long holds ~92 quadrillion dollars in cents, which is a
    /// comfortable margin for a noodle shop.
    ///
    /// Rounding is away-from-zero at the cent, applied once, at the wire
    /// boundary only -- the host's own <c>EconomyService.Cash</c> is never
    /// touched. If a sub-cent value ever did exist host-side, a client would
    /// see it rounded rather than mangled, and the host would remain the
    /// source of truth.
    /// </summary>
    public static class Money
    {
        public static long ToCents(decimal amount) =>
            (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

        public static decimal FromCents(long cents) => cents / 100m;
    }

    /// <summary>
    /// Day number plus phase, replicated as ONE value because they are one
    /// fact. Sending them as two separate <c>NetworkVariable</c>s would let a
    /// client briefly observe "day 4, still Open" during the frame the day
    /// rolls over -- a state the host never occupied. A struct cannot tear.
    ///
    /// <see cref="PhaseCode"/> is a byte rather than the <c>DayPhase</c> enum
    /// so the wire format does not silently change if the enum's underlying
    /// type is ever edited; <see cref="Phase"/> does the conversion and
    /// refuses to invent a value it does not recognise.
    /// </summary>
    public struct DayPhaseState : INetworkSerializable, IEquatable<DayPhaseState>
    {
        public int Day;
        public byte PhaseCode;

        public DayPhaseState(int day, DayPhase phase)
        {
            Day = day;
            PhaseCode = (byte)phase;
        }

        /// <summary>
        /// The decoded phase. An unrecognised code (only reachable from a
        /// version-mismatched peer) reads as <see cref="DayPhase.Prep"/>
        /// rather than a cast to an undefined enum value -- the restaurant
        /// being shut is the safe reading of "I don't know".
        /// </summary>
        public DayPhase Phase =>
            Enum.IsDefined(typeof(DayPhase), (int)PhaseCode) ? (DayPhase)PhaseCode : DayPhase.Prep;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Day);
            serializer.SerializeValue(ref PhaseCode);
        }

        public bool Equals(DayPhaseState other) => Day == other.Day && PhaseCode == other.PhaseCode;

        public override bool Equals(object obj) => obj is DayPhaseState other && Equals(other);

        public override int GetHashCode() => (Day * 397) ^ PhaseCode;

        public override string ToString() => $"Day {Day} ({Phase})";
    }

    /// <summary>
    /// The aggregate cleanliness reading, carrying all three numbers
    /// <c>CleanlinessChanged</c> does.
    ///
    /// Grouped for the same anti-tearing reason as
    /// <see cref="DayPhaseState"/>, and here it is not theoretical: a client
    /// that received a new dirty-count before the recomputed float would
    /// publish a <c>CleanlinessChanged</c> whose meter and whose counts
    /// contradict each other, and the event's own doc comment promises
    /// subscribers that the derived value and the two numbers it came from
    /// agree.
    /// </summary>
    public struct CleanlinessState : INetworkSerializable, IEquatable<CleanlinessState>
    {
        public float Cleanliness01;
        public int DirtyTableCount;
        public int TotalTables;

        public CleanlinessState(float cleanliness01, int dirtyTableCount, int totalTables)
        {
            Cleanliness01 = cleanliness01;
            DirtyTableCount = dirtyTableCount;
            TotalTables = totalTables;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Cleanliness01);
            serializer.SerializeValue(ref DirtyTableCount);
            serializer.SerializeValue(ref TotalTables);
        }

        public bool Equals(CleanlinessState other) =>
            Cleanliness01.Equals(other.Cleanliness01)
            && DirtyTableCount == other.DirtyTableCount
            && TotalTables == other.TotalTables;

        public override bool Equals(object obj) => obj is CleanlinessState other && Equals(other);

        public override int GetHashCode() =>
            (Cleanliness01.GetHashCode() * 397 ^ DirtyTableCount) * 397 ^ TotalTables;

        /// <summary>
        /// The value a replicator holds before the host has ever reported
        /// anything. An empty dining room with nothing dirty reads as
        /// spotless, matching <c>CleanlinessModel</c>'s own treatment of a
        /// zero-table room -- so a client that connects before the host has
        /// registered its tables shows "clean", not "filthy".
        /// </summary>
        public static CleanlinessState Spotless => new CleanlinessState(1f, 0, 0);
    }
}
