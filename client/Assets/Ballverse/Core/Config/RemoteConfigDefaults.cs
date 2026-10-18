using System.Collections.Generic;

namespace Ballverse.Core.Config
{
    /// <summary>
    /// Built-in default data that is larger than a scalar (spec §9.3 "Все значения имеют значения по умолчанию в сборке").
    /// Exposed to callers through <see cref="RemoteConfigValues.Defaults"/>.
    /// </summary>
    internal static class RemoteConfigDefaults
    {
        public const int InterstitialEveryNGames = 3;
        public const int InterstitialMinIntervalSec = 60;
        public const int InterstitialGraceGames = 0;
        public const bool RewardedExportEnabled = true;
        public const int RewardedWaitTimeoutSec = 5;
        public const int RewardedRetryWindowMin = 10;
        public const bool PaidModeTrialEnabled = true;
        public const int PaidModeTrialSeconds = 30;
        public const bool WatermarkEnabled = true;
        public const int RatePromptAfterExports = 2;

        /// <summary>Spec §9.3: {A:60, B:60, C:30}.</summary>
        public static TierValues<int> ExportDefaultFpsByTier()
        {
            return new TierValues<int>(60, 60, 30);
        }

        /// <summary>Spec §2.4 budget table: A 24 / 1.5 s, B 16 / 2.5 s, C 8 / 3.5 s.</summary>
        public static TierValues<GenerationBudget> GenerationBudgetByTier()
        {
            return new TierValues<GenerationBudget>(
                new GenerationBudget(24, 1500),
                new GenerationBudget(16, 2500),
                new GenerationBudget(8, 3500));
        }

        /// <summary>Spec §5.1 / §9.3: M01–M05 (the catalog's FreeByDefault modes).</summary>
        public static List<ModeCode> FreeModes()
        {
            var list = new List<ModeCode>();
            foreach (var m in ModeCatalog.All)
            {
                if (m.FreeByDefault) list.Add(m.Code);
            }
            return list;
        }

        /// <summary>Gallery order default: catalog order M01…M15.</summary>
        public static List<ModeCode> GalleryOrder()
        {
            var list = new List<ModeCode>();
            foreach (var m in ModeCatalog.All) list.Add(m.Code);
            return list;
        }

        /// <summary>
        /// Offline masks for TikTok, Reels and Shorts (spec §3.3): top tab bar, right-hand action column, bottom caption and navigation.
        /// Approximate placeholder geometry until measured against the live apps; Remote Config replaces it per platform.
        /// </summary>
        public static List<PlatformSafeZone> PlatformSafeZones()
        {
            return new List<PlatformSafeZone>
            {
                new PlatformSafeZone("reels", new[]
                {
                    new NormalizedRect(0f, 0f, 1f, 0.07f),
                    new NormalizedRect(0.85f, 0.45f, 0.15f, 0.375f),
                    new NormalizedRect(0f, 0.825f, 1f, 0.175f),
                }),
                new PlatformSafeZone("shorts", new[]
                {
                    new NormalizedRect(0f, 0f, 1f, 0.07f),
                    new NormalizedRect(0.85f, 0.425f, 0.15f, 0.375f),
                    new NormalizedRect(0f, 0.8f, 1f, 0.2f),
                }),
                new PlatformSafeZone("tiktok", new[]
                {
                    new NormalizedRect(0f, 0f, 1f, 0.08f),
                    new NormalizedRect(0.85f, 0.4f, 0.15f, 0.4f),
                    new NormalizedRect(0f, 0.8f, 1f, 0.2f),
                }),
            };
        }

        /// <summary>Spec §3.5 templates in English and Russian. <c>{n}</c> is the live "remaining" counter.</summary>
        public static List<TextTemplateSet> TextTemplates()
        {
            return new List<TextTemplateSet>
            {
                new TextTemplateSet("en", new[]
                {
                    new TextTemplate("who_wins", "Which ball will win?"),
                    new TextTemplate("comment_color", "Comment your color \U0001F447"),
                    new TextTemplate("part_2", "Part 2"),
                    new TextTemplate("try_not_to_stare", "Try not to get hooked"),
                    new TextTemplate("who_survives", "Who will survive?"),
                    new TextTemplate("remaining", "Remaining: {n}"),
                }),
                new TextTemplateSet("ru", new[]
                {
                    new TextTemplate("who_wins", "Какой шар победит?"),
                    new TextTemplate("comment_color", "Пиши свой цвет в комментариях \U0001F447"),
                    new TextTemplate("part_2", "Часть 2"),
                    new TextTemplate("try_not_to_stare", "Попробуй не залипнуть"),
                    new TextTemplate("who_survives", "Кто выживет?"),
                    new TextTemplate("remaining", "Осталось: {n}"),
                }),
            };
        }
    }
}
