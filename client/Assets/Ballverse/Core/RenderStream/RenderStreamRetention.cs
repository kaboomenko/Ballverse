using System;
using System.Collections.Generic;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Ring-buffer policy for cached render streams (spec §7.4: "кольцевой буфер последних 5 запусков хранит потоки
    /// рендера, пока их суммарный размер ≤ 500 МБ (вытесняются старые потоки, реплеи остаются)").
    /// Pure decision logic: the caller lists its streams newest first and deletes the ones past the returned count
    /// (replays are never touched).
    /// </summary>
    public static class RenderStreamRetention
    {
        public const int DefaultMaxStreams = 5;
        /// <summary>500 MB, interpreted as binary megabytes (500 × 2^20 bytes).</summary>
        public const long DefaultMaxTotalBytes = 500L * 1024 * 1024;

        /// <summary>
        /// Returns how many of the newest streams to keep: the longest prefix of <paramref name="sizesNewestFirst"/> with at
        /// most <paramref name="maxStreams"/> entries whose total size is ≤ <paramref name="maxTotalBytes"/>.
        /// The newest stream is always kept (it belongs to the run currently in use), even if it alone exceeds the budget.
        /// </summary>
        public static int CountToKeep(IReadOnlyList<long> sizesNewestFirst, int maxStreams = DefaultMaxStreams, long maxTotalBytes = DefaultMaxTotalBytes)
        {
            if (sizesNewestFirst == null) throw new ArgumentNullException(nameof(sizesNewestFirst));
            if (maxStreams < 1) throw new ArgumentOutOfRangeException(nameof(maxStreams));
            if (maxTotalBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxTotalBytes));

            long total = 0;
            int keep = 0;
            for (int i = 0; i < sizesNewestFirst.Count && keep < maxStreams; i++)
            {
                long size = sizesNewestFirst[i];
                if (size < 0) throw new ArgumentException("Stream sizes must be non-negative", nameof(sizesNewestFirst));
                if (i > 0 && size > maxTotalBytes - total) break;
                total += size;
                keep++;
            }
            return keep;
        }
    }
}
