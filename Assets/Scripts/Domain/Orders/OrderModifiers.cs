using System;

namespace Pho.Domain.Orders
{
    /// <summary>
    /// Portion size choice on an order. Kept as a 3-value enum (not a raw
    /// float multiplier) so the order board / kitchen UI can render a fixed,
    /// unambiguous label -- matches the GDD's size examples (small/medium/large).
    /// </summary>
    public enum PortionSize
    {
        Small, Medium, Large
    }

    /// <summary>
    /// Spice level choice on an order. Matches the GDD §"spice: medium" example.
    /// </summary>
    public enum SpiceLevel
    {
        Mild, Medium, Hot
    }

    /// <summary>
    /// Per-order customization. architecture.md §6.1 names this inline as
    /// "noOnion, extraMeat, size, spice" without specifying the exact shape --
    /// this is the M8 design: a small immutable value type so an
    /// <see cref="OrderModel"/> can carry it by value with no aliasing risk.
    /// </summary>
    public readonly struct OrderModifiers : IEquatable<OrderModifiers>
    {
        public readonly bool NoOnion;
        public readonly bool ExtraMeat;
        public readonly PortionSize Size;
        public readonly SpiceLevel Spice;

        public OrderModifiers(bool noOnion, bool extraMeat, PortionSize size, SpiceLevel spice)
        {
            NoOnion = noOnion;
            ExtraMeat = extraMeat;
            Size = size;
            Spice = spice;
        }

        /// <summary>No modifiers, medium size, medium spice -- the common case.</summary>
        public static readonly OrderModifiers Default = new OrderModifiers(false, false, PortionSize.Medium, SpiceLevel.Medium);

        public bool Equals(OrderModifiers other) =>
            NoOnion == other.NoOnion && ExtraMeat == other.ExtraMeat && Size == other.Size && Spice == other.Spice;

        public override bool Equals(object obj) => obj is OrderModifiers other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + NoOnion.GetHashCode();
                hash = hash * 31 + ExtraMeat.GetHashCode();
                hash = hash * 31 + (int)Size;
                hash = hash * 31 + (int)Spice;
                return hash;
            }
        }

        public static bool operator ==(OrderModifiers a, OrderModifiers b) => a.Equals(b);
        public static bool operator !=(OrderModifiers a, OrderModifiers b) => !a.Equals(b);
    }
}
