using System;
using System.Collections.Generic;
using Ballverse.Core.Params;

namespace Ballverse.Core.Modes.RingEscape
{
    /// <summary>
    /// Parameter ids, option ids and the schema of M01 «Побег из колец» (spec §2.9 M01 "Параметры режима" and "Переопределения
    /// общих параметров"). Option ids are stable (persisted in presets, projects and replays).
    /// </summary>
    public static class RingEscapeParams
    {
        public const string RingCount = "ringCount";
        public const string InnerRadius = "innerRadius";
        public const string RingSpacing = "ringSpacing";
        public const string RingThickness = "ringThickness";
        public const string Progression = "progression";
        public const string RotationSpeed = "rotationSpeed";
        public const string SpeedStep = "speedStep";
        public const string RotationDirection = "rotationDirection";
        public const string SpeedLaw = "speedLaw";
        public const string ReversePeriod = "reversePeriod";
        public const string GapSize = "gapSize";
        public const string GapCount = "gapCount";
        public const string GapPlacement = "gapPlacement";
        public const string GapSpiralStep = "gapSpiralStep";
        public const string GapAngle = "gapAngle";
        public const string GapShrink = "gapShrink";
        public const string DestroyPassed = "destroyPassed";
        public const string BreakFx = "breakFx";
        public const string RingPalette = "ringPalette";
        public const string LaunchSpeed = "launchSpeed";
        public const string LaunchAngleRandom = "launchAngleRandom";
        public const string LaunchAngle = "launchAngle";
        public const string GrowOnHit = "growOnHit";
        public const string Competition = "competition";
        public const string AfterEscape = "afterEscape";

        // progression
        public const string ProgressionCompress = "compress";
        public const string ProgressionCameraZoom = "cameraZoom";
        public const string ProgressionStatic = "static";
        // rotationDirection
        public const string DirectionClockwise = "clockwise";
        public const string DirectionCounterClockwise = "counterClockwise";
        public const string DirectionAlternate = "alternate";
        public const string DirectionRandom = "random";
        // speedLaw
        public const string LawConstant = "constant";
        public const string LawPulse = "pulse";
        public const string LawReverse = "reverse";
        // gapPlacement
        public const string PlacementRandom = "random";
        public const string PlacementAligned = "aligned";
        public const string PlacementSpiral = "spiral";
        public const string PlacementFixed = "fixed";
        // breakFx (RenderEventContract.md §3)
        public const string BreakShards = "shards";
        public const string BreakFlash = "flash";
        public const string BreakDissolve = "dissolve";
        public const string BreakBurst = "burst";
        // ringPalette
        public const string PaletteArena = "arena";
        public const string PaletteAlternate = "alternate";
        public const string PaletteRainbow = "rainbow";
        // afterEscape
        public const string AfterFinale = "finale";
        public const string AfterNewRound = "newRound";
        public const string AfterNewBall = "newBall";

        /// <summary>Common overrides (spec §2.9 M01): ballCount 1–20 default 1 capped 20 / 12 / 8; ballSize default 0.35 m;
        /// gravity 1 g; restitution 1.0; friction 0; maxSpeed 20 m/s.</summary>
        public const int MaxBalls = 20;
        public const double DefaultBallSize = 0.35;
        public const double DefaultRestitution = 1.0;
        public const double DefaultFriction = 0.0;
        public const double DefaultMaxSpeed = 20.0;

