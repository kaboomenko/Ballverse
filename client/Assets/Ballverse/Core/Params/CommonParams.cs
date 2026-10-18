using System;
using System.Collections.Generic;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// The common parameter table of spec §2.2, available in every mode. Ranges and defaults follow the table; entries
    /// the table marks "зависит от режима" / "режим" / "тема" get generic defaults that modes override through
    /// <see cref="ParamSchema.Merge"/> (e.g. <see cref="BallCount"/>, <see cref="BallSize"/>, <see cref="Restitution"/>).
    /// Live parameters (spec §2.2 panel rules: time scale, colours, trail, look, sound, HUD) can change while running;
    /// ball count / size, physics and director parameters are structural.
    /// </summary>
    /// <remarks>
    /// Modelling decisions: <see cref="BallColors"/> empty = use the theme palette; <see cref="Background"/> option
    /// "theme" = theme background; <see cref="Hud"/> is an enum (auto = mode default elements, minimal, full, off) since
    /// §7.6 has no flag-set type; <see cref="TargetDuration"/> is an int 10–180 s that also accepts "unlimited" (default);
    /// <see cref="ForcedWinner"/> is 0 = any, otherwise the 1-based participant number (checked against the actual
    /// participants by the director, spec §2.5); <see cref="Countdown"/> is a bool (off / 3 s); <see cref="Seed"/>
    /// defaults to 0 and the app supplies a random seed (Core is pure).
    /// </remarks>
    public static class CommonParams
    {
        public const string TimeScale = "timeScale";
        public const string BallCount = "ballCount";
        public const string BallSize = "ballSize";
        public const string Theme = "theme";
        public const string Gravity = "gravity";
        public const string GravityAngle = "gravityAngle";
        public const string Restitution = "restitution";
        public const string Friction = "friction";
        public const string MaxSpeed = "maxSpeed";
        public const string BallColors = "ballColors";
        public const string BallSkin = "ballSkin";
        public const string Trail = "trail";
        public const string Background = "background";
        public const string Glow = "glow";
        public const string ImpactFx = "impactFx";
        public const string Labels = "labels";
        public const string LabelNames = "labelNames";
        public const string Hud = "hud";
        public const string SoundPack = "soundPack";
        public const string Melody = "melody";
        public const string SfxVolume = "sfxVolume";
        public const string Haptics = "haptics";
        public const string TargetDuration = "targetDuration";
        public const string ForcedWinner = "forcedWinner";
        public const string FinaleStyle = "finaleStyle";
        public const string Countdown = "countdown";
        public const string Seed = "seed";

        /// <summary>Names list limits (spec §2.2 "до 32 строк по 16 символов").</summary>
        public const int MaxLabelNames = 32;
        public const int MaxLabelNameLength = 16;
        /// <summary>Max colours in <see cref="BallColors"/> (one per ball for up to 32 named balls).</summary>
        public const int MaxBallColors = 32;

        /// <summary>Release themes (spec §2.10).</summary>
        public static readonly IReadOnlyList<string> Themes = new[]
        {
            "Neon", "Pastel", "Mono", "Candy", "Space", "Ocean", "Sunset", "Retro", "Glass", "Contrast",
        };

        /// <summary>Sound packs (spec §2.7).</summary>
        public static readonly IReadOnlyList<string> SoundPacks = new[]
        {
            "Notes", "Melody", "Pop", "Glass", "Drums", "Asmr", "Chiptune", "None",
        };

        /// <summary>Built-in melodies named in spec §2.7; more arrive as data (pass them to <see cref="Build"/>).</summary>
        public static readonly IReadOnlyList<string> Melodies = new[]
        {
            "ode_to_joy", "fur_elise", "canon_in_d", "turkish_march", "sugar_plum_fairy", "mountain_king", "greensleeves",
        };

        public static readonly IReadOnlyList<string> BallSkins = new[] { "solid", "gradient", "outline", "gloss", "emoji", "flag", "letter" };
        public static readonly IReadOnlyList<string> Trails = new[] { "off", "short", "long", "rainbow" };
        public static readonly IReadOnlyList<string> Backgrounds = new[] { "theme", "color", "gradient", "grid", "stars", "blur" };
        public static readonly IReadOnlyList<string> ImpactFxs = new[] { "off", "sparks", "confetti", "shards" };
        public static readonly IReadOnlyList<string> LabelModes = new[] { "off", "numbers", "names" };
        public static readonly IReadOnlyList<string> HudModes = new[] { "auto", "minimal", "full", "off" };
        public static readonly IReadOnlyList<string> FinaleStyles = new[] { "stop", "slowMo", "zoom", "confetti" };

        static readonly Lazy<ParamSchema> s_schema = new Lazy<ParamSchema>(() => Build());

        /// <summary>The common schema with the built-in theme and melody lists.</summary>
        public static ParamSchema Schema => s_schema.Value;

        /// <summary>
        /// Builds the common schema. <paramref name="themes"/> / <paramref name="melodies"/> extend or replace the option
        /// lists when content arrives as data (spec §2.10, §2.7); they must contain the defaults "Neon" / "ode_to_joy".
        /// </summary>
        public static ParamSchema Build(IEnumerable<string> themes = null, IEnumerable<string> melodies = null)
        {
            var b = new ParamSchemaBuilder();

            // Basic
            b.Add(new FloatParam(TimeScale, 0.25, 3.0, 0.05, 1.0, "x").InGroup(ParamGroup.Basic, 10).AsLive());
            b.Add(new IntParam(BallCount, 1, 100, 1, 1).InGroup(ParamGroup.Basic, 20));
            b.Add(new FloatParam(BallSize, 0.1, 1.5, 0.01, 0.5, "m").InGroup(ParamGroup.Basic, 30));
            b.Add(new EnumParam(Theme, themes ?? Themes, "Neon").InGroup(ParamGroup.Basic, 40).AsLive());

            // Physics (structural: changes the simulation)
            b.Add(new FloatParam(Gravity, 0.0, 3.0, 0.05, 1.0, "g").InGroup(ParamGroup.Physics, 10));
            b.Add(new IntParam(GravityAngle, 0, 359, 1, 270, "°").InGroup(ParamGroup.Physics, 20));
            b.Add(new FloatParam(Restitution, 0.0, 1.2, 0.01, 0.8).InGroup(ParamGroup.Physics, 30));
            b.Add(new FloatParam(Friction, 0.0, 1.0, 0.01, 0.2).InGroup(ParamGroup.Physics, 40));
            b.Add(new FloatParam(MaxSpeed, 5.0, 60.0, 1.0, 30.0, "m/s").InGroup(ParamGroup.Physics, 50));

            // Look (live)
            b.Add(new ColorListParam(BallColors, 0, MaxBallColors, new ColorRgba[0]).InGroup(ParamGroup.Look, 10).AsLive());
            b.Add(new EnumParam(BallSkin, BallSkins, "solid").InGroup(ParamGroup.Look, 20).AsLive());
            b.Add(new EnumParam(Trail, Trails, "short").InGroup(ParamGroup.Look, 30).AsLive());
            b.Add(new EnumParam(Background, Backgrounds, "theme").InGroup(ParamGroup.Look, 40).AsLive());
            b.Add(new FloatParam(Glow, 0.0, 1.0, 0.05, 0.5).InGroup(ParamGroup.Look, 50).AsLive());
            b.Add(new EnumParam(ImpactFx, ImpactFxs, "sparks").InGroup(ParamGroup.Look, 60).AsLive());
            b.Add(new EnumParam(Labels, LabelModes, "off").InGroup(ParamGroup.Look, 70).AsLive());
            b.Add(new StringListParam(LabelNames, MaxLabelNames, MaxLabelNameLength, new string[0])
                .InGroup(ParamGroup.Look, 80).AsLive().WithVisibleIf(VisibleIf.Is(Labels, "names")));
            b.Add(new EnumParam(Hud, HudModes, "auto").InGroup(ParamGroup.Look, 90).AsLive());

            // Sound (live)
            b.Add(new EnumParam(SoundPack, SoundPacks, "Notes").InGroup(ParamGroup.Sound, 10).AsLive());
            b.Add(new EnumParam(Melody, melodies ?? Melodies, "ode_to_joy")
                .InGroup(ParamGroup.Sound, 20).AsLive().WithVisibleIf(VisibleIf.Is(SoundPack, "Melody")));
            b.Add(new IntParam(SfxVolume, 0, 100, 1, 80, "%").InGroup(ParamGroup.Sound, 30).AsLive());
            b.Add(new BoolParam(Haptics, true).InGroup(ParamGroup.Sound, 40).AsLive());

            // Director (structural: decided before the run)
            b.Add(new IntParam(TargetDuration, 10, SimConstants.MaxDurationSeconds, 1, SimConstants.MaxDurationSeconds, "s")
                .WithUnlimited(isDefault: true).InGroup(ParamGroup.Director, 10));
            b.Add(new IntParam(ForcedWinner, 0, MaxLabelNames, 1, 0).InGroup(ParamGroup.Director, 20));
            b.Add(new EnumParam(FinaleStyle, FinaleStyles, "slowMo").InGroup(ParamGroup.Director, 30));
            b.Add(new BoolParam(Countdown, false).InGroup(ParamGroup.Director, 40));
            b.Add(new SeedParam(Seed).InGroup(ParamGroup.Director, 50));

            return b.Build();
        }

        /// <summary>
        /// The common schema for one mode: <see cref="ForcedWinner"/> only exists in modes with a winner (spec §2.2, §2.5).
        /// Merge the mode layer on top of this.
        /// </summary>
        public static ParamSchema ForMode(ModeInfo mode)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            if (mode.HasWinner) return Schema;
            var b = new ParamSchemaBuilder();
            foreach (var d in Schema.Params)
                if (d.Id != ForcedWinner) b.Add(d);
            return b.Build();
        }
    }
}
