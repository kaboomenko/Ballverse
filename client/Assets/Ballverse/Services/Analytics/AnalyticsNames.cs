using System.Collections.Generic;

namespace Ballverse.Services
{
    /// <summary>Event names of spec §9.2 (snake_case). Automatic Firebase events (first_open, session_start, ad_impression) are not listed.</summary>
    public static class AnalyticsEvents
    {
        public const string ModeOpen = "mode_open";
        public const string SimStart = "sim_start";
        public const string SimGenerate = "sim_generate";
        public const string ParamChange = "param_change";
        public const string GameCompleted = "game_completed";
        public const string PreviewTrial = "preview_trial";
        public const string ClipCreate = "clip_create";
        public const string StudioOpen = "studio_open";
        public const string VideoImport = "video_import";
        public const string LayoutChange = "layout_change";
        public const string TextAdd = "text_add";
        public const string ExportStart = "export_start";
        public const string ExportSuccess = "export_success";
        public const string ExportFail = "export_fail";
        public const string ExportCancel = "export_cancel";
        public const string ShareTap = "share_tap";
        public const string AdRewardedOffer = "ad_rewarded_offer";
        public const string AdRewardedShow = "ad_rewarded_show";
        public const string AdRewardedReward = "ad_rewarded_reward";
        public const string AdRewardedFail = "ad_rewarded_fail";
        public const string AdRewardedDecline = "ad_rewarded_decline";
        public const string AdInterstitialShow = "ad_interstitial_show";
        public const string AdInterstitialSkip = "ad_interstitial_skip";
        public const string PaywallView = "paywall_view";
        public const string PurchaseStart = "purchase_start";
        public const string PurchaseSuccess = "purchase_success";
        public const string PurchaseFail = "purchase_fail";
        public const string PurchasePending = "purchase_pending";
        public const string RestorePurchases = "restore_purchases";
        public const string QuestStep = "quest_step";
        public const string SettingChange = "setting_change";
    }

    /// <summary>
    /// Parameter names of spec §9.2. <see cref="AnalyticsSanitizer"/> drops any parameter not listed here, so a free-text
    /// field (participant names, user text, file paths) can never reach Firebase by accident.
    /// </summary>
    public static class AnalyticsParams
    {
        public const string ModeId = "mode_id";
        public const string IsFree = "is_free";
        public const string IsOwned = "is_owned";
        public const string Source = "source";
        public const string PresetId = "preset_id";
        public const string IsGenerated = "is_generated";
        public const string TargetDuration = "target_duration";
        public const string BallCount = "ball_count";
        public const string Tier = "tier";
        public const string Style = "style";
        public const string HasForcedWinner = "has_forced_winner";
        public const string Candidates = "candidates";
        public const string Ms = "ms";
        public const string QualityOk = "quality_ok";
        public const string ParamId = "param_id";
        public const string Group = "group";
        public const string DurationS = "duration_s";
        public const string Interactive = "interactive";
        public const string Result = "result";
        public const string FromHistory = "from_history";
        public const string HasUserVideo = "has_user_video";
        public const string Res = "res";
        public const string Hdr = "hdr";
        public const string Layout = "layout";
        public const string Swap = "swap";
        public const string TemplateId = "template_id";
        public const string Fps = "fps";
        public const string Watermark = "watermark";
        public const string Segments = "segments";
        public const string Throttled = "throttled";
        public const string ErrorCode = "error_code";
        public const string Stage = "stage";
        public const string Progress = "progress";
        public const string Target = "target";
        public const string Placement = "placement";
        public const string WaitMs = "wait_ms";
        public const string Trigger = "trigger";
        public const string Reason = "reason";
        public const string Variant = "variant";
        public const string ProductId = "product_id";
        public const string RestoredCount = "restored_count";
        public const string Step = "step";
        public const string Setting = "setting";
        public const string Value = "value";

        static readonly HashSet<string> s_all = new HashSet<string>
        {
            ModeId, IsFree, IsOwned, Source, PresetId, IsGenerated, TargetDuration, BallCount, Tier, Style, HasForcedWinner,
            Candidates, Ms, QualityOk, ParamId, Group, DurationS, Interactive, Result, FromHistory, HasUserVideo, Res, Hdr,
            Layout, Swap, TemplateId, Fps, Watermark, Segments, Throttled, ErrorCode, Stage, Progress, Target, Placement,
            WaitMs, Trigger, Reason, Variant, ProductId, RestoredCount, Step, Setting, Value,
        };

        /// <summary>True for a §9.2 parameter name.</summary>
        public static bool IsKnown(string name)
        {
            return name != null && s_all.Contains(name);
        }
    }

    /// <summary>User properties of spec §9.2.</summary>
    public static class AnalyticsUserProperties
    {
        public const string Tier = "tier";
        public const string OwnedModesCount = "owned_modes_count";
        public const string HasNoExportAds = "has_no_export_ads";
        public const string HasNoInterstitials = "has_no_interstitials";
        public const string ExportsTotal = "exports_total";
        public const string UiLanguage = "ui_language";
        public const string FirstMode = "first_mode";

        static readonly HashSet<string> s_all = new HashSet<string>
        {
            Tier, OwnedModesCount, HasNoExportAds, HasNoInterstitials, ExportsTotal, UiLanguage, FirstMode,
        };

        public static bool IsKnown(string name)
        {
            return name != null && s_all.Contains(name);
        }

        /// <summary>The <c>exports_total</c> bucket of spec §9.2: 0 / 1 / 2–5 / 6–20 / 21+.</summary>
        public static string ExportsTotalBucket(long exports)
        {
            if (exports <= 0) return "0";
            if (exports == 1) return "1";
            if (exports <= 5) return "2-5";
            if (exports <= 20) return "6-20";
            return "21+";
        }
    }
}
