using System;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>
    /// Ear-clipping triangulation of a polygon given in the quantized int16 domain of the render stream's shape table
    /// (spec §7.7). All predicates use exact 64-bit integer arithmetic, so the result is identical on every platform and
    /// never suffers from floating-point robustness failures.
    /// <list type="bullet">
    /// <item>Either winding is accepted; output triangles are counter-clockwise.</item>
    /// <item>Consecutive duplicate vertices and collinear vertices (including zero-area spikes) are removed first.</item>
    /// <item>Concave simple polygons are triangulated exactly (n − 2 triangles of the cleaned outline).</item>
    /// <item>Self-intersecting polygons are reported (<see cref="TessellationStatus.SelfIntersecting"/>) and still
    /// triangulated with a fallback rule that always terminates: when no proper ear exists, the first convex (or any)
    /// vertex is clipped. Something sensible is drawn and nothing throws.</item>
    /// </list>
    /// Allocation-free (stack scratch); at most <see cref="MaxVertices"/> vertices.
    /// </summary>
    public static class PolygonTriangulator
    {
        /// <summary>Largest vertex count accepted (the shape table allows 64 polygon vertices).</summary>
        public const int MaxVertices = 256;

        /// <summary>
        /// Triangulates <paramref name="points"/>; writes vertex indices (into <paramref name="points"/>) to
        /// <paramref name="indices"/>, three per triangle, and returns the triangle count through <paramref name="triangleCount"/>.
        /// <paramref name="indices"/> needs room for 3 · (n − 2) entries.
        /// </summary>
        public static TessellationStatus Triangulate(ReadOnlySpan<ShapePoint> points, Span<int> indices, out int triangleCount)
        {
            triangleCount = 0;
            int n = points.Length;
            if (n > MaxVertices) throw new ArgumentException("Polygon has more than " + MaxVertices + " vertices", nameof(points));
            if (n < 3) return TessellationStatus.Empty;
            if (indices.Length < 3 * (n - 2)) throw new ArgumentException("indices too small", nameof(indices));

            Span<int> ring = stackalloc int[n];
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                if (count > 0 && points[ring[count - 1]] == points[i]) continue;
                ring[count++] = i;
            }
            while (count > 1 && points[ring[count - 1]] == points[ring[0]]) count--;

            count = RemoveCollinear(points, ring, count);
            if (count < 3) return TessellationStatus.Empty;

            long area2 = 0;
            for (int i = 0; i < count; i++)
            {
                ShapePoint a = points[ring[i]];
                ShapePoint b = points[ring[(i + 1) % count]];
                area2 += (long)a.X * b.Y - (long)b.X * a.Y;
            }
            bool selfIntersecting = HasSelfIntersection(points, ring, count);
            // A zero net area is either a degenerate outline or a figure-eight whose lobes cancel; only the latter draws.
            if (area2 == 0 && !selfIntersecting) return TessellationStatus.Empty;
            if (area2 < 0) ring.Slice(0, count).Reverse();

            Span<int> prev = stackalloc int[count];
            Span<int> next = stackalloc int[count];
            for (int i = 0; i < count; i++)
            {
                prev[i] = (i + count - 1) % count;
                next[i] = (i + 1) % count;
            }

            int remaining = count;
            int v = 0;
            int sinceLastEar = 0;
            int written = 0;
            while (remaining > 3)
            {
                int p = prev[v];
                int q = next[v];
                ShapePoint pp = points[ring[p]];
                ShapePoint pv = points[ring[v]];
                ShapePoint pq = points[ring[q]];
                long turn = Orient(pp, pv, pq);
                bool clip = false;
                bool emit = true;
                if (turn == 0)
                {
                    clip = true;
                    emit = false;   // zero-area corner: drop the vertex without a triangle
                }
                else if (turn > 0 && IsEar(points, ring, next, p, v, q))
                {
                    clip = true;
                }
                else if (sinceLastEar >= remaining)
                {
                    // No ear in a full pass: only possible for self-intersecting input. Clip the first convex vertex
                    // (or this one when none is convex) so the loop always terminates.
                    int c = v;
                    for (int k = 0; k < remaining; k++)
                    {
                        if (Orient(points[ring[prev[c]]], points[ring[c]], points[ring[next[c]]]) > 0) break;
                        c = next[c];
                    }
                    v = c;
                    p = prev[v];
                    q = next[v];
                    clip = true;
                    emit = Orient(points[ring[p]], points[ring[v]], points[ring[q]]) != 0;
                    selfIntersecting = true;
                }

                if (clip)
                {
                    if (emit)
                    {
                        Emit(points, ring, p, v, q, indices, ref written);
                    }
                    next[p] = q;
                    prev[q] = p;
                    remaining--;
                    sinceLastEar = 0;
                    v = p;   // re-test the neighbour, whose corner changed
                }
                else
                {
                    sinceLastEar++;
                    v = q;
                }
            }

            {
                int p = prev[v];
                int q = next[v];
                if (Orient(points[ring[p]], points[ring[v]], points[ring[q]]) != 0) Emit(points, ring, p, v, q, indices, ref written);
            }

            triangleCount = written / 3;
            if (triangleCount == 0) return TessellationStatus.Empty;
            return selfIntersecting ? TessellationStatus.SelfIntersecting : TessellationStatus.Ok;
        }

        static void Emit(ReadOnlySpan<ShapePoint> points, Span<int> ring, int p, int v, int q, Span<int> indices, ref int written)
        {
            // Keep counter-clockwise output even for a reflex fallback clip.
            if (Orient(points[ring[p]], points[ring[v]], points[ring[q]]) >= 0)
            {
                indices[written++] = ring[p];
                indices[written++] = ring[v];
                indices[written++] = ring[q];
            }
            else
            {
                indices[written++] = ring[p];
                indices[written++] = ring[q];
                indices[written++] = ring[v];
            }
        }

        /// <summary>True when the polygon (cleaned ring) has two non-adjacent edges that touch or cross.</summary>
        public static bool HasSelfIntersection(ReadOnlySpan<ShapePoint> points, ReadOnlySpan<int> ring, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ShapePoint a0 = points[ring[i]];
                ShapePoint a1 = points[ring[(i + 1) % count]];
                for (int j = i + 1; j < count; j++)
                {
                    bool adjacent = j == i + 1 || (i == 0 && j == count - 1);
                    ShapePoint b0 = points[ring[j]];
                    ShapePoint b1 = points[ring[(j + 1) % count]];
                    if (adjacent)
                    {
                        // Adjacent edges share one vertex; they only "intersect" if they overlap (fold back), which the
                        // collinear clean-up already removed. Coincident non-shared endpoints are a touch.
                        continue;
                    }
                    if (SegmentsIntersect(a0, a1, b0, b1)) return true;
                }
            }
            return false;
        }

        static bool IsEar(ReadOnlySpan<ShapePoint> points, Span<int> ring, Span<int> next, int p, int v, int q)
        {
            ShapePoint a = points[ring[p]];
            ShapePoint b = points[ring[v]];
            ShapePoint c = points[ring[q]];
            for (int r = next[q]; r != p; r = next[r])
            {
                ShapePoint x = points[ring[r]];
                if (x == a || x == b || x == c) continue;
                if (Orient(a, b, x) >= 0 && Orient(b, c, x) >= 0 && Orient(c, a, x) >= 0) return false;
            }
            return true;
        }

        static int RemoveCollinear(ReadOnlySpan<ShapePoint> points, Span<int> ring, int count)
        {
            bool changed = true;
            while (changed && count >= 3)
            {
                changed = false;
                for (int i = 0; i < count && count >= 3; i++)
                {
                    ShapePoint a = points[ring[(i + count - 1) % count]];
                    ShapePoint b = points[ring[i]];
                    ShapePoint c = points[ring[(i + 1) % count]];
                    if (Orient(a, b, c) == 0 || a == c)
                    {
                        for (int k = i; k < count - 1; k++) ring[k] = ring[k + 1];
                        count--;
                        changed = true;
                        i--;
                    }
                }
            }
            return count;
        }

        /// <summary>Twice the signed area of triangle a-b-c (positive = counter-clockwise), exact.</summary>
        public static long Orient(ShapePoint a, ShapePoint b, ShapePoint c)
        {
            return ((long)b.X - a.X) * ((long)c.Y - a.Y) - ((long)b.Y - a.Y) * ((long)c.X - a.X);
        }

        /// <summary>Exact closed-segment intersection test (touching counts).</summary>
        public static bool SegmentsIntersect(ShapePoint p1, ShapePoint p2, ShapePoint q1, ShapePoint q2)
        {
            long d1 = Orient(q1, q2, p1);
            long d2 = Orient(q1, q2, p2);
            long d3 = Orient(p1, p2, q1);
            long d4 = Orient(p1, p2, q2);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
            if (d1 == 0 && OnSegment(q1, q2, p1)) return true;
            if (d2 == 0 && OnSegment(q1, q2, p2)) return true;
            if (d3 == 0 && OnSegment(p1, p2, q1)) return true;
            if (d4 == 0 && OnSegment(p1, p2, q2)) return true;
            return false;
        }

        static bool OnSegment(ShapePoint a, ShapePoint b, ShapePoint p)
        {
            return Math.Min(a.X, b.X) <= p.X && p.X <= Math.Max(a.X, b.X) && Math.Min(a.Y, b.Y) <= p.Y && p.Y <= Math.Max(a.Y, b.Y);
        }
    }
}
