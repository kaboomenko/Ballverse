using System;
using Newtonsoft.Json;

namespace Ballverse.Core.Timeline
{
    /// <summary>
    /// Reads a nullable 64-bit integer only from a JSON integer or null token; strings, fractions, booleans, objects and
    /// integers beyond the long range throw <see cref="JsonSerializationException"/>. Used for Remote Config bitrates
    /// (spec §3.8) so a malformed payload is reported, not silently coerced.
    /// </summary>
    internal sealed class StrictInt64Converter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(long) || objectType == typeof(long?);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                if (objectType == typeof(long?)) return null;
                throw new JsonSerializationException("Expected an integer, got null.");
            }
            if (reader.TokenType == JsonToken.Integer && reader.Value is long value) return value;
            throw new JsonSerializationException("Expected a 64-bit integer, got " + reader.TokenType + ".");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null) writer.WriteNull();
            else writer.WriteValue((long)value);
        }
    }
}
