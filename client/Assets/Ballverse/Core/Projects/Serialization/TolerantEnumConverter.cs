using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Ballverse.Core.Projects
{
    /// <summary>
    /// String enum converter for project DTOs that maps an unknown enum <em>name</em> to an undefined numeric value instead of
    /// throwing, so <see cref="ProjectValidator"/> resets it to the default with an <see cref="IssueCode.InvalidValue"/> warning,
    /// exactly as it does for an unknown number. One unknown value (e.g. from a newer build that added an enum member without a
    /// schema bump) must not make the whole project unloadable (spec §3.9 compatibility, §8 ST6). Tokens of other types
    /// (bool, object…) still fail.
    /// </summary>
    public sealed class TolerantEnumConverter : StringEnumConverter
    {
        public TolerantEnumConverter()
        {
        }

        /// <param name="namingStrategyType">Naming strategy for written names, e.g. camelCase.</param>
        public TolerantEnumConverter(Type namingStrategyType)
            : base(namingStrategyType)
        {
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.String) return base.ReadJson(reader, objectType, existingValue, serializer);
            try
            {
                return base.ReadJson(reader, objectType, existingValue, serializer);
            }
            catch (JsonSerializationException)
            {
                return UndefinedValue(Nullable.GetUnderlyingType(objectType) ?? objectType);
            }
        }

        /// <summary>The largest negative value (from -1 down) that is not a member of <paramref name="enumType"/>.</summary>
        internal static object UndefinedValue(Type enumType)
        {
            for (int v = -1; ; v--)
            {
                var value = Enum.ToObject(enumType, v);
                if (!Enum.IsDefined(enumType, value)) return value;
            }
        }
    }
}
