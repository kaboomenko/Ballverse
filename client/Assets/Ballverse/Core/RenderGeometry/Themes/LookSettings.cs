using System;
using System.Collections.Generic;
using Ballverse.Core.Params;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary><c>ballSkin</c> options (spec §2.2).</summary>
    public enum BallSkin : byte
    {
        Solid = 0,
        Gradient = 1,
        Outline = 2,
        Gloss = 3,
        /// <summary>Image from the skin atlas named by the body's skin key (emoji set).</summary>
        Emoji = 4,
        /// <summary>Image from the skin atlas named by the body's skin key (neutral flag set).</summary>
        Flag = 5,
        /// <summary>The body's label drawn large on the ball (first letters of a name, or a number).</summary>
        Letter = 6,
    }

    /// <summary><c>trail</c> options (spec §2.2).</summary>
    public enum TrailStyle : byte
    {
        Off = 0,
        Short = 1,
        Long = 2,
        Rainbow = 3,
    }

    /// <summary><c>background</c> options (spec §2.2); <see cref="Theme"/> uses the theme's own background.</summary>
    public enum BackgroundOption : byte
    {
        Theme = 0,
        Color = 1,
        Gradient = 2,
        Grid = 3,
        Stars = 4,
        Blur = 5,
    }

    /// <summary><c>impactFx</c> options (spec §2.2).</summary>
    public enum ImpactFx : byte
    {
        Off = 0,
        Sparks = 1,
        Confetti = 2,
        Shards = 3,
    }

    /// <summary>Mode-level <c>breakFx</c> options (M01, M05 cards): how a broken ring / platform disappears.</summary>
    public enum BreakFx : byte
    {
        Shards = 0,
        Flash = 1,
        Dissolve = 2,
        Burst = 3,
    }

    /// <summary><c>labels</c> options (spec §2.2).</summary>
    public enum LabelMode : byte
    {
        Off = 0,
        Numbers = 1,
        Names = 2,
    }

    /// <summary>
    /// The "Look" parameters of spec §2.2 resolved for the renderer: theme, ball palette override, skin, trail, background,
    /// glow, impact and break effects, labels and HUD. Mutable value holder; build it with <see cref="FromParams"/> from a
    /// sanitized <see cref="ParamSet"/> (live parameters may change every frame).
    /// </summary>
    public sealed class LookSettings
    {
        /// <summary>Mode parameter id of the break effect (M01 / M05 cards).</summary>
        public const string BreakFxParam = "breakFx";

        readonly List<uint> _ballColors = new List<uint>();

        public RenderTheme Theme { get; set; } = RenderThemeCatalog.Default;
        /// <summary>User ball colours (spec §2.2 <c>ballColors</c>); empty = the theme palette.</summary>
        public IReadOnlyList<uint> BallColors => _ballColors;
        public BallSkin Skin { get; set; } = BallSkin.Solid;
        public TrailStyle Trail { get; set; } = TrailStyle.Short;
        public BackgroundOption Background { get; set; } = BackgroundOption.Theme;
        public uint BackgroundColor { get; set; } = ColorMath.Pack(0x10, 0x10, 0x18);
        /// <summary><c>glow</c> 0–1: scales the theme bloom.</summary>
        public float Glow { get; set; } = 0.5f;
        public ImpactFx Impact { get; set; } = ImpactFx.Sparks;
        public BreakFx Break { get; set; } = BreakFx.Shards;
        public LabelMode Labels { get; set; } = LabelMode.Off;
        public bool Hud { get; set; } = true;

        public void SetBallColors(IReadOnlyList<uint> colors)
        {
            _ballColors.Clear();
            if (colors == null) return;
            for (int i = 0; i < colors.Count; i++) _ballColors.Add(colors[i]);
        }

        /// <summary>
        /// Reads the common Look parameters (and the mode's <c>breakFx</c> when present) from <paramref name="values"/>.
        /// Missing or unknown values keep the defaults of spec §2.2; the theme is resolved through <paramref name="themes"/>
        /// (unknown ids fall back to Neon).
        /// </summary>
        public static LookSettings FromParams(ParamSet values, RenderThemeCatalog themes)
        {
            var look = new LookSettings();
            look.Apply(values, themes);
            return look;
        }

        /// <summary>In-place variant of <see cref="FromParams"/> (no new settings object; the colour list is reused).</summary>
        public void Apply(ParamSet values, RenderThemeCatalog themes)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (themes == null) throw new ArgumentNullException(nameof(themes));
            Theme = themes.GetOrDefault(String(values, CommonParams.Theme, RenderThemeCatalog.DefaultThemeId));
            _ballColors.Clear();
            if (values.TryGet(CommonParams.BallColors, out ParamValue colors) && colors.Kind == ParamValueKind.ColorList)
            {
                IReadOnlyList<ColorRgba> list = colors.AsColors;
                for (int i = 0; i < list.Count; i++) _ballColors.Add(list[i].ToRgba32());
            }
            Skin = Option(values, CommonParams.BallSkin, CommonParams.BallSkins, BallSkin.Solid);
            Trail = Option(values, CommonParams.Trail, CommonParams.Trails, TrailStyle.Short);
            Background = Option(values, CommonParams.Background, CommonParams.Backgrounds, BackgroundOption.Theme);
            if (values.TryGet(CommonParams.BackgroundColor, out ParamValue bg) && bg.Kind == ParamValueKind.Color)
            {
                BackgroundColor = bg.AsColor.ToRgba32();
            }
            Glow = values.TryGet(CommonParams.Glow, out ParamValue glow) && glow.Kind == ParamValueKind.Float
                ? Math.Max(0f, Math.Min(1f, (float)glow.AsFloat))
                : 0.5f;
            Impact = Option(values, CommonParams.ImpactFx, CommonParams.ImpactFxs, ImpactFx.Sparks);
            Break = Option(values, BreakFxParam, BreakFxOptions, BreakFx.Shards);
            Labels = Option(values, CommonParams.Labels, CommonParams.LabelModes, LabelMode.Off);
            Hud = !values.TryGet(CommonParams.Hud, out ParamValue hud) || hud.Kind != ParamValueKind.Bool || hud.AsBool;
        }

        /// <summary>Option strings of <c>breakFx</c> in enum order (shards, flash, dissolve, burst).</summary>
        public static readonly IReadOnlyList<string> BreakFxOptions = Array.AsReadOnly(new[] { "shards", "flash", "dissolve", "burst" });

        /// <summary>The ball palette colour of a participant index (ballColors when set, otherwise the theme palette).</summary>
        public uint BallColor(int index)
        {
            if (_ballColors.Count == 0) return Theme.PaletteColor(index);
            int i = index % _ballColors.Count;
            return _ballColors[i < 0 ? i + _ballColors.Count : i];
        }

        /// <summary>Fills a 256-entry table resolving every palette index (see <see cref="PaletteSlots"/>).</summary>
        public void ResolvePalette(uint[] table)
        {
            if (table == null || table.Length < 256) throw new ArgumentException("Palette table needs 256 entries", nameof(table));
            RenderTheme t = Theme;
            for (int i = 0; i < PaletteSlots.ThemeDecorFirst; i++) table[i] = BallColor(i);
            for (int i = PaletteSlots.ThemeDecorFirst; i < PaletteSlots.RolesFirst; i++) table[i] = t.PaletteColor(i - PaletteSlots.ThemeDecorFirst);
            table[PaletteSlots.Arena] = t.Arena;
            table[PaletteSlots.ArenaAlt] = t.ArenaAlt;
            table[PaletteSlots.Danger] = t.Danger;
            table[PaletteSlots.Accent] = t.Accent;
            table[PaletteSlots.Muted] = t.Muted;
            table[PaletteSlots.Text] = t.Text;
            table[PaletteSlots.Background] = t.BackgroundBottom;
            table[PaletteSlots.Gold] = t.Gold;
            for (int i = 248; i < 256; i++) table[i] = t.Arena;
        }

        /// <summary>The effective background style (the option, or the theme's for "theme" / "color" → solid).</summary>
        public BackgroundStyle EffectiveBackground
        {
            get
            {
                switch (Background)
                {
                    case BackgroundOption.Color: return BackgroundStyle.Solid;
                    case BackgroundOption.Gradient: return BackgroundStyle.Gradient;
                    case BackgroundOption.Grid: return BackgroundStyle.Grid;
                    case BackgroundOption.Stars: return BackgroundStyle.Stars;
                    case BackgroundOption.Blur: return BackgroundStyle.Blur;
                    default: return Theme.Background;
                }
            }
        }

        static string String(ParamSet values, string id, string fallback)
        {
            return values.TryGet(id, out ParamValue v) && v.Kind == ParamValueKind.String ? v.AsString : fallback;
        }

        static T Option<T>(ParamSet values, string id, IReadOnlyList<string> options, T fallback) where T : struct, Enum
        {
            if (!values.TryGet(id, out ParamValue v) || v.Kind != ParamValueKind.String) return fallback;
            string s = v.AsString;
            for (int i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i], s, StringComparison.Ordinal)) return (T)Enum.ToObject(typeof(T), i);
            }
            return fallback;
        }
    }
}
