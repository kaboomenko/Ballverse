using System;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>How two stroked polyline segments meet.</summary>
    public enum LineJoin : byte
    {
        /// <summary>Sharp corner, replaced by a bevel beyond <see cref="TessellationSettings.MiterLimit"/>.</summary>
        Miter = 0,
        /// <summary>Straight cut across the outer corner.</summary>
        Bevel = 1,
        /// <summary>Circular arc around the corner.</summary>
        Round = 2,
    }

    /// <summary>How an open stroke or a ring arc ends.</summary>
    public enum LineCap : byte
    {
        /// <summary>Flat cut at the end point.</summary>
        Butt = 0,
        /// <summary>Semicircle of half the thickness centred on the end point.</summary>
        Round = 1,
        /// <summary>Flat cut extended by half the thickness.</summary>
        Square = 2,
    }

    /// <summary>
    /// Error tolerance and stroke styling for <see cref="ShapeTessellator"/>. Curves are split so that the distance between
    /// the true curve and its chords is at most <see cref="TolerancePixels"/> on screen at <see cref="PixelsPerMetre"/>.
    /// </summary>
    public struct TessellationSettings : IEquatable<TessellationSettings>
    {
        /// <summary>Default tolerance in pixels (a quarter pixel: curves look perfectly round at any size).</summary>
        public const float DefaultTolerancePixels = 0.25f;
        public const int DefaultMinCircleSegments = 12;
        public const int DefaultMaxCircleSegments = 1024;
        public const float DefaultMiterLimit = 4f;

        /// <summary>Maximum chord error in screen pixels (&gt; 0).</summary>
        public float TolerancePixels;
        /// <summary>Screen pixels per metre at which the geometry is drawn (&gt; 0).</summary>
        public float PixelsPerMetre;
        /// <summary>Fewest segments of a full circle (small circles still look round when zoomed by bloom).</summary>
        public int MinCircleSegments;
        /// <summary>Most segments of a full circle (bounds the cost of huge curves; the tolerance may then be exceeded).</summary>
        public int MaxCircleSegments;
        public LineJoin Join;
        /// <summary>Caps of open polylines.</summary>
        public LineCap Cap;
        /// <summary>
        /// Ends of ring arcs at gap edges. Round by default: the release-1 solver models gap edges as semicircular ends of
        /// radius thickness / 2 centred on the gap boundary (M01 card), so the picture matches the collider.
        /// </summary>
        public LineCap RingGapCap;
        /// <summary>Largest miter length as a multiple of half the thickness before a miter join becomes a bevel.</summary>
        public float MiterLimit;

        public static TessellationSettings Create(float pixelsPerMetre, float tolerancePixels = DefaultTolerancePixels)
        {
            return new TessellationSettings
            {
                TolerancePixels = tolerancePixels,
                PixelsPerMetre = pixelsPerMetre,
                MinCircleSegments = DefaultMinCircleSegments,
                MaxCircleSegments = DefaultMaxCircleSegments,
                Join = LineJoin.Miter,
                Cap = LineCap.Round,
                RingGapCap = LineCap.Round,
                MiterLimit = DefaultMiterLimit,
            };
        }

        /// <summary>Maximum chord error in metres.</summary>
        public float ToleranceMetres => TolerancePixels / PixelsPerMetre;

        /// <summary>Throws when a field is out of range.</summary>
        public void Validate()
        {
            if (!(TolerancePixels > 0f) || float.IsInfinity(TolerancePixels)) throw new ArgumentOutOfRangeException(nameof(TolerancePixels));
            if (!(PixelsPerMetre > 0f) || float.IsInfinity(PixelsPerMetre)) throw new ArgumentOutOfRangeException(nameof(PixelsPerMetre));
            if (MinCircleSegments < 3) throw new ArgumentOutOfRangeException(nameof(MinCircleSegments));
            if (MaxCircleSegments < MinCircleSegments) throw new ArgumentOutOfRangeException(nameof(MaxCircleSegments));
            if (!(MiterLimit >= 1f)) throw new ArgumentOutOfRangeException(nameof(MiterLimit));
        }

        /// <summary>Same settings except <see cref="PixelsPerMetre"/> (which only changes the segment counts).</summary>
        public bool StyleEquals(in TessellationSettings other)
        {
            return TolerancePixels.Equals(other.TolerancePixels) && MinCircleSegments == other.MinCircleSegments
                && MaxCircleSegments == other.MaxCircleSegments && Join == other.Join && Cap == other.Cap
                && RingGapCap == other.RingGapCap && MiterLimit.Equals(other.MiterLimit);
        }

        public bool Equals(TessellationSettings other) => StyleEquals(other) && PixelsPerMetre.Equals(other.PixelsPerMetre);

        public override bool Equals(object obj) => obj is TessellationSettings other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = TolerancePixels.GetHashCode();
                h = h * 31 + PixelsPerMetre.GetHashCode();
                h = h * 31 + MinCircleSegments;
                h = h * 31 + MaxCircleSegments;
                h = h * 31 + (int)Join;
                h = h * 31 + (int)Cap;
                h = h * 31 + (int)RingGapCap;
                h = h * 31 + MiterLimit.GetHashCode();
                return h;
            }
        }
    }

    /// <summary>Outcome of tessellating one shape.</summary>
    public enum TessellationStatus : byte
    {
        Ok = 0,
        /// <summary>Nothing to draw (zero area, all points coincident…); the geometry is empty.</summary>
        Empty = 1,
        /// <summary>
        /// The polygon crosses itself. It is still triangulated with the fallback ear-clipping rule, so something sensible
        /// is drawn, but the fill may not match the outline exactly.
        /// </summary>
        SelfIntersecting = 2,
    }

    /// <summary>Segment counts for arcs at a chord-error tolerance.</summary>
    public static class ArcMath
    {
        /// <summary>
        /// Segments needed for a full circle of <paramref name="radius"/> metres so that the sagitta r·(1 − cos(θ/2)) of every
        /// chord is at most the tolerance, clamped to [MinCircleSegments, MaxCircleSegments].
        /// </summary>
        public static int CircleSegments(float radius, in TessellationSettings settings)
        {
            double r = radius * (double)settings.PixelsPerMetre;
            double e = settings.TolerancePixels;
            int n;
            if (!(r > e)) n = settings.MinCircleSegments;
            else
            {
                double theta = 2.0 * Math.Acos(1.0 - e / r);
                double count = Math.Ceiling(2.0 * Math.PI / theta - 1e-9);
                n = count > settings.MaxCircleSegments ? settings.MaxCircleSegments : (int)count;
            }
            if (n < settings.MinCircleSegments) n = settings.MinCircleSegments;
            if (n > settings.MaxCircleSegments) n = settings.MaxCircleSegments;
            return n;
        }

        /// <summary>Segments for an arc of <paramref name="sweep"/> radians (≥ 1), proportional to <see cref="CircleSegments"/>.</summary>
        public static int ArcSegments(float radius, float sweep, in TessellationSettings settings)
        {
            if (!(sweep > 0f)) return 1;
            int full = CircleSegments(radius, settings);
            double n = Math.Ceiling(full * (sweep / (2.0 * Math.PI)) - 1e-9);
            if (n < 1) return 1;
            return n > 4 * full ? 4 * full : (int)n;
        }

        /// <summary>Largest distance between an arc of <paramref name="radius"/> and its chords of <paramref name="segmentAngle"/> radians.</summary>
        public static double Sagitta(double radius, double segmentAngle)
        {
            return radius * (1.0 - Math.Cos(segmentAngle * 0.5));
        }
    }
}
