using System;
using System.Globalization;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>
    /// Helpers for colours packed as 0xRRGGBBAA (sRGB bytes, the same packing as <c>ColorRgba.ToRgba32</c>). Render code
    /// passes colours around in this form; the GPU converts sRGB to linear.
    /// </summary>
    public static class ColorMath
    {
        public const uint White = 0xFFFFFFFFu;
        public const uint Black = 0x000000FFu;
        public const uint Transparent = 0u;

        public static uint Pack(byte r, byte g, byte b, byte a = 255)
        {
            return ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a;
        }

        public static byte R(uint c) => (byte)(c >> 24);
        public static byte G(uint c) => (byte)(c >> 16);
        public static byte B(uint c) => (byte)(c >> 8);
        public static byte A(uint c) => (byte)c;

        /// <summary>Parses <c>#RRGGBB</c> or <c>#RRGGBBAA</c>.</summary>
        public static bool TryParseHex(string text, out uint rgba)
        {
            rgba = 0;
            if (text == null || (text.Length != 7 && text.Length != 9) || text[0] != '#') return false;
            if (!uint.TryParse(text.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint v)) return false;
            rgba = text.Length == 7 ? (v << 8) | 0xFFu : v;
            return true;
        }

        public static uint ParseHex(string text)
        {
            if (!TryParseHex(text, out uint v)) throw new FormatException("Expected #RRGGBB or #RRGGBBAA, got '" + text + "'");
            return v;
        }

        public static string ToHex(uint rgba)
        {
            return A(rgba) == 255
                ? "#" + (rgba >> 8).ToString("X6", CultureInfo.InvariantCulture)
                : "#" + rgba.ToString("X8", CultureInfo.InvariantCulture);
        }

        public static uint WithAlpha(uint c, float alpha)
        {
            int a = (int)Math.Round(A(c) * Clamp01(alpha));
            return (c & 0xFFFFFF00u) | (uint)a;
        }

        /// <summary>Component-wise interpolation of all four channels (in sRGB bytes).</summary>
        public static uint Lerp(uint a, uint b, float t)
        {
            t = Clamp01(t);
            return Pack(LerpByte(R(a), R(b), t), LerpByte(G(a), G(b), t), LerpByte(B(a), B(b), t), LerpByte(A(a), A(b), t));
        }

        /// <summary>Multiplies the colour channels (not alpha) by <paramref name="factor"/> (clamped).</summary>
        public static uint Scale(uint c, float factor)
        {
            return Pack(ScaleByte(R(c), factor), ScaleByte(G(c), factor), ScaleByte(B(c), factor), A(c));
        }

        /// <summary>Relative luminance in [0, 1] (Rec. 709 weights on linearized channels).</summary>
        public static float Luminance(uint c)
        {
            return 0.2126f * ToLinear(R(c)) + 0.7152f * ToLinear(G(c)) + 0.0722f * ToLinear(B(c));
        }

        /// <summary>Black or white, whichever reads better on <paramref name="background"/>.</summary>
        public static uint ContrastText(uint background)
        {
            return Luminance(background) > 0.35f ? Pack(0x14, 0x14, 0x1A) : White;
        }

        /// <summary>Opaque colour from hue (turns, wraps), saturation and value in [0, 1].</summary>
        public static uint FromHsv(float hue, float saturation, float value)
        {
            float h = hue - MathF.Floor(hue);
            float s = Clamp01(saturation);
            float v = Clamp01(value);
            float hh = h * 6f;
            int sector = (int)hh;
            if (sector > 5) sector = 5;
            float f = hh - sector;
            float p = v * (1f - s);
            float q = v * (1f - s * f);
            float t = v * (1f - s * (1f - f));
            float r, g, b;
            switch (sector)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return Pack(ToByte(r), ToByte(g), ToByte(b));
        }

        public static float ToLinear(byte channel)
        {
            float c = channel / 255f;
            return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        static byte LerpByte(byte a, byte b, float t) => (byte)Math.Round(a + (b - a) * t);

        static byte ScaleByte(byte v, float f)
        {
            float r = v * f;
            return r <= 0f ? (byte)0 : r >= 255f ? (byte)255 : (byte)Math.Round(r);
        }

        static byte ToByte(float v) => (byte)Math.Round(Clamp01(v) * 255f);

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : (float.IsNaN(v) ? 0f : v);
    }
}