        static readonly Lazy<ParamSchema> s_layer = new Lazy<ParamSchema>(BuildLayer);
        static readonly Lazy<ParamSchema> s_schema = new Lazy<ParamSchema>(() => ParamSchema.Merge(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M01)), s_layer.Value));

        /// <summary>The mode layer (overrides + mode parameters) for <see cref="ParamSchema.Merge"/>.</summary>
        public static ParamSchema Layer => s_layer.Value;

        /// <summary>The complete M01 schema with the built-in theme / melody lists.</summary>
        public static ParamSchema Schema => s_schema.Value;

        /// <summary>The complete M01 schema over a given common schema (themes / melodies delivered as data, spec §2.7, §2.10).</summary>
        public static ParamSchema SchemaFor(ParamSchema common)
        {
            return ParamSchema.Merge(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M01), common), s_layer.Value);
        }

        static ParamSchema BuildLayer()
        {
            var b = new ParamSchemaBuilder();
            // Common overrides.
            var ballCountValues = new List<ParamValue>();
            for (int i = 2; i <= MaxBalls; i++) ballCountValues.Add(ParamValue.Int(i));
            b.Add(new IntParam(CommonParams.BallCount, 1, MaxBalls, 1, 1).WithTierCap(BodyBudgets.BallCap(ModeCode.M01)));
            b.Add(new FloatParam(CommonParams.BallSize, 0.1, 1.5, 0.01, DefaultBallSize, "m"));
            b.Add(new FloatParam(CommonParams.Restitution, 0.0, 1.2, 0.01, DefaultRestitution));
            b.Add(new FloatParam(CommonParams.Friction, 0.0, 1.0, 0.01, DefaultFriction));
            b.Add(new FloatParam(CommonParams.MaxSpeed, 5.0, 60.0, 1.0, DefaultMaxSpeed, "m/s"));
            // Director: the winner exists only in «Соревнование» (spec §2.5), so M01 owns forcedWinner.
            b.Add(new IntParam(CommonParams.ForcedWinner, 0, 32, 1, 0).InGroup(ParamGroup.Director, 20)
                .WithVisibleIf(VisibleIf.IsTrue(Competition)));

            // Mode parameters in card order.
            int o = 0;
            b.Add(new IntParam(RingCount, 1, 100, 1, 12).InGroup(ParamGroup.Mode, o += 10));
            b.Add(new FloatParam(InnerRadius, 0.6, 2.5, 0.05, 1.2, "m").InGroup(ParamGroup.Mode, o += 10));
            b.Add(new FloatParam(RingSpacing, 0.3, 1.2, 0.05, 0.6, "m").InGroup(ParamGroup.Mode, o += 10));
            b.Add(new FloatParam(RingThickness, 0.04, 0.3, 0.01, 0.12, "m").InGroup(ParamGroup.Mode, o += 10));
            b.Add(new EnumParam(Progression, new[] { ProgressionCompress, ProgressionCameraZoom, ProgressionStatic }, ProgressionCompress)
                .InGroup(ParamGroup.Mode, o += 10));
            b.Add(new FloatParam(RotationSpeed, 0.0, 360.0, 1.0, 60.0, "°/s").InGroup(ParamGroup.Mode, o += 10).AsLive());
            b.Add(new IntParam(SpeedStep, -20, 20, 1, 5, "%").InGroup(ParamGroup.Mode, o += 10));
            b.Add(new EnumParam(RotationDirection, new[] { DirectionClockwise, DirectionCounterClockwise, DirectionAlternate, DirectionRandom },
                DirectionAlternate).InGroup(ParamGroup.Mode, o += 10));
            b.Add(new EnumParam(SpeedLaw, new[] { LawConstant, LawPulse, LawReverse }, LawConstant).InGroup(ParamGroup.Mode, o += 10));
            b.Add(new FloatParam(ReversePeriod, 3.0, 15.0, 0.5, 6.0, "s").InGroup(ParamGroup.Mode, o += 10)
                .WithVisibleIf(VisibleIf.Is(SpeedLaw, LawReverse)));
            b.Add(new FloatParam(GapSize, 10.0, 120.0, 1.0, 35.0, "°").InGroup(ParamGroup.Mode, o += 10));
            b.Add(new IntParam(GapCount, 1, 4, 1, 1).InGroup(ParamGroup.Mode, o += 10));
            b.Add(new EnumParam(GapPlacement, new[] { PlacementRandom, PlacementAligned, PlacementSpiral, PlacementFixed }, PlacementRandom)
                .InGroup(ParamGroup.Mode, o += 10));
            b.Add(new IntParam(GapSpiralStep, 0, 180, 1, 25, "°").InGroup(ParamGroup.Mode, o += 10)
                .WithVisibleIf(VisibleIf.Is(GapPlacement, PlacementSpiral)));
            b.Add(new IntParam(GapAngle, 0, 359, 1, 90, "°").InGroup(ParamGroup.Mode, o += 10)
                .WithVisibleIf(VisibleIf.Is(GapPlacement, PlacementFixed)));
            b.Add(new IntParam(GapShrink, 0, 50, 1, 0, "%").InGroup(ParamGroup.Mode, o += 10));
            b.Add(new BoolParam(DestroyPassed, true).InGroup(ParamGroup.Mode, o += 10)
                .WithVisibleIf(VisibleIf.In(Progression, ProgressionCameraZoom, ProgressionStatic)));
            b.Add(new EnumParam(BreakFx, new[] { BreakShards, BreakFlash, BreakDissolve, BreakBurst }, BreakShards)
                .InGroup(ParamGroup.Mode, o += 10).AsLive());
            b.Add(new EnumParam(RingPalette, new[] { PaletteAlternate, PaletteArena, PaletteRainbow }, PaletteAlternate)
                .InGroup(ParamGroup.Mode, o += 10).AsLive());
            b.Add(new FloatParam(LaunchSpeed, 0.0, 15.0, 0.5, 6.0, "m/s").InGroup(ParamGroup.Mode, o += 10));
            b.Add(new BoolParam(LaunchAngleRandom, true).InGroup(ParamGroup.Mode, o += 10));
            b.Add(new IntParam(LaunchAngle, 0, 359, 1, 90, "°").InGroup(ParamGroup.Mode, o += 10)
                .WithVisibleIf(VisibleIf.Is(LaunchAngleRandom, ParamValue.Bool(false))));
            b.Add(new IntParam(GrowOnHit, 0, 10, 1, 0, "%").InGroup(ParamGroup.Mode, o += 10));
            b.Add(new BoolParam(Competition, false).InGroup(ParamGroup.Mode, o += 10)
                .WithVisibleIf(VisibleIf.In(CommonParams.BallCount, ballCountValues)));
            b.Add(new EnumParam(AfterEscape, new[] { AfterFinale, AfterNewRound, AfterNewBall }, AfterFinale).InGroup(ParamGroup.Mode, o += 10));
            return b.BuildLayer();
        }
    }
}
