using System;
using Newtonsoft.Json;
using Pho.Domain.Identity;

namespace Pho.Domain.Expansion
{
    /// <summary>
    /// Typed string ID for a purchasable building lot -- one adjacent unit of
    /// the growing complex, e.g. <c>"lot.unit_2"</c>.
    ///
    /// Declared HERE rather than appended to <c>Domain/Identity/Ids.cs</c>
    /// because that file is shared, frozen M1 contract surface and three
    /// other agents are editing concurrently (architecture.md section 10's
    /// conflict table). It is byte-for-byte the same shape as every other
    /// typed ID in Ids.cs -- ordinal string equality, empty check, bare-string
    /// JSON via <see cref="StringIdConverter{T}"/> -- so a later integration
    /// pass can move it into Ids.cs with no behavioural change at all.
    ///
    /// ID format follows the project convention <c>^[a-z]+\.[a-z0-9_]+$</c>
    /// with the <c>lot.</c> prefix. That prefix is load-bearing twice over:
    /// it namespaces lot ids inside the shared <c>ProgressionDto.flags</c>
    /// dictionary used for persistence (see ExpansionService.Capture), and it
    /// keeps lot ids visually distinct from ingredient/equipment ids in a
    /// hand-read save file.
    /// </summary>
    [JsonConverter(typeof(StringIdConverter<LotId>))]
    public readonly struct LotId : IEquatable<LotId>, IStringId
    {
        /// <summary>Prefix every well-formed lot id carries. See the class doc.</summary>
        public const string Prefix = "lot.";

        public string Value { get; }

        public LotId(string value) => Value = value;

        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public override string ToString() => Value ?? "<none>";

        public bool Equals(LotId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is LotId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(LotId a, LotId b) => a.Equals(b);
        public static bool operator !=(LotId a, LotId b) => !a.Equals(b);
    }
}
