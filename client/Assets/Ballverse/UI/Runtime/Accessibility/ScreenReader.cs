using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
#if BALLVERSE_ACCESSIBILITY
using UnityEngine.Accessibility;
#endif
using UnityEngine.UIElements;

namespace Ballverse.UI.Accessibility
{
    /// <summary>
    /// Screen-reader metadata of interactive elements (spec §4.8: VoiceOver / TalkBack — labels on all buttons; 1.0
    /// minimum: the main buttons and navigation are announced). <see cref="Components.Tap"/> registers every tappable
    /// element with its action; icon-only buttons get a label key here (explicitly or by their UXML name through
    /// <see cref="A11yLabels"/>); elements with text are announced by their text.
    /// </summary>
    public static class A11y
    {
        internal sealed class Entry
        {
            public Action Invoke;
            public string LabelKey;
        }

        static readonly ConditionalWeakTable<VisualElement, Entry> s_entries = new ConditionalWeakTable<VisualElement, Entry>();

        /// <summary>Marks a tappable element: the screen reader announces it as a button and can activate it.</summary>
        public static void Button(VisualElement element, Action invoke)
        {
            if (element == null) return;
            var e = s_entries.GetOrCreateValue(element);
            e.Invoke = invoke;
        }

        /// <summary>Label key for an element without readable text (icon-only buttons).</summary>
        public static void Label(VisualElement element, string labelKey)
        {
            if (element == null) return;
            s_entries.GetOrCreateValue(element).LabelKey = labelKey;
        }

        internal static bool TryGet(VisualElement element, out Entry entry)
        {
            return s_entries.TryGetValue(element, out entry);
        }
    }

    /// <summary>
    /// Mirrors the visible interactive UI into Unity's screen-reader hierarchy (<c>UnityEngine.Accessibility.AssistiveSupport</c>,
    /// iOS VoiceOver and Android TalkBack, Unity 6) while a screen reader is on: one node per registered button and
    /// screen title in the top-most modal layer (dialog, else dimming sheet, else screen + open sheets + tab bar).
    /// Driven from <see cref="UiShell.LateUpdate"/>, after the panel update, and scheduled by <see cref="ScreenReaderSync"/>:
    /// a change (screen, sheet, dialog, language) is read once its elements are laid out (a new element has NaN bounds
    /// until the next panel update), and the visible UI is re-read twice a second so nodes that appear without an event
    /// (transition end, result card, chrome) are published too; otherwise only the node frames move. Needs the built-in
    /// Accessibility module (<c>com.unity.modules.accessibility</c>, define <c>BALLVERSE_ACCESSIBILITY</c> from the asmdef);
    /// without it this class does nothing.
    /// </summary>
#if BALLVERSE_ACCESSIBILITY
    public sealed class ScreenReaderBridge : IDisposable
    {
        const float StatusPollSeconds = 1f;

        readonly struct NodeSpec
        {
            public readonly VisualElement Element;
            public readonly string Label;
            public readonly bool Heading;
            public readonly Action Invoke;

            public NodeSpec(VisualElement element, string label, bool heading, Action invoke)
            {
                Element = element;
                Label = label;
                Heading = heading;
                Invoke = invoke;
            }

            public bool SameAs(NodeSpec other)
            {
                return ReferenceEquals(Element, other.Element) && Heading == other.Heading && string.Equals(Label, other.Label, StringComparison.Ordinal);
            }
        }

        readonly UiContext _ctx;
        readonly VisualElement _root;
        readonly ScreenReaderSync _sync = new ScreenReaderSync();
        readonly List<NodeSpec> _published = new List<NodeSpec>();
        readonly List<NodeSpec> _read = new List<NodeSpec>();
        AccessibilityHierarchy _hierarchy;
        bool _enabled;
        float _sinceStatusPoll = StatusPollSeconds;
        bool _disposed;

        public ScreenReaderBridge(UiContext ctx, VisualElement appRoot)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _root = appRoot ?? throw new ArgumentNullException(nameof(appRoot));
        }

        /// <summary>Screen readers are supported by Unity on iOS and Android only.</summary>
        public static bool PlatformSupported =>
            Application.platform == RuntimePlatform.IPhonePlayer || Application.platform == RuntimePlatform.Android;

        public bool Active => _hierarchy != null;

        /// <summary>Something visible changed (screen, sheet, dialog, language): rebuild once it is laid out.</summary>
        public void MarkDirty()
        {
            _sync.MarkDirty();
        }

