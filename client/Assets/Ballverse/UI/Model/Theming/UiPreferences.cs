using System;
using Ballverse.UI.Localization;

namespace Ballverse.UI.Theming
{
    /// <summary>Interface theme choice (spec §4.3 Settings, §4.4: dark by default).</summary>
    public enum ThemePreference
    {
        System = 0,
        Dark = 1,
        Light = 2,
    }

    public enum ResolvedTheme
    {
        Dark = 0,
        Light = 1,
    }

    /// <summary>"Reduce motion": follow the system setting or force it (spec §4.8).</summary>
    public enum ReduceMotionPreference
    {
        System = 0,
        On = 1,
        Off = 2,
    }

    /// <summary>What the device reports (native accessibility APIs; null = unknown).</summary>
    public sealed class SystemAppearanceState
    {
        public bool? PrefersDark { get; }
        public bool ReduceMotion { get; }
        public bool ReduceTransparency { get; }
        /// <summary>System font scale (1 = 100%).</summary>
        public float TextScale { get; }
        /// <summary>BCP 47 device language tag, e.g. "ru-RU".</summary>
        public string LanguageTag { get; }

        public SystemAppearanceState(bool? prefersDark, bool reduceMotion, bool reduceTransparency, float textScale, string languageTag)
        {
            PrefersDark = prefersDark;
            ReduceMotion = reduceMotion;
            ReduceTransparency = reduceTransparency;
            TextScale = textScale;
            LanguageTag = languageTag;
        }

        public static readonly SystemAppearanceState Unknown = new SystemAppearanceState(null, false, false, 1f, null);
    }

    /// <summary>User choices that change how the shell looks.</summary>
    public sealed class UiPreferences
    {
        public ThemePreference Theme { get; }
        public LanguagePreference Language { get; }
        public ReduceMotionPreference ReduceMotion { get; }
        /// <summary>Own "Reduce Transparency" switch (needed on Android, spec §4.5); iOS also follows the system.</summary>
        public bool ReduceTransparency { get; }
        /// <summary>Contrast theme (spec §4.8 "Цветовосприятие").</summary>
        public bool HighContrast { get; }

        public UiPreferences(ThemePreference theme, LanguagePreference language, ReduceMotionPreference reduceMotion,
            bool reduceTransparency, bool highContrast)
        {
            Theme = theme;
            Language = language;
            ReduceMotion = reduceMotion;
            ReduceTransparency = reduceTransparency;
            HighContrast = highContrast;
        }

        public static readonly UiPreferences Default = new UiPreferences(ThemePreference.System, LanguagePreference.System,
            ReduceMotionPreference.System, false, false);
    }

    /// <summary>Text size step applied to the interface (spec §4.4/§4.8: up to 130% without breaking layouts).</summary>
    public enum TextScaleStep
    {
        Normal = 100,
        Large = 115,
        Largest = 130,
    }

    /// <summary>Effective appearance from user preferences and the system state. Pure.</summary>
    public sealed class ResolvedAppearance : IEquatable<ResolvedAppearance>
    {
        public ResolvedTheme Theme { get; }
        public string Language { get; }
        public bool ReduceMotion { get; }
        public bool ReduceTransparency { get; }
        public bool HighContrast { get; }
        public TextScaleStep TextScale { get; }

        public ResolvedAppearance(ResolvedTheme theme, string language, bool reduceMotion, bool reduceTransparency,
            bool highContrast, TextScaleStep textScale)
        {
            Theme = theme;
            Language = language;
            ReduceMotion = reduceMotion;
            ReduceTransparency = reduceTransparency;
            HighContrast = highContrast;
            TextScale = textScale;
        }

        public static ResolvedAppearance Resolve(UiPreferences prefs, SystemAppearanceState system)
        {
            prefs = prefs ?? UiPreferences.Default;
            system = system ?? SystemAppearanceState.Unknown;
            ResolvedTheme theme;
            switch (prefs.Theme)
            {
                case ThemePreference.Light: theme = ResolvedTheme.Light; break;
                case ThemePreference.Dark: theme = ResolvedTheme.Dark; break;
                default: theme = system.PrefersDark == false ? ResolvedTheme.Light : ResolvedTheme.Dark; break;
            }
            bool reduceMotion = prefs.ReduceMotion == ReduceMotionPreference.On
                || (prefs.ReduceMotion == ReduceMotionPreference.System && system.ReduceMotion);
            return new ResolvedAppearance(theme, LanguageResolver.Resolve(prefs.Language, system.LanguageTag), reduceMotion,
                prefs.ReduceTransparency || system.ReduceTransparency, prefs.HighContrast, StepFor(system.TextScale));
        }

        /// <summary>Nearest supported text step for a system font scale (above 130% stays at 130%).</summary>
        public static TextScaleStep StepFor(float scale)
        {
            if (float.IsNaN(scale) || scale < 1.075f) return TextScaleStep.Normal;
            if (scale < 1.225f) return TextScaleStep.Large;
            return TextScaleStep.Largest;
        }

        public bool Equals(ResolvedAppearance o)
        {
            return o != null && Theme == o.Theme && Language == o.Language && ReduceMotion == o.ReduceMotion
                && ReduceTransparency == o.ReduceTransparency && HighContrast == o.HighContrast && TextScale == o.TextScale;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ResolvedAppearance);
        }

        public override int GetHashCode()
        {
            return ((int)Theme * 397) ^ (Language?.GetHashCode() ?? 0) ^ ((int)TextScale << 3);
        }
    }
}
