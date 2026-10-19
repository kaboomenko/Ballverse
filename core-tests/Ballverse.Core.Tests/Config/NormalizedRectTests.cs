using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ballverse.Core.Config;
using Ballverse.Core.Integration;
using Ballverse.Core.Layout;
using Xunit;
using static Ballverse.Core.Tests.Config.ConfigTestUtil;

namespace Ballverse.Core.Tests.Config
{
    /// <summary>
    /// <see cref="NormalizedRect"/> validation independent of the runtime's float evaluation precision. CI (Unity 6, Mono
    /// editor) failed every Services EditMode test with a TypeInitializationException of RemoteConfigValues: the check
    /// <c>x + w &lt;= 1f</c> rejected built-in defaults such as (0.85, _, 0.15, _) and (0, 0.8, 1, 0.2) because Mono evaluated
    /// the float sums at double precision (1.0000000298 / 1.0000000149), while CoreCLR rounds them to exactly 1f. These tests
    /// evaluate the validator with the sums in double — what Mono computes — so the failure mode is visible on CoreCLR.
    /// </summary>
    public class NormalizedRectTests
    {
        /// <summary>Every rect of the built-in config defaults (spec §3.3 offline masks).</summary>
        public static IEnumerable<object[]> DefaultRects()
        {
            foreach (PlatformSafeZone zone in RemoteConfigDefaults.PlatformSafeZones())
            {
                for (int i = 0; i < zone.Rects.Count; i++)
                {
                    NormalizedRect r = zone.Rects[i];
                    yield return new object[] { zone.Platform + " #" + i, r.X, r.Y, r.W, r.H };
                }
            }
        }

        /// <summary>The former rule <c>x + w &lt;= 1f &amp;&amp; y + h &lt;= 1f</c> with the sums evaluated in double, as Mono did.</summary>
        static bool FormerCheckEvaluatedInDouble(float x, float y, float w, float h)
        {
            return x >= 0f && y >= 0f && w > 0f && h > 0f && (double)x + w <= 1.0 && (double)y + h <= 1.0;
        }

        // The probe of the CI failure: the former check, evaluated the way Mono evaluates it, rejects built-in defaults.
        [Fact]
        public void FormerCheckEvaluatedInDouble_RejectsBuiltInDefaults()
        {
            var rejected = DefaultRects().Where(a => !FormerCheckEvaluatedInDouble((float)a[1], (float)a[2], (float)a[3], (float)a[4]))
                .Select(a => (string)a[0]).ToList();
            Assert.Contains("reels #1", rejected);  // 0.85 + 0.15
            Assert.Contains("shorts #2", rejected); // 0.8 + 0.2
            Assert.Contains("tiktok #2", rejected);
        }

        [Theory]
        [MemberData(nameof(DefaultRects))]
        public void BuiltInDefault_IsValidWithSumsEvaluatedInDouble_AndKeptBitExact(string name, float x, float y, float w, float h)
        {
            Assert.True(NormalizedRect.Problem((double)x, (double)y, (double)w, (double)h, out string param) == null, name + ": " + param);
            Assert.True(NormalizedRect.IsValid(x, y, w, h), name);
            Assert.True(NormalizedRect.IsInsideExactly(x, y, w, h), name);
            var copy = new NormalizedRect(x, y, w, h);
            Assert.Equal(BitConverter.SingleToInt32Bits(w), BitConverter.SingleToInt32Bits(copy.W));
            Assert.Equal(BitConverter.SingleToInt32Bits(h), BitConverter.SingleToInt32Bits(copy.H));
        }

        [Fact]
        public void DefaultSnapshotBuilds()
        {
            Assert.Equal(new[] { "reels", "shorts", "tiktok" }, RemoteConfigValues.Defaults.PlatformSafeZones.Select(p => p.Platform));
            Assert.All(RemoteConfigValues.Defaults.PlatformSafeZones, p => Assert.Equal(3, p.Rects.Count));
        }

