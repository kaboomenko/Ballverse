using System;
using System.Collections.Generic;

namespace Ballverse.Core.Config
{
    /// <summary>UI masks of one publishing platform, e.g. "tiktok" (spec §3.3 "Подсказки площадок").</summary>
    public sealed class PlatformSafeZone
    {
        /// <summary>Platform id as delivered by config (trimmed, non-empty). Lookups ignore case.</summary>
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
    }
}
