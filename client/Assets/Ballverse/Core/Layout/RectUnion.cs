using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Immutable set of pixel rectangles treated as their geometric union (platform UI masks, occupied regions).
    /// Overlap queries count every covered pixel once, however many rectangles cover it. Rect order is preserved
    /// as given (deterministic) but never affects query results.
    /// </summary>
    public sealed class RectUnion
    {
        public static readonly RectUnion Empty = new RectUnion(new IntRect[0]);

        readonly IntRect[] _rects;
        readonly ReadOnlyCollection<IntRect> _view;

        /// <summary>Empty rectangles are dropped. The input is copied.</summary>
        public RectUnion(IEnumerable<IntRect> rects)
        {
            if (rects == null) throw new ArgumentNullException(nameof(rects));
            var list = new List<IntRect>();
            foreach (IntRect r in rects)
            {
                if (!r.IsEmpty) list.Add(r);
            }
            _rects = list.ToArray();
            _view = Array.AsReadOnly(_rects);
        }

        /// <summary>Normalized rects (0..1 of the canvas) converted with <see cref="FloatRect.ToPixels"/>, in input order.</summary>
        public static RectUnion FromNormalized(IEnumerable<FloatRect> normalized, int canvasWidth, int canvasHeight)
        {
            if (normalized == null) throw new ArgumentNullException(nameof(normalized));
            PixelMath.RequireCanvas(canvasWidth, canvasHeight);
            var list = new List<IntRect>();
            foreach (FloatRect r in normalized) list.Add(r.ToPixels(canvasWidth, canvasHeight));
            return new RectUnion(list);
        }

        public int Count => _rects.Length;
        public IntRect this[int index] => _rects[index];

        /// <summary>The rectangles (read-only view).</summary>
        public IReadOnlyList<IntRect> Rects => _view;

        /// <summary>True if <paramref name="query"/> shares a positive area with any rectangle of the union.</summary>
        public bool Intersects(IntRect query)
        {
            for (int i = 0; i < _rects.Length; i++)
            {
                if (_rects[i].Intersects(query)) return true;
            }
            return false;
        }

        /// <summary>Exact pixel area of query ∩ union (overlapping rectangles are not double counted).</summary>
        public long IntersectionArea(IntRect query)
        {
            // Fast paths without allocation: no clipped rect, or exactly one.
            int hits = 0;
            IntRect only = IntRect.Empty;
            for (int i = 0; i < _rects.Length; i++)
            {
                IntRect c = _rects[i].Intersection(query);
                if (c.IsEmpty) continue;
                hits++;
                only = c;
            }
            if (hits == 0) return 0;
            if (hits == 1) return only.Area;
            var clips = new List<IntRect>(hits);
            for (int i = 0; i < _rects.Length; i++)
            {
                IntRect c = _rects[i].Intersection(query);
                if (!c.IsEmpty) clips.Add(c);
            }
            return UnionArea(clips);
        }

        /// <summary>Union of this set and more rectangles (this set's rects first).</summary>
        public RectUnion With(IEnumerable<IntRect> more)
        {
            if (more == null) throw new ArgumentNullException(nameof(more));
            var list = new List<IntRect>(_rects);
            list.AddRange(more);
            return new RectUnion(list);
        }

        /// <summary>Exact area of the union of the given rectangles (coordinate compression; fine for tens of rects).</summary>
        public static long UnionArea(IReadOnlyList<IntRect> rects)
        {
            if (rects.Count == 0) return 0;
            if (rects.Count == 1) return rects[0].Area;
            var xs = new List<int>(rects.Count * 2);
            var ys = new List<int>(rects.Count * 2);
            for (int i = 0; i < rects.Count; i++)
            {
                xs.Add(rects[i].X);
                xs.Add(rects[i].Right);
                ys.Add(rects[i].Y);
                ys.Add(rects[i].Bottom);
            }
            xs.Sort();
            ys.Sort();
            long area = 0;
            for (int xi = 0; xi + 1 < xs.Count; xi++)
            {
                int x0 = xs[xi], x1 = xs[xi + 1];
                if (x1 == x0) continue;
                for (int yi = 0; yi + 1 < ys.Count; yi++)
                {
                    int y0 = ys[yi], y1 = ys[yi + 1];
                    if (y1 == y0) continue;
                    for (int k = 0; k < rects.Count; k++)
                    {
                        IntRect r = rects[k];
                        if (r.X <= x0 && r.Right >= x1 && r.Y <= y0 && r.Bottom >= y1)
                        {
                            area += (long)(x1 - x0) * (y1 - y0);
                            break;
                        }
                    }
                }
            }
            return area;
        }
    }
}
