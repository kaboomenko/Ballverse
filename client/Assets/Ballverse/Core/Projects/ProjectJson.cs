using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Projects
{
    /// <summary>
    /// Reads and writes project.json with <see cref="CoreJson"/> settings (spec §7.4). Parsing keeps ISO-8601 timestamps
    /// as strings (no DateTime conversion) so they round-trip byte-exactly.
    /// </summary>
    public static class ProjectJson
    {
        static readonly JsonSerializer s_serializer = JsonSerializer.Create(CoreJson.Settings);
        static readonly JsonSerializer s_cloneSerializer = CreateCloneSerializer();

        /// <summary>Indented JSON of the document.</summary>
        public static string Serialize(ProjectDocument document)
        {
            return CoreJson.Serialize(document);
        }

        /// <summary>Parses text into a JSON object. Throws <see cref="JsonException"/> on malformed JSON, a non-object root or trailing content.</summary>
        public static JObject ParseObject(string json)
        {
            if (json == null) throw new JsonReaderException("Input is null.");
            using (var reader = new JsonTextReader(new StringReader(json)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                reader.FloatParseHandling = FloatParseHandling.Double;
                var token = JToken.ReadFrom(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                while (reader.Read())
                {
                    if (reader.TokenType != JsonToken.Comment) throw new JsonReaderException("Unexpected content after the root object.");
                }
                var obj = token as JObject;
                if (obj == null) throw new JsonReaderException("Root is not a JSON object.");
                return obj;
            }
        }

        /// <summary>Converts a (migrated) JSON object into the DTO. Throws <see cref="JsonException"/> on type mismatches.</summary>
        public static ProjectDocument ToDocument(JObject obj)
        {
            var doc = obj.ToObject<ProjectDocument>(s_serializer);
            if (doc == null) throw new JsonSerializationException("Project document is null.");
            return doc;
        }

        /// <summary>DTO to JSON object with <see cref="CoreJson"/> settings.</summary>
        public static JObject ToJObject(ProjectDocument document)
        {
            return JObject.FromObject(document, s_serializer);
        }

        /// <summary>Parses text straight into a DTO, without migration or sanitizing.</summary>
        public static ProjectDocument Deserialize(string json)
        {
            return ToDocument(ParseObject(json));
        }

        /// <summary>Deep copy via the JSON contract; null sections stay null (not replaced by constructor defaults).</summary>
        public static ProjectDocument Clone(ProjectDocument document)
        {
            if (document == null) throw new System.ArgumentNullException(nameof(document));
            var copy = JObject.FromObject(document, s_cloneSerializer).ToObject<ProjectDocument>(s_cloneSerializer);
            if (copy == null) throw new JsonSerializationException("Clone produced null.");
            return copy;
        }

        static JsonSerializer CreateCloneSerializer()
        {
            var serializer = JsonSerializer.Create(CoreJson.Settings);
            serializer.NullValueHandling = NullValueHandling.Include;
            return serializer;
        }
    }
}
