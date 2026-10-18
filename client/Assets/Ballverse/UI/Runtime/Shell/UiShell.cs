using System;
using Ballverse.Core;
using Ballverse.UI.Components;
using Ballverse.UI.Glass;
using Ballverse.UI.Localization;
using Ballverse.UI.Navigation;
using Ballverse.UI.Screens;
using Ballverse.UI.Theming;
using Ballverse.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI
{
    /// <summary>What the app (composition root) can ask of the UI shell.</summary>
    public interface IUiShell
    {
        UiNavigator Navigator { get; }
        DialogHost Dialogs { get; }
        ToastHost Toasts { get; }
        SheetHost Sheets { get; }
        Localizer Localizer { get; }
        ThemeController Theme { get; }
        GlassSurfaces Glass { get; }
        /// <summary>Opens a mode's simulation (first launch goes straight to M01, spec §4.3).</summary>
        void OpenMode(string modeId);
        /// <summary>The 0.5 s "Ad" plaque before an interstitial (spec §5.3.2 IN8); <paramref name="shown"/> runs after it.</summary>
        void ShowAdNotice(Action shown);
        /// <summary>Own explanation screen before the ATT system dialog (spec §5.6).</summary>
        void ShowTrackingPrePrompt();
        /// <summary>One-time photosensitivity warning (spec §4.8).</summary>
        void ShowPhotosensitivityWarning(Action acknowledged);
        void ShowToast(LocalizedText text);
        /// <summary>Device tier for the glass quality (spec §4.5, §8.1).</summary>
        void SetDeviceTier(DeviceTier tier);
    }

    /// <summary>
    /// Builds and runs the app shell inside a root element: layers, hosts, navigation, theme, localization, glass,
    /// safe area and the export flow. Driven every frame by <see cref="BallverseUIRoot"/>.
    /// </summary>
    public sealed class UiShell : IUiShell, IDisposable
    {
        readonly IBallverseViewModels _vms;
        readonly UiContext _ctx;
        readonly ExportFlowController _exportFlow;
        bool _disposed;

        public UiNavigator Navigator => _ctx.Navigator;
        public DialogHost Dialogs => _ctx.Dialogs;
        public ToastHost Toasts => _ctx.Toasts;
        public SheetHost Sheets => _ctx.Sheets;
        public Localizer Localizer => _ctx.Localizer;
        public ThemeController Theme => _ctx.Theme;
        public GlassSurfaces Glass => _ctx.Glass;
        public UiContext Context => _ctx;

        public UiShell(VisualElement host, IBallverseViewModels viewModels, Localizer.ITableSource tables = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            _vms = viewModels ?? throw new ArgumentNullException(nameof(viewModels));

            var shell = UiAssets.Instantiate("AppShell");
            host.Add(shell);
            var appRoot = UiAssets.Q<VisualElement>(shell, "app-root");
            UiAssets.AddGlobalStyles(appRoot);

            var prefs = ReadPreferences();
            var system = _vms.SystemAppearance?.Current ?? SystemAppearanceState.Unknown;
            var appearance = ResolvedAppearance.Resolve(prefs, system);
            var localizer = new Localizer(tables ?? new ResourcesTableSource(), appearance.Language);

            _ctx = new UiContext(localizer, _vms, appRoot);
            _ctx.Glass = new GlassSurfaces(appRoot);
            _ctx.SafeArea = new SafeAreaController(appRoot);
            _ctx.Theme = new ThemeController(appRoot, _ctx.Glass);
            _ctx.Sheets = new SheetHost(UiAssets.Q<VisualElement>(appRoot, "sheet-layer"));
            _ctx.Dialogs = new DialogHost(_ctx, UiAssets.Q<VisualElement>(appRoot, "dialog-layer"));
            _ctx.Toasts = new ToastHost(UiAssets.Q<VisualElement>(appRoot, "toast-layer"));
            var tabBar = new TabBar(UiAssets.Q<VisualElement>(appRoot, "tabbar-host"));
            _ctx.Navigator = new UiNavigator(_ctx, UiAssets.Q<VisualElement>(appRoot, "screen-layer"), tabBar);
            _ctx.Theme.Apply(appearance);
            _ctx.Prepare(UiAssets.Q<VisualElement>(appRoot, "tabbar-host"));

            RegisterScreens(_ctx.Navigator);
            _exportFlow = new ExportFlowController(_ctx);

            if (_vms.Settings != null) _vms.Settings.Changed += OnAppearanceInputsChanged;
            if (_vms.SystemAppearance != null) _vms.SystemAppearance.Changed += OnAppearanceInputsChanged;
            _ctx.Navigator.Start();
        }

        static void RegisterScreens(UiNavigator nav)
        {
            nav.Register(ScreenId.Gallery, () => new GalleryScreen());
            nav.Register(ScreenId.Projects, () => new ProjectsScreen());
            nav.Register(ScreenId.Shop, () => new ShopScreen());
            nav.Register(ScreenId.Simulation, () => new SimulationScreen());
            nav.Register(ScreenId.Studio, () => new StudioScreen());
            nav.Register(ScreenId.Settings, () => new SettingsScreen());
            nav.Register(ScreenId.Storage, () => new StorageScreen());
            nav.Register(ScreenId.About, () => new AboutScreen());
            nav.Register(ScreenId.TrackingPrePrompt, () => new TrackingPrePromptScreen());
            nav.Register(ScreenId.ExportProgress, () => new ExportProgressScreen());
            nav.Register(ScreenId.ExportDone, () => new ExportDoneScreen());
        }

        UiPreferences ReadPreferences()
        {
            var s = _vms.Settings;
            if (s == null) return UiPreferences.Default;
            return new UiPreferences(s.Theme, s.Language, s.ReduceMotion, s.ReduceTransparency, s.HighContrast);
        }

        void OnAppearanceInputsChanged()
        {
            if (_disposed) return;
            var appearance = ResolvedAppearance.Resolve(ReadPreferences(), _vms.SystemAppearance?.Current ?? SystemAppearanceState.Unknown);
            _ctx.Theme.Apply(appearance);
            if (_ctx.Localizer.SetLanguage(appearance.Language)) _ctx.Binder.Refresh(_ctx.AppRoot);
        }

        /// <summary>Per-frame update (from <see cref="BallverseUIRoot"/>.Update).</summary>
        public void Update(float deltaTime)
        {
            if (_disposed) return;
            if (BackButtonInput.WasPressedThisFrame()) _ctx.Navigator.Back();
            _ctx.SafeArea.Refresh();
            _ctx.Navigator.Tick(deltaTime);
        }

        /// <summary>Per-frame glass pass (from <see cref="BallverseUIRoot"/>.LateUpdate).</summary>
        public void LateUpdate()
        {
            if (_disposed) return;
            _ctx.Glass.Update();
        }

        public void OpenMode(string modeId)
        {
            _vms.Gallery?.OpenMode(modeId);
            _ctx.Navigator.Push(ScreenId.Simulation);
        }

        public void ShowAdNotice(Action shown)
        {
            _ctx.Toasts.ShowAdNotice(_ctx.L("ads.notice"), shown);
        }

        public void ShowTrackingPrePrompt()
        {
            _ctx.Navigator.Push(ScreenId.TrackingPrePrompt);
        }

        public void ShowPhotosensitivityWarning(Action acknowledged)
        {
            var spec = new DialogSpec
            {
                Title = LocalizedText.FromKey("a11y.photosensitivity.title"),
                Body = LocalizedText.FromKey("a11y.photosensitivity.body"),
                Icon = IconKind.Eye,
                Cancellable = false,
            };
            spec.Buttons.Add(DialogButton.Key("common.ok", DialogButtonStyle.Primary, acknowledged));
            _ctx.Dialogs.Show(spec);
        }

        public void ShowToast(LocalizedText text)
        {
            _ctx.Toasts.Show(_ctx.L(text));
        }

        public void SetDeviceTier(DeviceTier tier)
        {
            _ctx.Theme.SetTier(tier);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_vms.Settings != null) _vms.Settings.Changed -= OnAppearanceInputsChanged;
            if (_vms.SystemAppearance != null) _vms.SystemAppearance.Changed -= OnAppearanceInputsChanged;
            _exportFlow.Dispose();
            _ctx.Glass.Dispose();
        }
    }
}
