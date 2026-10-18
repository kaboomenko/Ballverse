using System;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>
    /// Tessellates every <see cref="ShapeKind"/> of the render stream's shape table into a triangle list in body-local
    /// metres (spec §7.7), honouring the body scale rules of <see cref="ShapeDefinition"/>:
    /// <list type="bullet">
    /// <item><see cref="ShapeKind.Circle"/>: a disc of radius = <paramref name="scale"/> (the dequantized body scale is the
    /// radius). The renderer normally draws circles with the SDF shader; the tessellation exists for completeness.</item>
    /// <item>Other kinds: every point and length is multiplied by <paramref name="scale"/>; the thickness too unless
    /// <see cref="ShapeDefinition.FixedThickness"/> (a non-scaling stroke).</item>
    /// <item>A fixed-thickness ring whose radius shrank below half its thickness is drawn as a filled disc of radius
    /// radius + thickness / 2 (the documented draw-time rule).</item>
    /// <item>Ring gaps run counter-clockwise from <see cref="RingGap.Start"/> to <see cref="RingGap.End"/>; the solid
    /// arcs end with <see cref="TessellationSettings.RingGapCap"/>.</item>
    /// <item>Polylines: zero-length segments are skipped; joins and open-end caps follow the settings.</item>
    /// <item>Polygons: exact ear clipping (<see cref="PolygonTriangulator"/>).</item>
    /// </list>
    /// Output is deterministic for equal inputs and allocation-free once <paramref name="output"/> has grown.
    /// Overlapping triangles are allowed (stroke joins overlap their segments); draw opaque shapes or accept the overlap.
    /// </summary>
    public static class ShapeTessellator
    {
        const double TwoPi = 2.0 * Math.PI;
        const double AngleUnit = TwoPi / 65536.0;

        /// <summary>Clears <paramref name="output"/> and fills it with the tessellation of <paramref name="shape"/> at <paramref name="scale"/>.</summary>
        public static TessellationStatus Tessellate(ShapeDefinition shape, RenderQuantization quantization, float scale, in TessellationSettings settings, LocalGeometry output)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            if (quantization == null) throw new ArgumentNullException(nameof(quantization));
            if (output == null) throw new ArgumentNullException(nameof(output));
            settings.Validate();
            output.Clear();
            if (!(scale > 0f) || float.IsInfinity(scale)) return TessellationStatus.Empty;

            switch (shape.Kind)
            {
                case ShapeKind.Circle:
                    AddDisc(output, 0f, 0f, scale, settings);
                    return output.TriangleCount > 0 ? TessellationStatus.Ok : TessellationStatus.Empty;
                case ShapeKind.Ring:
                    return TessellateRing(shape, quantization, scale, settings, output);
                case ShapeKind.Capsule:
                    return TessellateCapsule(shape, quantization, scale, settings, output);
                case ShapeKind.Polyline:
                    return TessellatePolyline(shape, quantization, scale, settings, output);
                case ShapeKind.Polygon:
                    return TessellatePolygon(shape, quantization, scale, output);
                default:
                    return TessellationStatus.Empty;
            }
        }

        /// <summary>Stroke thickness of a non-circle shape at <paramref name="scale"/>, metres.</summary>
        public static float StrokeThickness(ShapeDefinition shape, RenderQuantization quantization, float scale)
        {
            float t = quantization.DequantizeScale(shape.Thickness);
            return shape.FixedThickness ? t : t * scale;
        }

        /// <summary>
        /// Radius of a circle around the body origin that contains the shape drawn at <paramref name="scale"/>, metres
        /// (rotation-invariant; camera fitting and safe-zone fades). Circle: the scale; ring: radius + thickness / 2;
        /// capsule: length / 2 + thickness / 2; polygon: farthest vertex; polyline: farthest point + the stroke half-width
        /// times the default miter limit (joins never reach further). Allocation-free.
        /// </summary>
        public static float BoundingRadius(ShapeDefinition shape, RenderQuantization quantization, float scale)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            if (quantization == null) throw new ArgumentNullException(nameof(quantization));
            if (!(scale > 0f) || float.IsInfinity(scale)) return 0f;
            switch (shape.Kind)
            {
                case ShapeKind.Circle:
                    return scale;
                case ShapeKind.Ring:
                    return quantization.DequantizeScale(shape.Radius) * scale + StrokeThickness(shape, quantization, scale) * 0.5f;
                case ShapeKind.Capsule:
                    return (quantization.DequantizeScale(shape.Length) * scale + StrokeThickness(shape, quantization, scale)) * 0.5f;
                case ShapeKind.Polygon:
                case ShapeKind.Polyline:
                {
                    ReadOnlySpan<ShapePoint> pts = shape.Points;
                    float max2 = 0f;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float x = quantization.DequantizePosition(pts[i].X), y = quantization.DequantizePosition(pts[i].Y);
                        float d2 = x * x + y * y;
                        if (d2 > max2) max2 = d2;
                    }
                    float r = MathF.Sqrt(max2) * scale;
                    if (shape.Kind == ShapeKind.Polyline) r += StrokeThickness(shape, quantization, scale) * 0.5f * TessellationSettings.DefaultMiterLimit;
                    return r;
                }
                default:
                    return 0f;
            }
        }

        // ---------------------------------------------------------------- ring

        static TessellationStatus TessellateRing(ShapeDefinition shape, RenderQuantization q, float scale, in TessellationSettings s, LocalGeometry output)
        {
            float radius = q.DequantizeScale(shape.Radius) * scale;
            float thickness = StrokeThickness(shape, q, scale);
            float half = thickness * 0.5f;
            if (radius < half)
            {
                // Fixed-thickness ring shrunk below half its thickness (scaled rings keep t ≤ 2r by validation).
                AddDisc(output, 0f, 0f, radius + half, s);
                return output.TriangleCount > 0 ? TessellationStatus.Ok : TessellationStatus.Empty;
            }
            float inner = radius - half;
            float outer = radius + half;
            ReadOnlySpan<RingGap> gaps = shape.Gaps;
            if (gaps.Length == 0)
            {
                AddAnnulusArc(output, inner, outer, 0.0, TwoPi, true, s);
                return TessellationStatus.Ok;
            }
            for (int i = 0; i < gaps.Length; i++)
            {
                int startQ = gaps[i].End;
                int endQ = gaps[(i + 1) % gaps.Length].Start;
                int span = (endQ - startQ) & 0xFFFF;
                if (span == 0)
                {
                    if (gaps.Length > 1) continue;
                    span = 65536;   // one gap: the solid part runs from its end all the way round to its start
                }
                double a0 = startQ * AngleUnit;
                double a1 = a0 + span * AngleUnit;
                AddRingArc(output, radius, inner, outer, a0, a1, s);
            }
            return output.TriangleCount > 0 ? TessellationStatus.Ok : TessellationStatus.Empty;
        }

        static void AddRingArc(LocalGeometry output, float radius, float inner, float outer, double a0, double a1, in TessellationSettings s)
        {
            AddAnnulusArc(output, inner, outer, a0, a1, false, s);
            float half = (outer - inner) * 0.5f;
            switch (s.RingGapCap)
            {
                case LineCap.Round:
                    // Semicircles centred on the arc ends at the centre line, bulging into the gap.
                    AddHalfDiscAt(output, radius, a0, half, false, s);
                    AddHalfDiscAt(output, radius, a1, half, true, s);
                    break;
                case LineCap.Square:
                    AddSquareCapAt(output, radius, a0, half, false);
                    AddSquareCapAt(output, radius, a1, half, true);
                    break;
            }
        }

        /// <summary>Annulus sector [a0, a1] (radians, counter-clockwise) between <paramref name="inner"/> and <paramref name="outer"/>.</summary>
        static void AddAnnulusArc(LocalGeometry output, float inner, float outer, double a0, double a1, bool closed, in TessellationSettings s)
        {
            float sweep = (float)(a1 - a0);
            int n = closed ? ArcMath.CircleSegments(outer, s) : ArcMath.ArcSegments(outer, sweep, s);
            int columns = closed ? n : n + 1;
            output.Reserve(columns * 2, n * 6);
            int first = output.VertexCount;
            for (int k = 0; k < columns; k++)
            {
                double a = a0 + (a1 - a0) * k / n;
                float c = (float)Math.Cos(a);
                float sn = (float)Math.Sin(a);
                output.AddVertex(c * inner, sn * inner);
                output.AddVertex(c * outer, sn * outer);
            }
            for (int k = 0; k < n; k++)
            {
                int i0 = first + 2 * k;
                int i1 = first + 2 * ((k + 1) % columns);
                if (inner > 0f)
                {
                    output.AddTriangle(i0, i0 + 1, i1 + 1);
                    output.AddTriangle(i0, i1 + 1, i1);
                }
                else
                {
                    output.AddTriangle(i0, i0 + 1, i1 + 1);
                }
            }
        }

        /// <summary>Half disc centred at angle <paramref name="angle"/> on the circle of <paramref name="radius"/>, facing along the arc tangent.</summary>
        static void AddHalfDiscAt(LocalGeometry output, float radius, double angle, float half, bool forward, in TessellationSettings s)
        {
            if (!(half > 0f)) return;
            float c = (float)Math.Cos(angle);
            float sn = (float)Math.Sin(angle);
            float cx = c * radius;
            float cy = sn * radius;
            // Radial direction (c, sn); tangent (-sn, c) points counter-clockwise. The cap sweeps from the outer point,
            // round the tangent side, to the inner point.
            double radialAngle = Math.Atan2(sn, c);
            double start = forward ? radialAngle : radialAngle + Math.PI;
            int n = ArcMath.ArcSegments(half, (float)Math.PI, s);
            output.Reserve(n + 2, n * 3);
            int center = output.AddVertex(cx, cy);
            int prev = -1;
            for (int k = 0; k <= n; k++)
            {
                double a = start + Math.PI * k / n;
                int idx = output.AddVertex(cx + (float)Math.Cos(a) * half, cy + (float)Math.Sin(a) * half);
                if (prev >= 0) output.AddTriangle(center, prev, idx);
                prev = idx;
            }
        }

        static void AddSquareCapAt(LocalGeometry output, float radius, double angle, float half, bool forward)
        {
            float c = (float)Math.Cos(angle);
            float sn = (float)Math.Sin(angle);
            float tx = forward ? -sn : sn;
            float ty = forward ? c : -c;
            float ix = c * (radius - half), iy = sn * (radius - half);
            float ox = c * (radius + half), oy = sn * (radius + half);
            int a = output.AddVertex(ix, iy);
            int b = output.AddVertex(ox, oy);
            int d = output.AddVertex(ox + tx * half, oy + ty * half);
            int e = output.AddVertex(ix + tx * half, iy + ty * half);
            output.AddTriangle(a, b, d);
            output.AddTriangle(a, d, e);
        }

        // ---------------------------------------------------------------- capsule & disc

        static TessellationStatus TessellateCapsule(ShapeDefinition shape, RenderQuantization q, float scale, in TessellationSettings s, LocalGeometry output)
        {
            float length = q.DequantizeScale(shape.Length) * scale;
            float r = StrokeThickness(shape, q, scale) * 0.5f;
            if (!(r > 0f)) return TessellationStatus.Empty;
            if (length <= 0f)
            {
                AddDisc(output, 0f, 0f, r, s);
                return TessellationStatus.Ok;
            }
            float hx = length * 0.5f;
            int n = ArcMath.ArcSegments(r, (float)Math.PI, s);
            output.Reserve(2 * (n + 1) + 1, (2 * (n + 1)) * 3);
            int center = output.AddVertex(0f, 0f);
            int first = output.VertexCount;
            for (int k = 0; k <= n; k++)
            {
                double a = -Math.PI * 0.5 + Math.PI * k / n;
                output.AddVertex(hx + (float)Math.Cos(a) * r, (float)Math.Sin(a) * r);
            }
            for (int k = 0; k <= n; k++)
            {
                double a = Math.PI * 0.5 + Math.PI * k / n;
                output.AddVertex(-hx + (float)Math.Cos(a) * r, (float)Math.Sin(a) * r);
            }
            int count = output.VertexCount - first;
            for (int k = 0; k < count; k++)
            {
                output.AddTriangle(center, first + k, first + (k + 1) % count);
            }
            return TessellationStatus.Ok;
        }

        /// <summary>Filled disc (triangle fan) of <paramref name="radius"/> at (<paramref name="cx"/>, <paramref name="cy"/>).</summary>
        public static void AddDisc(LocalGeometry output, float cx, float cy, float radius, in TessellationSettings s)
        {
            if (!(radius > 0f)) return;
            int n = ArcMath.CircleSegments(radius, s);
            output.Reserve(n + 1, n * 3);
            int center = output.AddVertex(cx, cy);
            int first = output.VertexCount;
            for (int k = 0; k < n; k++)
            {
                double a = TwoPi * k / n;
                output.AddVertex(cx + (float)Math.Cos(a) * radius, cy + (float)Math.Sin(a) * radius);
            }
            for (int k = 0; k < n; k++) output.AddTriangle(center, first + k, first + (k + 1) % n);
        }

        // ---------------------------------------------------------------- polygon

        static TessellationStatus TessellatePolygon(ShapeDefinition shape, RenderQuantization q, float scale, LocalGeometry output)
        {
            ReadOnlySpan<ShapePoint> pts = shape.Points;
            Span<int> tri = stackalloc int[3 * Math.Max(1, pts.Length - 2)];
            TessellationStatus status = PolygonTriangulator.Triangulate(pts, tri, out int triangles);
            if (triangles == 0) return TessellationStatus.Empty;
            output.Reserve(pts.Length, triangles * 3);
            int first = output.VertexCount;
            for (int i = 0; i < pts.Length; i++)
            {
                output.AddVertex(q.DequantizePosition(pts[i].X) * scale, q.DequantizePosition(pts[i].Y) * scale);
            }
            for (int t = 0; t < triangles; t++)
            {
                output.AddTriangle(first + tri[3 * t], first + tri[3 * t + 1], first + tri[3 * t + 2]);
            }
            return status;
        }

        // ---------------------------------------------------------------- polyline

        static TessellationStatus TessellatePolyline(ShapeDefinition shape, RenderQuantization q, float scale, in TessellationSettings s, LocalGeometry output)
        {
            ReadOnlySpan<ShapePoint> pts = shape.Points;
            float half = StrokeThickness(shape, q, scale) * 0.5f;
            if (!(half > 0f)) return TessellationStatus.Empty;

            // Distinct consecutive points (quantized comparison: exact).
            int n = pts.Length;
            // ≤ 4096 points (RenderStreamLimits.MaxPolylinePoints): at most 16 KB of stack.
            Span<int> order = n <= RenderStreamLimits.MaxPolylinePoints ? stackalloc int[n] : new int[n];
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                if (m > 0 && pts[order[m - 1]] == pts[i]) continue;
                order[m++] = i;
            }
            bool closed = shape.Closed;
            if (closed) while (m > 1 && pts[order[m - 1]] == pts[order[0]]) m--;
            if (m == 1)
            {
                // Every point coincides: a dot when the ends are round, nothing otherwise.
                if (s.Cap != LineCap.Round && !closed) return TessellationStatus.Empty;
                AddDisc(output, q.DequantizePosition(pts[order[0]].X) * scale, q.DequantizePosition(pts[order[0]].Y) * scale, half, s);
                return TessellationStatus.Ok;
            }
            if (closed && m < 3) closed = false;

            int segments = closed ? m : m - 1;
            for (int k = 0; k < segments; k++)
            {
                Float2 a = Point(pts, order[k], q, scale);
                Float2 b = Point(pts, order[(k + 1) % m], q, scale);
                AddSegmentQuad(output, a, b, half);
            }

            // Joins at interior vertices (every vertex of a closed polyline).
            int firstJoin = closed ? 0 : 1;
            int lastJoin = closed ? m - 1 : m - 2;
            for (int k = firstJoin; k <= lastJoin; k++)
            {
                Float2 p0 = Point(pts, order[(k + m - 1) % m], q, scale);
                Float2 p1 = Point(pts, order[k], q, scale);
                Float2 p2 = Point(pts, order[(k + 1) % m], q, scale);
                AddJoin(output, p0, p1, p2, half, s);
            }

            if (!closed)
            {
                Float2 start = Point(pts, order[0], q, scale);
                Float2 second = Point(pts, order[1], q, scale);
                Float2 end = Point(pts, order[m - 1], q, scale);
                Float2 beforeEnd = Point(pts, order[m - 2], q, scale);
                AddCap(output, start, (start - second).Normalized(), half, s);
                AddCap(output, end, (end - beforeEnd).Normalized(), half, s);
            }
            return output.TriangleCount > 0 ? TessellationStatus.Ok : TessellationStatus.Empty;
        }

        static Float2 Point(ReadOnlySpan<ShapePoint> pts, int i, RenderQuantization q, float scale)
        {
            return new Float2(q.DequantizePosition(pts[i].X) * scale, q.DequantizePosition(pts[i].Y) * scale);
        }

        static void AddSegmentQuad(LocalGeometry output, Float2 a, Float2 b, float half)
        {
            Float2 d = (b - a).Normalized();
            Float2 nrm = d.PerpLeft() * half;
            int i0 = output.AddVertex(a.X + nrm.X, a.Y + nrm.Y);
            int i1 = output.AddVertex(a.X - nrm.X, a.Y - nrm.Y);
            int i2 = output.AddVertex(b.X - nrm.X, b.Y - nrm.Y);
            int i3 = output.AddVertex(b.X + nrm.X, b.Y + nrm.Y);
            output.AddTriangle(i0, i1, i2);
            output.AddTriangle(i0, i2, i3);
        }

        static void AddJoin(LocalGeometry output, Float2 p0, Float2 p1, Float2 p2, float half, in TessellationSettings s)
        {
            Float2 d0 = (p1 - p0).Normalized();
            Float2 d1 = (p2 - p1).Normalized();
            float cross = Float2.Cross(d0, d1);
            float dot = Float2.Dot(d0, d1);
            if (Math.Abs(cross) < 1e-6f && dot > 0f) return;   // straight: the two quads already meet
            // The outer side of the corner is to the right of a left turn and to the left of a right turn.
            float side = cross > 0f ? -1f : 1f;
            Float2 n0 = d0.PerpLeft() * (half * side);
            Float2 n1 = d1.PerpLeft() * (half * side);
            Float2 o0 = p1 + n0;
            Float2 o1 = p1 + n1;

            LineJoin join = s.Join;
            if (join == LineJoin.Round || (dot < -0.9999f && join == LineJoin.Miter))
            {
                if (join != LineJoin.Round)
                {
                    join = LineJoin.Bevel;
                }
                else
                {
                    double a0 = Math.Atan2(n0.Y, n0.X);
                    double a1 = Math.Atan2(n1.Y, n1.X);
                    double sweep = a1 - a0;
                    // Normals rotate with the direction: counter-clockwise for a left turn (outer side right, side < 0).
                    if (side < 0f) { while (sweep < 0) sweep += TwoPi; }
                    else { while (sweep > 0) sweep -= TwoPi; }
                    int segs = ArcMath.ArcSegments(half, (float)Math.Abs(sweep), s);
                    int center = output.AddVertex(p1.X, p1.Y);
                    int prev = output.AddVertex(o0.X, o0.Y);
                    for (int k = 1; k <= segs; k++)
                    {
                        double a = a0 + sweep * k / segs;
                        int idx = output.AddVertex(p1.X + (float)Math.Cos(a) * half, p1.Y + (float)Math.Sin(a) * half);
                        output.AddTriangle(center, prev, idx);
                        prev = idx;
                    }
                    return;
                }
            }

            int c = output.AddVertex(p1.X, p1.Y);
            int i0 = output.AddVertex(o0.X, o0.Y);
            int i1 = output.AddVertex(o1.X, o1.Y);
            if (join == LineJoin.Miter)
            {
                Float2 bis = (n0 + n1).Normalized();
                float cosHalf = Float2.Dot(bis, n0) / half;
                if (cosHalf > 1e-6f)
                {
                    float miterLen = half / cosHalf;
                    if (miterLen <= s.MiterLimit * half)
                    {
                        Float2 m = p1 + bis * miterLen;
                        int im = output.AddVertex(m.X, m.Y);
                        output.AddTriangle(c, i0, im);
                        output.AddTriangle(c, im, i1);
                        return;
                    }
                }
            }
            output.AddTriangle(c, i0, i1);
        }

        static void AddCap(LocalGeometry output, Float2 p, Float2 outward, float half, in TessellationSettings s)
        {
            if (s.Cap == LineCap.Butt) return;
            Float2 nrm = outward.PerpLeft() * half;
            if (s.Cap == LineCap.Square)
            {
                Float2 e = outward * half;
                int a = output.AddVertex(p.X + nrm.X, p.Y + nrm.Y);
                int b = output.AddVertex(p.X - nrm.X, p.Y - nrm.Y);
                int c = output.AddVertex(p.X - nrm.X + e.X, p.Y - nrm.Y + e.Y);
                int d = output.AddVertex(p.X + nrm.X + e.X, p.Y + nrm.Y + e.Y);
                output.AddTriangle(a, b, c);
                output.AddTriangle(a, c, d);
                return;
            }
            // Round: semicircle from -normal through outward to +normal.
            double start = Math.Atan2(-nrm.Y, -nrm.X);
            int n = ArcMath.ArcSegments(half, (float)Math.PI, s);
            int center = output.AddVertex(p.X, p.Y);
            int prev = -1;
            for (int k = 0; k <= n; k++)
            {
                double a = start + Math.PI * k / n;
                int idx = output.AddVertex(p.X + (float)Math.Cos(a) * half, p.Y + (float)Math.Sin(a) * half);
                if (prev >= 0) output.AddTriangle(center, prev, idx);
                prev = idx;
            }
        }
    }
}
