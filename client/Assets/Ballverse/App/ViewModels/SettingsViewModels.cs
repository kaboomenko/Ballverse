using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.Modes;
using Ballverse.Core.Monetization;
using Ballverse.Media;
using Ballverse.Services;
using Ballverse.UI.Localization;
using Ballverse.UI.Theming;
using Ballverse.UI.ViewModels;

namespace Ballverse.App
{
    /// <summary>
    /// Settings (spec §4.3, §4.8): sound, haptics, quality (re-tiers the renderer and generator), language, theme, accessibility,
    /// watermark (owners of NO_WATERMARK), auto-save, caption copy, storage (usage, clear cache), restore purchases, privacy
    /// options (UMP) and ATT status, rate, feedback (diagnostics without personal data), about. Persisted in
    /// <see cref="AppSettingsStore"/>; the shell re-reads the appearance choices on <see cref="ViewModelBase.Changed"/>.
    /// </summary>
    public sealed class AppSettingsViewModel : ViewModelBase, IAppSettingsViewModel
    {
        public const string FeedbackAddress = "support@ballverse.app";

        static readonly LicenseEntry[] s_licenses =
        {
            new LicenseEntry("Newtonsoft.Json", "Copyright (c) 2007 James Newton-King. Licensed under the MIT License."),
            new LicenseEntry("K4os.Compression.LZ4", "Copyright (c) 2017 Milosz Krajewski. Licensed under the MIT License."),
            new LicenseEntry("Google Mobile Ads / User Messaging Platform", "Copyright Google LLC. Licensed under the Apache License 2.0."),
        };

        readonly AppContext _ctx;
        readonly ShopViewModel _shop;
        StorageInfo _storage = StorageInfo.Empty;

        public AppSettingsViewModel(AppContext ctx, ShopViewModel shop)
            : base(ctx.Notices.Report)
        {
            _ctx = ctx;
            _shop = shop;
            ctx.Services.Purchases.EntitlementsChanged += _ => Notify();
            ctx.Services.Consent.ConsentChanged += Notify;
        }

        AppSettingsData D => _ctx.Settings.Data;

        public bool SoundEnabled => D.SoundEnabled;
        public bool HapticsEnabled => D.HapticsEnabled;
        public QualityPreference Quality => D.Quality;
        public LanguagePreference Language => D.Language;
        public ThemePreference Theme => D.Theme;
        public bool SafeFlashes => D.SafeFlashes;
        public ReduceMotionPreference ReduceMotion => D.ReduceMotion;
        public bool ReduceTransparency => D.ReduceTransparency;
        public bool HighContrast => D.HighContrast;
        public bool BallPatterns => D.BallPatterns;
        public bool HasNoWatermark => _ctx.Services.Entitlements.Contains(Entitlement.NoWatermark);
        public bool WatermarkEnabled => !HasNoWatermark || D.KeepWatermark;
        public bool AutoSaveToGallery => D.AutoSaveToGallery;
        public bool CopyCaptionOnShare => D.CopyCaptionOnShare;
        public bool PrivacyOptionsAvailable => _ctx.Services.Consent.PrivacyOptions == PrivacyOptionsRequirement.Required;

        public LocalizedText TrackingStatus
        {
            get
            {
                switch (_ctx.Services.Consent.TrackingStatus)
                {
                    case TrackingAuthorizationStatus.NotDetermined: return LocalizedText.FromKey("settings.tracking.not_determined");
                    case TrackingAuthorizationStatus.Authorized: return LocalizedText.FromKey("settings.tracking.allowed");
                    case TrackingAuthorizationStatus.Denied:
                    case TrackingAuthorizationStatus.Restricted: return LocalizedText.FromKey("settings.tracking.denied");
                    default: return default;
                }
            }
        }

        public string AppVersion => _ctx.Device.AppVersion;
        public StorageInfo Storage => _storage;
        public IReadOnlyList<LicenseEntry> Licenses => s_licenses;

        public event Action<LocalizedText> Notice;

        /// <summary>The quality or power state changed: re-tier (renderer, generator, studio).</summary>
        public event Action QualityChanged;

        public void SetSoundEnabled(bool on) => Change(d => d.SoundEnabled = on, () => ReapplyAudio());

        public void SetHapticsEnabled(bool on) => Change(d => d.HapticsEnabled = on, () =>
        {
            if (_ctx.Haptics != null) _ctx.Haptics.SettingEnabled = on;
        });

        public void SetQuality(QualityPreference quality) => Change(d => d.Quality = quality, () =>
        {
            _ctx.RefreshTier();
            QualityChanged?.Invoke();
        });

        public void SetLanguage(LanguagePreference language) => Change(d => d.Language = language, null);
        public void SetTheme(ThemePreference theme) => Change(d => d.Theme = theme, null);
        public void SetSafeFlashes(bool on) => Change(d => d.SafeFlashes = on, ApplyMotion);
        public void SetReduceMotion(ReduceMotionPreference preference) => Change(d => d.ReduceMotion = preference, ApplyMotion);
        public void SetReduceTransparency(bool on) => Change(d => d.ReduceTransparency = on, null);
        public void SetHighContrast(bool on) => Change(d => d.HighContrast = on, null);
        public void SetBallPatterns(bool on) => Change(d => d.BallPatterns = on, null);

