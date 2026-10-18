using System;
using Ballverse.UI.Components;
using Ballverse.UI.Format;
using Ballverse.UI.Localization;
using Ballverse.UI.Navigation;
using Ballverse.UI.Theming;
using Ballverse.UI.ViewModels;
using UnityEngine.UIElements;

namespace Ballverse.UI.Screens
{
    /// <summary>
    /// App settings (spec §4.3 "Настройки", §4.8): sound and haptics, graphics quality, language, theme, accessibility,
    /// watermark, auto-save, storage, restore purchases, privacy (UMP / ATT), rate, feedback, about. Solid surfaces
    /// (spec §4.5: long lists are not glass).
    /// </summary>
    public sealed class SettingsScreen : ScreenController
    {
        static readonly QualityPreference[] s_quality = { QualityPreference.Auto, QualityPreference.High, QualityPreference.Economy };
        static readonly LanguagePreference[] s_language = { LanguagePreference.System, LanguagePreference.English, LanguagePreference.Russian };
        static readonly ThemePreference[] s_theme = { ThemePreference.System, ThemePreference.Dark, ThemePreference.Light };
        static readonly ReduceMotionPreference[] s_motion = { ReduceMotionPreference.System, ReduceMotionPreference.On, ReduceMotionPreference.Off };

        IAppSettingsViewModel _vm;
        VisualElement _content;
        BvSwitch _sound;
        BvSwitch _haptics;
        SegmentedControl _quality;
        SegmentedControl _language;
        SegmentedControl _theme;
        BvSwitch _flashes;
        SegmentedControl _motion;
        BvSwitch _transparency;
        BvSwitch _contrast;
        BvSwitch _patterns;
        BvSwitch _autosave;
        VisualElement _watermarkHost;
        VisualElement _privacyHost;
        VisualElement _storageRow;
        VisualElement _aboutRow;
        bool _built;

        public override ScreenId Id => ScreenId.Settings;
        protected override string Layout => "Settings";

        protected override void OnCreate()
        {
            _vm = Ctx.ViewModels.Settings;
            _content = Q<VisualElement>("content");
            Q<ScrollView>("scroll").verticalScrollerVisibility = ScrollerVisibility.Hidden;
            Tap.On(Root, "back", () => Ctx.Navigator.Pop());
        }

        protected override void OnShow()
        {
            _vm.Changed += Refresh;
            _vm.Notice += OnNotice;
            if (!_built) Build();
            Refresh();
        }

        protected override void OnHide()
        {
            _vm.Changed -= Refresh;
            _vm.Notice -= OnNotice;
        }

        protected override void OnLanguageChanged()
        {
            Build();
            Refresh();
        }

        void OnNotice(LocalizedText text)
        {
            Ctx.Toasts.Show(Ctx.L(text));
        }

        void Build()
        {
            _built = true;
            _content.Clear();
            var f = new FormBuilder(Ctx, _content);
            f.Section("settings.section.sound");
            _sound = f.Toggle("settings.sound", true, v => _vm.SetSoundEnabled(v));
            _haptics = f.Toggle("settings.haptics", true, v => _vm.SetHapticsEnabled(v));

            f.Section("settings.section.display");
            _quality = f.Segmented("settings.quality",
                new[] { Ctx.L("settings.quality.auto"), Ctx.L("settings.quality.high"), Ctx.L("settings.quality.economy") }, 0,
                i => _vm.SetQuality(s_quality[i]));
            _language = f.Segmented("settings.language",
                new[] { Ctx.L("language.system"), Ctx.L("language.en"), Ctx.L("language.ru") }, 0, i => _vm.SetLanguage(s_language[i]));
            _theme = f.Segmented("settings.theme",
                new[] { Ctx.L("settings.theme.system"), Ctx.L("settings.theme.dark"), Ctx.L("settings.theme.light") }, 0,
                i => _vm.SetTheme(s_theme[i]));

            f.Section("settings.section.accessibility");
            _flashes = f.Toggle("settings.safe_flashes", false, v => _vm.SetSafeFlashes(v), "settings.safe_flashes_note");
            _motion = f.Segmented("settings.reduce_motion",
                new[] { Ctx.L("settings.motion.system"), Ctx.L("common.on"), Ctx.L("common.off") }, 0, i => _vm.SetReduceMotion(s_motion[i]));
            _transparency = f.Toggle("settings.reduce_transparency", false, v => _vm.SetReduceTransparency(v), "settings.reduce_transparency_note");
            _contrast = f.Toggle("settings.high_contrast", false, v => _vm.SetHighContrast(v));
            _patterns = f.Toggle("settings.ball_patterns", false, v => _vm.SetBallPatterns(v), "settings.ball_patterns_note");

            f.Section("settings.section.video");
            _watermarkHost = new VisualElement();
            _content.Add(_watermarkHost);
            _autosave = f.Toggle("settings.autosave", true, v => _vm.SetAutoSaveToGallery(v));
            _storageRow = f.NavRow(IconKind.Storage, "settings.storage", "", () => Ctx.Navigator.Push(ScreenId.Storage));

            f.Section("settings.section.purchases");
            f.NavRow(IconKind.Restore, "settings.restore", null, () => _vm.RestorePurchases(), false, false);

            f.Section("settings.section.privacy");
            _privacyHost = new VisualElement();
            _content.Add(_privacyHost);

            f.Section("settings.section.about");
            f.NavRow(IconKind.Star, "settings.rate", null, () => _vm.RateApp(), false, false);
            f.NavRow(IconKind.Mail, "settings.feedback", null, () => _vm.SendFeedback(), false, false);
            _aboutRow = f.NavRow(IconKind.Info, "settings.about", "", () => Ctx.Navigator.Push(ScreenId.About));
        }

