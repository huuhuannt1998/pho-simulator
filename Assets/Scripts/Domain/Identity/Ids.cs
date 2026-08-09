using System;
using Newtonsoft.Json;

namespace Pho.Domain.Identity
{
    // Typed string IDs. Purpose: prevent "passed a RecipeId where an
    // IngredientId was expected" bugs at compile time. Format convention
    // (enforced by an EditMode content-validation test, not here):
    // ^[a-z]+\.[a-z0-9_]+$, e.g. "ing.beef_brisket", "rec.pho_tai".
    // IDs are authored on the ScriptableObject and must equal the asset
    // filename; they are immutable once committed.

    [JsonConverter(typeof(StringIdConverter<IngredientId>))]
    public readonly struct IngredientId : IEquatable<IngredientId>, IStringId
    {
        public string Value { get; }
        public IngredientId(string value) => Value = value;
        public bool IsEmpty => string.IsNullOrEmpty(Value);
        public override string ToString() => Value ?? "<none>";
        public bool Equals(IngredientId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is IngredientId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(IngredientId a, IngredientId b) => a.Equals(b);
        public static bool operator !=(IngredientId a, IngredientId b) => !a.Equals(b);
    }

    [JsonConverter(typeof(StringIdConverter<RecipeId>))]
    public readonly struct RecipeId : IEquatable<RecipeId>, IStringId
    {
        public string Value { get; }
        public RecipeId(string value) => Value = value;
        public bool IsEmpty => string.IsNullOrEmpty(Value);
        public override string ToString() => Value ?? "<none>";
        public bool Equals(RecipeId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is RecipeId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(RecipeId a, RecipeId b) => a.Equals(b);
        public static bool operator !=(RecipeId a, RecipeId b) => !a.Equals(b);
    }

    [JsonConverter(typeof(StringIdConverter<EquipmentId>))]
    public readonly struct EquipmentId : IEquatable<EquipmentId>, IStringId
    {
        public string Value { get; }
        public EquipmentId(string value) => Value = value;
        public bool IsEmpty => string.IsNullOrEmpty(Value);
        public override string ToString() => Value ?? "<none>";
        public bool Equals(EquipmentId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is EquipmentId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(EquipmentId a, EquipmentId b) => a.Equals(b);
        public static bool operator !=(EquipmentId a, EquipmentId b) => !a.Equals(b);
    }

    [JsonConverter(typeof(StringIdConverter<ArchetypeId>))]
    public readonly struct ArchetypeId : IEquatable<ArchetypeId>, IStringId
    {
        public string Value { get; }
        public ArchetypeId(string value) => Value = value;
        public bool IsEmpty => string.IsNullOrEmpty(Value);
        public override string ToString() => Value ?? "<none>";
        public bool Equals(ArchetypeId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ArchetypeId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(ArchetypeId a, ArchetypeId b) => a.Equals(b);
        public static bool operator !=(ArchetypeId a, ArchetypeId b) => !a.Equals(b);
    }

    // OrderId and CustomerId are runtime-minted (not authored content), so they
    // carry an opaque instance token rather than a "category.name" content ID.
    // Still string-backed for save-file readability and JSON round-tripping.

    [JsonConverter(typeof(StringIdConverter<OrderId>))]
    public readonly struct OrderId : IEquatable<OrderId>, IStringId
    {
        public string Value { get; }
        public OrderId(string value) => Value = value;
        public bool IsEmpty => string.IsNullOrEmpty(Value);
        public override string ToString() => Value ?? "<none>";
        public bool Equals(OrderId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is OrderId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(OrderId a, OrderId b) => a.Equals(b);
        public static bool operator !=(OrderId a, OrderId b) => !a.Equals(b);
    }

    [JsonConverter(typeof(StringIdConverter<CustomerId>))]
    public readonly struct CustomerId : IEquatable<CustomerId>, IStringId
    {
        public string Value { get; }
        public CustomerId(string value) => Value = value;
        public bool IsEmpty => string.IsNullOrEmpty(Value);
        public override string ToString() => Value ?? "<none>";
        public bool Equals(CustomerId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is CustomerId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(CustomerId a, CustomerId b) => a.Equals(b);
        public static bool operator !=(CustomerId a, CustomerId b) => !a.Equals(b);
    }
}
