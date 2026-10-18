using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Melody JSON format (spec §2.7: melodies are a simple JSON list of notes and can be delivered through Remote Config):
    /// <code>{ "id": "ode_to_joy", "name": "Ode to Joy", "notes": [ { "pitch": 64, "durWeight": 1, "velocity": 0.8 } ] }</code>
    /// Rules: id 1…64 chars of [a-z0-9_-]; name 1…128 chars; 1…4096 notes; pitch an integer MIDI number 0…127;
    /// durWeight finite and &gt; 0 (default 1 when absent); velocity finite 0…1 (default 1 when absent).
    /// <see cref="TryParse"/> never throws on bad remote data — it returns the list of problems.
    /// </summary>
    public static class MelodyJson
    {
        public const int MaxNotes = 4096;
        public const int MaxIdLength = 64;
        public const int MaxNameLength = 128;
        public const int MaxJsonLength = 512 * 1024;

        sealed class MelodyDto
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public List<NoteDto> Notes { get; set; }
        }

        sealed class NoteDto
        {
            public double? Pitch { get; set; }
            public double? DurWeight { get; set; }
            public double? Velocity { get; set; }
        }

        /// <summary>Parses and validates a melody. On failure <paramref name="melody"/> is null and errors are non-empty.</summary>
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

            MelodyDto dto;
            try
            {
                dto = CoreJson.Deserialize<MelodyDto>(json);
            }
            catch (Exception ex)
            {
                // Parse boundary for remote data: any reader/converter failure becomes an error, never an exception.
                list.Add("malformed JSON: " + ex.Message);
                return false;
            }
            if (dto == null)
            {
                list.Add("JSON is not an object");
                return false;
            }

            ValidateHeader(dto.Id, dto.Name, list);
            MelodyNote[] notes = null;
            if (dto.Notes == null || dto.Notes.Count == 0) list.Add("notes must contain at least one note");
            else if (dto.Notes.Count > MaxNotes) list.Add("notes: at most " + MaxNotes.ToString(CultureInfo.InvariantCulture) + " notes");
            else
            {
                notes = new MelodyNote[dto.Notes.Count];
                for (int i = 0; i < notes.Length; i++)
                {
                    var n = dto.Notes[i];
                    if (n == null)
                    {
                        list.Add(NotePrefix(i) + "is null");
                        continue;
                    }
                    if (!n.Pitch.HasValue)
                    {
                        list.Add(NotePrefix(i) + "pitch is missing");
                        continue;
                    }
                    double pitch = n.Pitch.Value;
                    double dur = n.DurWeight ?? 1.0;
                    double vel = n.Velocity ?? 1.0;
                    if (double.IsNaN(pitch) || double.IsInfinity(pitch) || pitch != Math.Floor(pitch) || pitch < 0 || pitch > 127)
                    {
                        list.Add(NotePrefix(i) + "pitch must be an integer MIDI note 0..127");
                        continue;
                    }
                    if (dur > float.MaxValue) dur = double.PositiveInfinity;
                    int before = list.Count;
                    ValidateNote(i, (int)pitch, (float)dur, (float)vel, list);
                    if (list.Count == before) notes[i] = new MelodyNote((int)pitch, (float)dur, (float)vel);
                }
            }

            if (list.Count > 0) return false;
            melody = new Melody(dto.Id, dto.Name, notes);
            return true;
        }

        /// <summary>Serializes a melody in the format accepted by <see cref="TryParse"/>.</summary>
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

        internal static void ValidateHeader(string id, string name, List<string> errors)
        {
            if (string.IsNullOrEmpty(id)) errors.Add("id is missing");
            else if (id.Length > MaxIdLength) errors.Add("id is longer than 64 characters");
            else
            {
                for (int i = 0; i < id.Length; i++)
                {
                    char c = id[i];
                    bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                    if (!ok)
                    {
                        errors.Add("id may contain only a-z, 0-9, '_' and '-'");
                        break;
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(name)) errors.Add("name is missing");
            else if (name.Length > MaxNameLength) errors.Add("name is longer than 128 characters");
        }

        internal static void ValidateNote(int index, int pitch, float durWeight, float velocity, List<string> errors)
        {
            if (pitch < 0 || pitch > 127) errors.Add(NotePrefix(index) + "pitch must be an integer MIDI note 0..127");
            if (float.IsNaN(durWeight) || float.IsInfinity(durWeight) || !(durWeight > 0f)) errors.Add(NotePrefix(index) + "durWeight must be a finite number > 0");
            if (float.IsNaN(velocity) || velocity < 0f || velocity > 1f) errors.Add(NotePrefix(index) + "velocity must be within 0..1");
        }

        static string NotePrefix(int i)
        {
            return "notes[" + i.ToString(CultureInfo.InvariantCulture) + "].";
        }
    }
}
