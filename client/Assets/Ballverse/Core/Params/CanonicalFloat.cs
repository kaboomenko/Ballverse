using System;
using System.Globalization;
using System.Text;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// Platform-independent text form of a float for canonical parameter JSON (spec §7.4 paramsHash): the shortest
    /// decimal (1–9 significant digits) that parses back to the same float, written in plain notation without an
    /// exponent and always with a fractional part ("1.0", "0.25", "-3.5"). Negative zero is written as "0.0".
    /// Avoids runtime-specific default float formatting ("R" / "G" differ between .NET Framework, Mono and .NET Core).
    /// </summary>
    public static class CanonicalFloat
    {
        public static string Format(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException("Non-finite float cannot be serialized", nameof(value));
            if (value == 0f) return "0.0";

            string e = null;
            for (int precision = 1; precision <= 9; precision++)
            {
                e = value.ToString("E" + (precision - 1), CultureInfo.InvariantCulture);
                if (float.Parse(e, NumberStyles.Float, CultureInfo.InvariantCulture) == value) break;
            }

            // e = "[-]d.dddE+xxx"
            bool negative = e[0] == '-';
            int ePos = e.IndexOf('E');
            string mantissa = e.Substring(negative ? 1 : 0, ePos - (negative ? 1 : 0)).Replace(".", "");
            int exponent = int.Parse(e.Substring(ePos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            string digits = mantissa.TrimEnd('0');
            if (digits.Length == 0) digits = "0";

            // value = 0.digits × 10^(exponent + 1)
            int point = exponent + 1;
            var sb = new StringBuilder(digits.Length + 8);
            if (negative) sb.Append('-');
            if (point <= 0)
            {
                sb.Append("0.").Append('0', -point).Append(digits);
            }
            else if (point >= digits.Length)
            {
                sb.Append(digits).Append('0', point - digits.Length).Append(".0");
            }
            else
            {
                sb.Append(digits, 0, point).Append('.').Append(digits, point, digits.Length - point);
            }
            return sb.ToString();
        }
    }
}
