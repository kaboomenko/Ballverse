using System;
using System.Collections.Generic;
using System.Linq;
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

        // Same failure class in Layout: PlatformUiMasks keeps an in-frame rect bit-exact only if its right/bottom edge equals
        // the clamped edge. Evaluated in double (as Mono did), 0.85 + 0.15 ≠ 1, so the defaults were rebuilt with a width
        // 2 ulps smaller under Mono. The edge is now rounded to float explicitly, which equals the IEEE single sum.
        [Theory]
        [MemberData(nameof(DefaultRects))]
        public void LayoutMasksOfDefaults_EdgeRoundedToFloatEqualsSingleSum(string name, float x, float y, float w, float h)
        {
            Assert.Equal(x + w, (float)((double)x + w));
            Assert.Equal(y + h, (float)((double)y + h));
            Assert.True(name.Length > 0);
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
