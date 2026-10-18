using System;
using System.Collections.Generic;

namespace Ballverse.Core.Config
{
    /// <summary>
    /// A melody delivered by Remote Config (spec §2.7 "Мелодии для пакета «Мелодия»").
    /// JSON form: <c>{"id":"ode_to_joy","name":"Ode to Joy","notes":[{"pitch":64,"durWeight":1,"velocity":0.8}, …]}</c>.
    /// </summary>
    public sealed class RemoteMelody
    {
        /// <summary>Stable melody id (trimmed, non-empty, unique within the config).</summary>
        public string Id { get; }
        /// <summary>Display name (trimmed, non-empty).</summary>
        public string Name { get; }
        /// <summary>At least one note.</summary>
        public IReadOnlyList<MelodyNote> Notes { get; }

        /// <exception cref="ArgumentException">Empty id/name, no notes or a null note.</exception>
        public RemoteMelody(string id, string name, IEnumerable<MelodyNote> notes)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id must be non-empty", nameof(id));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name must be non-empty", nameof(name));
            if (notes == null) throw new ArgumentNullException(nameof(notes));
            var list = new List<MelodyNote>(notes);
            if (list.Count == 0) throw new ArgumentException("a melody needs at least one note", nameof(notes));
            foreach (var n in list)
            {
                if (n == null) throw new ArgumentException("null note", nameof(notes));
            }
            Id = id.Trim();
            Name = name.Trim();
            Notes = list.AsReadOnly();
        }
    }
}
