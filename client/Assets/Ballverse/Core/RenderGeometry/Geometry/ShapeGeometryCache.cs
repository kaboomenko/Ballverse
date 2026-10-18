using System;
using System.Collections.Generic;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>
    /// Per-shape tessellation cache (spec §7.7 "меш-кэш по shapeId"). Shapes are static, so a tessellation is reused
    /// across frames and bodies:
    /// <list type="bullet">
    /// <item>Polygons are scale independent: one entry per shape, drawn with the body scale in the transform.</item>
    /// <item>Curved shapes with a scaling thickness are built at unit scale for a power-of-two "pixel scale" bucket
    /// (body scale × pixels per metre rounded up), so the chord error stays within the tolerance at any zoom while the
    /// entry is shared by all bodies and frames in that bucket; drawn with the body scale in the transform.</item>
    /// <item>Fixed-thickness shapes (non-scaling strokes, e.g. shrinking M01 rings) are built at the exact quantized
    /// body scale and drawn with transform scale 1. Such entries live only a few frames.</item>
    /// </list>
    /// Allocation-free in steady state: entries and their <see cref="LocalGeometry"/> buffers are pooled; the map is
    /// pre-sized. Not thread-safe.
    /// </summary>
    public sealed class ShapeGeometryCache
    {
        /// <summary>Frames an unused fixed-thickness entry survives.</summary>
        public const int FixedScaleKeepFrames = 2;
        /// <summary>Frames an unused shared entry survives.</summary>
        public const int SharedKeepFrames = 600;

        readonly Dictionary<long, int> _map;
        readonly LocalGeometry[] _geometry;
        readonly long[] _keys;
        readonly int[] _lastUsed;
        readonly int[] _keepFrames;
        readonly TessellationStatus[] _status;
        readonly int[] _free;
        readonly LocalGeometry _scratch = new LocalGeometry();
        int _freeCount;
        int _frame;
        IReadOnlyList<ShapeDefinition> _shapes;
        RenderQuantization _quantization;
        TessellationSettings _style;
        bool _hasStyle;

        public ShapeGeometryCache(int capacity = 1024)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
            _map = new Dictionary<long, int>(capacity);
            _geometry = new LocalGeometry[capacity];
            _keys = new long[capacity];
            _lastUsed = new int[capacity];
            _keepFrames = new int[capacity];
            _status = new TessellationStatus[capacity];
            _free = new int[capacity];
            for (int i = 0; i < capacity; i++)
            {
                _geometry[i] = new LocalGeometry();
                _free[i] = capacity - 1 - i;
            }
            _freeCount = capacity;
        }

        public int Capacity { get; }

        /// <summary>Entries currently cached.</summary>
        public int Count => Capacity - _freeCount;

        /// <summary>Tessellations performed since construction (cache misses), for diagnostics and tests.</summary>
        public long BuildCount { get; private set; }

        /// <summary>Binds the cache to a stream's shape table and quantization, dropping all entries.</summary>
        public void Reset(IReadOnlyList<ShapeDefinition> shapes, RenderQuantization quantization)
        {
            _shapes = shapes ?? throw new ArgumentNullException(nameof(shapes));
            _quantization = quantization ?? throw new ArgumentNullException(nameof(quantization));
            Flush();
        }

        /// <summary>Drops all entries (keeps the bound shape table).</summary>
        public void Flush()
        {
            _map.Clear();
            Array.Clear(_keepFrames, 0, Capacity);
            _freeCount = Capacity;
            for (int i = 0; i < Capacity; i++) _free[i] = Capacity - 1 - i;
        }

        /// <summary>Advances the frame counter and evicts entries unused for longer than their keep time.</summary>
        public void BeginFrame()
        {
            _frame++;
            if (_freeCount == Capacity) return;
            for (int i = 0; i < Capacity; i++)
            {
                if (_keepFrames[i] == 0) continue;   // free slot
                if (_frame - _lastUsed[i] > _keepFrames[i]) Evict(i);
            }
        }

        /// <summary>
        /// Returns the body-local geometry of shape <paramref name="shapeId"/> for a body of quantized scale
        /// <paramref name="scaleQ"/>, and the scale to put in the body transform (<paramref name="drawScale"/>: the
        /// dequantized body scale for shared entries, 1 for fixed-scale entries). The returned object is owned by the
        /// cache and valid until the next call.
        /// </summary>
        public LocalGeometry Get(int shapeId, ushort scaleQ, in TessellationSettings settings, out float drawScale, out TessellationStatus status)
        {
            if (_shapes == null) throw new InvalidOperationException("Reset must be called first");
            if ((uint)shapeId >= (uint)_shapes.Count) throw new ArgumentOutOfRangeException(nameof(shapeId));
            if (!_hasStyle || !_style.StyleEquals(settings))
            {
                settings.Validate();
                _style = settings;
                _hasStyle = true;
                Flush();
            }
            ShapeDefinition shape = _shapes[shapeId];
            float bodyScale = _quantization.DequantizeScale(scaleQ);

            long key;
            float buildScale;
            float buildPpm;
            bool scaleInTransform;
            int keep;
            if (shape.Kind == ShapeKind.Polygon)
            {
                key = (long)shapeId << 32;
                buildScale = 1f;
                buildPpm = settings.PixelsPerMetre;
                scaleInTransform = true;
                keep = SharedKeepFrames;
            }
            else
            {
                int bucket = PixelScaleBucket(bodyScale * settings.PixelsPerMetre);
                if (shape.FixedThickness && shape.Kind != ShapeKind.Circle)
                {
                    key = ((long)shapeId << 32) | (1L << 31) | ((long)scaleQ << 8) | (uint)(bucket + 128);
                    buildScale = bodyScale;
                    buildPpm = PowerOfTwo(bucket) / Math.Max(bodyScale, 1e-6f);   // ≥ the real pixels per metre, stable in the bucket
                    scaleInTransform = false;
                    keep = FixedScaleKeepFrames;
                }
                else
                {
                    key = ((long)shapeId << 32) | (uint)(bucket + 128);
                    buildScale = 1f;
                    buildPpm = PowerOfTwo(bucket);
                    scaleInTransform = true;
                    keep = SharedKeepFrames;
                }
            }
            drawScale = scaleInTransform ? bodyScale : 1f;

            if (_map.TryGetValue(key, out int slot))
            {
                _lastUsed[slot] = _frame;
                status = _status[slot];
                return _geometry[slot];
            }

            TessellationSettings build = settings;
            build.PixelsPerMetre = buildPpm;
            if (_freeCount == 0) EvictLeastRecentlyUsed();
            BuildCount++;
            if (_freeCount == 0)
            {
                status = ShapeTessellator.Tessellate(shape, _quantization, buildScale, build, _scratch);
                return _scratch;
            }
            slot = _free[--_freeCount];
            status = ShapeTessellator.Tessellate(shape, _quantization, buildScale, build, _geometry[slot]);
            _keys[slot] = key;
            _lastUsed[slot] = _frame;
            _keepFrames[slot] = keep;
            _status[slot] = status;
            _map.Add(key, slot);
            return _geometry[slot];
        }

        /// <summary>⌈log2(pixels)⌉ clamped to [−8, 20]: the power-of-two pixel scale a shared tessellation is built for.</summary>
        public static int PixelScaleBucket(float pixelsPerUnit)
        {
            if (!(pixelsPerUnit > 0f)) return -8;
            int b = (int)Math.Ceiling(Math.Log(pixelsPerUnit, 2.0) - 1e-9);
            if (b < -8) b = -8;
            if (b > 20) b = 20;
            return b;
        }

        static float PowerOfTwo(int exponent)
        {
            return (float)Math.Pow(2.0, exponent);
        }

        void Evict(int slot)
        {
            _map.Remove(_keys[slot]);
            _keepFrames[slot] = 0;
            _free[_freeCount++] = slot;
        }

        void EvictLeastRecentlyUsed()
        {
            int best = -1;
            int bestUsed = int.MaxValue;
            for (int i = 0; i < Capacity; i++)
            {
                if (_keepFrames[i] == 0) continue;
                if (_lastUsed[i] == _frame) continue;   // in use this frame: the caller may still hold it
                if (_lastUsed[i] < bestUsed)
                {
                    bestUsed = _lastUsed[i];
                    best = i;
                }
            }
            if (best >= 0) Evict(best);
        }
    }
}
