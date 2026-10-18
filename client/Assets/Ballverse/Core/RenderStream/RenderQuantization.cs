using System;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Quantization of render-stream values (spec §7.4 "позиции квантуются в 16-битные координаты арены… углы — 16 бит").
    /// <list type="bullet">
    /// <item>Position: int16 over [-<see cref="PositionRange"/>, +<see cref="PositionRange"/>) metres. With the default
    /// 16 m range the step is 32 m / 65536 ≈ 0.49 mm. Deviation from the spec's "≈ 0.4 mm на арене 16 м": spanning only the
    /// 16 m arena height would give 16 m / 65536 ≈ 0.24 mm, but the arena is 9 × 16 m centred at (0, 0) (spec §2), so the
    /// symmetric ±16 m range covers twice the arena and keeps off-screen bodies representable. 0.49 mm is still far below
    /// one pixel at 1080 × 1920; the spec figure should be updated to match (open question for the owner).</item>
    /// <item>Angle: uint16 over one full turn, step 2π/65536 ≈ 0.0055°; angles wrap.</item>
    /// <item>Radius / scale: uint16 over [0, <see cref="ScaleMax"/>] metres, step 8 m / 65535 ≈ 0.12 mm by default.</item>
    /// </list>
    /// Out-of-range values clamp; NaN quantizes to 0. For every quantized value q, Quantize(Dequantize(q)) == q,
    /// so the stream round-trips bit-exactly in the quantized domain. The parameters are stored in the .bvs header.
    /// </summary>
    public sealed class RenderQuantization : IEquatable<RenderQuantization>
    {
        /// <summary>Default position half-range, metres.</summary>
        public const float DefaultPositionRange = 16f;
        /// <summary>Default maximum radius / scale, metres.</summary>
        public const float DefaultScaleMax = 8f;
        /// <summary>Upper bound accepted for either parameter (sanity limit for header validation).</summary>
        public const float MaxParameter = 1_000_000f;

        const double TwoPi = 2.0 * Math.PI;

        /// <summary>±16 m positions, 8 m max scale.</summary>
        public static readonly RenderQuantization Default = new RenderQuantization(DefaultPositionRange, DefaultScaleMax);

        readonly double _positionToQ;
        readonly double _qToPosition;
        readonly double _scaleToQ;
        readonly double _qToScale;

        /// <summary>Half-range of positions in metres: representable positions are [-range, +range).</summary>
        public float PositionRange { get; }
        /// <summary>Largest representable radius / scale in metres.</summary>
        public float ScaleMax { get; }

        public RenderQuantization(float positionRange, float scaleMax)
        {
            if (!IsValidParameter(positionRange)) throw new ArgumentOutOfRangeException(nameof(positionRange));
            if (!IsValidParameter(scaleMax)) throw new ArgumentOutOfRangeException(nameof(scaleMax));
            PositionRange = positionRange;
            ScaleMax = scaleMax;
            _positionToQ = 32768.0 / positionRange;
            _qToPosition = positionRange / 32768.0;
            _scaleToQ = 65535.0 / scaleMax;
            _qToScale = scaleMax / 65535.0;
        }

        /// <summary>True for a finite parameter in (0, <see cref="MaxParameter"/>].</summary>
        public static bool IsValidParameter(float value)
        {
            return !float.IsNaN(value) && value > 0f && value <= MaxParameter;
        }

        /// <summary>Position quantization step, metres (≈ 0.000488 m for the default range).</summary>
        public double PositionStep => _qToPosition;

        /// <summary>Angle quantization step, radians.</summary>
        public static double AngleStep => TwoPi / 65536.0;

        /// <summary>Scale quantization step, metres.</summary>
        public double ScaleStep => _qToScale;

        public short QuantizePosition(float metres)
        {
            double v = Math.Round(metres * _positionToQ, MidpointRounding.AwayFromZero);
            if (double.IsNaN(v)) return 0;
            if (v <= short.MinValue) return short.MinValue;
            if (v >= short.MaxValue) return short.MaxValue;
            return (short)v;
        }

        public float DequantizePosition(short q)
        {
            return (float)(q * _qToPosition);
        }

        /// <summary>Wraps any finite angle into one turn. Non-finite angles quantize to 0.</summary>
        public static ushort QuantizeAngle(float radians)
        {
            if (float.IsNaN(radians) || float.IsInfinity(radians)) return 0;
            double turns = radians / TwoPi;
            turns -= Math.Floor(turns);
            double v = Math.Round(turns * 65536.0, MidpointRounding.AwayFromZero);
            return (ushort)((long)v & 0xFFFF);
        }

        /// <summary>Returns an angle in [0, 2π).</summary>
        public static float DequantizeAngle(ushort q)
        {
            return (float)(q * (TwoPi / 65536.0));
        }

        public ushort QuantizeScale(float metres)
        {
            double v = Math.Round(metres * _scaleToQ, MidpointRounding.AwayFromZero);
            if (double.IsNaN(v) || v <= 0) return 0;
            if (v >= ushort.MaxValue) return ushort.MaxValue;
            return (ushort)v;
        }

        public float DequantizeScale(ushort q)
        {
            return (float)(q * _qToScale);
        }

        /// <summary>Builds a quantized <see cref="BodyState"/> from world-space values (metres, radians).</summary>
        public BodyState QuantizeBody(int id, BodyKind kind, float x, float y, float angle, float scale, byte color, BodyFlags flags)
        {
            return new BodyState(id, kind, QuantizePosition(x), QuantizePosition(y), QuantizeAngle(angle), QuantizeScale(scale), color, flags);
        }

        public bool Equals(RenderQuantization other)
        {
            return other != null && PositionRange.Equals(other.PositionRange) && ScaleMax.Equals(other.ScaleMax);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as RenderQuantization);
        }

        public override int GetHashCode()
        {
            return (PositionRange.GetHashCode() * 397) ^ ScaleMax.GetHashCode();
        }
    }
}