        public void SetWatermarkEnabled(bool on)
        {
            if (!HasNoWatermark) return;
            Change(d => d.KeepWatermark = on, null);
        }

        public void SetAutoSaveToGallery(bool on) => Change(d => d.AutoSaveToGallery = on, null);
        public void SetCopyCaptionOnShare(bool on) => Change(d => d.CopyCaptionOnShare = on, null);

        public void RestorePurchases() => _shop.RestorePurchases();

        public void OpenPrivacyOptions()
        {
            try
            {
                _ctx.Services.Consent.ShowPrivacyOptions(error =>
                {
                    if (error != null)
                    {
                        _ctx.Notices.Report("settings.privacy", new InvalidOperationException(error));
                        Raise(LocalizedText.FromKey(AppStrings.PrivacyFailed));
                    }
                    Notify();
                });
            }
            catch (Exception e)
            {
                _ctx.Notices.Report("settings.privacy", e);
                Raise(LocalizedText.FromKey(AppStrings.PrivacyFailed));
            }
        }

        public void RateApp()
        {
            try
            {
                _ctx.Platform.OpenStorePage();
            }
            catch (Exception e)
            {
                _ctx.Notices.Report("settings.rate", e);
            }
        }

        public void SendFeedback()
        {
            // Diagnostics without personal data or file paths (spec §7.12).
            string body = "\n\n---\nBallverse " + _ctx.Device.AppVersion + "\n" + _ctx.Device.Platform + " " + _ctx.Device.Model
                          + "\nGPU " + _ctx.Device.GpuName + ", RAM " + _ctx.Device.SystemMemoryMb + " MB, tier " + _ctx.Tier
                          + "\nServices " + _ctx.Services.Plan;
            try
            {
                _ctx.Platform.SendFeedback(FeedbackAddress, "Ballverse feedback", body);
            }
            catch (Exception e)
            {
                _ctx.Notices.Report("settings.feedback", e);
            }
        }

        public void RefreshStorage()
        {
            try
            {
                StudioUsage u = _ctx.Studio.MeasureUsage();
                long cache = DirectorySize(_ctx.Paths.Thumbnails);
                _storage = new StorageInfo(u.ProjectsBytes, u.ClipsBytes, 0, u.RecentRunsBytes + u.ExportScratchBytes + cache, StorageInfo.Empty.WarnAboveBytes);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                _ctx.Notices.Report("settings.storage", e);
            }
            Notify();
        }

        public void ClearCache()
        {
            try
            {
                _ctx.Studio.ClearCaches();
                string thumbs = _ctx.Paths.Thumbnails;
                if (Directory.Exists(thumbs))
                {
                    foreach (string f in Directory.GetFiles(thumbs)) File.Delete(f);
                }
                Raise(LocalizedText.FromKey(AppStrings.CacheCleared));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                _ctx.Notices.Report("settings.clear_cache", e, AppStrings.GenericError);
            }
            RefreshStorage();
        }

        void Change(Action<AppSettingsData> change, Action after)
        {
            _ctx.Settings.Update(change);
            _ctx.Settings.Save();
            try
            {
                after?.Invoke();
            }
            catch (Exception e)
            {
                _ctx.Notices.Report("settings.apply", e);
            }
            Notify();
        }

        void ReapplyAudio()
        {
            if (_ctx.Audio == null) return;
            ParamSetVolume(out float volume);
            _ctx.Audio.Gain = (D.SoundEnabled ? 1f : 0f) * volume;
        }

        void ParamSetVolume(out float volume)
        {
            volume = 0.8f;
            Core.Params.ParamSet values = _ctx.Live?.Values;
            if (values != null && values.TryGet(Core.Params.CommonParams.SfxVolume, out Core.Params.ParamValue v)) volume = v.AsInt / 100f;
        }

        /// <summary>Camera shake and strobing off under "Без резких вспышек" or reduced motion (spec §4.8).</summary>
        internal void ApplyMotion()
        {
            bool system = false;
            try
            {
                system = _ctx.Media.Signals != null && _ctx.Media.Signals.ReduceMotion;
            }
            catch (Exception e) when (e is MediaException || e is InvalidOperationException)
            {
                _ctx.Notices.Report("settings.signals", e);
            }
            bool reduce = D.SafeFlashes || D.ReduceMotion == ReduceMotionPreference.On || (D.ReduceMotion == ReduceMotionPreference.System && system);
            _ctx.Surface?.SetReduceMotion(reduce);
        }

        static long DirectorySize(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            long total = 0;
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) total += new FileInfo(f).Length;
            return total;
        }

