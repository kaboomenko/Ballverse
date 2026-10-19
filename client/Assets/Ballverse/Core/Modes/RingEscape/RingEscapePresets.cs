using System;
using System.Collections.Generic;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;

namespace Ballverse.Core.Modes.RingEscape
{
    /// <summary>
    /// Built-in presets of M01 (spec §2.3, §2.9 M01 «Пресеты»). Values are partial (defaults fill the rest); every preset is
    /// valid as authored (no constraint issue) and escapes before 180 s on ≥ 99 % of seeds (tested, card test (в)).
    /// </summary>
    public static class RingEscapePresets
    {
        public const string Classic = "m01.classic";
        public const string NeonChaos = "m01.neon_chaos";
        public const string Meditation = "m01.meditation";
        public const string HundredRings = "m01.hundred_rings";
        public const string ImpossibleEscape = "m01.impossible_escape";
        public const string Rainbow = "m01.rainbow";
        public const string GapSpiral = "m01.gap_spiral";
        public const string ColorRace = "m01.color_race";

        static readonly Lazy<IReadOnlyList<Preset>> s_all = new Lazy<IReadOnlyList<Preset>>(Build);

        public static IReadOnlyList<Preset> All => s_all.Value;

        static IReadOnlyList<Preset> Build()
        {
            var list = new List<Preset>
            {
                P(Classic, Set()),
                P(NeonChaos, Set(
                    (CommonParams.BallCount, ParamValue.Int(8)),
                    (CommonParams.Theme, ParamValue.String("Neon")),
                    (CommonParams.Trail, ParamValue.String("long")),
                    (CommonParams.Glow, ParamValue.Float(0.9)),
                    (RingEscapeParams.RotationSpeed, ParamValue.Float(150)),
                    (RingEscapeParams.SpeedLaw, ParamValue.String(RingEscapeParams.LawReverse)),
                    (RingEscapeParams.ReversePeriod, ParamValue.Float(4)),
                    (RingEscapeParams.GapSize, ParamValue.Float(50)),
                    (RingEscapeParams.LaunchSpeed, ParamValue.Float(10)),
                    (RingEscapeParams.RingCount, ParamValue.Int(60)),
                    (RingEscapeParams.BreakFx, ParamValue.String(RingEscapeParams.BreakBurst)))),
                P(Meditation, Set(
                    (CommonParams.Theme, ParamValue.String("Pastel")),
                    (CommonParams.SoundPack, ParamValue.String("Asmr")),
                    (CommonParams.Trail, ParamValue.String("long")),
                    (RingEscapeParams.RingCount, ParamValue.Int(10)),
                    (RingEscapeParams.RotationSpeed, ParamValue.Float(30)),
                    (RingEscapeParams.SpeedStep, ParamValue.Int(0)),
                    (RingEscapeParams.GapSize, ParamValue.Float(45)),
                    (RingEscapeParams.LaunchSpeed, ParamValue.Float(4)),
                    (RingEscapeParams.BreakFx, ParamValue.String(RingEscapeParams.BreakDissolve)))),
                P(HundredRings, Set(
                    (RingEscapeParams.RingCount, ParamValue.Int(100)),
                    (RingEscapeParams.GapCount, ParamValue.Int(2)),
                    (RingEscapeParams.GapSize, ParamValue.Float(100)),
                    (RingEscapeParams.RotationSpeed, ParamValue.Float(120)),
                    (RingEscapeParams.SpeedStep, ParamValue.Int(0)),
                    (RingEscapeParams.LaunchSpeed, ParamValue.Float(10)),
                    (RingEscapeParams.RingSpacing, ParamValue.Float(0.6)))),
                P(ImpossibleEscape, Set(
                    (RingEscapeParams.RingCount, ParamValue.Int(4)),
                    (RingEscapeParams.InnerRadius, ParamValue.Float(1.6)),
                    (RingEscapeParams.RingThickness, ParamValue.Float(0.06)),
                    (CommonParams.BallSize, ParamValue.Float(0.2)),
                    (RingEscapeParams.GapSize, ParamValue.Float(30)),
                    (RingEscapeParams.GapShrink, ParamValue.Int(50)),
                    (RingEscapeParams.RotationSpeed, ParamValue.Float(90)),
                    (RingEscapeParams.RotationDirection, ParamValue.String(RingEscapeParams.DirectionAlternate)),
                    (RingEscapeParams.LaunchSpeed, ParamValue.Float(8)))),
                P(Rainbow, Set(
                    (CommonParams.Trail, ParamValue.String("rainbow")),
                    (CommonParams.Theme, ParamValue.String("Candy")),
                    (RingEscapeParams.RingPalette, ParamValue.String(RingEscapeParams.PaletteRainbow)),
                    (RingEscapeParams.RingCount, ParamValue.Int(16)),
                    (RingEscapeParams.GapSize, ParamValue.Float(40)))),
                P(GapSpiral, Set(
                    (RingEscapeParams.GapPlacement, ParamValue.String(RingEscapeParams.PlacementSpiral)),
                    (RingEscapeParams.GapSpiralStep, ParamValue.Int(25)),
                    (RingEscapeParams.RotationDirection, ParamValue.String(RingEscapeParams.DirectionCounterClockwise)),
                    (RingEscapeParams.RingCount, ParamValue.Int(14)),
                    (RingEscapeParams.GapSize, ParamValue.Float(40)))),
                P(ColorRace, Set(
                    (CommonParams.BallCount, ParamValue.Int(6)),
                    (RingEscapeParams.Competition, ParamValue.Bool(true)),
                    (CommonParams.HudLeaders, ParamValue.Bool(true)),
                    (RingEscapeParams.RingCount, ParamValue.Int(50)),
                    (RingEscapeParams.GapSize, ParamValue.Float(30)),
                    (RingEscapeParams.LaunchSpeed, ParamValue.Float(8)))),
            };
            return list.AsReadOnly();
        }

        static Preset P(string id, ParamSet values)
        {
            return Preset.Create(id, "ring_escape", values, PresetSource.BuiltIn, nameKey: "presets." + id);
        }

        static ParamSet Set(params (string id, ParamValue value)[] values)
        {
            var pairs = new List<KeyValuePair<string, ParamValue>>(values.Length);
            foreach (var (id, value) in values) pairs.Add(new KeyValuePair<string, ParamValue>(id, value));
            return new ParamSet(pairs);
        }
    }
}
