using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core.Layout;
using Newtonsoft.Json.Linq;
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
            foreach (string id in d.PlatformIds) Assert.Equal(3, d.Get(id).Count);
            Assert.True(d.Contains("TikTok"));
            Assert.False(d.Contains("snapchat"));
            Assert.Empty(d.Get("snapchat"));
            Assert.Empty(d.Get(null));
        }

        [Fact]
        public void DefaultsEqualConfigOfflineDefaults()
        {
            // Same numbers as the Config module's RemoteConfigDefaults.PlatformSafeZones (single source of geometry).
            PlatformUiMasks d = PlatformUiMasks.Default;
            Assert.Equal(new[] { new FloatRect(0f, 0f, 1f, 0.08f), new FloatRect(0.85f, 0.4f, 0.15f, 0.4f), new FloatRect(0f, 0.8f, 1f, 0.2f) },
                d.Get("tiktok"));
            Assert.Equal(new[] { new FloatRect(0f, 0f, 1f, 0.07f), new FloatRect(0.85f, 0.45f, 0.15f, 0.375f), new FloatRect(0f, 0.825f, 1f, 0.175f) },
                d.Get("reels"));
            Assert.Equal(new[] { new FloatRect(0f, 0f, 1f, 0.07f), new FloatRect(0.85f, 0.425f, 0.15f, 0.375f), new FloatRect(0f, 0.8f, 1f, 0.2f) },
                d.Get("shorts"));
        }

        [Fact]
        public void ToPixelsConvertsTikTokMasks()
        {
            RectUnion u = PlatformUiMasks.Default.ToPixels(1080, 1920, new[] { PlatformUiMasks.TikTok });
            Assert.Equal(3, u.Count);
            Assert.Equal(new IntRect(0, 0, 1080, 154), u[0]); // 0.08 * 1920 = 153.6
            Assert.Equal(IntRect.FromEdges(918, 768, 1080, 1536), u[1]);
            Assert.Equal(IntRect.FromEdges(0, 1536, 1080, 1920), u[2]);
        }

        [Fact]
        public void UnionQueryOverAllPlatformsIsOrderIndependent()
        {
            RectUnion all = PlatformUiMasks.Default.ToPixels(1080, 1920);
            Assert.Equal(9, all.Count);
            RectUnion reordered = PlatformUiMasks.Default.ToPixels(1080, 1920, new[] { "tiktok", "shorts", "REELS", "unknown", "", null });
            Assert.Equal(all.Rects, reordered.Rects);
            Assert.True(all.Intersects(new IntRect(1050, 1000, 1, 1)));
            Assert.False(all.Intersects(new IntRect(540, 960, 1, 1)));
            // Top bars of all platforms overlap; union counts them once.
            Assert.Equal(1080L * 154, all.IntersectionArea(new IntRect(0, 0, 1080, 154)));
            Assert.Equal(0, PlatformUiMasks.Default.ToPixels(1080, 1920, new string[0]).Count);
        }

        [Fact]
        public void NormalizedUnionMatchesPixelUnion()
        {
            IReadOnlyList<FloatRect> tiktok = PlatformUiMasks.Default.Union(new[] { "TIKTOK" });
            Assert.Equal(PlatformUiMasks.Default.Get("tiktok"), tiktok);
            IReadOnlyList<FloatRect> all = PlatformUiMasks.Default.Union();
            Assert.Equal(9, all.Count);
            Assert.Equal(PlatformUiMasks.Default.ToPixels(720, 1280).Rects, RectUnion.FromNormalized(all, 720, 1280).Rects);
        }

        [Fact]
        public void ConfigPayloadShapeBuildsTheModel()
        {
            // The Config module owns platform_safe_zones and its root-map JSON shape; the app hands its parsed zones to the
            // constructor. This mirrors that adapter with the payload shape of Config's sample.
            JObject root = JObject.Parse(ReadTestData("platform_safe_zones_config_shape.json"));
            var platforms = new List<KeyValuePair<string, IEnumerable<FloatRect>>>();
            foreach (JProperty p in root.Properties())
            {
                var rects = new List<FloatRect>();
                foreach (JObject r in (JArray)p.Value)
                {
                    rects.Add(new FloatRect((float)r["x"], (float)r["y"], (float)r["w"], (float)r["h"]));
                }
                platforms.Add(new KeyValuePair<string, IEnumerable<FloatRect>>(p.Name, rects));
            }
            var remote = new PlatformUiMasks(platforms);
            Assert.Equal(new[] { "snapchat", "tiktok" }, remote.PlatformIds);
            Assert.Equal(new FloatRect(0.86f, 0.38f, 0.14f, 0.42f), remote.Get("tiktok")[1]);

            PlatformUiMasks merged = PlatformUiMasks.Default.WithOverrides(remote);
            Assert.Equal(new[] { "reels", "shorts", "snapchat", "tiktok" }, merged.PlatformIds);
            Assert.Equal(new FloatRect(0.86f, 0.38f, 0.14f, 0.42f), merged.Get("tiktok")[1]);
            Assert.Equal(PlatformUiMasks.Default.Get("reels"), merged.Get("reels"));
        }

        [Fact]
        public void InvalidRectsAreDroppedOrClipped()
        {
            var m = new PlatformUiMasks(new[]
            {
                P("snap", new FloatRect(0f, 0.9f, 1f, 0.1f), new FloatRect(0.5f, 0.5f, -0.1f, 0.2f),
                    new FloatRect(-0.5f, 0.95f, 2f, 0.5f), new FloatRect(float.NaN, 0f, 0.1f, 0.1f)),
            });
            IReadOnlyList<FloatRect> snap = m.Get("snap");
            Assert.Equal(2, snap.Count);
            Assert.True(snap[1].ApproximatelyEquals(FloatRect.FromEdges(0f, 0.95f, 1f, 1f), 1e-6f));
        }

        [Fact]
        public void ReturnedCollectionsCannotMutateTheModel()
        {
            IReadOnlyList<FloatRect> tiktok = PlatformUiMasks.Default.Get("tiktok");
            Assert.False(tiktok is FloatRect[]);
            var asList = (IList<FloatRect>)tiktok;
            Assert.True(asList.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => asList[0] = new FloatRect(0f, 0f, 1f, 0.9f));
            Assert.Equal(new FloatRect(0f, 0f, 1f, 0.08f), PlatformUiMasks.Default.Get("tiktok")[0]);

            IReadOnlyList<string> ids = PlatformUiMasks.Default.PlatformIds;
            Assert.False(ids is string[]);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)ids)[0] = "zzz");
            Assert.True(PlatformUiMasks.Default.Contains("reels"));

            // The constructor copies its input.
            var src = new[] { new FloatRect(0f, 0f, 1f, 0.1f) };
            var m = new PlatformUiMasks(new[] { P("x", src) });
            src[0] = new FloatRect(0f, 0f, 1f, 0.5f);
            Assert.Equal(new FloatRect(0f, 0f, 1f, 0.1f), m.Get("x")[0]);
        }

        [Fact]
        public void InvalidInputThrows()
        {
            Assert.Throws<ArgumentException>(() => new PlatformUiMasks(new[] { P(" ") }));
            Assert.Throws<ArgumentException>(() => new PlatformUiMasks(new[] { P("a"), P("A ") }));
            Assert.Throws<ArgumentNullException>(() => new PlatformUiMasks(null));
            Assert.Throws<ArgumentNullException>(() => PlatformUiMasks.Default.WithOverrides(null));
            Assert.Throws<ArgumentNullException>(() => RectUnion.FromNormalized(null, 10, 10));
        }

        [Fact]
        public void NullRectListIsEmptyPlatform()
        {
            var m = new PlatformUiMasks(new[] { new KeyValuePair<string, IEnumerable<FloatRect>>("x", null) });
            Assert.True(m.Contains("x"));
            Assert.Empty(m.Get("x"));
        }
    }
}
