using Ballverse.UI.Navigation;
using Xunit;

namespace Ballverse.UI.Tests
{
    public class NavigationStackTests
    {
        [Fact]
        public void StartsOnModesRootWithTabBar()
        {
            var s = new NavigationStack();
            Assert.Equal(ScreenId.Gallery, s.Top);
            Assert.True(s.ShowsTabBar);
            Assert.False(s.CanPop);
        }

        [Fact]
        public void PushPopAndTabBarVisibility()
        {
            var s = new NavigationStack();
            var t = s.Push(ScreenId.Simulation);
            Assert.Equal(NavTransitionKind.Push, t.Kind);
            Assert.False(s.ShowsTabBar);
            s.Push(ScreenId.Studio);
            Assert.Equal(3, s.Depth);
            t = s.Pop();
            Assert.Equal(NavTransitionKind.Pop, t.Kind);
            Assert.Equal(ScreenId.Studio, t.From);
            Assert.Equal(ScreenId.Simulation, t.To);
            Assert.Single(t.Removed);
        }

        [Fact]
        public void PushingAScreenAlreadyInTheStackPopsBackToIt()
        {
            var s = new NavigationStack();
            s.Push(ScreenId.Simulation);
            s.Push(ScreenId.Studio);
            var t = s.Push(ScreenId.Simulation);
            Assert.Equal(NavTransitionKind.Pop, t.Kind);
            Assert.Equal(ScreenId.Simulation, s.Top);
            Assert.Equal(2, s.Depth);
        }

        [Fact]
        public void SelectingATabResetsTheStack()
        {
            var s = new NavigationStack();
            s.Push(ScreenId.Simulation);
            s.Push(ScreenId.Studio);
            var t = s.SelectTab(TabId.Projects);
            Assert.Equal(NavTransitionKind.Tab, t.Kind);
            Assert.Equal(ScreenId.Projects, s.Top);
            Assert.Equal(1, s.Depth);
            Assert.Equal(3, t.Removed.Count);
            Assert.Equal(NavTransitionKind.None, s.SelectTab(TabId.Projects).Kind);
            Assert.Equal(NavTransitionKind.Tab, s.Push(ScreenId.Shop).Kind);
            Assert.Equal(TabId.Shop, s.CurrentTab);
        }

        [Fact]
        public void ReplaceSwapsTheTop()
        {
            var s = new NavigationStack();
            s.Push(ScreenId.Studio);
            s.Push(ScreenId.ExportProgress);
            var t = s.Replace(ScreenId.ExportDone);
            Assert.Equal(NavTransitionKind.Replace, t.Kind);
            Assert.Equal(ScreenId.ExportDone, s.Top);
            Assert.Equal(3, s.Depth);
        }
    }

    public class BackDispatcherTests
    {
        [Fact]
        public void OrderIsDialogSheetScreenPopHomeExit()
        {
            var s = new NavigationStack(TabId.Shop);
            s.Push(ScreenId.Settings);
            bool dialog = true, sheet = true, screen = true;
            Assert.Equal(BackOutcome.ClosedDialog, BackDispatcher.Dispatch(s, () => { bool d = dialog; dialog = false; return d; },
                () => { bool x = sheet; sheet = false; return x; }, id => { bool x = screen; screen = false; return x; }, out _));
            Assert.Equal(BackOutcome.ClosedSheet, BackDispatcher.Dispatch(s, () => false, () => { bool x = sheet; sheet = false; return x; }, id => false, out _));
            Assert.Equal(BackOutcome.HandledByScreen, BackDispatcher.Dispatch(s, () => false, () => false, id => { bool x = screen; screen = false; return x; }, out _));
            Assert.Equal(BackOutcome.Popped, BackDispatcher.Dispatch(s, () => false, () => false, id => false, out var t));
            Assert.Equal(NavTransitionKind.Pop, t.Kind);
            Assert.Equal(BackOutcome.SwitchedToHomeTab, BackDispatcher.Dispatch(s, () => false, () => false, id => false, out _));
            Assert.Equal(ScreenId.Gallery, s.Top);
            Assert.Equal(BackOutcome.ExitRequested, BackDispatcher.Dispatch(s, () => false, () => false, id => false, out _));
        }

        [Fact]
        public void ExportProgressIgnoresSystemBack()
        {
            var s = new NavigationStack();
            s.Push(ScreenId.Studio);
            s.Push(ScreenId.ExportProgress);
            Assert.Equal(BackOutcome.Ignored, BackDispatcher.Dispatch(s, () => false, () => false, id => true, out _));
            Assert.Equal(ScreenId.ExportProgress, s.Top);
        }
    }
}
