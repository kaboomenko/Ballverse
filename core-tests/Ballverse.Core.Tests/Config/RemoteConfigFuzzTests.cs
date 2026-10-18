using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ballverse.Core.Config;
using Xunit;
using static Ballverse.Core.Tests.Config.ConfigTestUtil;

namespace Ballverse.Core.Tests.Config
{
    /// <summary>Never-throws, determinism and invariants under Rng-generated garbage (spec §9.3: the app must always work).</summary>
    public class RemoteConfigFuzzTests
    {
        const string Alphabet = "{}[]\":,0123456789.-+eEtrufalsnM \\/*xywh_ABCabc\t\n";

        static string Garbage(ref Rng rng)
        {
            int len = rng.NextInt(0, 60);
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
            {
                int pick = rng.NextInt(0, 20);
                if (pick == 0) sb.Append((char)rng.NextInt(0, 0x10000)); // any UTF-16 unit, lone surrogates included
                else sb.Append(Alphabet[rng.NextInt(0, Alphabet.Length)]);
            }
            return sb.ToString();
        }

        /// <summary>A valid raw value corrupted by a few random edits: exercises the deep paths, not only the JSON reader.</summary>
        static string Mutate(string s, ref Rng rng)
        {
            var sb = new StringBuilder(s);
            int edits = rng.NextInt(1, 4);
            for (int e = 0; e < edits; e++)
            {
                int op = rng.NextInt(0, 4);
                int pos = sb.Length == 0 ? 0 : rng.NextInt(0, sb.Length);
                switch (op)
                {
                    case 0:
                        if (sb.Length > 0) sb.Remove(pos, 1);
                        break;
                    case 1:
                        sb.Insert(pos, Alphabet[rng.NextInt(0, Alphabet.Length)]);
                        break;
                    case 2:
                        if (sb.Length > 0) sb[pos] = Alphabet[rng.NextInt(0, Alphabet.Length)];
                        break;
                    default:
                        sb.Length = pos;
                        break;
                }
            }
            return sb.ToString();
        }

        static readonly string[] s_rich =
        {
            "[\"M01\",\"M02\",\"M03\",\"M04\",\"M05\",\"M09\"]",
            "{\"M01\":{\"w1\":1,\"w2\":0.5,\"w3\":2,\"w4\":0.25,\"w5\":0.75},\"orbit\":[1,2,3,4,5]}",
            "{\"A\":{\"candidates\":32,\"ms\":2000},\"C\":{\"ms\":5000}}",
            "{\"SM-A155F\":\"C\",\"Pixel 6a\":\"B\"}",
            "{\"tiktok\":[{\"x\":0.8,\"y\":0.3,\"w\":0.2,\"h\":0.5}],\"x\":[]}",
            "[{\"modeId\":\"ring_escape\",\"params\":{\"rings\":100}}]",
            "[{\"id\":\"ode\",\"name\":\"Ode\",\"notes\":[{\"pitch\":64,\"durWeight\":1,\"velocity\":0.8}]}]",
            "{\"de\":[{\"id\":\"who_wins\",\"text\":\"Welche Kugel gewinnt?\"}]}",
            "{\"720p30\":5000,\"1080p60\":20000}",
            "{\"A\":30,\"B\":60}",
            "17", "true", "-4", "1e3",
        };

        static Dictionary<string, string> RandomInput(ref Rng rng)
        {
            var d = new Dictionary<string, string>();
            var defaults = RemoteConfigWriter.ToRaw(RemoteConfigValues.Defaults);
            int count = rng.NextInt(0, 12);
            for (int i = 0; i < count; i++)
            {
                string key = rng.NextInt(0, 10) == 0 ? Garbage(ref rng) : RemoteConfigKeys.All[rng.NextInt(0, RemoteConfigKeys.All.Count)];
                string value;
                switch (rng.NextInt(0, 4))
                {
                    case 0: value = Garbage(ref rng); break;
                    case 1: value = Mutate(defaults[rng.NextInt(0, defaults.Count)].Value, ref rng); break;
                    case 2: value = Mutate(s_rich[rng.NextInt(0, s_rich.Length)], ref rng); break;
                    default: value = s_rich[rng.NextInt(0, s_rich.Length)]; break;
                }
                d[key] = value;
            }
            return d;
        }

        static void AssertInvariants(RemoteConfigValues v)
        {
            Assert.NotNull(v);
            Assert.Equal(AllCodes(), v.GalleryOrder.OrderBy(c => (int)c));
            Assert.Equal(v.FreeModes.Count, v.FreeModes.Distinct().Count());
            Assert.InRange(v.InterstitialEveryNGames, 1, 20);
            Assert.InRange(v.InterstitialMinIntervalSec, 0, 600);
            Assert.InRange(v.InterstitialGraceGames, 0, 50);
            Assert.InRange(v.RewardedWaitTimeoutSec, 1, 30);
            Assert.InRange(v.RewardedRetryWindowMin, 0, 60);
            Assert.InRange(v.PaidModeTrialSeconds, 10, 120);
            Assert.InRange(v.RatePromptAfterExports, 1, 20);
            foreach (var res in new[] { ExportResolution.P720, ExportResolution.P1080 })
            {
                foreach (int fps in new[] { 30, 60 }) Assert.InRange(v.ExportBitrates.GetKbps(res, fps), 1000, 50000);
            }
            foreach (var t in new[] { DeviceTier.A, DeviceTier.B, DeviceTier.C })
            {
                Assert.True(RemoteConfigLimits.IsValidFps(v.ExportDefaultFpsByTier.Get(t)));
                Assert.InRange(v.GenerationBudgetByTier.Get(t).Candidates, 1, 64);
                Assert.InRange(v.GenerationBudgetByTier.Get(t).Milliseconds, 100, 10000);
            }
            foreach (var p in v.PlatformSafeZones)
            {
                foreach (var r in p.Rects) Assert.True(NormalizedRect.IsValid(r.X, r.Y, r.W, r.H));
            }
        }

