using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Melody JSON format (spec §2.7: melodies are a simple JSON list of notes and can be delivered through Remote Config):
    /// <code>{ "id": "ode_to_joy", "name": "Ode to Joy", "notes": [ { "pitch": 64, "durWeight": 1, "velocity": 0.8 } ] }</code>
    /// One rule set for every melody source, identical to the Remote Config melody entries (core/config
    /// <c>RemoteConfigParser</c>), so a melody accepted there always builds here and vice versa:
    /// <list type="bullet">
    /// <item><c>id</c>, <c>name</c>: JSON strings, not blank; stored trimmed.</item>
    /// <item><c>notes</c>: non-empty array of objects.</item>
    /// <item><c>pitch</c>: JSON number with an integral value, MIDI 0…127.</item>
    /// <item><c>durWeight</c>: JSON number, finite, &gt; 0 and representable as a positive float. Required.</item>
    /// <item><c>velocity</c>: JSON number 0…1. Required.</item>
    /// </list>
    /// Numbers given as strings (<c>"64"</c>) are rejected. Config should validate each entry with
    /// <see cref="TryParse(JToken, out Melody, out IReadOnlyList{string})"/> rather than keep its own copy of these rules.
    /// The parsers never throw on bad remote data — they return the list of problems.
    /// </summary>
    public static class MelodyJson
    {
        /// <summary>Largest JSON text accepted by <see cref="TryParse(string, out Melody, out IReadOnlyList{string})"/>, characters.</summary>
        public const int MaxJsonLength = 512 * 1024;

        sealed class MelodyDto
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public List<NoteDto> Notes { get; set; }
        }

        sealed class NoteDto
        {
            public int Pitch { get; set; }
            public float DurWeight { get; set; }
            public float Velocity { get; set; }
        }

        /// <summary>Parses and validates a melody document. On failure <paramref name="melody"/> is null and errors are non-empty.</summary>
        public static bool TryParse(string json, out Melody melody, out IReadOnlyList<string> errors)
        {
            melody = null;
            var list = new List<string>();
            errors = list;
            if (string.IsNullOrWhiteSpace(json))
            {
                list.Add("empty input");
                return false;
            }
            if (json.Length > MaxJsonLength)
            {
                list.Add("input exceeds " + MaxJsonLength.ToString(CultureInfo.InvariantCulture) + " characters");
                return false;
            }

            JToken token;
            try
            {
                token = CoreJson.Deserialize<JToken>(json);
            }
            catch (Exception ex)
            {
                // Parse boundary for remote data: any reader failure becomes an error, never an exception.
                list.Add("malformed JSON: " + ex.Message);
                return false;
            }
            return TryParse(token, out melody, out errors);
        }

        /// <summary>
        /// Validates an already parsed melody object (e.g. one entry of a Remote Config melody list). Never throws; on
        /// failure <paramref name="melody"/> is null and errors are non-empty.
        /// </summary>
        public static bool TryParse(JToken token, out Melody melody, out IReadOnlyList<string> errors)
        {
            melody = null;
            var list = new List<string>();
            errors = list;
            if (token == null || token.Type != JTokenType.Object)
            {
                list.Add("JSON is not an object");
                return false;
            }
            var obj = (JObject)token;
            string id = GetString(obj["id"]);
            string name = GetString(obj["name"]);
            ValidateHeader(id, name, list);

            JToken notesToken = obj["notes"];
            MelodyNote[] notes = null;
            if (notesToken == null || notesToken.Type != JTokenType.Array || ((JArray)notesToken).Count == 0)
            {
                list.Add("notes must contain at least one note");
            }
            else
            {
                var array = (JArray)notesToken;
                notes = new MelodyNote[array.Count];
                for (int i = 0; i < notes.Length; i++) ParseNote(array[i], i, list, out notes[i]);
            }

            if (list.Count > 0) return false;
            melody = new Melody(id, name, notes);
            return true;
        }

        /// <summary>Serializes a melody in the format accepted by the parsers.</summary>
        public static string Serialize(Melody melody, bool indented = true)
        {
            if (melody == null) throw new ArgumentNullException(nameof(melody));
            var dto = new MelodyDto { Id = melody.Id, Name = melody.Name, Notes = new List<NoteDto>(melody.Count) };
            for (int i = 0; i < melody.Count; i++)
            {
                var n = melody[i];
                dto.Notes.Add(new NoteDto { Pitch = n.Pitch, DurWeight = n.DurWeight, Velocity = n.Velocity });
            }
            return CoreJson.Serialize(dto, indented);
        }

        static void ParseNote(JToken n, int i, List<string> errors, out MelodyNote note)
        {
            note = default;
            if (n == null || n.Type != JTokenType.Object)
            {
                errors.Add(NotePrefix(i) + "is not an object");
                return;
            }
            JToken p = n["pitch"];
            JToken d = n["durWeight"];
            JToken v = n["velocity"];
            int before = errors.Count;
            long pitch = 0;
            double dur = 0, vel = 0;
            if (p == null) errors.Add(NotePrefix(i) + "pitch is missing");
            else if (!TryGetInteger(p, out pitch) || pitch < 0 || pitch > 127) errors.Add(NotePrefix(i) + "pitch must be an integer MIDI note 0..127");
            if (d == null) errors.Add(NotePrefix(i) + "durWeight is missing");
            else if (!TryGetNumber(d, out dur) || !IsValidDurWeight(dur)) errors.Add(NotePrefix(i) + "durWeight must be a finite number > 0");
            if (v == null) errors.Add(NotePrefix(i) + "velocity is missing");
            else if (!TryGetNumber(v, out vel) || !(vel >= 0.0 && vel <= 1.0)) errors.Add(NotePrefix(i) + "velocity must be a number within 0..1");
            if (errors.Count == before) note = new MelodyNote((int)pitch, (float)dur, (float)vel);
        }

        internal static void ValidateHeader(string id, string name, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(id)) errors.Add("id is missing (non-blank string required)");
            if (string.IsNullOrWhiteSpace(name)) errors.Add("name is missing (non-blank string required)");
        }

        internal static void ValidateNote(int index, int pitch, float durWeight, float velocity, List<string> errors)
        {
            if (pitch < 0 || pitch > 127) errors.Add(NotePrefix(index) + "pitch must be an integer MIDI note 0..127");
            if (!IsValidDurWeight(durWeight)) errors.Add(NotePrefix(index) + "durWeight must be a finite number > 0");
            if (!(velocity >= 0f && velocity <= 1f)) errors.Add(NotePrefix(index) + "velocity must be a number within 0..1");
        }

        static bool IsValidDurWeight(double w)
        {
            return !double.IsNaN(w) && !double.IsInfinity(w) && w > 0.0 && w <= float.MaxValue && (float)w > 0f;
        }

        static string GetString(JToken token)
        {
            return token != null && token.Type == JTokenType.String ? (string)((JValue)token).Value : null;
        }

        static bool TryGetInteger(JToken token, out long value)
        {
            value = 0;
            if (token.Type == JTokenType.Integer)
            {
                object o = ((JValue)token).Value;
                if (o is BigInteger big)
                {
                    value = big.Sign < 0 ? long.MinValue : long.MaxValue;
                    return true;
                }
                value = Convert.ToInt64(o, CultureInfo.InvariantCulture);
                return true;
            }
            if (token.Type == JTokenType.Float)
            {
                double d = Convert.ToDouble(((JValue)token).Value, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d) || Math.Floor(d) != d) return false;
                value = d >= 9.2233720368547758E18 ? long.MaxValue : d <= -9.2233720368547758E18 ? long.MinValue : (long)d;
                return true;
            }
            return false;
        }

        static bool TryGetNumber(JToken token, out double value)
        {
            value = 0;
            if (token.Type == JTokenType.Integer)
            {
                object o = ((JValue)token).Value;
                value = o is BigInteger big ? (double)big : Convert.ToDouble(o, CultureInfo.InvariantCulture);
                return !double.IsInfinity(value);
            }
            if (token.Type == JTokenType.Float)
            {
                value = Convert.ToDouble(((JValue)token).Value, CultureInfo.InvariantCulture);
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            return false;
        }

        static string NotePrefix(int i)
        {
            return "notes[" + i.ToString(CultureInfo.InvariantCulture) + "].";
        }
    }
}
