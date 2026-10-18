using System;
using System.Globalization;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// 8-bit RGBA colour of a <see cref="ParamType.Color"/> parameter (spec §7.6).
    /// Text form is <c>#RRGGBB</c> (opaque) or <c>#RRGGBBAA</c>; parsing is case-insensitive, formatting is uppercase
    /// and uses the short form whenever alpha is 255, so every colour has exactly one canonical string.
    /// </summary>
    public readonly struct ColorRgba : IEquatable<ColorRgba>
    {
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }
        public byte A { get; }

        public ColorRgba(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        /// <summary>Packed as 0xRRGGBBAA.</summary>
        public uint ToRgba32()
        {
            return ((uint)R << 24) | ((uint)G << 16) | ((uint)B << 8) | A;
        }

        public static ColorRgba FromRgba32(uint rgba)
        {
            return new ColorRgba((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        }

        /// <summary>Parses <c>#RRGGBB</c> or <c>#RRGGBBAA</c> (hex digits in any case). Throws <see cref="FormatException"/>.</summary>
        public static ColorRgba Parse(string text)
        {
            if (!TryParse(text, out ColorRgba c))
                throw new FormatException("Expected a colour as #RRGGBB or #RRGGBBAA, got '" + text + "'");
            return c;
        }

        public static bool TryParse(string text, out ColorRgba color)
        {
            color = default;
            if (text == null || (text.Length != 7 && text.Length != 9) || text[0] != '#') return false;
            uint v = 0;
            for (int i = 1; i < text.Length; i++)
            {
                int d = HexDigit(text[i]);
                if (d < 0) return false;
                v = (v << 4) | (uint)d;
            }
            if (text.Length == 7) v = (v << 8) | 0xFFu;
            color = FromRgba32(v);
            return true;
        }

        /// <summary>Canonical uppercase form: <c>#RRGGBB</c> when opaque, otherwise <c>#RRGGBBAA</c>.</summary>
        public override string ToString()
        {
            return A == 255
                ? "#" + R.ToString("X2", CultureInfo.InvariantCulture) + G.ToString("X2", CultureInfo.InvariantCulture)
                      + B.ToString("X2", CultureInfo.InvariantCulture)
                : "#" + ToRgba32().ToString("X8", CultureInfo.InvariantCulture);
        }

        public bool Equals(ColorRgba other)
        {
            return ToRgba32() == other.ToRgba32();
        }

        public override bool Equals(object obj)
        {
            return obj is ColorRgba other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)ToRgba32();
        }

        public static bool operator ==(ColorRgba a, ColorRgba b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(ColorRgba a, ColorRgba b)
        {
            return !a.Equals(b);
        }

        static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}
