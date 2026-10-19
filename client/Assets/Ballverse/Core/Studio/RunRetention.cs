using System;
using System.Collections.Generic;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.Studio
{
    /// <summary>What to delete from the recent-runs ring (see <see cref="RunRetention.Decide"/>).</summary>
    public sealed class RunRetentionPlan
    {
        /// <summary>Runs to delete entirely (older than the newest <c>maxRuns</c>).</summary>
        public IReadOnlyList<string> DeleteRuns { get; }
        /// <summary>Kept runs whose render stream and events are evicted (the replay stays, spec §7.4).</summary>
        public IReadOnlyList<string> EvictStreams { get; }

        public RunRetentionPlan(IReadOnlyList<string> deleteRuns, IReadOnlyList<string> evictStreams)
        {
            DeleteRuns = deleteRuns;
            EvictStreams = evictStreams;
        }

        public bool IsEmpty => DeleteRuns.Count == 0 && EvictStreams.Count == 0;
    }

    /// <summary>
    /// Ring buffer of the last runs (spec §3.1 "кольцевой буфер последних 5 запусков", §7.4 "хранит потоки рендера, пока их
    /// суммарный размер ≤ 500 МБ (вытесняются старые потоки, реплеи остаются)", §8 SZ4): only the newest <c>maxRuns</c> runs are
    /// kept; among them the render streams follow <see cref="RenderStreamRetention.CountToKeep"/> (the newest always stays). Pure.
    /// </summary>
    public static class RunRetention
    {
        /// <param name="runsNewestFirst">Committed runs, newest first.</param>
        /// <param name="maxRuns">Ring size (5).</param>
        /// <param name="maxStreamBytes">Budget of the kept render streams (500 MiB).</param>
        public static RunRetentionPlan Decide(IReadOnlyList<ClipEntry> runsNewestFirst, int maxRuns = RenderStreamRetention.DefaultMaxStreams,
            long maxStreamBytes = RenderStreamRetention.DefaultMaxTotalBytes)
        {
            if (runsNewestFirst == null) throw new ArgumentNullException(nameof(runsNewestFirst));
            if (maxRuns < 1) throw new ArgumentOutOfRangeException(nameof(maxRuns));
            var delete = new List<string>();
            var evict = new List<string>();
            int kept = Math.Min(maxRuns, runsNewestFirst.Count);
            for (int i = kept; i < runsNewestFirst.Count; i++) delete.Add(runsNewestFirst[i].Id);
            var sizes = new long[kept];
            for (int i = 0; i < kept; i++) sizes[i] = runsNewestFirst[i].HasRenderStream || runsNewestFirst[i].HasEvents ? runsNewestFirst[i].StreamBytes : 0;
            int keepStreams = kept == 0 ? 0 : RenderStreamRetention.CountToKeep(sizes, maxRuns, maxStreamBytes);
            for (int i = keepStreams; i < kept; i++)
            {
                if (runsNewestFirst[i].HasRenderStream || runsNewestFirst[i].HasEvents) evict.Add(runsNewestFirst[i].Id);
            }
            return new RunRetentionPlan(delete, evict);
        }
    }
}
