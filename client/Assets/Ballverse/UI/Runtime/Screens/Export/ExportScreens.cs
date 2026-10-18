using Ballverse.UI.Components;
using Ballverse.UI.Format;
using Ballverse.UI.Navigation;
using Ballverse.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Screens
{
    /// <summary>
    /// Full-screen glass export progress (spec §3.8 step 3): current frame, percent, remaining time, cancel. The screen
    /// stays awake while it is shown. System back is ignored here (cancel has its own button and confirmation).
    /// </summary>
    public sealed class ExportProgressScreen : ScreenController
    {
        IExportViewModel _vm;
        VisualElement _backdrop;
        VisualElement _preview;
        Label _percent;
        Label _remaining;
        Label _throttled;
        VisualElement _fill;
        int _sleepTimeout;

        public override ScreenId Id => ScreenId.ExportProgress;
        protected override string Layout => "ExportProgress";

        protected override void OnCreate()
        {
            _vm = Ctx.ViewModels.Export;
            _backdrop = Q<VisualElement>("backdrop");
            _preview = Q<VisualElement>("preview");
            _percent = Q<Label>("percent");
            _remaining = Q<Label>("remaining");
            _throttled = Q<Label>("throttled");
            _fill = Q<VisualElement>("fill");
            Tap.On(Root, "cancel", () =>
                Ctx.Dialogs.Confirm("export.cancel_title", "export.cancel_body", "export.cancel_confirm", true, () => _vm.CancelExport()));
        }

        protected override void OnShow()
        {
            _sleepTimeout = Screen.sleepTimeout;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            _vm.Changed += Refresh;
            Refresh();
        }

        protected override void OnHide()
        {
            _vm.Changed -= Refresh;
            Screen.sleepTimeout = _sleepTimeout;
            Ctx.Glass.SetSource(null, _backdrop);
        }

        public override void Tick(float deltaTime)
        {
            Refresh();
        }

        void Refresh()
        {
            float p = Mathf.Clamp01(_vm.Progress);
            _percent.text = Ctx.Format("export.progress.percent", Mathf.FloorToInt(p * 100f));
            _fill.style.width = Length.Percent(p * 100f);
            _remaining.text = DisplayFormat.Remaining(_vm.RemainingMs, Ctx.Localizer);
            _throttled.style.display = _vm.Throttled ? DisplayStyle.Flex : DisplayStyle.None;
            TextureSlot.Set(_preview, _vm.ProgressPreview);
            TextureSlot.Set(_backdrop, _vm.ProgressPreview);
            Ctx.Glass.SetSource(_vm.ProgressPreview, _backdrop);
        }
    }

    /// <summary>Export finished (spec §3.8 step 4): preview, save to gallery, share, platform buttons, next steps.</summary>
    public sealed class ExportDoneScreen : ScreenController
    {
        IExportViewModel _vm;
        VisualElement _preview;
        VisualElement _saved;
        VisualElement _save;
        VisualElement _upsell;

        public override ScreenId Id => ScreenId.ExportDone;
        protected override string Layout => "ExportDone";

        protected override void OnCreate()
        {
            _vm = Ctx.ViewModels.Export;
            _preview = Q<VisualElement>("preview");
            _saved = Q<VisualElement>("saved");
            _save = Q<VisualElement>("save");
            _upsell = Q<VisualElement>("upsell");
            Q<ScrollView>("scroll").verticalScrollerVisibility = ScrollerVisibility.Hidden;
            Tap.On(Root, "close", () => _vm.Close());
            Tap.On(_save, () => _vm.SaveToGallery());
            Tap.On(Root, "share", () => _vm.Share(SharePlatform.System));
            Tap.On(Root, "share-tiktok", () => SharePlatformHint(SharePlatform.TikTok));
            Tap.On(Root, "share-instagram", () => SharePlatformHint(SharePlatform.Instagram));
            Tap.On(Root, "share-youtube", () => SharePlatformHint(SharePlatform.YouTube));
            Tap.On(_upsell, () => ShopOfferSheet.Open(Ctx, "shop.watermark_offer.title"));
            Tap.On(Root, "new-video", () =>
            {
                _vm.NewVideo();
                Ctx.Navigator.SelectTab(TabId.Modes);
            });
            Tap.On(Root, "to-projects", () =>
            {
                _vm.GoToProjects();
                Ctx.Navigator.SelectTab(TabId.Projects);
            });
        }

        protected override void OnShow()
        {
            _vm.Changed += Refresh;
            Refresh();
        }

        protected override void OnHide()
        {
            _vm.Changed -= Refresh;
        }

        public override bool HandleBack()
        {
            _vm.Close();
            return true;
        }

        void SharePlatformHint(SharePlatform platform)
        {
            // Release 1 opens the system share sheet with a hint (direct publishing is 1.1, spec §3.11).
            Ctx.Toasts.Show(Ctx.L("export.done.platform_hint"));
            _vm.Share(platform);
        }

        void Refresh()
        {
            TextureSlot.Set(_preview, _vm.ResultPreview);
            _saved.style.display = _vm.SavedToGallery ? DisplayStyle.Flex : DisplayStyle.None;
            _save.style.display = _vm.SavedToGallery ? DisplayStyle.None : DisplayStyle.Flex;
            _upsell.style.display = _vm.ShowWatermarkUpsell ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
