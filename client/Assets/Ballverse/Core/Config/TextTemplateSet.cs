using System;
using System.Collections.Generic;

namespace Ballverse.Core.Config
{
    /// <summary>Text templates of one language (spec §3.5, §4.7: template language is chosen separately from the UI language).</summary>
    public sealed class TextTemplateSet
    {
        /// <summary>Language tag as delivered by config, e.g. "en", "ru" (trimmed, non-empty). Lookups ignore case.</summary>
        public string Language { get; }
        /// <summary>Templates in config order, unique ids.</summary>
        public IReadOnlyList<TextTemplate> Templates { get; }

        /// <exception cref="ArgumentException">Empty language, null or duplicate template.</exception>
        public TextTemplateSet(string language, IEnumerable<TextTemplate> templates)
        {
            if (string.IsNullOrWhiteSpace(language)) throw new ArgumentException("language must be non-empty", nameof(language));
            if (templates == null) throw new ArgumentNullException(nameof(templates));
            var list = new List<TextTemplate>(templates);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in list)
            {
                if (t == null) throw new ArgumentException("null template", nameof(templates));
                if (!ids.Add(t.Id)) throw new ArgumentException("duplicate template id " + t.Id, nameof(templates));
            }
            Language = language.Trim();
            Templates = list.AsReadOnly();
        }
    }
}