        // Pairs of two- and three-decimal values that end exactly on the edge: accepted on every runtime, stored unchanged
        // when they fit at float precision, and always inside the frame at float precision.
        [Fact]
        public void EdgeSumsOfDecimalValues_AreAcceptedAndStayInside()
        {
            foreach (int scale in new[] { 100, 1000 })
            {
                for (int j = 1; j < scale; j++)
                {
                    float origin = (float)((double)j / scale), size = (float)((double)(scale - j) / scale);
                    var r = new NormalizedRect(origin, 0.5f, size, 0.5f);
                    Assert.True((float)((double)r.X + r.W) <= 1f, origin + " + " + size);
                    if ((float)((double)origin + size) <= 1f) Assert.Equal(size, r.W);
                    Assert.True(r.W > 0f && size - r.W <= 1e-6f);
                    var t = new NormalizedRect(0.25f, origin, 0.5f, size);
                    Assert.True((float)((double)t.Y + t.H) <= 1f);
                }
            }
        }

        [Fact]
        public void OvershootWithinTolerance_IsSnappedToTheEdge()
        {
            var r = new NormalizedRect(0.5f, 0.25f, 0.500004f, 0.750003f);
            Assert.Equal(0.5f, r.X);
            Assert.True((float)((double)r.X + r.W) <= 1f);
            Assert.True((float)((double)r.Y + r.H) <= 1f);
            Assert.Equal(0.5f, r.W, 6);
            Assert.Equal(0.75f, r.H, 6);
            Assert.True(NormalizedRect.IsInsideExactly(r.X, r.Y, r.W, r.H));
            var full = new NormalizedRect(0f, 0f, 1.000005f, 1f);
            Assert.Equal(1f, full.W);
        }

