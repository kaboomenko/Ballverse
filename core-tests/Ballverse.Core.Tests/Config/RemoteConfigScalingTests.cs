using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Ballverse.Core.Config;
using Xunit;
using static Ballverse.Core.Tests.Config.ConfigTestUtil;

namespace Ballverse.Core.Tests.Config
{
    /// <summary>
    /// Open-map parsing scales linearly (spec §9.3: payloads are untrusted, a quadratic duplicate check made 20 000 entries take
    /// ≈ 3 s per map). Timing-based, so it runs in <see cref="SerialMeasurementCollection"/>.
    /// </summary>
    [Collection(SerialMeasurementCollection.Name)]
    [Trait("Category", "Perf")]
    public class RemoteConfigScalingTests
    {
        // n vs 8n entries. Linear parsing scales by ≈ 8 (measured 4–8 at these sizes, unloaded); the former O(n²) duplicate check
        // adds ≈ 7.5 ms at n and ≈ 480 ms at 8n, a ratio of ≈ 40 or more. The sizes stay at or below 10 000 entries per map
        // (≈ 28 MB allocated): above that, wall-clock grows faster than the (exactly linear) allocations because of gen2
        // collections and cache misses, which was the noise source of the previous n vs 4n version at 20 000 entries.
        const int N = 1250;
        const int Spread = 8;
        const double MaxRatio = 20.0; // between linear (Spread = 8) and quadratic (Spread² = 64) growth
        const int Attempts = 3;

        [Fact]
        public void LargeOpenMapsParseInLinearTime()
        {
            // tier_overrides and text_templates: the cheapest per-entry maps, so the duplicate check dominates.
            // platform_safe_zones shares the same HashSet-based check and MergeByKey (its rect parsing is allocation-heavy,
            // which would make the timing noisy).
            var small = OpenMaps(N);
            var large = OpenMaps(Spread * N);

            // Correctness at the large size (independent of timing).
            RemoteConfigParser.Parse(small); // warm-up (JIT)
            var r = RemoteConfigParser.Parse(large);
            Single(r, RemoteConfigIssueKind.DuplicateEntry, RemoteConfigIssueSeverity.Warning);
            Assert.Equal(Spread * N, r.Values.TierOverrides.Count);
            Assert.Equal(Spread * N + 2, r.Values.TextTemplates.Count);
            Assert.Equal(new[] { "p1", "reels", "shorts", "tiktok" }, r.Values.PlatformSafeZones.Select(p => p.Platform));

            // Timing: best of five interleaved runs per size, each after a full collection. A real quadratic parser exceeds the
            // bound on every attempt; a transient stall (another process, a page-in) does not survive three attempts.
            var ratios = new List<string>();
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                double tSmall = double.MaxValue, tLarge = double.MaxValue;
                for (int i = 0; i < 5; i++)
                {
                    tSmall = Math.Min(tSmall, TimeParse(small));
                    tLarge = Math.Min(tLarge, TimeParse(large));
                }
                double ratio = tLarge / Math.Max(tSmall, 1e-3);
                if (ratio < MaxRatio) return;
                ratios.Add(ratio.ToString("F1", CultureInfo.InvariantCulture) + "× (" + tSmall.ToString("F1", CultureInfo.InvariantCulture)
                    + " ms → " + tLarge.ToString("F1", CultureInfo.InvariantCulture) + " ms)");
            }
            Assert.Fail(Spread + "× entries took " + string.Join(", ", ratios) + " as long on " + Attempts
                + " attempts; expected ≈ " + Spread + "× for linear parsing, bound " + MaxRatio.ToString(CultureInfo.InvariantCulture) + "×");
        }

        static double TimeParse(Dictionary<string, string> input)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var sw = Stopwatch.StartNew();
            RemoteConfigParser.Parse(input);
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }

        static Dictionary<string, string> OpenMaps(int n)
        {
            var tiers = new StringBuilder("{");
            var texts = new StringBuilder("{");
            for (int i = 0; i < n; i++)
            {
                string sep = i == 0 ? "" : ",";
                string id = i.ToString(CultureInfo.InvariantCulture);
                tiers.Append(sep).Append("\"model-").Append(id).Append("\":\"C\"");
                texts.Append(sep).Append("\"l").Append(id).Append("\":[{\"id\":\"a\",\"text\":\"b\"}]");
            }
            tiers.Append('}');
            texts.Append('}');
            return new Dictionary<string, string>
            {
                { RemoteConfigKeys.TierOverrides, tiers.ToString() },
                { RemoteConfigKeys.TextTemplates, texts.ToString() },
                { RemoteConfigKeys.PlatformSafeZones, "{\"p1\":[{\"x\":0,\"y\":0,\"w\":1,\"h\":0.1}],\"P1\":[],\"tiktok\":[]}" },
            };
        }
    }
}
