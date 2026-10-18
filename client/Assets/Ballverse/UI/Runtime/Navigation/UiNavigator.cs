using System;
using System.Collections.Generic;
using Ballverse.UI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Navigation
{
    /// <summary>
    /// Navigation service of the shell (spec §4.2): screen stack with push / pop / tabs, transitions (spring slide,
    /// instant under reduced motion), tab bar visibility and the system back action (Android back button, spec §4.6).
    /// </summary>
    public sealed class UiNavigator
    {
        readonly UiContext _ctx;
        readonly VisualElement _layer;
        readonly NavigationStack _stack;
        readonly Dictionary<ScreenId, Func<ScreenController>> _factories = new Dictionary<ScreenId, Func<ScreenController>>();
        readonly Dictionary<ScreenId, ScreenController> _live = new Dictionary<ScreenId, ScreenController>();
        readonly TabBar _tabBar;
        ScreenController _current;

        /// <summary>Raised after the visible screen changed.</summary>
        public event Action<ScreenId> ScreenChanged;
        /// <summary>Back on the Modes root (Android: move the app to the background).</summary>
        public event Action ExitRequested;

        public UiNavigator(UiContext ctx, VisualElement screenLayer, TabBar tabBar, TabId initialTab = TabId.Modes)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _layer = screenLayer ?? throw new ArgumentNullException(nameof(screenLayer));
            _tabBar = tabBar;
            _stack = new NavigationStack(initialTab);
            if (_tabBar != null) _tabBar.TabSelected += t => SelectTab(t);
        }

        public ScreenId Current => _stack.Top;
        public NavigationStack Stack => _stack;
        public ScreenController CurrentController => _current;

        public void Register(ScreenId id, Func<ScreenController> factory)
        {
            _factories[id] = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>Shows the initial screen (call once after registering factories).</summary>
        public void Start()
        {
            ShowTop(NavTransitionKind.None, null);
        }

        public void Push(ScreenId id)
        {
            Execute(_stack.Push(id));
        }

        public void Pop()
        {
            Execute(_stack.Pop());
        }

        public void PopToRoot()
        {
            Execute(_stack.PopToRoot());
        }

        public void Replace(ScreenId id)
        {
            Execute(_stack.Replace(id));
        }

        public void SelectTab(TabId tab)
        {
            _ctx.Sheets?.CloseAll();
            Execute(_stack.SelectTab(tab));
        }

        /// <summary>System back action. Returns what happened.</summary>
        public BackOutcome Back()
        {
            var outcome = BackDispatcher.Dispatch(_stack,
                () => _ctx.Dialogs != null && _ctx.Dialogs.CloseTop(),
                () => _ctx.Sheets != null && _ctx.Sheets.CloseTop(),
                id => _current != null && _current.Id == id && _current.HandleBack(),
                out NavTransition transition);
            if (transition.Kind != NavTransitionKind.None) Execute(transition);
            if (outcome == BackOutcome.ExitRequested) ExitRequested?.Invoke();
            return outcome;
        }

        /// <summary>Per-frame tick of the visible screen.</summary>
        public void Tick(float dt)
        {
            _current?.Tick(dt);
        }

        void Execute(NavTransition t)
        {
            if (t.Kind == NavTransitionKind.None) return;
            ShowTop(t.Kind, t.Removed);
        }

        void ShowTop(NavTransitionKind kind, IReadOnlyList<ScreenId> removed)
        {
            var previous = _current;
            var next = GetOrCreate(_stack.Top);
            if (previous == next) return;

            previous?.Hide();
            if (next.Root.parent != _layer) _layer.Add(next.Root);
            next.Root.BringToFront();
            next.Root.style.display = DisplayStyle.Flex;
            _current = next;
            next.Show();
            UpdateTabBar();

            float width = _layer.layout.width;
            if (float.IsNaN(width) || width <= 0) width = 400f;
            if (previous != null)
            {
                var prevRoot = previous.Root;
                var prevController = previous;
                bool destroyPrevious = removed != null && Contains(removed, previous.Id) && !ScreenIds.IsTabRoot(previous.Id);
                Action finish = () =>
                {
                    prevRoot.style.translate = new Translate(0, 0);
                    prevRoot.style.opacity = 1f;
                    // A later navigation may have made this screen current again while the transition ran.
                    if (prevController == _current) return;
                    if (destroyPrevious) Destroy(prevController);
                    else prevRoot.style.display = DisplayStyle.None;
                };
                switch (kind)
                {
                    case NavTransitionKind.Push:
                        next.Root.style.translate = new Translate(width, 0);
                        Tween.Run(next.Root, Tween.DefaultDuration, Ease.OutCubic,
                            k => next.Root.style.translate = new Translate(width * (1f - k), 0), finish);
                        break;
                    case NavTransitionKind.Pop:
                        prevRoot.BringToFront();
                        Tween.Run(prevRoot, 0.25f, Ease.OutCubic,
                            k => prevRoot.style.translate = new Translate(width * k, 0), finish);
                        break;
                    default:
                        next.Root.style.opacity = 0f;
                        Tween.Run(next.Root, 0.2f, Ease.OutCubic, k => next.Root.style.opacity = k, () =>
                        {
                            next.Root.style.opacity = 1f;
                            finish();
                        });
                        break;
                }
            }
            if (removed != null)
            {
                foreach (var id in removed)
                {
                    if (previous != null && id == previous.Id) continue;
                    if (ScreenIds.IsTabRoot(id)) continue;
                    if (_live.TryGetValue(id, out var c)) Destroy(c);
                }
            }
            ScreenChanged?.Invoke(next.Id);
        }

        static bool Contains(IReadOnlyList<ScreenId> list, ScreenId id)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == id) return true;
            return false;
        }

        void Destroy(ScreenController c)
        {
            if (_current == c) return;
            _live.Remove(c.Id);
            c.Destroy();
        }

        ScreenController GetOrCreate(ScreenId id)
        {
            if (_live.TryGetValue(id, out var c)) return c;
            if (!_factories.TryGetValue(id, out var factory))
                throw new InvalidOperationException("No screen registered for " + id);
            c = factory();
            c.Create(_ctx);
            c.Root.style.display = DisplayStyle.None;
            _live[id] = c;
            return c;
        }

        void UpdateTabBar()
        {
            if (_tabBar == null) return;
            _tabBar.SetVisible(_stack.ShowsTabBar);
            _tabBar.SetSelected(_stack.CurrentTab);
        }
    }

    /// <summary>Glass tab bar "Режимы · Проекты · Магазин" (spec §4.2).</summary>
    public sealed class TabBar
    {
        readonly VisualElement _root;
        readonly Dictionary<TabId, VisualElement> _items = new Dictionary<TabId, VisualElement>();

        public event Action<TabId> TabSelected;
        public VisualElement Root => _root;

        public TabBar(VisualElement root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            Bind(TabId.Modes, "tab-modes");
            Bind(TabId.Projects, "tab-projects");
            Bind(TabId.Shop, "tab-shop");
        }

        void Bind(TabId tab, string name)
        {
            var item = UiAssets.Q<VisualElement>(_root, name);
            _items[tab] = item;
            Tap.On(item, () => TabSelected?.Invoke(tab));
        }

        public void SetVisible(bool visible)
        {
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetSelected(TabId tab)
        {
            foreach (var kv in _items) kv.Value.EnableInClassList("bv-tabbar__item--selected", kv.Key == tab);
        }
    }

    /// <summary>
    /// System back input: Android back button (reported as Escape by both input back-ends) and Escape in the editor.
    /// Uses the legacy Input Manager when enabled, otherwise the Input System package when installed.
    /// </summary>
    public static class BackButtonInput
    {
        public static bool WasPressedThisFrame()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#elif BALLVERSE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
            return false;
#endif
        }
    }
}
