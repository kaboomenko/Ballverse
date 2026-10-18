using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Platform UI zones (spec §3.3 "Подсказки площадок"; Remote Config key <c>platform_safe_zones</c>, spec §9.3):
    /// platform id -&gt; normalized rectangles (0..1 of a 9:16 frame) covered by that platform's interface. Immutable.
    /// <para>
    /// The built-in <see cref="Default"/> values are rough, deliberately conservative approximations of the TikTok, Reels and
    /// Shorts overlays (top tabs, right action rail, bottom caption, bottom bar) and are meant to be replaced through
    /// Remote Config (<see cref="FromJson"/>, <see cref="WithOverrides"/>) when the platforms change their UI.
    /// </para>
    /// Platform ids are lower-cased; all enumeration is in ordinal id order so results never depend on input order.
    /// </summary>
    public sealed class PlatformUiMasks
    {
        public const string TikTok = "tiktok";
        public const string Reels = "reels";
        public const string Shorts = "shorts";

        /// <summary>JSON schema version written by <see cref="ToJson"/>.</summary>
        public const int JsonVersion = 1;

        /// <summary>Built-in approximations (see class remarks).</summary>
        public static readonly PlatformUiMasks Default = CreateDefault();

        readonly string[] _ids;
        readonly FloatRect[][] _rects;

        /// <summary>
        /// Builds the model. Rectangles are clipped to [0,1]; empty or non-finite ones are dropped. Ids are trimmed and
        /// lower-cased; blank ids and duplicates (after that normalization) throw.
        /// </summary>
        public PlatformUiMasks(IEnumerable<KeyValuePair<string, IEnumerable<FloatRect>>> platforms)
        {
            if (platforms == null) throw new ArgumentNullException(nameof(platforms));
            var map = new SortedDictionary<string, FloatRect[]>(StringComparer.Ordinal);
            foreach (var kv in platforms)
            {
                string id = NormalizeId(kv.Key);
                if (map.ContainsKey(id)) throw new ArgumentException("duplicate platform id: " + id, nameof(platforms));
                map.Add(id, Sanitize(kv.Value));
            }
            _ids = new string[map.Count];
            _rects = new FloatRect[map.Count][];
            int i = 0;
            foreach (var kv in map)
            {
                _ids[i] = kv.Key;
                _rects[i] = kv.Value;
                i++;
            }
        }

        /// <summary>Platform ids in ordinal order.</summary>
        public IReadOnlyList<string> PlatformIds => _ids;

        public bool Contains(string platformId)
        {
            return IndexOf(platformId) >= 0;
        }

        /// <summary>Normalized mask rectangles of a platform; empty for an unknown id.</summary>
        public IReadOnlyList<FloatRect> Get(string platformId)
        {
            int i = IndexOf(platformId);
            return i >= 0 ? _rects[i] : new FloatRect[0];
        }

        /// <summary>
        /// Remote Config merge: platforms present in <paramref name="overrides"/> replace this model's rectangles entirely;
        /// other platforms are kept.
        /// </summary>
        public PlatformUiMasks WithOverrides(PlatformUiMasks overrides)
        {
            if (overrides == null) throw new ArgumentNullException(nameof(overrides));
            var list = new List<KeyValuePair<string, IEnumerable<FloatRect>>>();
            for (int i = 0; i < _ids.Length; i++)
            {
                if (!overrides.Contains(_ids[i])) list.Add(new KeyValuePair<string, IEnumerable<FloatRect>>(_ids[i], _rects[i]));
            }
            for (int i = 0; i < overrides._ids.Length; i++)
            {
                list.Add(new KeyValuePair<string, IEnumerable<FloatRect>>(overrides._ids[i], overrides._rects[i]));
            }
            return new PlatformUiMasks(list);
        }

        /// <summary>
        /// Union query: masks of the given platforms (all platforms when null; unknown ids ignored) in canvas pixels,
        /// each edge rounded half-up (<see cref="FloatRect.ToPixels"/>). Rect order follows ordinal platform id order.
        /// </summary>
        public RectUnion ToPixels(int canvasWidth, int canvasHeight, IEnumerable<string> platformIds = null)
        {
            PixelMath.RequireCanvas(canvasWidth, canvasHeight);
            var wanted = new bool[_ids.Length];
            if (platformIds == null)
            {
                for (int i = 0; i < wanted.Length; i++) wanted[i] = true;
            }
            else
            {
                foreach (string id in platformIds)
                {
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    int i = IndexOf(id);
                    if (i >= 0) wanted[i] = true;
                }
            }
            var rects = new List<IntRect>();
            for (int i = 0; i < _ids.Length; i++)
            {
                if (!wanted[i]) continue;
                foreach (FloatRect r in _rects[i]) rects.Add(r.ToPixels(canvasWidth, canvasHeight));
            }
            return new RectUnion(rects);
        }

        /// <summary>
        /// Parses the Remote Config payload (CoreJson): <c>{"version":1,"platforms":{"tiktok":[{"x":0,"y":0,"w":1,"h":0.065}]}}</c>.
        /// Invalid rectangles are dropped (see constructor); malformed JSON throws <see cref="JsonException"/>.
        /// </summary>
        public static PlatformUiMasks FromJson(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var dto = CoreJson.Deserialize<PlatformUiMasksJson>(json);
            if (dto == null || dto.Platforms == null) throw new JsonSerializationException("platform masks: missing 'platforms'");
            var list = new List<KeyValuePair<string, IEnumerable<FloatRect>>>();
            foreach (var kv in dto.Platforms)
            {
                var rects = new List<FloatRect>();
                if (kv.Value != null)
                {
                    foreach (MaskRectJson r in kv.Value)
                    {
                        if (r != null) rects.Add(new FloatRect(r.X, r.Y, r.W, r.H));
                    }
                }
                list.Add(new KeyValuePair<string, IEnumerable<FloatRect>>(kv.Key, rects));
            }
            return new PlatformUiMasks(list);
        }

        /// <summary>Serializes with CoreJson; platform keys in ordinal order (stable bytes).</summary>
        public string ToJson(bool indented = true)
        {
            var dto = new PlatformUiMasksJson
            {
                Version = JsonVersion,
                Platforms = new SortedDictionary<string, List<MaskRectJson>>(StringComparer.Ordinal),
            };
            for (int i = 0; i < _ids.Length; i++)
            {
                var list = new List<MaskRectJson>();
                foreach (FloatRect r in _rects[i]) list.Add(new MaskRectJson { X = r.X, Y = r.Y, W = r.Width, H = r.Height });
                dto.Platforms.Add(_ids[i], list);
            }
            return CoreJson.Serialize(dto, indented);
        }

        int IndexOf(string platformId)
        {
            if (string.IsNullOrWhiteSpace(platformId)) return -1;
            int i = Array.BinarySearch(_ids, platformId.Trim().ToLowerInvariant(), StringComparer.Ordinal);
            return i >= 0 ? i : -1;
        }

        static string NormalizeId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("platform id must not be blank", nameof(id));
            return id.Trim().ToLowerInvariant();
        }

        static FloatRect[] Sanitize(IEnumerable<FloatRect> rects)
        {
            var list = new List<FloatRect>();
            if (rects == null) return list.ToArray();
            foreach (FloatRect r in rects)
            {
                if (!IsFinite(r.X) || !IsFinite(r.Y) || !IsFinite(r.Width) || !IsFinite(r.Height)) continue;
                float l = PixelMath.ClampF(r.X, 0f, 1f);
                float t = PixelMath.ClampF(r.Y, 0f, 1f);
                float rr = PixelMath.ClampF(r.X + r.Width, 0f, 1f);
                float b = PixelMath.ClampF(r.Y + r.Height, 0f, 1f);
                if (rr > l && b > t) list.Add(FloatRect.FromEdges(l, t, rr, b));
            }
            return list.ToArray();
        }

        static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        static PlatformUiMasks CreateDefault()
        {
            // Normalized (x, y, w, h) approximations at 9:16. Conservative: full-width top tabs and bottom bar.
            var tiktok = new[]
            {
                new FloatRect(0f, 0f, 1f, 0.065f),      // top tabs (Following / For You, LIVE, search)
                new FloatRect(0.86f, 0.33f, 0.14f, 0.55f), // right rail (avatar, like, comment, save, share, disc)
                new FloatRect(0f, 0.77f, 0.86f, 0.15f),  // caption, username, sound
                new FloatRect(0f, 0.92f, 1f, 0.08f),     // bottom navigation
            };
            var reels = new[]
            {
                new FloatRect(0f, 0f, 1f, 0.06f),
                new FloatRect(0.87f, 0.48f, 0.13f, 0.42f),
                new FloatRect(0f, 0.80f, 0.87f, 0.12f),
                new FloatRect(0f, 0.92f, 1f, 0.08f),
            };
            var shorts = new[]
            {
                new FloatRect(0f, 0f, 1f, 0.06f),
                new FloatRect(0.85f, 0.42f, 0.15f, 0.46f),
                new FloatRect(0f, 0.79f, 0.85f, 0.13f),
                new FloatRect(0f, 0.92f, 1f, 0.08f),
            };
            return new PlatformUiMasks(new[]
            {
                new KeyValuePair<string, IEnumerable<FloatRect>>(TikTok, tiktok),
                new KeyValuePair<string, IEnumerable<FloatRect>>(Reels, reels),
                new KeyValuePair<string, IEnumerable<FloatRect>>(Shorts, shorts),
            });
        }

        sealed class PlatformUiMasksJson
        {
            public int Version { get; set; }
            public IDictionary<string, List<MaskRectJson>> Platforms { get; set; }
        }

        sealed class MaskRectJson
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float W { get; set; }
            public float H { get; set; }
        }
    }
}
