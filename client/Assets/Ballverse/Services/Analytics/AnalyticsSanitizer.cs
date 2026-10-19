using System.Collections.Generic;

namespace Ballverse.Services
{
    /// <summary>
    /// Enforces Firebase limits and the "no personal data" rule of spec §9.2 before anything is sent:
    /// <list type="bullet">
    /// <item>event names: <c>[a-z][a-z0-9_]{0,39}</c>, not starting with the reserved <c>firebase_</c>, <c>google_</c>, <c>ga_</c>;</item>
    /// <item>parameters: only §9.2 names (<see cref="AnalyticsParams"/>), at most 25, duplicates keep the first;</item>
    /// <item>string values: at most 100 characters; a value that looks like a path, URL or e-mail (contains '/', '\\', ':' or '@')
    /// is replaced by <c>"redacted"</c>;</item>
    /// <item>NaN and infinite doubles are dropped.</item>
    /// </list>
    /// Pure and deterministic.
    /// </summary>
    public static class AnalyticsSanitizer
    {
        public const int MaxEventNameLength = 40;
        public const int MaxParams = 25;
        public const int MaxStringValueLength = 100;
        public const int MaxUserPropertyValueLength = 36;
        public const string Redacted = "redacted";

        static readonly string[] s_reservedPrefixes = { "firebase_", "google_", "ga_" };

        public static bool IsValidEventName(string name)
        {
            if (!IsSnakeIdentifier(name, MaxEventNameLength)) return false;
            foreach (string p in s_reservedPrefixes)
            {
                if (name.StartsWith(p, System.StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>
        /// Cleans an event. Returns false (event must be dropped) only for an invalid name. <paramref name="dropped"/> lists
        /// the names of removed parameters (null when nothing was removed or changed).
        /// </summary>
        public static bool TrySanitizeEvent(string name, IReadOnlyList<AnalyticsParam> parameters,
            out List<AnalyticsParam> clean, out List<string> dropped)
        {
            clean = new List<AnalyticsParam>();
            dropped = null;
            if (!IsValidEventName(name)) return false;
            if (parameters == null) return true;

            var seen = new HashSet<string>();
            foreach (var p in parameters)
            {
                if (!AnalyticsParams.IsKnown(p.Name) || !seen.Add(p.Name) || clean.Count >= MaxParams)
                {
                    (dropped = dropped ?? new List<string>()).Add(p.Name ?? "(null)");
                    continue;
                }
                if (p.Type == AnalyticsParamType.Double && (double.IsNaN(p.DoubleValue) || double.IsInfinity(p.DoubleValue)))
                {
                    (dropped = dropped ?? new List<string>()).Add(p.Name);
                    continue;
                }
                if (p.Type == AnalyticsParamType.String)
                {
                    string v = CleanString(p.StringValue, MaxStringValueLength);
                    if (!string.Equals(v, p.StringValue, System.StringComparison.Ordinal))
                    {
                        (dropped = dropped ?? new List<string>()).Add(p.Name + "(value)");
                        clean.Add(p.WithString(v));
                        continue;
                    }
                }
                clean.Add(p);
            }
            return true;
        }

        /// <summary>Cleans a user property. False when the name is not a §9.2 user property.</summary>
        public static bool TrySanitizeUserProperty(string name, string value, out string clean)
        {
            clean = null;
            if (!AnalyticsUserProperties.IsKnown(name)) return false;
            clean = value == null ? null : CleanString(value, MaxUserPropertyValueLength);
            return true;
        }

        static string CleanString(string value, int maxLength)
        {
            if (value == null) return string.Empty;
            if (value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0 || value.IndexOf('@') >= 0 || value.IndexOf(':') >= 0)
                return Redacted;
            return value.Length > maxLength ? value.Substring(0, maxLength) : value;
        }

        static bool IsSnakeIdentifier(string s, int maxLength)
        {
            if (string.IsNullOrEmpty(s) || s.Length > maxLength) return false;
            if (s[0] < 'a' || s[0] > 'z') return false;
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return true;
        }
    }
}
