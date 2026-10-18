using System;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>The world rectangle (metres) shown by the simulation slot: centre and size. Y grows upwards (spec §2.1).</summary>
    public struct ViewRect : IEquatable<ViewRect>
    {
        public float CenterX;
        public float CenterY;
        public float Width;
        public float Height;

        public ViewRect(float centerX, float centerY, float width, float height)
        {
            CenterX = centerX;
            CenterY = centerY;
            Width = width;
            Height = height;
        }

        public float MinX => CenterX - Width * 0.5f;
        public float MaxX => CenterX + Width * 0.5f;
        public float MinY => CenterY - Height * 0.5f;
        public float MaxY => CenterY + Height * 0.5f;

        public bool Equals(ViewRect o) => CenterX.Equals(o.CenterX) && CenterY.Equals(o.CenterY) && Width.Equals(o.Width) && Height.Equals(o.Height);

        public override bool Equals(object obj) => obj is ViewRect o && Equals(o);

        public override int GetHashCode() => ((CenterX.GetHashCode() * 31 + CenterY.GetHashCode()) * 31 + Width.GetHashCode()) * 31 + Height.GetHashCode();

        public override string ToString() => "view(" + CenterX + ", " + CenterY + " | " + Width + " x " + Height + ")";
    }

    /// <summary>
    /// Arena-to-viewport mapping (spec §2.1: arena 9 × 16 m centred at (0, 0), safe zone 8 × 12 m; §3.3 layouts crop the
    /// slot). For any canvas the arena is scaled to cover the canvas ("Cover", no empty bars) but never so far that the
    /// safe zone would be cropped: scale = min(max(w/9, h/16), min(w/8, h/12)) pixels per metre. A 9:16 canvas shows the
    /// arena exactly; a wide L2 half or a short L1 band crops only outside the safe zone; extreme shapes letterbox.
    /// </summary>
    public static class ViewMapping
    {
        /// <summary>Pixels per metre at zoom 1 for a <paramref name="width"/> × <paramref name="height"/> canvas.</summary>
        public static float ArenaPixelsPerMetre(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            float cover = Math.Max(width / SimConstants.ArenaWidth, height / SimConstants.ArenaHeight);
            float safe = Math.Min(width / SimConstants.SafeZoneWidth, height / SimConstants.SafeZoneHeight);
            return Math.Min(cover, safe);
        }

        /// <summary>The fixed camera: arena centre, zoom 1.</summary>
        public static ViewRect Fixed(int width, int height)
        {
            float ppm = ArenaPixelsPerMetre(width, height);
            return new ViewRect(0f, 0f, width / ppm, height / ppm);
        }

        /// <summary>A view of the canvas aspect whose height is <paramref name="worldHeight"/> metres, centred at (cx, cy).</summary>
        public static ViewRect WithHeight(int width, int height, float cx, float cy, float worldHeight)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            return new ViewRect(cx, cy, worldHeight * width / height, worldHeight);
        }

        /// <summary>World point → pixel (origin bottom-left, y up) of a canvas of <paramref name="width"/> × <paramref name="height"/>.</summary>
        public static void WorldToPixel(in ViewRect view, int width, int height, float x, float y, out float px, out float py)
        {
            px = (x - view.MinX) / view.Width * width;
            py = (y - view.MinY) / view.Height * height;
        }

        public static void PixelToWorld(in ViewRect view, int width, int height, float px, float py, out float x, out float y)
        {
            x = view.MinX + px / width * view.Width;
            y = view.MinY + py / height * view.Height;
        }

        /// <summary>
        /// Column-major orthographic projection (OpenGL convention, clip z in [−1, 1], view space looking down −z with the
        /// geometry at z = 0) mapping the view rectangle to clip space: 16 floats m00, m10, m20, m30, m01, ….
        /// The Unity layer converts it to the GPU convention of the platform.
        /// </summary>
        public static void Orthographic(in ViewRect view, Span<float> m)
        {
            if (m.Length < 16) throw new ArgumentException("needs 16 floats", nameof(m));
            m.Clear();
            float l = view.MinX, r = view.MaxX, b = view.MinY, t = view.MaxY;
            const float near = -10f, far = 10f;
            m[0] = 2f / (r - l);
            m[5] = 2f / (t - b);
            m[10] = -2f / (far - near);
            m[12] = -(r + l) / (r - l);
            m[13] = -(t + b) / (t - b);
            m[14] = -(far + near) / (far - near);
            m[15] = 1f;
        }
    }
}