        void Refresh()
        {
            _sound.SetValueWithoutNotify(_vm.SoundEnabled);
            _haptics.SetValueWithoutNotify(_vm.HapticsEnabled);
            _quality.SetSelectedWithoutNotify(Array.IndexOf(s_quality, _vm.Quality));
            _language.SetSelectedWithoutNotify(Array.IndexOf(s_language, _vm.Language));
            _theme.SetSelectedWithoutNotify(Array.IndexOf(s_theme, _vm.Theme));
            _flashes.SetValueWithoutNotify(_vm.SafeFlashes);
            _motion.SetSelectedWithoutNotify(Array.IndexOf(s_motion, _vm.ReduceMotion));
            _transparency.SetValueWithoutNotify(_vm.ReduceTransparency);
            _contrast.SetValueWithoutNotify(_vm.HighContrast);
            _patterns.SetValueWithoutNotify(_vm.BallPatterns);
            _autosave.SetValueWithoutNotify(_vm.AutoSaveToGallery);
            _storageRow.Q<Label>("value").text = DisplayFormat.Bytes(_vm.Storage.TotalBytes, Ctx.Localizer);
            _aboutRow.Q<Label>("value").text = _vm.AppVersion ?? "";

            _watermarkHost.Clear();
            var wf = new FormBuilder(Ctx, _watermarkHost);
            if (_vm.HasNoWatermark) wf.Toggle("settings.watermark", _vm.WatermarkEnabled, v => _vm.SetWatermarkEnabled(v));
            else wf.NavRow(IconKind.Sparkle, "settings.watermark", Ctx.L("export.watermark_on"),
                () => ShopOfferSheet.Open(Ctx, "shop.watermark_offer.title"), true);

            _privacyHost.Clear();
            var pf = new FormBuilder(Ctx, _privacyHost);
            if (_vm.PrivacyOptionsAvailable)
                pf.NavRow(IconKind.Shield, "settings.privacy_options", null, () => _vm.OpenPrivacyOptions());
            if (!_vm.TrackingStatus.IsEmpty)
                pf.NavRow(IconKind.Eye, "settings.tracking_status", Ctx.L(_vm.TrackingStatus), null, false, false);
            if (!_vm.PrivacyOptionsAvailable && _vm.TrackingStatus.IsEmpty) pf.NoteKey("settings.privacy_none");
        }
    }

    /// <summary>Settings → Storage (spec §3.9): sizes of projects, clips, user videos, cache; clear cache.</summary>
    public sealed class StorageScreen : ScreenController
    {
        IAppSettingsViewModel _vm;
        VisualElement _content;

        public override ScreenId Id => ScreenId.Storage;
        protected override string Layout => "Storage";

        protected override void OnCreate()
        {
            _vm = Ctx.ViewModels.Settings;
            _content = Q<VisualElement>("content");
            Tap.On(Root, "back", () => Ctx.Navigator.Pop());
        }

        protected override void OnShow()
        {
            _vm.Changed += Refresh;
            _vm.RefreshStorage();
            Refresh();
        }

        protected override void OnHide()
        {
            _vm.Changed -= Refresh;
        }

        protected override void OnLanguageChanged()
        {
            Refresh();
        }

        void Refresh()
        {
            var s = _vm.Storage;
            _content.Clear();
            var f = new FormBuilder(Ctx, _content);
            if (s.TotalBytes > s.WarnAboveBytes) f.NoteKey("storage.warning", "bv-note bv-note--warning");
            f.NavRow(IconKind.Projects, "storage.projects", DisplayFormat.Bytes(s.ProjectsBytes, Ctx.Localizer), null, false, false);
            f.NavRow(IconKind.Clip, "storage.clips", DisplayFormat.Bytes(s.ClipsBytes, Ctx.Localizer), null, false, false);
            f.NavRow(IconKind.Video, "storage.videos", DisplayFormat.Bytes(s.VideosBytes, Ctx.Localizer), null, false, false);
            f.NavRow(IconKind.Storage, "storage.cache", DisplayFormat.Bytes(s.CacheBytes, Ctx.Localizer), null, false, false);
            f.Divider();
            f.NavRow(IconKind.Info, "storage.total", DisplayFormat.Bytes(s.TotalBytes, Ctx.Localizer), null, false, false);
            f.Button("storage.clear_cache", IconKind.Trash, () =>
                Ctx.Dialogs.Confirm("storage.clear_cache_title", "storage.clear_cache_body", "storage.clear_cache", true, () => _vm.ClearCache()));
            f.Button("storage.manage_projects", IconKind.Projects, () => Ctx.Navigator.SelectTab(TabId.Projects), "plain");
            f.NoteKey("storage.note");
        }
    }

    /// <summary>Settings → About: version and third-party licenses.</summary>
    public sealed class AboutScreen : ScreenController
    {
        public override ScreenId Id => ScreenId.About;
        protected override string Layout => "About";

        protected override void OnCreate()
        {
            Tap.On(Root, "back", () => Ctx.Navigator.Pop());
        }

        protected override void OnShow()
        {
            Build();
        }

        protected override void OnLanguageChanged()
        {
            Build();
        }

        void Build()
        {
            var vm = Ctx.ViewModels.Settings;
            var content = Q<VisualElement>("content");
            content.Clear();
            var f = new FormBuilder(Ctx, content);
            f.NavRow(IconKind.Info, "about.version", vm.AppVersion ?? "", null, false, false);
            f.Section("about.licenses");
            foreach (var l in vm.Licenses)
            {
                content.Add(UiContext.TextLabel(l.Name, "bv-license__name"));
                content.Add(UiContext.TextLabel(l.Text, "bv-license__text"));
            }
        }
    }
}
