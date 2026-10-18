using System;
using System.Collections.Generic;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// A melody for the Melody sound pack (spec §2.7): id (as stored in project.json <c>"melody": "ode_to_joy"</c>, §7.4),
    /// display name and a non-empty note list. Immutable once constructed. The rules are those of <see cref="MelodyJson"/>
    /// (the same as the Remote Config melody entries): id and name are trimmed non-blank strings, every note valid.
    /// Remote or content data goes through <see cref="MelodyJson.TryParse(string, out Melody, out IReadOnlyList{string})"/>
    /// or <see cref="TryCreate"/>, which never throw.
    /// </summary>
    public sealed class Melody
    {
        readonly MelodyNote[] _notes;

        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<MelodyNote> Notes => _notes;
        public int Count => _notes.Length;

        /// <summary>Creates a melody; throws <see cref="ArgumentException"/> when the data breaks the rules of <see cref="MelodyJson"/>.</summary>
        public Melody(string id, string name, IReadOnlyList<MelodyNote> notes)
        {
            var errors = new List<string>();
            Validate(id, name, notes, errors);
            if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors));

            Id = id.Trim();
            Name = name.Trim();
            _notes = new MelodyNote[notes.Count];
            for (int i = 0; i < _notes.Length; i++) _notes[i] = notes[i];
        }

        /// <summary>
        /// Non-throwing factory for adapters (e.g. a Remote Config melody entry): on failure <paramref name="melody"/> is
        /// null and <paramref name="errors"/> lists every problem.
        /// </summary>
        public static bool TryCreate(string id, string name, IReadOnlyList<MelodyNote> notes, out Melody melody, out IReadOnlyList<string> errors)
        {
            var list = new List<string>();
            errors = list;
            Validate(id, name, notes, list);
            melody = list.Count == 0 ? new Melody(id, name, notes) : null;
            return melody != null;
        }

        public MelodyNote this[int index] => _notes[index];

        static void Validate(string id, string name, IReadOnlyList<MelodyNote> notes, List<string> errors)
        {
            MelodyJson.ValidateHeader(id, name, errors);
            if (notes == null || notes.Count == 0)
            {
                errors.Add("notes must contain at least one note");
                return;
            }
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                MelodyJson.ValidateNote(i, n.Pitch, n.DurWeight, n.Velocity, errors);
            }
        }
    }
}
