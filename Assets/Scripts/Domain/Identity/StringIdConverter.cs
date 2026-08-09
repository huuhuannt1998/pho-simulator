using System;
using Newtonsoft.Json;

namespace Pho.Domain.Identity
{
    /// <summary>
    /// Serializes any typed string-ID struct as a bare JSON string (e.g. "ing.beef_brisket")
    /// instead of an object wrapper, so save files stay hand-readable.
    /// </summary>
    public sealed class StringIdConverter<T> : JsonConverter<T> where T : struct, IStringId
    {
        public override void WriteJson(JsonWriter writer, T value, JsonSerializer serializer)
        {
            writer.WriteValue(value.Value);
        }

        public override T ReadJson(JsonReader reader, Type objectType, T existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var raw = reader.Value as string;
            return (T)Activator.CreateInstance(typeof(T), raw);
        }
    }

    /// <summary>Common shape every typed string ID exposes, so the converter can be generic.</summary>
    public interface IStringId
    {
        string Value { get; }
    }
}