        [Fact]
        public void NeverThrowsOnRandomGarbage()
        {
            var rng = Rng.FromSeed(0xBADC0FFEEUL, 7);
            for (int iter = 0; iter < 3000; iter++)
            {
                var input = RandomInput(ref rng);
                var result = RemoteConfigParser.Parse(input);
                AssertInvariants(result.Values);
                Assert.DoesNotContain(result.Issues, i => i.Kind == RemoteConfigIssueKind.InternalError);
                foreach (var issue in result.Issues) Assert.False(string.IsNullOrEmpty(issue.Message));
            }
        }

        [Fact]
        public void ParsedValuesAreIdempotentThroughWriter()
        {
            var rng = Rng.FromSeed(0x1DE4UL, 3);
            for (int iter = 0; iter < 1000; iter++)
            {
                var first = RemoteConfigParser.Parse(RandomInput(ref rng)).Values;
                var raw = RawDictionary(first);
                var second = RemoteConfigParser.Parse(raw);
                Assert.Equal(raw, RawDictionary(second.Values));
                // Only the advisory narrowing warning may reappear; everything else was normalized away.
                Assert.All(second.Issues, i => Assert.Equal(RemoteConfigIssueKind.NarrowedFreeModes, i.Kind));
            }
        }

        [Fact]
        public void SameInputSameOutput()
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                var a = Rng.FromSeed(seed, 11);
                var b = Rng.FromSeed(seed, 11);
                var ra = RemoteConfigParser.Parse(RandomInput(ref a));
                var rb = RemoteConfigParser.Parse(RandomInput(ref b));
                Assert.Equal(ra.Issues.Select(i => i.ToString()), rb.Issues.Select(i => i.ToString()));
                Assert.Equal(RemoteConfigWriter.ToRaw(ra.Values), RemoteConfigWriter.ToRaw(rb.Values));
            }
        }

        [Fact]
        public void OutputIndependentOfDictionaryInsertionOrder()
        {
            var rng = Rng.FromSeed(42UL, 5);
            for (int iter = 0; iter < 200; iter++)
            {
                var pairs = RandomInput(ref rng).ToList();
                var forward = new Dictionary<string, string>();
                foreach (var kv in pairs) forward.Add(kv.Key, kv.Value);
                var backward = new Dictionary<string, string>();
                for (int i = pairs.Count - 1; i >= 0; i--) backward.Add(pairs[i].Key, pairs[i].Value);
                var sorted = new SortedDictionary<string, string>(forward, StringComparer.Ordinal);

                var rf = RemoteConfigParser.Parse(forward);
                var rb = RemoteConfigParser.Parse(backward);
                var rs = RemoteConfigParser.Parse(sorted);
                Assert.Equal(rf.Issues.Select(i => i.ToString()), rb.Issues.Select(i => i.ToString()));
                Assert.Equal(rf.Issues.Select(i => i.ToString()), rs.Issues.Select(i => i.ToString()));
                Assert.Equal(RemoteConfigWriter.ToRaw(rf.Values), RemoteConfigWriter.ToRaw(rb.Values));
                Assert.Equal(RemoteConfigWriter.ToRaw(rf.Values), RemoteConfigWriter.ToRaw(rs.Values));
            }
        }

        [Fact]
        public void DeepNestingIsRejectedNotCrashing()
        {
            string deep = new string('[', 5000) + new string(']', 5000);
            foreach (var key in RemoteConfigKeys.All)
            {
                var r = Parse(key, deep);
                AssertInvariants(r.Values);
                Assert.Single(r.Issues);
                Assert.Equal(RemoteConfigIssueSeverity.Error, r.Issues[0].Severity);
            }
        }

        [Fact]
        public void HostileDictionaryDoesNotThrow()
        {
            var r = RemoteConfigParser.Parse(new ThrowingDictionary());
            AssertInvariants(r.Values);
            Assert.Contains(r.Issues, i => i.Kind == RemoteConfigIssueKind.InternalError);
            Assert.Equal(RawDictionary(RemoteConfigValues.Defaults), RawDictionary(r.Values));
        }

        sealed class ThrowingDictionary : IReadOnlyDictionary<string, string>
        {
            public string this[string key] => throw new InvalidOperationException("boom");
            public IEnumerable<string> Keys => throw new InvalidOperationException("boom");
            public IEnumerable<string> Values => throw new InvalidOperationException("boom");
            public int Count => throw new InvalidOperationException("boom");
            public bool ContainsKey(string key) => throw new InvalidOperationException("boom");
            public bool TryGetValue(string key, out string value) => throw new InvalidOperationException("boom");
            public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => throw new InvalidOperationException("boom");
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
