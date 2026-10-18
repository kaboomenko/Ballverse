using System;
using System.Collections.Generic;
using Ballverse.UI.Localization;
using Ballverse.UI.Theming;

namespace Ballverse.UI.ViewModels
{
    public enum QualityPreference
    {
        Auto = 0,
        High = 1,
        Economy = 2,
    }

    public sealed class StorageInfo
    {
        public long ProjectsBytes { get; }
        public long ClipsBytes { get; }
        public long VideosBytes { get; }
        public long CacheBytes { get; }
        public long TotalBytes => ProjectsBytes + ClipsBytes + VideosBytes + CacheBytes;
        /// <summary>Warn above this (spec §3.9: 2 GB).</summary>
        public long WarnAboveBytes { get; }

        public StorageInfo(long projectsBytes, long clipsBytes, long videosBytes, long cacheBytes, long warnAboveBytes)
        {
            ProjectsBytes = projectsBytes;
            ClipsBytes = clipsBytes;
            VideosBytes = videosBytes;
            CacheBytes = cacheBytes;
            WarnAboveBytes = warnAboveBytes;
        }

        public static readonly StorageInfo Empty = new StorageInfo(0, 0, 0, 0, 2L * 1024 * 1024 * 1024);
    }

    public sealed class LicenseEntry
    {
        /// <summary>Component name (literal).</summary>
        public string Name { get; }
        /// <summary>License text (literal, shown as is).</summary>
        public string Text { get; }

        public LicenseEntry(string name, string text)
        {
            Name = name ?? "";
            Text = text ?? "";
        }
    }

    /// <summary>App settings (spec §4.3 "Настройки", §4.8). The shell also reads the appearance choices from it.</summary>
    public interface IAppSettingsViewModel : IObservableViewModel
    {
        bool SoundEnabled { get; }
        bool HapticsEnabled { get; }
        QualityPreference Quality { get; }
        LanguagePreference Language { get; }
        ThemePreference Theme { get; }
        /// <summary>"Без резких вспышек" (spec §4.8, US-20).</summary>
        bool SafeFlashes { get; }
        ReduceMotionPreference ReduceMotion { get; }
        bool ReduceTransparency { get; }
        bool HighContrast { get; }
        /// <summary>"Узоры на шарах".</summary>
        bool BallPatterns { get; }
        bool HasNoWatermark { get; }
        bool WatermarkEnabled { get; }
        bool AutoSaveToGallery { get; }
        /// <summary>
        /// Copy the caption with hashtags ("#ballverse #satisfying" + the mode's tags) to the clipboard when a platform
        /// button is tapped on the done screen (spec §3.11, switch in Settings).
        /// </summary>
        bool CopyCaptionOnShare { get; }
        /// <summary>UMP privacy options entry point is required in this region.</summary>
        bool PrivacyOptionsAvailable { get; }
        /// <summary>ATT status line (iOS); empty hides it.</summary>
        LocalizedText TrackingStatus { get; }
        /// <summary>Version string, e.g. "1.0.0 (42)".</summary>
        string AppVersion { get; }
        StorageInfo Storage { get; }
        IReadOnlyList<LicenseEntry> Licenses { get; }
        event Action<LocalizedText> Notice;

        void SetSoundEnabled(bool on);
        void SetHapticsEnabled(bool on);
        void SetQuality(QualityPreference quality);
        void SetLanguage(LanguagePreference language);
        void SetTheme(ThemePreference theme);
        void SetSafeFlashes(bool on);
        void SetReduceMotion(ReduceMotionPreference preference);
        void SetReduceTransparency(bool on);
        void SetHighContrast(bool on);
        void SetBallPatterns(bool on);
        void SetWatermarkEnabled(bool on);
        void SetAutoSaveToGallery(bool on);
        void SetCopyCaptionOnShare(bool on);
        void RestorePurchases();
        void OpenPrivacyOptions();
        void RateApp();
        void SendFeedback();
        void RefreshStorage();
        void ClearCache();
    }
}
