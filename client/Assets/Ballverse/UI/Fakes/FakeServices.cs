using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.UI.Localization;
using Ballverse.UI.Theming;
using Ballverse.UI.ViewModels;
using UnityEngine;

namespace Ballverse.UI.Fakes
{
    sealed class FakeExport : FakeViewModelBase, IExportViewModel
    {
        static readonly int[] s_fps = { 30, 60 };
        readonly FakeShop _shop;
        readonly ProceduralPreview _preview = new ProceduralPreview(180, 320, 6, 21, new Color32(0x0A, 0x0C, 0x16, 0xFF));
        float _progressTime;
        int _exports;

        public FakeExport(FakeShop shop)
        {
            _shop = shop;
        }

        public ExportStage Stage { get; private set; }
        public ExportResolution Resolution { get; private set; } = ExportResolution.P1080;
        public int Fps { get; private set; } = 60;
        public IReadOnlyList<int> FpsOptions => s_fps;
        public bool HasNoWatermark => _shop.Owns(FakeShop.NoAds) || (_shop.Owns(FakeShop.RemoveExport) && _shop.Owns(FakeShop.RemoveInterstitial));
        public bool WatermarkEnabled { get; private set; }
        public long EstimatedBytes => (Resolution == ExportResolution.P1080 ? (Fps == 60 ? 16L : 10L) : (Fps == 60 ? 10L : 6L)) * 1000000L / 8L * 20L;
        public long EstimatedMs => Resolution == ExportResolution.P1080 ? (Fps == 60 ? 40000 : 20000) : 13000;
        public bool EnoughSpace => true;
        public string RemoveExportAdsPrice => _shop.PriceOf(FakeShop.RemoveExport);
        public float Progress { get; private set; }
        public long RemainingMs { get; private set; }
        public Texture ProgressPreview => _preview.Texture;
        public bool Throttled => false;
        public Texture ResultPreview => _preview.Texture;
        public bool SavedToGallery { get; private set; }
        public bool ShowWatermarkUpsell => !HasNoWatermark && _exports > 2 && _exports % 3 == 0;
        public LocalizedText FailureMessage => default;
        public bool CanFallback => true;

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (Stage != ExportStage.Progress) return;
            _preview.Step(dt);
            _progressTime += dt;
            Progress = Mathf.Clamp01(_progressTime / 4f);
            RemainingMs = (long)((4f - _progressTime) * 1000f);
            if (Progress >= 1f)
            {
                _exports++;
                SavedToGallery = true;
                Stage = ExportStage.Done;
            }
            Notify();
        }

        public void Begin() { Stage = ExportStage.Settings; Notify(); }
        public void SetResolution(ExportResolution resolution) { Resolution = resolution; Notify(); }
        public void SetFps(int fps) { Fps = fps; Notify(); }
        public void SetWatermarkEnabled(bool enabled) { WatermarkEnabled = enabled; Notify(); }
        public void OpenWatermarkOffer() { }

        public void Confirm()
        {
            if (_shop.Owns(FakeShop.RemoveExport) || _shop.Owns(FakeShop.NoAds)) StartProgress();
            else
            {
                Stage = ExportStage.Gate;
                Notify();
            }
        }

        public void WatchAdAndExport()
        {
            Stage = ExportStage.WaitingForAd;
            Notify();
            After(1f, () =>
            {
                Stage = ExportStage.External;
                Notify();
                After(1f, StartProgress);
            });
        }

        public void BuyRemoveExportAds()
        {
            Stage = ExportStage.External;
            Notify();
            After(1f, () =>
            {
                _shop.Grant(FakeShop.RemoveExport);
                StartProgress();
            });
        }

        void StartProgress()
        {
            _progressTime = 0f;
            Progress = 0f;
            SavedToGallery = false;
            Stage = ExportStage.Progress;
            Notify();
        }

