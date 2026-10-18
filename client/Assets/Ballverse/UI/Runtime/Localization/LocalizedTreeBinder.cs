using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Ballverse.UI.Localization
{
    /// <summary>
    /// Localizes text authored in UXML. Convention: a text attribute holding <c>#key</c> is a localization key, e.g.
    /// <c>&lt;ui:Label text="#gallery.title" /&gt;</c> — layouts contain keys only, never user-visible literals.
    /// The key is remembered per element so a runtime language switch re-applies it.
    /// </summary>
    public sealed class LocalizedTreeBinder
    {
        public const char KeyPrefix = '#';

        sealed class KeyBox
        {
            public string Key;
        }

        readonly Localizer _localizer;
        readonly ConditionalWeakTable<TextElement, KeyBox> _keys = new ConditionalWeakTable<TextElement, KeyBox>();

        public LocalizedTreeBinder(Localizer localizer)
        {
            _localizer = localizer;
        }

        /// <summary>
        /// Replaces <c>#key</c> texts under <paramref name="root"/> and remembers the keys. Call it once right after a
        /// layout is instantiated, before code sets dynamic texts (user content may legitimately start with '#').
        /// </summary>
        public void Bind(VisualElement root)
        {
            if (root == null) return;
            root.Query<TextElement>().ForEach(Apply);
        }

        /// <summary>Re-applies remembered keys under <paramref name="root"/> (after a language switch).</summary>
        public void Refresh(VisualElement root)
        {
            if (root == null) return;
            root.Query<TextElement>().ForEach(e =>
            {
                if (_keys.TryGetValue(e, out KeyBox box)) e.text = _localizer.Get(box.Key);
            });
        }

        /// <summary>Sets an element's text from a key and remembers it for language switches.</summary>
        public void SetKey(TextElement element, string key)
        {
            if (element == null) return;
            if (_keys.TryGetValue(element, out KeyBox box)) box.Key = key;
            else _keys.Add(element, new KeyBox { Key = key });
            element.text = _localizer.Get(key);
        }

        /// <summary>Forgets a remembered key (the controller sets the text itself from now on).</summary>
        public void Forget(TextElement element)
        {
            if (element != null) _keys.Remove(element);
        }

        void Apply(TextElement e)
        {
            string text = e.text;
            if (string.IsNullOrEmpty(text) || text.Length < 2 || text[0] != KeyPrefix) return;
            SetKey(e, text.Substring(1));
        }
    }
}