        void Raise(LocalizedText text)
        {
            Action<LocalizedText> h = Notice;
            if (h != null) h(text);
            else _ctx.Notices.Toast(text);
        }
    }

    /// <summary>
    /// Device appearance (spec §4.5, §4.8): system language, reduce motion / transparency from the media bridge's device
    /// signals, font scale. Re-read when the app returns to the foreground.
    /// </summary>
    public sealed class SystemAppearanceViewModel : ISystemAppearance
    {
        readonly AppContext _ctx;

        public SystemAppearanceViewModel(AppContext ctx)
        {
            _ctx = ctx;
            Current = Read();
            ctx.Lifecycle.Resumed += Refresh;
        }

        public SystemAppearanceState Current { get; private set; }

        public event Action Changed;

        public void Refresh()
        {
            SystemAppearanceState next = Read();
            if (Same(next, Current)) return;
            Current = next;
            try
            {
                Changed?.Invoke();
            }
            catch (Exception e)
            {
                _ctx.Notices.Report("appearance.changed", e);
            }
        }

        SystemAppearanceState Read()
        {
            bool motion = false, transparency = false;
            try
            {
                DeviceSignals s = _ctx.Media.Signals;
                if (s != null)
                {
                    motion = s.ReduceMotion;
                    transparency = s.ReduceTransparency;
                }
            }
            catch (Exception e) when (e is MediaException || e is InvalidOperationException)
            {
                _ctx.Notices.Report("appearance.signals", e);
            }
            return new SystemAppearanceState(null, motion, transparency, _ctx.Device.FontScale, _ctx.Device.LanguageTag);
        }

        static bool Same(SystemAppearanceState a, SystemAppearanceState b)
        {
            if (a == null || b == null) return a == b;
            return a.PrefersDark == b.PrefersDark && a.ReduceMotion == b.ReduceMotion && a.ReduceTransparency == b.ReduceTransparency
                   && Math.Abs(a.TextScale - b.TextScale) < 0.001f && a.LanguageTag == b.LanguageTag;
        }
    }

    /// <summary>
    /// The iOS ATT pre-prompt (spec §5.6): the services call <see cref="Show"/> right before the first ad; the UI shows its own
    /// explanation screen; «Продолжить» calls <see cref="ContinueToSystemPrompt"/>, which opens the system dialog.
    /// </summary>
    public sealed class TrackingConsentViewModel : ITrackingConsentViewModel, IAttPrePrompt
    {
        readonly Func<IAppUi> _ui;
        Action<bool> _continue;

        public TrackingConsentViewModel(Func<IAppUi> ui)
        {
            _ui = ui ?? (() => null);
        }

        public void Show(Action<bool> onContinue)
        {
            IAppUi ui = _ui();
            if (ui == null)
            {
                onContinue?.Invoke(true);
                return;
            }
            _continue = onContinue;
            ui.ShowTrackingPrePrompt();
        }

        public void Dismiss()
        {
            _continue = null;
            _ui()?.DismissTrackingPrePrompt();
        }

        public void ContinueToSystemPrompt()
        {
            Action<bool> c = _continue;
            _continue = null;
            c?.Invoke(true);
        }
    }

    /// <summary>
    /// The first-video quest celebration (spec §4.3, release 1): once every step is done, a short celebration suggests another
    /// release-1 mode (the first registered mode other than the one just used) and sells nothing. Shown once.
    /// </summary>
    public sealed class QuestCelebrationViewModel : ViewModelBase, IQuestCelebrationViewModel
    {
        readonly AppContext _ctx;
        readonly SimulationViewModel _sim;

        public QuestCelebrationViewModel(AppContext ctx, SimulationViewModel sim)
            : base(ctx.Notices.Report)
        {
            _ctx = ctx;
            _sim = sim;
            sim.QuestChanged += Refresh;
        }

        public QuestCelebrationData Pending
        {
            get
            {
                AppSettingsData d = _ctx.Settings.Data;
                if (d.QuestCelebrated || !d.QuestRan || !d.QuestGenerated || !d.QuestExported) return null;
                string current = _sim.ModeId ?? d.LastModeId;
                foreach (IModeDefinition m in _ctx.Remote.GalleryModes())
                {
                    if (m.Info.Id != current) return new QuestCelebrationData(m.Info.Id, "mode." + m.Info.Id + ".name");
                }
                return null;
            }
        }

        public void MarkShown()
        {
            _ctx.Settings.Update(d => d.QuestCelebrated = true);
            _ctx.Settings.Save();
            Notify();
            _sim.NotifyChanged();
        }

        /// <summary>
        /// Re-evaluates after a quest step. With every step done but no other mode to suggest (only one mode registered), the
        /// quest is closed without a celebration so its card does not stay on screen.
        /// </summary>
        public void Refresh()
        {
            AppSettingsData d = _ctx.Settings.Data;
            if (!d.QuestCelebrated && d.QuestRan && d.QuestGenerated && d.QuestExported && Pending == null)
            {
                _ctx.Settings.Update(x => x.QuestCelebrated = true);
                _sim.NotifyChanged();
            }
            Notify();
        }
    }
}
