using System;
using System.Collections.Generic;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// A melody for the Melody sound pack (spec §2.7): id (as stored in project.json <c>"melody": "ode_to_joy"</c>, §7.4),
    /// display name and a non-empty note list. Immutable once constructed; instances are created by
    /// <see cref="MelodyJson.TryParse"/> (remote/content data) or by code with already valid data.
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
            MelodyJson.ValidateHeader(id, name, errors);
            if (notes == null || notes.Count == 0) errors.Add("notes must contain at least one note");
            else if (notes.Count > MelodyJson.MaxNotes) errors.Add("notes: at most " + MelodyJson.MaxNotes + " notes");
            else
            {
                for (int i = 0; i < notes.Count; i++)
                {
                    var n = notes[i];
                    MelodyJson.ValidateNote(i, n.Pitch, n.DurWeight, n.Velocity, errors);
                }
            }
            if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors));

            Id = id;
            Name = name;
            _notes = new MelodyNote[notes.Count];
            for (int i = 0; i < _notes.Length; i++) _notes[i] = notes[i];
        }

        public MelodyNote this[int index] => _notes[index];
    }
}
