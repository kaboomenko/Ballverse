using System;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>
    /// Maps stream positions to the 2D drawing plane of the view. The standard view is the identity
    /// (<see cref="PlanarProjector"/>); the Helix view projects the cylindrical tower (<see cref="HelixProjection"/>).
    /// Implemented by structs and used through generic constraints, so projecting costs no allocation and no virtual call.
    /// </summary>
    public interface IPointProjector
    {
        /// <summary>A body position from the stream → drawing plane; <paramref name="scale"/> = size factor at that point.</summary>
        void ProjectBody(float x, float y, out float sx, out float sy, out float scale);

        /// <summary>A 3D point (x right, y up, z depth; z &lt; 0 towards the viewer, 0 for planar effects) → drawing plane.</summary>
        void ProjectPoint(float x, float y, float z, out float sx, out float sy, out float scale);
    }

    /// <summary>The identity projection of the standard 2D view (z is ignored).</summary>
    public struct PlanarProjector : IPointProjector
    {
        public void ProjectBody(float x, float y, out float sx, out float sy, out float scale)
        {
            sx = x;
            sy = y;
            scale = 1f;
        }

        public void ProjectPoint(float x, float y, float z, out float sx, out float sy, out float scale)
        {
            sx = x;
            sy = y;
            scale = 1f;
        }
    }

    /// <summary>Trail appearance for one frame (derived from the look, theme and quality profile).</summary>
    public struct TrailBuildSettings
    {
        /// <summary>Trail length in ticks after the quality fraction (≥ 2 to draw anything).</summary>
        public int LengthTicks;
        /// <summary>Ribbon width at the head as a fraction of the body diameter.</summary>
        public float WidthFactor;
        /// <summary>Width at the tail relative to the head (taper).</summary>
        public float TailWidth;
        /// <summary>Alpha at the head (fades to 0 at the tail).</summary>
        public float HeadAlpha;
        /// <summary>Fade curve exponent (1 = linear).</summary>
        public float FadePower;
        public float Emission;
        /// <summary>Hue cycles along the trail instead of the body colour.</summary>
        public bool Rainbow;
        /// <summary>Colour mixed into highlighted bodies' trails (fireball, leader).</summary>
        public uint HighlightColor;

        /// <summary>Nominal full lengths (spec §2.2 <c>trail</c>): short 0.25 s, long and rainbow 1 s.</summary>
        public static int FullLengthTicks(TrailStyle style)
        {
            switch (style)
            {
                case TrailStyle.Short: return SimConstants.TickRate / 4;
                case TrailStyle.Long:
                case TrailStyle.Rainbow: return SimConstants.TickRate;
                default: return 0;
            }
        }

        public static TrailBuildSettings Create(TrailStyle style, float qualityFraction, float emission, uint highlightColor)
        {
            int full = FullLengthTicks(style);
            int len = (int)Math.Round(full * Math.Max(0f, Math.Min(1f, qualityFraction)));
            return new TrailBuildSettings
            {
                LengthTicks = style == TrailStyle.Off ? 0 : Math.Max(len, full > 0 ? 2 : 0),
                WidthFactor = 0.8f,
                TailWidth = 0.15f,
                HeadAlpha = 0.6f,
                FadePower = 1.4f,
                Emission = emission,
                Rainbow = style == TrailStyle.Rainbow,
                HighlightColor = highlightColor,
            };
        }
    }

    /// <summary>
    /// Builds trail ribbons (spec §7.7 "ленты из треугольников с затуханием") from a <see cref="TrailTracker"/>: two
    /// vertices per sample, width tapering and alpha fading with the sample's age in ticks, so the picture depends only
    /// on the tick. Allocation-free.
    /// </summary>
    public static class TrailRibbonBuilder
    {
        /// <summary>Most samples per trail used (longer trails are capped).</summary>
        public const int MaxSamplesPerTrail = 256;

        /// <summary>
        /// Appends the ribbons visible at <paramref name="tick"/> to <paramref name="output"/>. Colours come from
        /// <paramref name="palette"/> (256 resolved entries). Returns the number of ribbons drawn.
        /// </summary>
        public static int Build<TProj>(TrailTracker tracker, int tick, in TrailBuildSettings settings, uint[] palette, GeometryBatch output, ref TProj projector)
            where TProj : struct, IPointProjector
        {
            if (tracker == null) throw new ArgumentNullException(nameof(tracker));
            if (palette == null || palette.Length < 256) throw new ArgumentException("palette needs 256 entries", nameof(palette));
            if (output == null) throw new ArgumentNullException(nameof(output));
            int length = Math.Min(settings.LengthTicks, tracker.CapacityTicks);
            if (length < 2) return 0;
            int ribbons = 0;
            Span<float> px = stackalloc float[MaxSamplesPerTrail];
            Span<float> py = stackalloc float[MaxSamplesPerTrail];
            Span<float> ps = stackalloc float[MaxSamplesPerTrail];
            Span<float> age = stackalloc float[MaxSamplesPerTrail];
            ReadOnlySpan<int> slots = tracker.ActiveSlotsById();
            for (int si = 0; si < slots.Length; si++)
            {
                int slot = slots[si];
                int available = Math.Min(tracker.SampleCount(slot), MaxSamplesPerTrail);
                int m = 0;
                TrailSample newest = default;
                for (int k = 0; k < available; k++)
                {
                    TrailSample s = tracker.GetSample(slot, k);
                    if (s.Tick > tick) continue;
                    int a = tick - s.Tick;
                    if (a >= length) break;
                    if (m == 0) newest = s;
                    projector.ProjectBody(s.X, s.Y, out px[m], out py[m], out ps[m]);
                    age[m] = a / (float)length;
                    m++;
                }
                if (m < 2) continue;
                uint baseColor = palette[newest.Color];
                float emission = settings.Emission;
                if ((newest.Flags & BodyFlags.Highlight) != 0)
                {
                    baseColor = ColorMath.Lerp(baseColor, settings.HighlightColor, 0.6f);
                    emission *= 1.8f;
                }
                if ((newest.Flags & BodyFlags.Dead) != 0) emission = 1f;
                float headWidth = newest.Radius * 2f * settings.WidthFactor;
                if (AppendRibbon(px, py, ps, age, m, headWidth, settings, baseColor, emission, tick, output)) ribbons++;
            }
            return ribbons;
        }

        static bool AppendRibbon(Span<float> px, Span<float> py, Span<float> ps, Span<float> age, int m, float headWidth,
            in TrailBuildSettings s, uint baseColor, float emission, int tick, GeometryBatch output)
        {
            output.Reserve(2 * m, 6 * (m - 1));
            int first = output.VertexCount;
            float lastNx = 0f, lastNy = 0f;
            bool haveNormal = false;
            for (int i = 0; i < m; i++)
            {
                // Tangent from the neighbours (newest → oldest order).
                int a = i > 0 ? i - 1 : i;
                int b = i < m - 1 ? i + 1 : i;
                float tx = px[a] - px[b];
                float ty = py[a] - py[b];
                float len = MathF.Sqrt(tx * tx + ty * ty);
                float nx, ny;
                if (len > 1e-6f)
                {
                    nx = -ty / len;
                    ny = tx / len;
                    lastNx = nx;
                    lastNy = ny;
                    haveNormal = true;
                }
                else
                {
                    nx = lastNx;
                    ny = lastNy;
                }
                float f = age[i];
                float width = headWidth * (1f + (s.TailWidth - 1f) * f) * ps[i];
                float alpha = s.HeadAlpha * MathF.Pow(1f - f, s.FadePower);
                uint color = s.Rainbow ? ColorMath.FromHsv((tick - f * s.LengthTicks) / 90f, 0.85f, 1f) : baseColor;
                color = ColorMath.WithAlpha(color, alpha * (ColorMath.A(baseColor) / 255f));
                float h = width * 0.5f;
                output.AddVertex(px[i] + nx * h, py[i] + ny * h, color, emission);
                output.AddVertex(px[i] - nx * h, py[i] - ny * h, color, emission);
            }
            if (!haveNormal)
            {
                // The body did not move: no ribbon (drop the vertices again).
                RemoveVertices(output, first);
                return false;
            }
            // Samples before the first movement had a zero normal: give them the first real one.
            for (int i = 0; i < m; i++)
            {
                ref BatchVertex v0 = ref output.VertexArray[first + 2 * i];
                ref BatchVertex v1 = ref output.VertexArray[first + 2 * i + 1];
                if (v0.X != v1.X || v0.Y != v1.Y) break;
                float h = headWidth * (1f + (s.TailWidth - 1f) * age[i]) * ps[i] * 0.5f;
                float nx = FirstNormal(px, py, m, out float ny);
                v0.X = px[i] + nx * h;
                v0.Y = py[i] + ny * h;
                v1.X = px[i] - nx * h;
                v1.Y = py[i] - ny * h;
            }
            for (int i = 0; i < m - 1; i++)
            {
                int v = first + 2 * i;
                output.AddTriangle(v, v + 1, v + 3);
                output.AddTriangle(v, v + 3, v + 2);
            }
            return true;
        }

        static float FirstNormal(Span<float> px, Span<float> py, int m, out float ny)
        {
            for (int i = 0; i < m - 1; i++)
            {
                float tx = px[i] - px[i + 1];
                float ty = py[i] - py[i + 1];
                float len = MathF.Sqrt(tx * tx + ty * ty);
                if (len > 1e-6f)
                {
                    ny = tx / len;
                    return -ty / len;
                }
            }
            ny = 0f;
            return 0f;
        }

        static void RemoveVertices(GeometryBatch output, int first)
        {
            output.TruncateVertices(first);
        }
    }
}
