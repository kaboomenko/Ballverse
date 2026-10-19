using System;
using System.Collections.Generic;
using Ballverse.Core.Params;
using Ballverse.Core.Sim;

namespace Ballverse.Core.Modes.RingEscape
{
    public enum RingProgression
    {
        /// <summary>«Сжатие»: passed rings vanish, outer rings shift inwards.</summary>
        Compress = 0,
        /// <summary>«Отдаление камеры»: fixed radii, AutoZoom camera.</summary>
        CameraZoom = 1,
        /// <summary>«Статично»: every ring inside the 4 m safe zone.</summary>
        Static = 2,
    }

    public enum RingDirection
    {
        Clockwise = 0,
        CounterClockwise = 1,
        Alternate = 2,
        Random = 3,
    }

    public enum RingSpeedLaw
    {
        Constant = 0,
        /// <summary>±40 % with a 4 s period.</summary>
        Pulse = 1,
        /// <summary>Direction flips every T s.</summary>
        Reverse = 2,
    }

    public enum GapPlacementKind
    {
        Random = 0,
        Aligned = 1,
        Spiral = 2,
        Fixed = 3,
    }

    public enum AfterEscapeKind
    {
        Finale = 0,
        NewRound = 1,
        NewBall = 2,
    }

    /// <summary>
    /// Typed view of an M01 parameter set in SI units (metres, seconds; angles in degrees unless named *Rad). Built from a
    /// complete, sanitized set; values are the float values stored in the set, so every consumer computes the same numbers.
    /// </summary>
    public sealed class RingEscapeConfig
    {
        public int BallCount;
        /// <summary>Ball diameter, m.</summary>
        public float BallSize;
        public float GravityG;
        public float GravityAngleDeg;
        public float Restitution;
        public float Friction;
        public float MaxSpeed;
        public int RingCount;
        public float InnerRadius;
        public float RingSpacing;
        public float RingThickness;
        public RingProgression Progression;
        public float RotationSpeedDeg;
        /// <summary>Fraction per ring (−0.2…0.2).</summary>
        public float SpeedStep;
        public RingDirection Direction;
        public RingSpeedLaw SpeedLaw;
        public float ReversePeriod;
        public float GapSizeDeg;
        public int GapCount;
        public GapPlacementKind Placement;
        public float GapSpiralStepDeg;
        public float GapAngleDeg;
        /// <summary>Fraction 0–0.5.</summary>
        public float GapShrink;
        /// <summary>As set; in «Сжатие» passed rings are always destroyed (see <see cref="EffectiveDestroyPassed"/>).</summary>
        public bool DestroyPassed;
        public string BreakFx;
        public string RingPalette;
        public float LaunchSpeed;
        public bool LaunchAngleRandom;
        public float LaunchAngleDeg;
        /// <summary>Fraction 0–0.1.</summary>
        public float GrowOnHit;
        public bool Competition;
        public AfterEscapeKind AfterEscape;
        /// <summary>0 = any, else 1-based participant.</summary>
        public int ForcedWinner;
        /// <summary>Target duration in ticks, or null for "unlimited".</summary>
        public int? TargetDurationTicks;
        public string FinaleStyle;
        public bool Countdown;
        public int BallColorCount;
        public string Labels;
        public IReadOnlyList<string> LabelNames;
        public string Trail;

        public bool EffectiveDestroyPassed => Progression == RingProgression.Compress || DestroyPassed;

