using System;
using System.Collections.Generic;

namespace Ballverse.Core.Config
{
    /// <summary>UI masks of one publishing platform, e.g. "tiktok" (spec §3.3 "Подсказки площадок").</summary>
    public sealed class PlatformSafeZone
    {
        /// <summary>
        /// Platform id as delivered by config (trimmed, non-empty). Two ids name the same platform when they are equal ignoring
        /// case (<see cref="StringComparer.OrdinalIgnoreCase"/>) <i>or</i> equal after <see cref="CanonicalId"/> (the
        /// normalization Layout's <c>PlatformUiMasks</c> applies); see <see cref="SameId"/>.
        /// </summary>
        public string Platform { get; }
        /// <summary>Areas covered by the platform UI. May be empty (no masks for this platform).</summary>
        public IReadOnlyList<NormalizedRect> Rects { get; }

        public PlatformSafeZone(string platform, IEnumerable<NormalizedRect> rects)
        {
            if (string.IsNullOrWhiteSpace(platform)) throw new ArgumentException("platform must be non-empty", nameof(platform));
            if (rects == null) throw new ArgumentNullException(nameof(rects));
            var list = new List<NormalizedRect>(rects);
            foreach (var r in list)
            {
                if (r == null) throw new ArgumentException("null rect", nameof(rects));
            }
            Platform = platform.Trim();
            Rects = list.AsReadOnly();
        }

        /// <summary>
        /// The id as Layout's <c>PlatformUiMasks</c> stores it: trimmed and lower-cased with the invariant culture. Config and
        /// Layout must agree on which ids collide, otherwise ids that are distinct here (e.g. U+212A KELVIN SIGN and "k", which
        /// differ under <see cref="StringComparer.OrdinalIgnoreCase"/>) would collide in Layout.
        /// </summary>
        public static string CanonicalId(string platform)
        {
            if (platform == null) throw new ArgumentNullException(nameof(platform));
            return platform.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// True when two ids name the same platform: equal ignoring case (Config's historical rule) or equal after
        /// <see cref="CanonicalId"/> (Layout's rule). Config treats either as a duplicate, so every id it keeps is unique under
        /// both rules.
        /// </summary>
        public static bool SameId(string a, string b)
        {
            if (a == null || b == null) return a == null && b == null;
            string ta = a.Trim(), tb = b.Trim();
            return StringComparer.OrdinalIgnoreCase.Equals(ta, tb)
                || string.Equals(ta.ToLowerInvariant(), tb.ToLowerInvariant(), StringComparison.Ordinal);
        }
    }
}
