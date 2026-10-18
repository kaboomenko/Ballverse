using System;
using System.Text;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Immutable body-local geometry of a render-stream body (spec §7.7 "многоугольники и цепочки … перестраиваемые из
    /// потока рендера", §2.9 mode cards). Geometry is static: it is registered once with
    /// <see cref="RenderStreamWriter.DefineShape"/>, stored once in the shape table and referenced by
    /// <see cref="BodyState.ShapeId"/>; only the body transform (position, angle, uniform scale) changes per frame.
    /// <para>
    /// All values are in the quantized domain of the stream's <see cref="RenderQuantization"/>: points use the position
    /// quantizer (int16), lengths (thickness, ring radius, capsule length) the scale quantizer (uint16 metres at scale 1),
    /// ring gap angles the angle quantizer.
    /// </para>
    /// <para>
    /// Body scale (see <see cref="BodyState.Scale"/>): for <see cref="ShapeKind.Circle"/> the dequantized scale is the radius
    /// in metres; for every other kind it is a dimensionless factor (1.0 = the geometry as defined, see
    /// <see cref="RenderQuantization.UnitScale"/>) that multiplies all points and lengths, including the thickness unless
    /// <see cref="FixedThickness"/> is set (a non-scaling stroke, e.g. M01 rings that shrink at a constant thickness).
    /// </para>
    /// <para>
    /// Validation covers counts, flags and ring gap ordering. Polygon simplicity (no self-intersection) and winding are
    /// not checked; the renderer triangulates either winding.
    /// </para>
    /// </summary>
    public sealed class ShapeDefinition : IEquatable<ShapeDefinition>
    {
        /// <summary>The unit circle (radius = body scale).</summary>
        public static readonly ShapeDefinition Circle = new ShapeDefinition(ShapeKind.Circle, Array.Empty<ShapePoint>(), Array.Empty<RingGap>(), false, false, 0, 0, 0);

        readonly ShapePoint[] _points;
        readonly RingGap[] _gaps;
        readonly int _hash;

        ShapeDefinition(ShapeKind kind, ShapePoint[] points, RingGap[] gaps, bool closed, bool fixedThickness, ushort thickness, ushort radius, ushort length)
        {
            Kind = kind;
            _points = points;
            _gaps = gaps;
            Closed = closed;
            FixedThickness = fixedThickness;
            Thickness = thickness;
            Radius = radius;
            Length = length;
            _hash = ComputeHash();
        }

        public ShapeKind Kind { get; }

        /// <summary>Polygon vertices or polyline points (body-local, quantized); empty for other kinds.</summary>
        public ReadOnlySpan<ShapePoint> Points => _points;

        public int PointCount => _points.Length;

        /// <summary>Ring gaps in ascending <see cref="RingGap.Start"/> order, non-overlapping; empty for other kinds and for a full ring.</summary>
        public ReadOnlySpan<RingGap> Gaps => _gaps;

        /// <summary>Polyline only: the last point connects back to the first.</summary>
        public bool Closed { get; }

        /// <summary>Polyline, ring, capsule: the thickness is not multiplied by the body scale.</summary>
        public bool FixedThickness { get; }

        /// <summary>Stroke width (polyline, ring) or diameter (capsule), quantized with the scale quantizer.</summary>
        public ushort Thickness { get; }

        /// <summary>Ring only: radius of the ring's centre line, quantized with the scale quantizer.</summary>
        public ushort Radius { get; }

        /// <summary>Capsule only: distance between the two cap centres, quantized with the scale quantizer.</summary>
        public ushort Length { get; }

        /// <summary>A filled polygon of 3–64 body-local vertices (convex or concave). The span is copied.</summary>
        public static ShapeDefinition Polygon(ReadOnlySpan<ShapePoint> vertices)
        {
            string error = ValidatePointCount(ShapeKind.Polygon, false, vertices.Length);
            if (error != null) throw new ArgumentException(error, nameof(vertices));
            return new ShapeDefinition(ShapeKind.Polygon, vertices.ToArray(), Array.Empty<RingGap>(), false, false, 0, 0, 0);
        }

        /// <summary>
        /// A polyline (chain) of 2–4096 body-local points (≥ 3 when <paramref name="closed"/>) stroked with
        /// <paramref name="thickness"/>. The span is copied.
        /// </summary>
        public static ShapeDefinition Polyline(ReadOnlySpan<ShapePoint> points, bool closed, ushort thickness, bool fixedThickness = false)
        {
            string error = ValidatePointCount(ShapeKind.Polyline, closed, points.Length);
            if (error != null) throw new ArgumentException(error, nameof(points));
            return new ShapeDefinition(ShapeKind.Polyline, points.ToArray(), Array.Empty<RingGap>(), closed, fixedThickness, thickness, 0, 0);
        }

        /// <summary>
        /// A ring centred at the body origin with up to 64 gaps. Gaps must be listed in strictly ascending
        /// <see cref="RingGap.Start"/> order, each with a non-zero span, and must not overlap or touch (also across angle 0),
        /// so at least some of the ring remains. No gaps = a full ring. The span is copied.
        /// </summary>
        public static ShapeDefinition Ring(ushort radius, ushort thickness, ReadOnlySpan<RingGap> gaps, bool fixedThickness = false)
        {
            string error = ValidateGaps(gaps);
            if (error != null) throw new ArgumentException(error, nameof(gaps));
            return new ShapeDefinition(ShapeKind.Ring, Array.Empty<ShapePoint>(), gaps.ToArray(), false, fixedThickness, thickness, radius, 0);
        }

        /// <summary>A capsule (segment with round caps) from (−length/2, 0) to (+length/2, 0) with diameter <paramref name="thickness"/>.</summary>
        public static ShapeDefinition Capsule(ushort length, ushort thickness, bool fixedThickness = false)
        {
            return new ShapeDefinition(ShapeKind.Capsule, Array.Empty<ShapePoint>(), Array.Empty<RingGap>(), false, fixedThickness, thickness, 0, length);
        }

        /// <summary>Returns null if <paramref name="count"/> points are valid for the kind, otherwise the reason.</summary>
        internal static string ValidatePointCount(ShapeKind kind, bool closed, int count)
        {
            if (kind == ShapeKind.Polygon)
            {
                if (count < RenderStreamLimits.MinPolygonVertices || count > RenderStreamLimits.MaxPolygonVertices)
                {
                    return "Polygon needs " + RenderStreamLimits.MinPolygonVertices + ".." + RenderStreamLimits.MaxPolygonVertices + " vertices, got " + count;
                }
                return null;
            }
            int min = closed ? RenderStreamLimits.MinPolygonVertices : RenderStreamLimits.MinPolylinePoints;
            if (count < min || count > RenderStreamLimits.MaxPolylinePoints)
            {
                return (closed ? "Closed" : "Open") + " polyline needs " + min + ".." + RenderStreamLimits.MaxPolylinePoints + " points, got " + count;
            }
            return null;
        }

        /// <summary>Returns null if the ring gaps are valid (see <see cref="Ring"/>), otherwise the reason.</summary>
        internal static string ValidateGaps(ReadOnlySpan<RingGap> gaps)
        {
            if (gaps.Length > RenderStreamLimits.MaxRingGaps) return "Ring has more than " + RenderStreamLimits.MaxRingGaps + " gaps";
            for (int i = 0; i < gaps.Length; i++)
            {
                if (gaps[i].Span == 0) return "Ring gap " + i + " has zero span (start == end)";
                if (i > 0 && gaps[i].Start <= gaps[i - 1].Start) return "Ring gaps must be in strictly ascending start order";
                if (i > 0 && gaps[i - 1].Start + gaps[i - 1].Span >= gaps[i].Start) return "Ring gaps " + (i - 1) + " and " + i + " overlap or touch";
            }
            if (gaps.Length > 1)
            {
                RingGap last = gaps[gaps.Length - 1];
                if (last.Start + last.Span >= gaps[0].Start + 65536) return "Last ring gap overlaps or touches the first";
            }
            return null;
        }

        /// <summary>Builds a shape from already validated decoded fields (the arrays are taken over, not copied).</summary>
        internal static ShapeDefinition CreateValidated(ShapeKind kind, ShapePoint[] points, RingGap[] gaps, bool closed, bool fixedThickness, ushort thickness, ushort radius, ushort length)
        {
            if (kind == ShapeKind.Circle) return Circle;
            return new ShapeDefinition(kind, points, gaps, closed, fixedThickness, thickness, radius, length);
        }

        public bool Equals(ShapeDefinition other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || _hash != other._hash) return false;
            return Kind == other.Kind && Closed == other.Closed && FixedThickness == other.FixedThickness && Thickness == other.Thickness
                && Radius == other.Radius && Length == other.Length
                && Points.SequenceEqual(other.Points) && Gaps.SequenceEqual(other.Gaps);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ShapeDefinition);
        }

        public override int GetHashCode()
        {
            return _hash;
        }

        int ComputeHash()
        {
            unchecked
            {
                int h = (int)Kind;
                h = h * 31 + (Closed ? 1 : 0);
                h = h * 31 + (FixedThickness ? 1 : 0);
                h = h * 31 + Thickness;
                h = h * 31 + Radius;
                h = h * 31 + Length;
                for (int i = 0; i < _points.Length; i++) h = h * 31 + _points[i].GetHashCode();
                for (int i = 0; i < _gaps.Length; i++) h = h * 31 + _gaps[i].GetHashCode();
                return h;
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Kind);
            switch (Kind)
            {
                case ShapeKind.Polygon:
                    sb.Append(" n=").Append(_points.Length);
                    break;
                case ShapeKind.Polyline:
                    sb.Append(" n=").Append(_points.Length).Append(Closed ? " closed" : " open").Append(" t=").Append(Thickness);
                    break;
                case ShapeKind.Ring:
                    sb.Append(" r=").Append(Radius).Append(" t=").Append(Thickness).Append(" gaps=").Append(_gaps.Length);
                    break;
                case ShapeKind.Capsule:
                    sb.Append(" l=").Append(Length).Append(" t=").Append(Thickness);
                    break;
            }
            if (FixedThickness) sb.Append(" fixed");
            return sb.ToString();
        }
    }
}
