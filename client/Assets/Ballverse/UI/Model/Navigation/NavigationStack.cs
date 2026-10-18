using System;
using System.Collections.Generic;

namespace Ballverse.UI.Navigation
{
    public enum NavTransitionKind
    {
        None = 0,
        /// <summary>A screen was pushed on top (slide in from the right).</summary>
        Push = 1,
        /// <summary>The top screen was removed (slide out to the right).</summary>
        Pop = 2,
        /// <summary>The stack was reset to a tab root (cross-fade).</summary>
        Tab = 3,
        /// <summary>The top screen was replaced (cross-fade).</summary>
        Replace = 4,
    }

    /// <summary>What a navigation operation did: the screens that became hidden / visible.</summary>
    public readonly struct NavTransition
    {
        public NavTransitionKind Kind { get; }
        public ScreenId? From { get; }
        public ScreenId? To { get; }
        /// <summary>Screens removed from the stack (to be destroyed), top first.</summary>
        public IReadOnlyList<ScreenId> Removed { get; }

        public NavTransition(NavTransitionKind kind, ScreenId? from, ScreenId? to, IReadOnlyList<ScreenId> removed)
        {
            Kind = kind;
            From = from;
            To = to;
            Removed = removed ?? Array.Empty<ScreenId>();
        }

        public static readonly NavTransition NoChange = new NavTransition(NavTransitionKind.None, null, null, null);
    }

    /// <summary>
    /// Screen stack of the app shell (spec §4.2): the bottom entry is the current tab root; selecting a tab resets the
    /// stack to that root; other screens are pushed and popped. A screen id appears at most once (pushing an id already
    /// in the stack pops back to it). Sheets and dialogs live in their own hosts. Pure C#.
    /// </summary>
    public sealed class NavigationStack
    {
        readonly List<ScreenId> _stack = new List<ScreenId>();

        public NavigationStack(TabId initialTab = TabId.Modes)
        {
            CurrentTab = initialTab;
            _stack.Add(ScreenIds.RootOf(initialTab));
        }

        public TabId CurrentTab { get; private set; }
        public ScreenId Top => _stack[_stack.Count - 1];
        public int Depth => _stack.Count;
        public IReadOnlyList<ScreenId> Entries => _stack.AsReadOnly();
        public bool CanPop => _stack.Count > 1;
        /// <summary>The tab bar is visible only on a tab root.</summary>
        public bool ShowsTabBar => _stack.Count == 1 && ScreenIds.IsTabRoot(Top);

        public bool Contains(ScreenId id)
        {
            return _stack.Contains(id);
        }

        public NavTransition SelectTab(TabId tab)
        {
            var root = ScreenIds.RootOf(tab);
            if (CurrentTab == tab && _stack.Count == 1) return NavTransition.NoChange;
            var from = Top;
            var removed = new List<ScreenId>();
            for (int i = _stack.Count - 1; i >= 0; i--)
                if (_stack[i] != root) removed.Add(_stack[i]);
            _stack.Clear();
            _stack.Add(root);
            CurrentTab = tab;
            return new NavTransition(NavTransitionKind.Tab, from, root, removed);
        }

        public NavTransition Push(ScreenId id)
        {
            if (Top == id) return NavTransition.NoChange;
            if (_stack.Contains(id)) return PopTo(id);
            if (ScreenIds.TryGetTab(id, out TabId tab)) return SelectTab(tab);
            var from = Top;
            _stack.Add(id);
            return new NavTransition(NavTransitionKind.Push, from, id, null);
        }

        /// <summary>Replaces the top screen (not allowed on the root).</summary>
        public NavTransition Replace(ScreenId id)
        {
            if (!CanPop) return Push(id);
            if (Top == id) return NavTransition.NoChange;
            var from = Top;
            var removed = new List<ScreenId> { from };
            _stack.RemoveAt(_stack.Count - 1);
            int existing = _stack.IndexOf(id);
            if (existing >= 0)
            {
                for (int i = _stack.Count - 1; i > existing; i--)
                {
                    removed.Add(_stack[i]);
                    _stack.RemoveAt(i);
                }
            }
            else
            {
                _stack.Add(id);
            }
            return new NavTransition(NavTransitionKind.Replace, from, id, removed);
        }

        public NavTransition Pop()
        {
            if (!CanPop) return NavTransition.NoChange;
            var from = Top;
            _stack.RemoveAt(_stack.Count - 1);
            return new NavTransition(NavTransitionKind.Pop, from, Top, new[] { from });
        }

        public NavTransition PopTo(ScreenId id)
        {
            int index = _stack.IndexOf(id);
            if (index < 0 || index == _stack.Count - 1) return NavTransition.NoChange;
            var from = Top;
            var removed = new List<ScreenId>();
            for (int i = _stack.Count - 1; i > index; i--)
            {
                removed.Add(_stack[i]);
                _stack.RemoveAt(i);
            }
            return new NavTransition(NavTransitionKind.Pop, from, Top, removed);
        }

        public NavTransition PopToRoot()
        {
            return PopTo(_stack[0]);
        }
    }

    /// <summary>What the system back action did (Android back button / edge swipe, spec §4.6).</summary>
    public enum BackOutcome
    {
        /// <summary>Nothing to do (e.g. back is blocked while exporting).</summary>
        Ignored = 0,
        ClosedDialog = 1,
        ClosedSheet = 2,
        /// <summary>The top screen consumed it (e.g. interactive game: pause with confirmation).</summary>
        HandledByScreen = 3,
        Popped = 4,
        /// <summary>On another tab's root: back goes to the Modes tab first.</summary>
        SwitchedToHomeTab = 5,
        /// <summary>On the Modes root: the app should go to the background (Android).</summary>
        ExitRequested = 6,
    }

    /// <summary>
    /// Back-action order of the shell: dialog → sheet → screen handler → pop → home tab → exit. The hosts are passed
    /// as delegates so the order is testable without UI.
    /// </summary>
    public static class BackDispatcher
    {
        public static BackOutcome Dispatch(NavigationStack stack, Func<bool> closeTopDialog, Func<bool> closeTopSheet,
            Func<ScreenId, bool> screenHandlesBack, out NavTransition transition)
        {
            if (stack == null) throw new ArgumentNullException(nameof(stack));
            transition = NavTransition.NoChange;
            if (closeTopDialog != null && closeTopDialog()) return BackOutcome.ClosedDialog;
            if (closeTopSheet != null && closeTopSheet()) return BackOutcome.ClosedSheet;
            if (ScreenIds.BlocksSystemBack(stack.Top)) return BackOutcome.Ignored;
            if (screenHandlesBack != null && screenHandlesBack(stack.Top)) return BackOutcome.HandledByScreen;
            if (stack.CanPop)
            {
                transition = stack.Pop();
                return BackOutcome.Popped;
            }
            if (stack.CurrentTab != TabId.Modes)
            {
                transition = stack.SelectTab(TabId.Modes);
                return BackOutcome.SwitchedToHomeTab;
            }
            return BackOutcome.ExitRequested;
        }
    }
}
