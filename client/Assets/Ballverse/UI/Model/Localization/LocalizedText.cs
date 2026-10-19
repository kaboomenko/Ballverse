using System;

namespace Ballverse.UI.Localization
{
    /// <summary>
    /// Text a view model hands to the UI without knowing the language: a localization key with optional format
    /// arguments (and an optional plural count), or a literal for user content (project titles, ball names, prices
    /// from the store). The UI resolves it with <see cref="Localizer.Resolve"/>.
    /// </summary>
    public readonly struct LocalizedText : IEquatable<LocalizedText>
    {
        public string Key { get; }
        public string Literal { get; }
        public object[] Args { get; }
        /// <summary>Plural count; selects the plural form and is also available as <c>{0}</c> before <see cref="Args"/>.</summary>
        public long? Count { get; }

        LocalizedText(string key, string literal, object[] args, long? count)
        {
            Key = key;
            Literal = literal;
            Args = args;
            Count = count;
        }

        public bool IsEmpty => Key == null && Literal == null;

        public static LocalizedText FromKey(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key is required", nameof(key));
            return new LocalizedText(key, null, args != null && args.Length > 0 ? args : null, null);
        }

        public static LocalizedText Plural(string key, long count, params object[] args)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key is required", nameof(key));
            return new LocalizedText(key, null, args != null && args.Length > 0 ? args : null, count);
        }

        /// <summary>User content or store-provided text shown as is.</summary>
        public static LocalizedText FromLiteral(string text)
        {
            return new LocalizedText(null, text ?? "", null, null);
        }

        public bool Equals(LocalizedText other)
        {
            if (Key != other.Key || Literal != other.Literal || Count != other.Count) return false;
            int n = Args?.Length ?? 0;
            if (n != (other.Args?.Length ?? 0)) return false;
            for (int i = 0; i < n; i++)
                if (!Equals(Args[i], other.Args[i])) return false;
            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is LocalizedText t && Equals(t);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Key?.GetHashCode() ?? 0) * 397) ^ (Literal?.GetHashCode() ?? 0) ^ Count.GetHashCode();
            }
        }

        public override string ToString()
        {
            return Key != null ? "#" + Key : Literal ?? "";
        }
    }
}
