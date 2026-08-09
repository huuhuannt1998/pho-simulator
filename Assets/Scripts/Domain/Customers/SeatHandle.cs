using System;

namespace Pho.Domain.Customers
{
    /// <summary>
    /// Stable, comparable token representing one claimed seat at one table.
    /// Not specified beyond name/usage sites in architecture.md section 6.2
    /// (<see cref="ICustomerWorld.TryClaimSeat"/> /
    /// <see cref="ICustomerWorld.ReleaseSeat"/> /
    /// <see cref="ICustomerWorld.PlaceOrder"/> all refer to "this specific
    /// seat" via this type) -- designed here as a table ID + seat index
    /// within that table, matching the GDD's "Table 3" framing and the
    /// equality-member convention <c>Identity/Ids.cs</c> uses for its typed
    /// IDs. A small serializable-friendly value type, deliberately carrying
    /// no world position -- resolving a seat to a Vec3 is a Unity/world
    /// concern owned by the concrete <c>CustomerAgent</c>, not this pure type.
    /// </summary>
    public readonly struct SeatHandle : IEquatable<SeatHandle>
    {
        public readonly string TableId;
        public readonly int SeatIndex;

        public SeatHandle(string tableId, int seatIndex)
        {
            TableId = tableId;
            SeatIndex = seatIndex;
        }

        public static readonly SeatHandle None = new SeatHandle(null, -1);

        public bool IsEmpty => string.IsNullOrEmpty(TableId);

        public bool Equals(SeatHandle other) =>
            string.Equals(TableId, other.TableId, StringComparison.Ordinal) && SeatIndex == other.SeatIndex;

        public override bool Equals(object obj) => obj is SeatHandle other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (TableId == null ? 0 : StringComparer.Ordinal.GetHashCode(TableId));
                hash = hash * 31 + SeatIndex;
                return hash;
            }
        }

        public static bool operator ==(SeatHandle a, SeatHandle b) => a.Equals(b);
        public static bool operator !=(SeatHandle a, SeatHandle b) => !a.Equals(b);

        public override string ToString() => IsEmpty ? "<none>" : $"{TableId}#{SeatIndex}";
    }
}
