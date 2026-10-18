using System;
using System.Collections.Generic;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>A colour blot stuck to a Helix platform (M05 VFX "клякса цвета шара на платформе").</summary>
    public struct HelixSplat
    {
        /// <summary>Body id of the platform (ring body) the blot sticks to.</summary>
        public int BodyId;
        /// <summary>Platform-local azimuth of the blot (rotates with the platform), radians.</summary>
        public float LocalAngle;
        public float Radius;
        public uint Color;
    }

    /// <summary>
    /// Builds the Helix Drop tower in pseudo-3D (M05 card): every visible <see cref="ShapeKind.Ring"/> body is a platform
    /// — a horizontal annulus at height = body y (its top face), rotated by the body angle, with the ring's gaps as
    /// openings and a slab thickness of <see cref="HelixViewSettings.PlatformThickness"/> — drawn with
    /// <see cref="HelixProjection"/>.
    /// <para>
    /// Painter's order (front/back): platforms from the lowest to the highest; for each platform its back half (top face),
    /// then the column segment standing on it, then its front half (outer rim, visible gap walls, top face), then its
    /// splats. Platforms at or below <c>ballHeight</c> go to the <c>under</c> batch, higher ones to <c>over</c>; the renderer
    /// draws under → ball → over, so the ball is hidden by an overhanging higher platform and covers the one it bounces on.
    /// Equal heights draw in ascending body id (a deadly-sector ring at the same height as its platform draws on top when it
    /// has the higher id).
    /// </para>
    /// Allocation-free once the scratch arrays reached the platform count.
    /// </summary>
    public sealed class HelixTowerBuilder
    {
        const float HalfPi = MathF.PI * 0.5f;
        const double AngleUnit = 2.0 * Math.PI / 65536.0;

        int[] _order = new int[32];
        float[] _height = new float[32];

        /// <summary>Platforms drawn by the last <see cref="Build"/>.</summary>
        public int PlatformCount { get; private set; }

        /// <summary>Platforms put into the over-ball batch by the last <see cref="Build"/>.</summary>
        public int OverCount { get; private set; }

        public void Build(RenderFrame frame, RenderQuantization quantization, IReadOnlyList<ShapeDefinition> shapes, uint[] palette,
            RenderTheme theme, HelixViewSettings view, in HelixProjection projection, in TessellationSettings tess, float ballHeight,
            ReadOnlySpan<HelixSplat> splats, GeometryBatch under, GeometryBatch over)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (quantization == null) throw new ArgumentNullException(nameof(quantization));
            if (shapes == null) throw new ArgumentNullException(nameof(shapes));
            if (palette == null || palette.Length < 256) throw new ArgumentException("palette needs 256 entries", nameof(palette));
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (under == null) throw new ArgumentNullException(nameof(under));
            if (over == null) throw new ArgumentNullException(nameof(over));

            ReadOnlySpan<BodyState> bodies = frame.Bodies;
            int count = 0;
            float minInner = float.MaxValue;
            for (int i = 0; i < bodies.Length; i++)
            {
                BodyState b = bodies[i];
                if ((b.Flags & BodyFlags.Visible) == 0) continue;
                if ((uint)b.ShapeId >= (uint)shapes.Count || shapes[b.ShapeId].Kind != ShapeKind.Ring) continue;
                if (count == _order.Length)
                {
                    Array.Resize(ref _order, count * 2);
                    Array.Resize(ref _height, count * 2);
                }
                float h = quantization.DequantizePosition(b.Y);
                // Insertion sort by (height, id); frames are id-sorted, so ties keep id order.
                int k = count;
                while (k > 0 && _height[k - 1] > h)
                {
                    _order[k] = _order[k - 1];
                    _height[k] = _height[k - 1];
                    k--;
                }
                _order[k] = i;
                _height[k] = h;
                count++;
                Radii(shapes[b.ShapeId], quantization, b.Scale, out float inner, out _, out _);
                if (inner < minInner) minInner = inner;
            }
            PlatformCount = count;
            OverCount = 0;
            if (count == 0) return;

            float t = Math.Max(0f, view.PlatformThickness);
            float columnRadius = view.ColumnRadius > 0f ? view.ColumnRadius : Math.Max(0.05f, minInner);
            uint columnColor = palette[PaletteSlots.ArenaAlt];
            float emission = theme.ShapeEmission;

            // Column below the lowest platform.
            AddColumn(under, projection, columnRadius, _height[0] - t - view.ColumnBottomExtra, _height[0] - t, columnColor, tess, emission);

            Span<float> pieces = stackalloc float[4 * (RenderStreamLimits.MaxRingGaps + 2) * 3];
            for (int p = 0; p < count; p++)
            {
                BodyState b = bodies[_order[p]];
                float h = _height[p];
                bool isOver = !float.IsNaN(ballHeight) && h > ballHeight;
                if (isOver) OverCount++;
                GeometryBatch batch = isOver ? over : under;
                ShapeDefinition shape = shapes[b.ShapeId];
                Radii(shape, quantization, b.Scale, out float inner, out float outer, out _);
                float theta = RenderQuantization.DequantizeAngle(b.Angle);
                uint color = palette[b.Color];
                if ((b.Flags & BodyFlags.Dead) != 0) color = ColorMath.Lerp(color, palette[PaletteSlots.Muted], 0.7f);
                float em = (b.Flags & BodyFlags.Highlight) != 0 ? emission * 1.6f : emission;

                int pieceCount = SplitPieces(shape, theta, pieces);

                // 1. Back half (top face only: back rims face away from the viewer).
                for (int k = 0; k < pieceCount; k++)
                {
                    float a0 = pieces[4 * k], a1 = pieces[4 * k + 1];
                    if (HelixProjection.IsFront((a0 + a1) * 0.5f)) continue;
                    AddTopFace(batch, projection, inner, outer, h, a0, a1, color, theme.HelixBackShade, tess, em);
                }

                // 2. Column standing on this platform, up to the next one (or the tower top).
                float top = p + 1 < count ? _height[p + 1] - t : h + view.ColumnTopExtra;
                if (top > h) AddColumn(batch, projection, columnRadius, h, top, columnColor, tess, emission);

                // 3. Front half: outer rim, visible gap walls, then the top face over them.
                for (int k = 0; k < pieceCount; k++)
                {
                    float a0 = pieces[4 * k], a1 = pieces[4 * k + 1];
                    if (!HelixProjection.IsFront((a0 + a1) * 0.5f)) continue;
                    if (t > 0f)
                    {
                        AddRim(batch, projection, outer, h, h - t, a0, a1, ColorMath.Scale(color, view.RimShade), theme.HelixBackShade, tess, em);
                        bool startIsGapEdge = pieces[4 * k + 2] != 0f;
                        bool endIsGapEdge = pieces[4 * k + 3] != 0f;
                        uint wall = ColorMath.Scale(color, view.GapWallShade);
                        if (startIsGapEdge && MathF.Sin(a0) > 0f) AddGapWall(batch, projection, inner, outer, h, h - t, a0, wall, theme.HelixBackShade, em);
                        if (endIsGapEdge && MathF.Sin(a1) < 0f) AddGapWall(batch, projection, inner, outer, h, h - t, a1, wall, theme.HelixBackShade, em);
                    }
                    AddTopFace(batch, projection, inner, outer, h, a0, a1, color, theme.HelixBackShade, tess, em);
                }

                // 4. Splats on this platform.
                for (int s = 0; s < splats.Length; s++)
                {
                    if (splats[s].BodyId != b.Id) continue;
                    float rho = Math.Max(inner, Math.Min(outer, projection.BallPathRadius));
                    AddSplat(batch, projection, rho, splats[s].LocalAngle + theta, h, splats[s].Radius, splats[s].Color, tess, em);
                }
            }
        }

        /// <summary>Inner and outer radius (and thickness) of a ring body, honouring FixedThickness.</summary>
        public static void Radii(ShapeDefinition ring, RenderQuantization q, ushort scaleQ, out float inner, out float outer, out float thickness)
        {
            float scale = q.DequantizeScale(scaleQ);
            float radius = q.DequantizeScale(ring.Radius) * scale;
            thickness = ShapeTessellator.StrokeThickness(ring, q, scale);
            inner = Math.Max(0f, radius - thickness * 0.5f);
            outer = radius + thickness * 0.5f;
        }

        /// <summary>
        /// Splits the solid arcs of <paramref name="ring"/> (world azimuths, rotated by <paramref name="theta"/>) at the
        /// front/back boundaries ψ = ±90°. Each piece is 4 floats: start, end (end &gt; start), start-is-gap-edge,
        /// end-is-gap-edge (1/0). Returns the piece count.
        /// </summary>
        public static int SplitPieces(ShapeDefinition ring, float theta, Span<float> pieces)
        {
            int count = 0;
            ReadOnlySpan<RingGap> gaps = ring.Gaps;
            if (gaps.Length == 0)
            {
                return SplitArc(theta, theta + 2f * MathF.PI, false, pieces, count);
            }
            for (int i = 0; i < gaps.Length; i++)
            {
                int startQ = gaps[i].End;
                int endQ = gaps[(i + 1) % gaps.Length].Start;
                int span = (endQ - startQ) & 0xFFFF;
                if (span == 0)
                {
                    if (gaps.Length > 1) continue;
                    span = 65536;
                }
                float a0 = theta + (float)(startQ * AngleUnit);
                float a1 = a0 + (float)(span * AngleUnit);
                count = SplitArc(a0, a1, true, pieces, count);
            }
            return count;
        }

        static int SplitArc(float a0, float a1, bool gapEdges, Span<float> pieces, int count)
        {
            // Boundaries at π/2 + kπ strictly inside (a0, a1).
            double first = Math.Ceiling((a0 - HalfPi) / Math.PI);
            float start = a0;
            bool startEdge = gapEdges;
            for (double k = first; ; k++)
            {
                float b = (float)(HalfPi + k * Math.PI);
                if (b <= a0 + 1e-6f) continue;
                if (b >= a1 - 1e-6f) break;
                if (4 * count + 4 > pieces.Length) return count;
                pieces[4 * count] = start;
                pieces[4 * count + 1] = b;
                pieces[4 * count + 2] = startEdge ? 1f : 0f;
                pieces[4 * count + 3] = 0f;
                count++;
                start = b;
                startEdge = false;
            }
            if (4 * count + 4 > pieces.Length) return count;
            pieces[4 * count] = start;
            pieces[4 * count + 1] = a1;
            pieces[4 * count + 2] = startEdge ? 1f : 0f;
            pieces[4 * count + 3] = gapEdges ? 1f : 0f;
            return count + 1;
        }

        static int Segments(float radius, float sweep, in HelixProjection proj, in TessellationSettings tess)
        {
            return ArcMath.ArcSegments(radius, sweep, tess);
        }

        static void AddTopFace(GeometryBatch batch, in HelixProjection proj, float inner, float outer, float h, float a0, float a1,
            uint color, float backShade, in TessellationSettings tess, float emission)
        {
            int n = Segments(outer, a1 - a0, proj, tess);
            batch.Reserve(2 * (n + 1), 6 * n);
            int first = batch.VertexCount;
            for (int k = 0; k <= n; k++)
            {
                float a = a0 + (a1 - a0) * k / n;
                uint c = ColorMath.Scale(color, HelixProjection.Shade(a, backShade));
                proj.ProjectCylindrical(inner, a, h, out float ix, out float iy, out _, out _);
                proj.ProjectCylindrical(outer, a, h, out float ox, out float oy, out _, out _);
                batch.AddVertex(ix, iy, c, emission);
                batch.AddVertex(ox, oy, c, emission);
            }
            for (int k = 0; k < n; k++)
            {
                int v = first + 2 * k;
                batch.AddTriangle(v, v + 1, v + 3);
                batch.AddTriangle(v, v + 3, v + 2);
            }
        }

        static void AddRim(GeometryBatch batch, in HelixProjection proj, float radius, float yTop, float yBottom, float a0, float a1,
            uint color, float backShade, in TessellationSettings tess, float emission)
        {
            int n = Segments(radius, a1 - a0, proj, tess);
            batch.Reserve(2 * (n + 1), 6 * n);
            int first = batch.VertexCount;
            for (int k = 0; k <= n; k++)
            {
                float a = a0 + (a1 - a0) * k / n;
                uint c = ColorMath.Scale(color, HelixProjection.Shade(a, backShade));
                proj.ProjectCylindrical(radius, a, yTop, out float tx, out float ty, out _, out _);
                proj.ProjectCylindrical(radius, a, yBottom, out float bx, out float by, out _, out _);
                batch.AddVertex(tx, ty, c, emission);
                batch.AddVertex(bx, by, c, emission);
            }
            for (int k = 0; k < n; k++)
            {
                int v = first + 2 * k;
                batch.AddTriangle(v, v + 1, v + 3);
                batch.AddTriangle(v, v + 3, v + 2);
            }
        }

        static void AddGapWall(GeometryBatch batch, in HelixProjection proj, float inner, float outer, float yTop, float yBottom, float a,
            uint color, float backShade, float emission)
        {
            uint c = ColorMath.Scale(color, HelixProjection.Shade(a, backShade));
            proj.ProjectCylindrical(inner, a, yTop, out float x0, out float y0, out _, out _);
            proj.ProjectCylindrical(outer, a, yTop, out float x1, out float y1, out _, out _);
            proj.ProjectCylindrical(outer, a, yBottom, out float x2, out float y2, out _, out _);
            proj.ProjectCylindrical(inner, a, yBottom, out float x3, out float y3, out _, out _);
            int i0 = batch.AddVertex(x0, y0, c, emission);
            int i1 = batch.AddVertex(x1, y1, c, emission);
            int i2 = batch.AddVertex(x2, y2, c, emission);
            int i3 = batch.AddVertex(x3, y3, c, emission);
            batch.AddQuad(i0, i1, i2, i3);
        }

        static void AddColumn(GeometryBatch batch, in HelixProjection proj, float radius, float y0, float y1, uint color,
            in TessellationSettings tess, float emission)
        {
            if (!(y1 > y0)) return;
            int n = Math.Max(8, ArcMath.ArcSegments(radius, MathF.PI, tess));
            batch.Reserve(2 * (n + 1), 6 * n);
            int first = batch.VertexCount;
            for (int k = 0; k <= n; k++)
            {
                float a = -HalfPi + MathF.PI * k / n;
                uint c = ColorMath.Scale(color, 0.55f + 0.45f * MathF.Cos(a));
                proj.ProjectCylindrical(radius, a, y0, out float bx, out float by, out _, out _);
                proj.ProjectCylindrical(radius, a, y1, out float tx, out float ty, out _, out _);
                batch.AddVertex(bx, by, c, emission);
                batch.AddVertex(tx, ty, c, emission);
            }
            for (int k = 0; k < n; k++)
            {
                int v = first + 2 * k;
                batch.AddTriangle(v, v + 1, v + 3);
                batch.AddTriangle(v, v + 3, v + 2);
            }
        }

        static void AddSplat(GeometryBatch batch, in HelixProjection proj, float rho, float azimuth, float h, float radius, uint color,
            in TessellationSettings tess, float emission)
        {
            if (!(radius > 0f) || ColorMath.A(color) == 0) return;
            float cx = proj.AxisX + rho * MathF.Sin(azimuth);
            float cz = -rho * MathF.Cos(azimuth);
            int n = Math.Max(10, ArcMath.CircleSegments(radius, tess));
            batch.Reserve(n + 1, 3 * n);
            proj.Project(cx, h, cz, out float sx, out float sy, out _, out _);
            int center = batch.AddVertex(sx, sy, color, emission);
            int first = batch.VertexCount;
            for (int k = 0; k < n; k++)
            {
                float a = 2f * MathF.PI * k / n;
                proj.Project(cx + radius * MathF.Cos(a), h, cz + radius * MathF.Sin(a), out float px, out float py, out _, out _);
                batch.AddVertex(px, py, ColorMath.WithAlpha(color, 0.85f), emission);
            }
            for (int k = 0; k < n; k++) batch.AddTriangle(center, first + k, first + (k + 1) % n);
        }
    }
}
