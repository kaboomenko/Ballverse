using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.UI.Localization;
using UnityEngine;

namespace Ballverse.UI.ViewModels
{
    /// <summary>Export flow stages (spec §3.8 "Поток экспорта", §5.3.1).</summary>
    public enum ExportStage
    {
        /// <summary>No export UI.</summary>
        Idle = 0,
        /// <summary>Export settings sheet: resolution, FPS, watermark, estimates.</summary>
        Settings = 1,
        /// <summary>Rewarded gate dialog (no NO_EXPORT_ADS).</summary>
        Gate = 2,
        /// <summary>The user chose to watch the ad; waiting for it to load (≤ rewarded_wait_timeout_sec).</summary>
        WaitingForAd = 3,
        /// <summary>Full-screen ad or store purchase on top of the app.</summary>
        External = 4,
        Progress = 5,
        Done = 6,
        Failed = 7,
        /// <summary>
        /// At launch: the last export was interrupted because the system unloaded the app (spec §3.8, RW4); offer
        /// "Продолжить экспорт" from the last finished segment, without another ad.
        /// </summary>
        ResumeOffer = 8,
    }

    public enum SharePlatform
    {
        /// <summary>System share sheet.</summary>
        System = 0,
        TikTok = 1,
        Instagram = 2,
        YouTube = 3,
    }

    /// <summary>Export settings, gate, progress and result (spec §3.8, §3.10, §5.3.1).</summary>
    public interface IExportViewModel : IObservableViewModel
    {
        ExportStage Stage { get; }

        // Settings
        ExportResolution Resolution { get; }
        int Fps { get; }
        IReadOnlyList<int> FpsOptions { get; }
        /// <summary>User owns NO_WATERMARK (release 1: No Ads Bundle or both ad removals).</summary>
        bool HasNoWatermark { get; }
        bool WatermarkEnabled { get; }
        long EstimatedBytes { get; }
        long EstimatedMs { get; }
        /// <summary>Free space check (spec §3.8 "Мало места"); false shows the message and a link to Storage.</summary>
        bool EnoughSpace { get; }

        // Gate
        /// <summary>Store price of Remove Export Ads, or null when the store is unavailable (button hidden).</summary>
        string RemoveExportAdsPrice { get; }

        // Progress
        float Progress { get; }
        long RemainingMs { get; }
        Texture ProgressPreview { get; }
        /// <summary>Thermal slow-down hint (spec §3.8 "Перегрев").</summary>
        bool Throttled { get; }

        // Done
        Texture ResultPreview { get; }
        bool SavedToGallery { get; }
        /// <summary>"Убрать водяной знак" line under the preview (spec §5.2: not in the first 2 exports, ≤ 1 in 3).</summary>
        bool ShowWatermarkUpsell { get; }

        // Resume offer
        /// <summary>Share of the interrupted export already encoded (0–1), shown in the resume offer.</summary>
        float InterruptedProgress { get; }

        // Failed
        LocalizedText FailureMessage { get; }
        /// <summary>Offer "Export 720p/30" after an encoder error.</summary>
        bool CanFallback { get; }

        /// <summary>Studio "Экспорт" pressed: Stage → Settings.</summary>
        void Begin();
        void SetResolution(ExportResolution resolution);
        void SetFps(int fps);
        void SetWatermarkEnabled(bool enabled);
        /// <summary>Tap on the locked watermark row: open the store offer (spec §3.10). The UI opens the shop.</summary>
        void OpenWatermarkOffer();
        /// <summary>Export pressed in the settings sheet: Gate or Progress next.</summary>
        void Confirm();
        void WatchAdAndExport();
        void BuyRemoveExportAds();
        void CancelGate();
        void CancelExport();
        void SaveToGallery();
        void Share(SharePlatform platform);
        void RetryFailed(bool fallback720p30);
        /// <summary>Close the settings sheet / failure / done screen and return to the studio.</summary>
        void Close();
        /// <summary>"Продолжить экспорт": continue the interrupted export from its last finished segment (no ad, RW4).</summary>
        void ResumeInterrupted();
        /// <summary>Drop the interrupted export (its finished segments are deleted).</summary>
        void DiscardInterrupted();
        /// <summary>"Новый ролик": the UI goes to the gallery.</summary>
        void NewVideo();
        /// <summary>"В проекты": the UI goes to the projects tab.</summary>
        void GoToProjects();
    }
}