        /// <summary>Per frame, after the panel update (from <see cref="UiShell.LateUpdate"/>).</summary>
        public void Update(float deltaTime)
        {
            if (_disposed || !PlatformSupported) return;
            _sinceStatusPoll += deltaTime;
            if (_sinceStatusPoll >= StatusPollSeconds)
            {
                _sinceStatusPoll = 0f;
                bool enabled = AssistiveSupport.isScreenReaderEnabled;
                if (enabled != _enabled)
                {
                    _enabled = enabled;
                    _sync.MarkDirty();
                    if (!enabled) Deactivate();
                }
            }
            if (!_enabled || !_sync.Due(deltaTime)) return;

            _read.Clear();
            bool ready = true;
            foreach (var layer in ModalLayers()) Collect(layer, ref ready);
            switch (_sync.Decide(ready, !SameAsPublished()))
            {
                case ScreenReaderStep.PublishScreen:
                    Publish(true);
                    break;
                case ScreenReaderStep.PublishLayout:
                    Publish(false);
                    break;
                case ScreenReaderStep.RefreshFrames:
                    _hierarchy?.RefreshNodeFrames();
                    break;
            }
        }

        bool SameAsPublished()
        {
            if (_hierarchy == null || _read.Count != _published.Count) return false;
            for (int i = 0; i < _read.Count; i++)
                if (!_read[i].SameAs(_published[i])) return false;
            return true;
        }

        void Publish(bool screenChanged)
        {
            var hierarchy = new AccessibilityHierarchy();
            var scale = PanelToScreenScale();
            foreach (var spec in _read)
            {
                var node = hierarchy.AddNode(spec.Label);
                var target = spec.Element;
                node.frameGetter = () => Frame(target, scale);
                if (spec.Heading)
                {
                    node.role = AccessibilityRole.Header;
                    continue;
                }
                node.role = AccessibilityRole.Button;
                var invoke = spec.Invoke;
                if (invoke != null)
                {
                    node.invoked += () =>
                    {
                        if (!target.enabledInHierarchy) return false;
                        invoke();
                        return true;
                    };
                }
            }
            _published.Clear();
            _published.AddRange(_read);
            _hierarchy = hierarchy;
            AssistiveSupport.activeHierarchy = hierarchy;
            if (screenChanged) AssistiveSupport.notificationDispatcher.SendScreenChanged();
            else AssistiveSupport.notificationDispatcher.SendLayoutChanged();
        }

        void Deactivate()
        {
            _published.Clear();
            if (_hierarchy == null) return;
            _hierarchy = null;
            AssistiveSupport.activeHierarchy = null;
        }

        /// <summary>Layers whose content is reachable: a dialog hides everything behind it, a dimming sheet hides the screen.</summary>
        IEnumerable<VisualElement> ModalLayers()
        {
            var dialogs = _root.Q<VisualElement>("dialog-layer");
            if (dialogs != null && dialogs.childCount > 0)
            {
                yield return dialogs[dialogs.childCount - 1];
                yield break;
            }
            var sheets = _root.Q<VisualElement>("sheet-layer");
            var top = _ctx.Sheets?.Top;
            if (top != null && top.Options.ScrimOpacity > 0f)
            {
                yield return top.Root;
                yield break;
            }
            var screen = _ctx.Navigator?.CurrentController?.Root;
            if (screen != null) yield return screen;
            if (sheets != null) yield return sheets;
            var tabbar = _root.Q<VisualElement>("tabbar-host");
            if (tabbar != null) yield return tabbar;
        }

        /// <summary>Titles announced as headings (screen, sheet and dialog titles).</summary>
        static readonly string[] s_headingClasses = { "bv-header__title", "bv-sheet__title", "bv-dialog__title" };

        static bool IsHeading(VisualElement element)
        {
            if (!(element is Label)) return false;
            foreach (string c in s_headingClasses)
                if (element.ClassListContains(c)) return true;
            return false;
        }