        public void CancelGate() { CancelTimers(); Stage = ExportStage.Idle; Notify(); }
        public void CancelExport() { Stage = ExportStage.Idle; Notify(); }
        public void SaveToGallery() { SavedToGallery = true; Notify(); }
        public void Share(SharePlatform platform) { }
        public void RetryFailed(bool fallback720p30)
        {
            if (fallback720p30)
            {
                Resolution = ExportResolution.P720;
                Fps = 30;
            }
            StartProgress();
        }
        public void Close() { Stage = ExportStage.Idle; Notify(); }
        public void NewVideo() { Stage = ExportStage.Idle; Notify(); }
        public void GoToProjects() { Stage = ExportStage.Idle; Notify(); }
    }

    sealed class FakeProjects : FakeViewModelBase, IProjectsViewModel
    {
        readonly List<ProjectCardData> _projects = new List<ProjectCardData>();
        readonly List<ProjectCardData> _clips = new List<ProjectCardData>();
        int _counter;

        public FakeProjects()
        {
            var now = new DateTime(2026, 10, 1, 12, 0, 0);
            _projects.Add(new ProjectCardData("p1", "Ring escape #1", 21000, now, "ring_escape", null));
            _projects.Add(new ProjectCardData("p2", "Triangle chaos", 34000, now.AddDays(-1), "geometry_bounce", null));
            _projects.Add(new ProjectCardData("p3", "Helix 100", 18000, now.AddDays(-3), "helix_drop", null));
            _clips.Add(new ProjectCardData("c1", "Clip 1", 12000, now.AddDays(-2), "ring_escape", null));
        }

        public IReadOnlyList<ProjectCardData> Projects => _projects;
        public IReadOnlyList<ProjectCardData> Clips => _clips;
        public int MaxTitleLength => 40;

        List<ProjectCardData> ListOf(ProjectsTab tab)
        {
            return tab == ProjectsTab.Projects ? _projects : _clips;
        }

        public OpenTarget Open(ProjectsTab tab, string id)
        {
            return tab == ProjectsTab.Projects ? OpenTarget.Studio : OpenTarget.Simulation;
        }

        public bool Rename(ProjectsTab tab, string id, string title)
        {
            if (string.IsNullOrWhiteSpace(title) || title.Length > MaxTitleLength) return false;
            var list = ListOf(tab);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id != id) continue;
                var p = list[i];
                list[i] = new ProjectCardData(p.Id, title.Trim(), p.DurationMs, p.ModifiedLocal, p.ModeId, p.Cover);
                Notify();
                return true;
            }
            return false;
        }

        public void Duplicate(ProjectsTab tab, string id)
        {
            var list = ListOf(tab);
            var p = list.Find(x => x.Id == id);
            if (p == null) return;
            _counter++;
            list.Insert(0, new ProjectCardData(id + "_copy" + _counter, p.Title, p.DurationMs, p.ModifiedLocal, p.ModeId, p.Cover));
            Notify();
        }

        public void Delete(ProjectsTab tab, string id)
        {
            ListOf(tab).RemoveAll(x => x.Id == id);
            Notify();
        }

        public OpenTarget ExportAgain(string projectId)
        {
            return OpenTarget.Studio;
        }
    }

    sealed class FakeShop : FakeViewModelBase, IShopViewModel
    {
        public const string RemoveExport = "ballverse.ads.remove_export";
        public const string RemoveInterstitial = "ballverse.ads.remove_interstitial";
        public const string NoAds = "ballverse.bundle.no_ads";

        // Store-provided price strings (in the app they come localized from the store).
        static readonly Dictionary<string, string> s_prices = new Dictionary<string, string>
        {
            { RemoveExport, "$6.99" },
            { RemoveInterstitial, "$5.99" },
            { NoAds, "$8.99" },
        };

        readonly HashSet<string> _owned = new HashSet<string>();
        string _purchasing;

        public bool StoreAvailable => true;
        public bool IsRestoring { get; private set; }
        public bool AllPurchased => Owns(NoAds) || (Owns(RemoveExport) && Owns(RemoveInterstitial));
        public event Action<LocalizedText> Notice;

        public bool Owns(string id)
        {
            return _owned.Contains(id);
        }

        public string PriceOf(string id)
        {
            return s_prices.TryGetValue(id, out var p) ? p : null;
        }

        public void Grant(string id)
        {
            _owned.Add(id);
            Notify();
        }

        public IReadOnlyList<ShopProductData> Products
        {
            get
            {
                var list = new List<ShopProductData>();
                if (AllPurchased) return list;
                bool anyAd = Owns(RemoveExport) || Owns(RemoveInterstitial);
                if (!anyAd)
                {
                    list.Add(new ShopProductData(NoAds, "shop.product.no_ads.title", "shop.product.no_ads.description",
                        new[] { "shop.includes.no_export_ads", "shop.includes.no_interstitials", "shop.includes.no_watermark" },
                        s_prices[NoAds], "$12.98", 31, State(NoAds), false, true, default));
                }
                if (!Owns(RemoveExport))
                    list.Add(new ShopProductData(RemoveExport, "shop.product.remove_export.title", "shop.product.remove_export.description",
                        new[] { "shop.includes.no_export_ads" }, s_prices[RemoveExport], null, 0, State(RemoveExport), false, false,
                        Owns(RemoveInterstitial) ? LocalizedText.FromKey("shop.last_step_watermark") : default));
                if (!Owns(RemoveInterstitial))
                    list.Add(new ShopProductData(RemoveInterstitial, "shop.product.remove_interstitial.title",
                        "shop.product.remove_interstitial.description", new[] { "shop.includes.no_interstitials" },
                        s_prices[RemoveInterstitial], null, 0, State(RemoveInterstitial), false, false,
                        Owns(RemoveExport) ? LocalizedText.FromKey("shop.last_step_watermark") : default));
                return list;
            }
        }

        ProductState State(string id)
        {
            if (Owns(id)) return ProductState.Owned;
            return _purchasing == id ? ProductState.Purchasing : ProductState.Available;
        }

        public void Purchase(string productId)
        {
            _purchasing = productId;
            Notify();
            After(1f, () =>
            {
                _purchasing = null;
                _owned.Add(productId);
                Notice?.Invoke(LocalizedText.FromKey("shop.purchase_success"));
                Notify();
            });
        }

        public void RestorePurchases()
        {
            IsRestoring = true;
            Notify();
            After(1f, () =>
            {
                IsRestoring = false;
                Notice?.Invoke(LocalizedText.FromKey("shop.restore_done"));
                Notify();
            });
        }

        public void OpenTerms() { Application.OpenURL("https://example.com/terms"); }
        public void OpenPrivacyPolicy() { Application.OpenURL("https://example.com/privacy"); }
        public void SetScreenActive(bool active) { }
    }

    sealed class FakeSettings : FakeViewModelBase, IAppSettingsViewModel
    {
        readonly FakeShop _shop;
        readonly LicenseEntry[] _licenses =
        {
            new LicenseEntry("Newtonsoft.Json", "MIT License. Copyright (c) 2007 James Newton-King."),
            new LicenseEntry("Inter", "SIL Open Font License 1.1."),
        };

        public FakeSettings(FakeShop shop)
        {
            _shop = shop;
            _shop.Changed += Notify;
        }

        public bool SoundEnabled { get; private set; } = true;
        public bool HapticsEnabled { get; private set; } = true;
        public QualityPreference Quality { get; private set; }
        public LanguagePreference Language { get; private set; }
        public ThemePreference Theme { get; private set; }
        public bool SafeFlashes { get; private set; }
        public ReduceMotionPreference ReduceMotion { get; private set; }
        public bool ReduceTransparency { get; private set; }
        public bool HighContrast { get; private set; }
        public bool BallPatterns { get; private set; }
        public bool HasNoWatermark => _shop.AllPurchased;
        public bool WatermarkEnabled { get; private set; }
        public bool AutoSaveToGallery { get; private set; } = true;
        public bool PrivacyOptionsAvailable => true;
        public LocalizedText TrackingStatus => LocalizedText.FromKey("settings.tracking.not_determined");
        public string AppVersion => Application.version;
        public StorageInfo Storage { get; private set; } = new StorageInfo(120L << 20, 48L << 20, 600L << 20, 35L << 20, 2L << 30);
        public IReadOnlyList<LicenseEntry> Licenses => _licenses;
        public event Action<LocalizedText> Notice;

        public void SetSoundEnabled(bool on) { SoundEnabled = on; Notify(); }
        public void SetHapticsEnabled(bool on) { HapticsEnabled = on; Notify(); }
        public void SetQuality(QualityPreference quality) { Quality = quality; Notify(); }
        public void SetLanguage(LanguagePreference language) { Language = language; Notify(); }
        public void SetTheme(ThemePreference theme) { Theme = theme; Notify(); }
        public void SetSafeFlashes(bool on) { SafeFlashes = on; Notify(); }
        public void SetReduceMotion(ReduceMotionPreference preference) { ReduceMotion = preference; Notify(); }
        public void SetReduceTransparency(bool on) { ReduceTransparency = on; Notify(); }
        public void SetHighContrast(bool on) { HighContrast = on; Notify(); }
        public void SetBallPatterns(bool on) { BallPatterns = on; Notify(); }
        public void SetWatermarkEnabled(bool on) { WatermarkEnabled = on; Notify(); }
        public void SetAutoSaveToGallery(bool on) { AutoSaveToGallery = on; Notify(); }
        public void RestorePurchases() { _shop.RestorePurchases(); }
        public void OpenPrivacyOptions() { Notice?.Invoke(LocalizedText.FromKey("settings.privacy_options")); }
        public void RateApp() { }
        public void SendFeedback() { }
        public void RefreshStorage() { Notify(); }

        public void ClearCache()
        {
            Storage = new StorageInfo(Storage.ProjectsBytes, Storage.ClipsBytes, Storage.VideosBytes, 0, Storage.WarnAboveBytes);
            Notice?.Invoke(LocalizedText.FromKey("storage.cache_cleared"));
            Notify();
        }
    }

    sealed class FakeConsent : ITrackingConsentViewModel
    {
        public void ContinueToSystemPrompt() { }
    }

    sealed class FakeSystemAppearance : ISystemAppearance
    {
        public SystemAppearanceState Current =>
            new SystemAppearanceState(null, false, false, 1f, ResourcesTableSource.SystemLanguageTag());

        public event Action Changed
        {
            add { }
            remove { }
        }
    }
}