        public static RingEscapeConfig From(ParamSet v)
        {
            if (v == null) throw new ArgumentNullException(nameof(v));
            var c = new RingEscapeConfig
            {
                BallCount = v.GetInt(CommonParams.BallCount),
                BallSize = v.GetFloat(CommonParams.BallSize),
                GravityG = v.GetFloat(CommonParams.Gravity),
                GravityAngleDeg = v.GetInt(CommonParams.GravityAngle),
                Restitution = v.GetFloat(CommonParams.Restitution),
                Friction = v.GetFloat(CommonParams.Friction),
                MaxSpeed = v.GetFloat(CommonParams.MaxSpeed),
                RingCount = v.GetInt(RingEscapeParams.RingCount),
                InnerRadius = v.GetFloat(RingEscapeParams.InnerRadius),
                RingSpacing = v.GetFloat(RingEscapeParams.RingSpacing),
                RingThickness = v.GetFloat(RingEscapeParams.RingThickness),
                Progression = ParseProgression(v.GetString(RingEscapeParams.Progression)),
                RotationSpeedDeg = v.GetFloat(RingEscapeParams.RotationSpeed),
                SpeedStep = v.GetInt(RingEscapeParams.SpeedStep) / 100f,
                Direction = ParseDirection(v.GetString(RingEscapeParams.RotationDirection)),
                SpeedLaw = ParseLaw(v.GetString(RingEscapeParams.SpeedLaw)),
                ReversePeriod = v.GetFloat(RingEscapeParams.ReversePeriod),
                GapSizeDeg = v.GetFloat(RingEscapeParams.GapSize),
                GapCount = v.GetInt(RingEscapeParams.GapCount),
                Placement = ParsePlacement(v.GetString(RingEscapeParams.GapPlacement)),
                GapSpiralStepDeg = v.GetInt(RingEscapeParams.GapSpiralStep),
                GapAngleDeg = v.GetInt(RingEscapeParams.GapAngle),
                GapShrink = v.GetInt(RingEscapeParams.GapShrink) / 100f,
                DestroyPassed = v.GetBool(RingEscapeParams.DestroyPassed),
                BreakFx = v.GetString(RingEscapeParams.BreakFx),
                RingPalette = v.GetString(RingEscapeParams.RingPalette),
                LaunchSpeed = v.GetFloat(RingEscapeParams.LaunchSpeed),
                LaunchAngleRandom = v.GetBool(RingEscapeParams.LaunchAngleRandom),
                LaunchAngleDeg = v.GetInt(RingEscapeParams.LaunchAngle),
                GrowOnHit = v.GetInt(RingEscapeParams.GrowOnHit) / 100f,
                Competition = v.GetBool(RingEscapeParams.Competition),
                AfterEscape = ParseAfter(v.GetString(RingEscapeParams.AfterEscape)),
                ForcedWinner = v.GetInt(CommonParams.ForcedWinner),
                TargetDurationTicks = ModeGenerator.TargetDurationTicks(v),
                FinaleStyle = v.GetString(CommonParams.FinaleStyle),
                Countdown = v.GetBool(CommonParams.Countdown),
                BallColorCount = v.GetColors(CommonParams.BallColors).Count,
                Labels = v.GetString(CommonParams.Labels),
                LabelNames = v.GetStrings(CommonParams.LabelNames),
                Trail = v.GetString(CommonParams.Trail),
            };
            return c;
        }

        /// <summary>
        /// Teams (spec §2.5: the participant is a colour): <c>ballColors</c> when it has ≥ 2 colours, otherwise up to
        /// <see cref="DefaultTeamColors"/> theme colours; never more than the balls. Fixed when the scene is built.
        /// </summary>
        public int TeamCount => TeamCountFor(BallCount, BallColorCount);

        /// <summary>Theme colours used as teams when <c>ballColors</c> has fewer than 2 entries.</summary>
        public const int DefaultTeamColors = 8;

        public static int TeamCountFor(int ballCount, int ballColorCount)
        {
            int colours = ballColorCount >= 2 ? ballColorCount : DefaultTeamColors;
            return Math.Max(1, Math.Min(ballCount, colours));
        }

        /// <summary>«Соревнование» is active: on, with at least 2 teams.</summary>
        public bool CompetitionActive => Competition && TeamCount >= 2;

        public float RotationSpeedRad => RotationSpeedDeg * SimMath.DegToRad;

        static RingProgression ParseProgression(string s)
        {
            switch (s)
            {
                case RingEscapeParams.ProgressionCameraZoom: return RingProgression.CameraZoom;
                case RingEscapeParams.ProgressionStatic: return RingProgression.Static;
                default: return RingProgression.Compress;
            }
        }

        static RingDirection ParseDirection(string s)
        {
            switch (s)
            {
                case RingEscapeParams.DirectionClockwise: return RingDirection.Clockwise;
                case RingEscapeParams.DirectionCounterClockwise: return RingDirection.CounterClockwise;
                case RingEscapeParams.DirectionRandom: return RingDirection.Random;
                default: return RingDirection.Alternate;
            }
        }

        static RingSpeedLaw ParseLaw(string s)
        {
            switch (s)
            {
                case RingEscapeParams.LawPulse: return RingSpeedLaw.Pulse;
                case RingEscapeParams.LawReverse: return RingSpeedLaw.Reverse;
                default: return RingSpeedLaw.Constant;
            }
        }

        static GapPlacementKind ParsePlacement(string s)
        {
            switch (s)
            {
                case RingEscapeParams.PlacementAligned: return GapPlacementKind.Aligned;
                case RingEscapeParams.PlacementSpiral: return GapPlacementKind.Spiral;
                case RingEscapeParams.PlacementFixed: return GapPlacementKind.Fixed;
                default: return GapPlacementKind.Random;
            }
        }

        static AfterEscapeKind ParseAfter(string s)
        {
            switch (s)
            {
                case RingEscapeParams.AfterNewRound: return AfterEscapeKind.NewRound;
                case RingEscapeParams.AfterNewBall: return AfterEscapeKind.NewBall;
                default: return AfterEscapeKind.Finale;
            }
        }
    }
}
