using System;

namespace Ballverse.Core.Config
{
    /// <summary>
    /// A localized overlay text template (spec §3.5 "Шаблоны"). <c>{n}</c> in the text is the live counter placeholder.
    /// JSON form: <c>{"id":"who_wins","text":"Which ball wins?"}</c>.
    /// </summary>
    public sealed class TextTemplate
    {
        /// <summary>Stable id shared across languages (trimmed, non-empty).</summary>
        public string Id { get; }
        /// <summary>Template text (non-blank; kept verbatim).</summary>
        public string Text { get; }

        /// <exception cref="ArgumentException">Empty id or text.</exception>
        public TextTemplate(string id, string text)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id must be non-empty", nameof(id));
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("text must be non-empty", nameof(text));
            Id = id.Trim();
            Text = text;
        }
    }
}
