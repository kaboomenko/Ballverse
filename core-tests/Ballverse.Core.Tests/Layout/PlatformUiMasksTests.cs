using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core.Layout;
using Newtonsoft.Json;
using Xunit;

namespace Ballverse.Core.Tests.Layout
{
    public class PlatformUiMasksTests
    {
        static string ReadTestData(string name)
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Layout", name));
        }

        static KeyValuePair<string, IEnumerable<FloatRect>> P(string id, params FloatRect[] rects)
        {
            return new KeyValuePair<string, IEnumerable<FloatRect>>(id, rects);
        }

        [Fact]
        public void DefaultsCoverTheThreePlatforms()
        {
            PlatformUiMasks d = PlatformUiMasks.Default;
            Assert.Equal(new[] { "reels", "shorts", "tiktok" }, d.PlatformIds);
            foreach (string id in d.PlatformIds) Assert.Equal(4, d.Get(id).Count);
            Assert.True(d.Contains("TikTok"));
            Assert.False(d.Contains("snapchat"));
            Assert.Empty(d.Get("snapchat"));
            Assert.Empty(d.Get(null));
        }

        [Fact]
        public void ToPixelsConvertsTikTokMasks()
        {
            RectUnion u = PlatformUiMasks.Default.ToPixels(1080, 1920, new[] { PlatformUiMasks.TikTok });
            Assert.Equal(4, u.Count);
            Assert.Equal(new IntRect(0, 0, 1080, 125), u[0]);
            Assert.Equal(IntRect.FromEdges(929, 634, 1080, 1690), u[1]);
            Assert.Equal(IntRect.FromEdges(0, 1478, 929, 1766), u[2]);
            Assert.Equal(IntRect.FromEdges(0, 1766, 1080, 1920), u[3]);
        }

        [Fact]
        public void UnionQueryOverAllPlatformsIsOrderIndependent()
        {
            RectUnion all = PlatformUiMasks.Default.ToPixels(1080, 1920);
            Assert.Equal(12, all.Count);
            RectUnion reordered = PlatformUiMasks.Default.ToPixels(1080, 1920, new[] { "tiktok", "shorts", "REELS", "unknown", "" });
            Assert.Equal(all.Rects, reordered.Rects);
            // A point in the right rail of any platform is covered; the frame center is not.
            Assert.True(all.Intersects(new IntRect(1050, 1000, 1, 1)));
            Assert.False(all.Intersects(new IntRect(540, 960, 1, 1)));
            // Top bars of all platforms overlap; union counts them once.
            Assert.Equal(1080L * 125, all.IntersectionArea(new IntRect(0, 0, 1080, 125)));
            Assert.Equal(0, PlatformUiMasks.Default.ToPixels(1080, 1920, new string[0]).Count);
        }

        [Fact]
        public void JsonRoundTripIsStable()
        {
            string json = PlatformUiMasks.Default.ToJson();
            PlatformUiMasks back = PlatformUiMasks.FromJson(json);
            Assert.Equal(PlatformUiMasks.Default.PlatformIds, back.PlatformIds);
            foreach (string id in back.PlatformIds) Assert.Equal(PlatformUiMasks.Default.Get(id), back.Get(id));
            Assert.Equal(json, back.ToJson());
            Assert.Contains("\"platforms\"", json);
            Assert.Contains("\"version\": 1", json);
            Assert.True(json.IndexOf("\"reels\"", StringComparison.Ordinal) < json.IndexOf("\"tiktok\"", StringComparison.Ordinal));
        }

        [Fact]
        public void RemoteConfigOverrideReplacesPlatformsAndSanitizes()
        {
            PlatformUiMasks remote = PlatformUiMasks.FromJson(ReadTestData("platform_safe_zones_override.json"));
            Assert.Equal(new[] { "snapchat", "tiktok" }, remote.PlatformIds);
            Assert.Equal(2, remote.Get("tiktok").Count);
            // Negative width and NaN dropped; out-of-range rect clipped to the frame.
            IReadOnlyList<FloatRect> snap = remote.Get("snapchat");
            Assert.Equal(2, snap.Count);
            Assert.True(snap[1].ApproximatelyEquals(FloatRect.FromEdges(0f, 0.95f, 1f, 1f), 1e-6f));

            PlatformUiMasks merged = PlatformUiMasks.Default.WithOverrides(remote);
            Assert.Equal(new[] { "reels", "shorts", "snapchat", "tiktok" }, merged.PlatformIds);
            Assert.Equal(new FloatRect(0f, 0f, 1f, 0.1f), merged.Get("tiktok")[0]);
            Assert.Equal(PlatformUiMasks.Default.Get("reels"), merged.Get("reels"));
        }

        [Fact]
        public void InvalidInputThrows()
        {
            Assert.Throws<ArgumentException>(() => new PlatformUiMasks(new[] { P(" ") }));
            Assert.Throws<ArgumentException>(() => new PlatformUiMasks(new[] { P("a"), P("A ") }));
            Assert.Throws<ArgumentNullException>(() => new PlatformUiMasks(null));
            Assert.Throws<ArgumentNullException>(() => PlatformUiMasks.FromJson(null));
            Assert.ThrowsAny<JsonException>(() => PlatformUiMasks.FromJson("{ not json"));
            Assert.ThrowsAny<JsonException>(() => PlatformUiMasks.FromJson("{\"version\":1}"));
            Assert.Throws<ArgumentNullException>(() => PlatformUiMasks.Default.WithOverrides(null));
        }

        [Fact]
        public void NullRectListIsEmptyPlatform()
        {
            var m = new PlatformUiMasks(new[] { new KeyValuePair<string, IEnumerable<FloatRect>>("x", null) });
            Assert.True(m.Contains("x"));
            Assert.Empty(m.Get("x"));
            PlatformUiMasks fromJson = PlatformUiMasks.FromJson("{\"platforms\":{\"y\":null}}");
            Assert.Empty(fromJson.Get("y"));
        }
    }
}
