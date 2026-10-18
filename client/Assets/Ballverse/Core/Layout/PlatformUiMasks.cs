using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Platform UI zones (spec §3.3 "Подсказки площадок"): platform id -&gt; normalized rectangles (0..1 of a 9:16 frame)
    /// covered by that platform's interface. Immutable geometry model only.
    /// <para>
    /// Ownership: the Remote Config key <c>platform_safe_zones</c> (spec §9.3), its JSON shape (platform map at the root,
    /// <c>{"tiktok":[{"x":0,"y":0,"w":1,"h":0.08}, ...]}</c>), its validation and its offline defaults belong to the Config
    /// module. The app builds this model from Config's parsed zones with the constructor (one entry per platform, plain
    /// normalized rects); this module has no parser of its own.
    /// </para>
    /// <para>
    /// <see cref="Default"/> mirrors Config's offline defaults value for value (rough approximations of the TikTok, Reels and
    /// Shorts overlays: top tabs, right action rail, bottom caption + navigation). It exists so pure geometry callers and
    /// tests have masks without a Config instance; Remote Config values replace it at runtime.
    /// </para>
    /// Platform ids are trimmed and lower-cased; all enumeration is in ordinal id order so results never depend on input order.
    /// </summary>
    public sealed class PlatformUiMasks
    {
        public const string TikTok = "tiktok";
        public const string Reels = "reels";
        public const string Shorts = "shorts";

        static readonly ReadOnlyCollection<FloatRect> s_none = Array.AsReadOnly(new FloatRect[0]);

        /// <summary>Built-in approximations, identical to the Config module's offline defaults (see class remarks).</summary>
        public static readonly PlatformUiMasks Default = CreateDefault();

        readonly string[] _ids;
        readonly FloatRect[][] _rects;
        readonly ReadOnlyCollection<string> _idsView;
        readonly ReadOnlyCollection<FloatRect>[] _rectViews;

        /// <summary>
        /// Builds the model (input copied). Rectangles are clipped to [0,1] (in-range ones are kept unchanged); empty or
        /// non-finite ones are dropped. Ids are trimmed and
        /// lower-cased; blank ids and duplicates (after that normalization) throw <see cref="ArgumentException"/>.
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
            _rectViews = new ReadOnlyCollection<FloatRect>[map.Count];
            int i = 0;
            foreach (var kv in map)
            {
                _ids[i] = kv.Key;
                _rects[i] = kv.Value;
                _rectViews[i] = Array.AsReadOnly(kv.Value);
                i++;
            }
            _idsView = Array.AsReadOnly(_ids);
        }

        /// <summary>Platform ids in ordinal order (read-only view).</summary>
        public IReadOnlyList<string> PlatformIds => _idsView;

        public bool Contains(string platformId)
        {
            return IndexOf(platformId) >= 0;
        }

        /// <summary>Normalized mask rectangles of a platform (read-only view); empty for an unknown id.</summary>
        public IReadOnlyList<FloatRect> Get(string platformId)
        {
            int i = IndexOf(platformId);
            return i >= 0 ? _rectViews[i] : s_none;
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
        /// Union query in normalized coordinates: rectangles of the given platforms (all platforms when null; unknown and
        /// blank ids ignored), in ordinal platform id order. This is the canvas-independent form <see cref="FrameRequest"/> takes.
        /// </summary>
        public IReadOnlyList<FloatRect> Union(IEnumerable<string> platformIds = null)
        {
            bool[] wanted = Wanted(platformIds);
            var rects = new List<FloatRect>();
            for (int i = 0; i < _ids.Length; i++)
            {
                if (wanted[i]) rects.AddRange(_rects[i]);
            }
            return rects.AsReadOnly();
        }

        /// <summary>
        /// Union query in canvas pixels: <see cref="Union"/> with each edge rounded half-up (<see cref="FloatRect.ToPixels"/>).
        /// </summary>
        public RectUnion ToPixels(int canvasWidth, int canvasHeight, IEnumerable<string> platformIds = null)
        {
            PixelMath.RequireCanvas(canvasWidth, canvasHeight);
            return RectUnion.FromNormalized(Union(platformIds), canvasWidth, canvasHeight);
        }

        bool[] Wanted(IEnumerable<string> platformIds)
        {
            var wanted = new bool[_ids.Length];
            if (platformIds == null)
            {
                for (int i = 0; i < wanted.Length; i++) wanted[i] = true;
                return wanted;
            }
            foreach (string id in platformIds)
            {
                int i = IndexOf(id);
                if (i >= 0) wanted[i] = true;
            }
            return wanted;
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
                if (!(rr > l && b > t)) continue;
                bool inside = l == r.X && t == r.Y && rr == r.X + r.Width && b == r.Y + r.Height;
                list.Add(inside ? r : FloatRect.FromEdges(l, t, rr, b)); // keep in-range rects bit-exact
            }
            return list.ToArray();
        }

        static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        static PlatformUiMasks CreateDefault()
        {
            // Normalized (x, y, w, h) at 9:16, the same numbers as Config's RemoteConfigDefaults.PlatformSafeZones:
            // top tab bar, right action column, bottom caption + navigation (full width).
            var reels = new[]
            {
                new FloatRect(0f, 0f, 1f, 0.07f),
                new FloatRect(0.85f, 0.45f, 0.15f, 0.375f),
                new FloatRect(0f, 0.825f, 1f, 0.175f),
            };
            var shorts = new[]
            {
                new FloatRect(0f, 0f, 1f, 0.07f),
                new FloatRect(0.85f, 0.425f, 0.15f, 0.375f),
                new FloatRect(0f, 0.8f, 1f, 0.2f),
            };
            var tiktok = new[]
            {
                new FloatRect(0f, 0f, 1f, 0.08f),
                new FloatRect(0.85f, 0.4f, 0.15f, 0.4f),
                new FloatRect(0f, 0.8f, 1f, 0.2f),
            };
            return new PlatformUiMasks(new[]
            {
                new KeyValuePair<string, IEnumerable<FloatRect>>(Reels, reels),
                new KeyValuePair<string, IEnumerable<FloatRect>>(Shorts, shorts),
                new KeyValuePair<string, IEnumerable<FloatRect>>(TikTok, tiktok),
            });
        }
    }
}
