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
    /// Rebuilt when the screen, sheets, dialogs or language change; node frames follow the elements. Needs the built-in
    /// Accessibility module (<c>com.unity.modules.accessibility</c>, define <c>BALLVERSE_ACCESSIBILITY</c> from the asmdef);
    /// without it this class does nothing.
    /// </summary>
#if BALLVERSE_ACCESSIBILITY
    public sealed class ScreenReaderBridge : IDisposable
    {
        const float RefreshSeconds = 0.5f;
        const float StatusPollSeconds = 1f;

        readonly UiContext _ctx;
        readonly VisualElement _root;
        AccessibilityHierarchy _hierarchy;
        bool _enabled;
        bool _dirty = true;
        float _sinceRefresh;
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

        /// <summary>Something visible changed (screen, sheet, dialog, language): rebuild on the next update.</summary>
        public void MarkDirty()
        {
            _dirty = true;
        }

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
                    _dirty = true;
                    if (!enabled) Deactivate();
                }
            }
            if (!_enabled) return;
            if (_dirty)
            {
                _dirty = false;
                Rebuild();
                return;
            }
            _sinceRefresh += deltaTime;
            if (_sinceRefresh >= RefreshSeconds && _hierarchy != null)
            {
                _sinceRefresh = 0f;
                _hierarchy.RefreshNodeFrames();
            }
        }

        void Rebuild()
        {
            var hierarchy = new AccessibilityHierarchy();
            var scale = PanelToScreenScale();
            foreach (var layer in ModalLayers()) Collect(hierarchy, layer, scale);
            _hierarchy = hierarchy;
            _sinceRefresh = 0f;
            AssistiveSupport.activeHierarchy = hierarchy;
            AssistiveSupport.notificationDispatcher.SendScreenChanged();
        }

        void Deactivate()
        {
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

        void Collect(AccessibilityHierarchy hierarchy, VisualElement element, Vector2 scale)
        {
            if (!IsShown(element)) return;
            if (IsHeading(element) && element is Label title && !string.IsNullOrEmpty(title.text))
            {
                var heading = hierarchy.AddNode(title.text);
                heading.role = AccessibilityRole.Header;
                var target = title;
                heading.frameGetter = () => Frame(target, scale);
                return;
            }
            if (A11y.TryGet(element, out var entry))
            {
                string label = LabelOf(element, entry);
                if (!string.IsNullOrEmpty(label))
                {
                    var node = hierarchy.AddNode(label);
                    node.role = AccessibilityRole.Button;
                    var target = element;
                    node.frameGetter = () => Frame(target, scale);
                    var invoke = entry.Invoke;
                    if (invoke != null)
                    {
                        node.invoked += () =>
                        {
                            if (!target.enabledInHierarchy) return false;
                            invoke();
                            return true;
                        };
                    }
                    // A labelled button is announced as one node; its inner texts are not separate nodes.
                    return;
                }
            }
            var children = element.hierarchy;
            for (int i = 0; i < children.childCount; i++) Collect(hierarchy, children[i], scale);
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
                if (text == null && IsShown(l) && !string.IsNullOrEmpty(l.text)) text = l.text;
            });
            return text;
        }

        static bool IsShown(VisualElement e)
        {
            var s = e.resolvedStyle;
            if (s.display == DisplayStyle.None || s.visibility == Visibility.Hidden || s.opacity <= 0.01f) return false;
            var b = e.worldBound;
            return !(float.IsNaN(b.width) || float.IsNaN(b.height));
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