        [Fact]
        public void OvershootBeyondTolerance_IsRejected_NamingTheSum()
        {
            var e = Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedRect(0.5f, 0f, 0.6f, 0.1f));
            Assert.Equal("w", e.ParamName);
            Assert.Contains("x + w must be <= 1, got 0.5 + 0.6 = 1.1", e.Message);
            e = Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedRect(0f, 0.5f, 0.1f, 0.50002f));
            Assert.Equal("h", e.ParamName);
            Assert.Contains("y + h must be <= 1, got 0.5 + 0.50002 = ", e.Message);
            Assert.False(NormalizedRect.IsValid(0f, 0.5f, 0.1f, 0.50002f));
        }

        [Theory]
        [InlineData(-0.1f, 0f, 0.5f, 0.1f, "x", "x must be >= 0, got -0.1")]
        [InlineData(0f, -0.25f, 0.5f, 0.1f, "y", "y must be >= 0, got -0.25")]
        [InlineData(0f, 0f, 0f, 0.1f, "w", "w must be > 0, got 0")]
        [InlineData(0f, 0f, 0.5f, -1f, "h", "h must be > 0, got -1")]
        [InlineData(float.NaN, 0f, 0.5f, 0.1f, "x", "x must be finite, got NaN")]
        [InlineData(0f, float.PositiveInfinity, 0.5f, 0.1f, "y", "y must be finite")]
        [InlineData(0f, 0f, float.NaN, 0.1f, "w", "w must be finite")]
        [InlineData(0f, 0f, 0.5f, float.NegativeInfinity, "h", "h must be finite")]
        [InlineData(1f, 0f, 0.000001f, 0.1f, "x", "x must be < 1, got 1")]
        [InlineData(0f, 1f, 0.5f, 0.000001f, "y", "y must be < 1, got 1")]
        public void InvalidValue_MessageNamesIt(float x, float y, float w, float h, string param, string message)
        {
            Assert.False(NormalizedRect.IsValid(x, y, w, h));
            var e = Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedRect(x, y, w, h));
            Assert.Equal(param, e.ParamName);
            Assert.Contains(message, e.Message);
        }

        // The parser keeps rects that fit at float precision bit for bit and clamps the others; either way the result fits.
        [Fact]
        public void ParsedRects_FitAtFloatPrecision()
        {
            var r = Parse(RemoteConfigKeys.PlatformSafeZones,
                "{\"p\":[{\"x\":0.85,\"y\":0.4,\"w\":0.15,\"h\":0.4},{\"x\":0,\"y\":0.8,\"w\":1,\"h\":0.2},"
                + "{\"x\":0.5,\"y\":0.1,\"w\":0.500004,\"h\":0.1},{\"x\":0.33,\"y\":0.2,\"w\":0.67,\"h\":0.1}]}");
            Assert.True(r.Values.TryGetSafeZones("p", out var rects));
            Assert.Equal(4, rects.Count);
            foreach (NormalizedRect rect in rects) Assert.True(NormalizedRect.IsInsideExactly(rect.X, rect.Y, rect.W, rect.H));
            Assert.Equal(0.15f, rects[0].W);
            Assert.Equal(0.2f, rects[1].H);
            Single(r, RemoteConfigIssueKind.OutOfRange, RemoteConfigIssueSeverity.Warning); // 0.500004 overshoots by 4e-6
        }

        // Same failure class in Layout: PlatformUiMasks.Sanitize keeps an in-frame rect bit-exact only if its right/bottom
        // edge equals the clamped edge. The former keep-check `rr == r.X + r.Width`, with the sum evaluated in double as Mono
        // did (0.85 + 0.15 = 1.0000000298 ≠ 1), rebuilt most built-in defaults 1-2 ulps narrower. CoreCLR cannot evaluate the
        // product code that way, so the revert guard is the static check below; this shows what it guards against.
        static bool FormerLayoutKeepCheckEvaluatedInDouble(float x, float y, float w, float h)
        {
            float rr = Math.Min(Math.Max((float)((double)x + w), 0f), 1f); // ClampF's float parameter narrows the sum
            float b = Math.Min(Math.Max((float)((double)y + h), 0f), 1f);
            return rr == (double)x + w && b == (double)y + h;
        }

        [Fact]
        public void FormerLayoutKeepCheckEvaluatedInDouble_RebuildsBuiltInDefaults()
        {
            var rebuilt = DefaultRects().Where(a => !FormerLayoutKeepCheckEvaluatedInDouble((float)a[1], (float)a[2], (float)a[3], (float)a[4]))
                .Select(a => (string)a[0]).ToList();
            Assert.Contains("reels #1", rebuilt);  // 0.85 + 0.15
            Assert.Contains("shorts #2", rebuilt); // 0.8 + 0.2
        }

        // The parser's FitSize had the same raw sum (`origin + size > 1f`): evaluated in double it shrank a clamped 0.15 at
        // x = 0.85 by 2 ulps. The product keeps 0.15f bit-exact (on Mono via the explicit float rounding; guarded statically).
        static float FormerFitSizeEvaluatedInDouble(float origin, float size)
        {
            for (int i = 0; i < 8 && size > 0f && (double)origin + size > 1.0; i++)
            {
                size = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(size) - 1);
            }
            return size;
        }

        [Fact]
        public void ClampedRectEndingOnTheEdge_KeepsItsFloatSize()
        {
            Assert.NotEqual(BitConverter.SingleToInt32Bits(0.15f), BitConverter.SingleToInt32Bits(FormerFitSizeEvaluatedInDouble(0.85f, 0.15f)));
            var r = Parse(RemoteConfigKeys.PlatformSafeZones, "{\"p\":[{\"x\":0.85,\"y\":0.8,\"w\":0.150004,\"h\":0.2000003}]}");
            Assert.True(r.Values.TryGetSafeZones("p", out var rects));
            Assert.Equal(BitConverter.SingleToInt32Bits(0.15f), BitConverter.SingleToInt32Bits(rects[0].W));
            Assert.Equal(BitConverter.SingleToInt32Bits(0.2f), BitConverter.SingleToInt32Bits(rects[0].H));
        }

        /// <summary>
        /// Static check (CoreCLR rounds every float operation to single, so no runtime test here can see Mono's wider
        /// evaluation; the real-Mono probe is manual, see the track notes): non-Sim Core must not compare an unwidened sum
        /// against an exact float limit (<c>a + b &lt;= 1f</c>, <c>0f &gt; a + b</c>), and Config/Layout must not test a
        /// value for equality with an unwidened sum (<c>rr == r.X + r.Width</c>). Write <c>(float)((double)a + b)</c> (the
        /// IEEE single sum on every runtime) or compare in double. Sim and Replay are out of scope (determinism per binary).
        /// Each former defect — NormalizedRect.IsValid, PlatformUiMasks.Sanitize, RemoteConfigParser.FitSize — matches.
        /// </summary>
        [Fact]
        public void CoreSources_HaveNoUnwidenedFloatSumAgainstAnExactLimit()
        {
            const string Sum = @"(?<!\(double\))(?<![\w.])[\w.]+\s*\+\s*[\w.]+";
            var againstLimit = new Regex(Sum + @"\s*(<=|>=|<|>|==|!=)\s*[01](\.0)?f\b|\b[01](\.0)?f\s*(<=|>=|<|>|==|!=)\s*" + Sum);
            var equalsSum = new Regex(@"(==|!=)\s*" + Sum + "|" + Sum + @"\s*(==|!=)");
            Assert.Matches(againstLimit, "&& x + w <= 1f && y + h <= 1f;");
            Assert.Matches(againstLimit, "size > 0f && origin + size > 1f; i++");
            Assert.Matches(againstLimit, "if ((float)x + w > 1f) return;");
            Assert.Matches(equalsSum, "t == r.Y && rr == r.X + r.Width && b == r.Y + r.Height;");
            Assert.DoesNotMatch(againstLimit, "return (float)((double)origin + size) <= 1f;");
            Assert.DoesNotMatch(equalsSum, "bool inside = l == r.X && t == r.Y && rr == right && b == bottom;");

            string core = FindCoreSourceDirectory();
            int files = 0;
            foreach (string path in Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories))
            {
                string rel = path.Substring(core.Length + 1).Replace('\\', '/');
                if (rel.StartsWith("Sim/", StringComparison.Ordinal) || rel.StartsWith("Replay/", StringComparison.Ordinal)) continue;
                bool configOrLayout = rel.StartsWith("Config/", StringComparison.Ordinal) || rel.StartsWith("Layout/", StringComparison.Ordinal);
                files++;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = lines[i];
                    int comment = code.IndexOf("//", StringComparison.Ordinal);
                    if (comment >= 0) code = code.Substring(0, comment);
                    Assert.False(againstLimit.IsMatch(code), rel + ":" + (i + 1) + " compares an unwidened sum with an exact float limit: " + lines[i].Trim());
                    Assert.False(configOrLayout && equalsSum.IsMatch(code), rel + ":" + (i + 1) + " tests equality with an unwidened sum: " + lines[i].Trim());
                }
            }
            Assert.True(files >= 50, "found only " + files + " Core sources in " + core);
        }

        static string FindCoreSourceDirectory()
        {
            string d = AppContext.BaseDirectory;
            while (d != null)
            {
                string candidate = Path.Combine(d, "client", "Assets", "Ballverse", "Core");
                if (Directory.Exists(Path.Combine(candidate, "Config"))) return candidate;
                d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            throw new DirectoryNotFoundException("client/Assets/Ballverse/Core not found above " + AppContext.BaseDirectory);
        }

        [Fact]
        public void LayoutMasksOfDefaults_AreBitExactCopies()
        {
            PlatformUiMasks masks = RemoteConfigAdapters.ToPlatformUiMasks(RemoteConfigValues.Defaults);
            foreach (PlatformSafeZone zone in RemoteConfigValues.Defaults.PlatformSafeZones)
            {
                IReadOnlyList<FloatRect> rects = masks.Get(zone.Platform);
                Assert.Equal(zone.Rects.Count, rects.Count);
                for (int i = 0; i < rects.Count; i++)
                {
                    Assert.Equal(BitConverter.SingleToInt32Bits(zone.Rects[i].W), BitConverter.SingleToInt32Bits(rects[i].Width));
                    Assert.Equal(BitConverter.SingleToInt32Bits(zone.Rects[i].H), BitConverter.SingleToInt32Bits(rects[i].Height));
                }
            }
        }
    }
}