        /// <summary>
        /// Reads the nodes under <paramref name="element"/>. Hidden subtrees (display none, visibility hidden) are skipped;
        /// a shown element without a layout yet clears <paramref name="ready"/> (it is read once laid out). Opacity is
        /// ignored: dialogs and screens fade in from 0, and hidden chrome ends with visibility hidden.
        /// </summary>
        void Collect(VisualElement element, ref bool ready)
        {
            var state = StateOf(element);
            if (state == Shown.Hidden) return;
            if (state == Shown.NotLaidOut)
            {
                ready = false;
                return;
            }
            if (IsHeading(element) && element is Label title && !string.IsNullOrEmpty(title.text))
            {
                _read.Add(new NodeSpec(title, title.text, true, null));
                return;
            }
            if (A11y.TryGet(element, out var entry))
            {
                string label = LabelOf(element, entry);
                if (!string.IsNullOrEmpty(label))
                {
                    // A labelled button is announced as one node; its inner texts are not separate nodes.
                    _read.Add(new NodeSpec(element, label, false, entry.Invoke));
                    return;
                }
            }
            var children = element.hierarchy;
            for (int i = 0; i < children.childCount; i++) Collect(children[i], ref ready);
        }

        string LabelOf(VisualElement element, A11y.Entry entry)
        {
            if (!string.IsNullOrEmpty(entry.LabelKey)) return _ctx.L(entry.LabelKey);
            if (element is TextElement own && !string.IsNullOrEmpty(own.text)) return own.text;
            string byName = A11yLabels.KeyFor(element.name);
            if (byName != null) return _ctx.L(byName);
            // Text of the first visible label inside (e.g. "Экспорт", tab titles, rows).
            string text = null;
            element.Query<Label>().ForEach(l =>
            {
                if (text == null && StateOf(l) != Shown.Hidden && !string.IsNullOrEmpty(l.text)) text = l.text;
            });
            return text;
        }

        enum Shown
        {
            Visible = 0,
            Hidden = 1,
            NotLaidOut = 2,
        }

        static Shown StateOf(VisualElement e)
        {
            var s = e.resolvedStyle;
            if (s.display == DisplayStyle.None || s.visibility == Visibility.Hidden) return Shown.Hidden;
            var b = e.worldBound;
            return float.IsNaN(b.x) || float.IsNaN(b.y) || float.IsNaN(b.width) || float.IsNaN(b.height) ? Shown.NotLaidOut : Shown.Visible;
        }

        Vector2 PanelToScreenScale()
        {
            var panelRoot = _root.panel?.visualTree;
            float pw = panelRoot != null ? panelRoot.layout.width : 0f;
            float ph = panelRoot != null ? panelRoot.layout.height : 0f;
            if (!(pw > 0f) || !(ph > 0f)) return Vector2.one;
            return new Vector2(Screen.width / pw, Screen.height / ph);
        }

        /// <summary>Panel rect → screen pixels, origin top-left (panel scaled to the screen by the panel settings).</summary>
        static Rect Frame(VisualElement element, Vector2 scale)
        {
            var b = element.worldBound;
            if (float.IsNaN(b.x) || float.IsNaN(b.width)) return Rect.zero;
            return new Rect(b.x * scale.x, b.y * scale.y, b.width * scale.x, b.height * scale.y);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (PlatformSupported) Deactivate();
        }
    }

#else
    public sealed class ScreenReaderBridge : IDisposable
    {
        public ScreenReaderBridge(UiContext ctx, VisualElement appRoot)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (appRoot == null) throw new ArgumentNullException(nameof(appRoot));
        }

        /// <summary>False: the built-in Accessibility module (com.unity.modules.accessibility) is not in the project.</summary>
        public static bool PlatformSupported => false;
        public bool Active => false;
        public void MarkDirty() { }
        public void Update(float deltaTime) { }
        public void Dispose() { }
    }
#endif

    /// <summary>Screen-reader labels of icon-only buttons in the layouts, by element name (keys in both tables).</summary>
    public static class A11yLabels
    {
        static readonly Dictionary<string, string> s_byName = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "back", "a11y.back" },
            { "close", "a11y.close" },
            { "settings", "a11y.settings" },
            { "retry", "a11y.retry" },
            { "play", "a11y.play_pause" },
            { "generate", "a11y.generate" },
            { "speed", "a11y.speed" },
            { "undo", "a11y.undo" },
            { "redo", "a11y.redo" },
            { "fullscreen", "a11y.fullscreen" },
            { "fullscreen-close", "a11y.close" },
            { "fullscreen-play", "a11y.play_pause" },
            { "result-retry", "a11y.retry" },
            { "result-generate", "a11y.generate" },
            { "quest-header", "quest.title" },
            { "title-area", "a11y.presets" },
        };

        /// <summary>Element names that have a label key.</summary>
        public static IEnumerable<string> Names => s_byName.Keys;

        public static string KeyFor(string elementName)
        {
            if (string.IsNullOrEmpty(elementName)) return null;
            return s_byName.TryGetValue(elementName, out var key) ? key : null;
        }
    }
}
