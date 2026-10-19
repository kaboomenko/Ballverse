using System;
using System.Collections.Generic;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.Tests.RenderGeometry
{
    static class GeometryTestUtil
    {
        public static readonly RenderQuantization Q = RenderQuantization.Default;

        public static ShapePoint P(float x, float y) => Q.QuantizePoint(x, y);

        public static ShapePoint[] Pts(params float[] xy)
        {
            var r = new ShapePoint[xy.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = P(xy[2 * i], xy[2 * i + 1]);
            return r;
        }

        /// <summary>Area covered by the union of the triangles, estimated on an n × n sample grid over the bounds.</summary>
        public static double CoverageArea(LocalGeometry g, int n = 400)
        {
            if (!g.TryGetBounds(out float minX, out float minY, out float maxX, out float maxY)) return 0;
            ReadOnlySpan<Float2> p = g.Positions;
            ReadOnlySpan<int> idx = g.Indices;
            double w = maxX - minX, h = maxY - minY;
            int inside = 0;
            for (int j = 0; j < n; j++)
            {
                double y = minY + (j + 0.5) * h / n;
                for (int i = 0; i < n; i++)
                {
                    double x = minX + (i + 0.5) * w / n;
                    for (int t = 0; t + 2 < idx.Length; t += 3)
                    {
                        if (InTriangle(p[idx[t]], p[idx[t + 1]], p[idx[t + 2]], x, y))
                        {
                            inside++;
                            break;
                        }
                    }
                }
            }
            return inside * w * h / ((double)n * n);
        }

        public static bool InTriangle(Float2 a, Float2 b, Float2 c, double x, double y)
        {
            double d1 = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
            double d2 = (c.X - b.X) * (y - b.Y) - (c.Y - b.Y) * (x - b.X);
            double d3 = (a.X - c.X) * (y - c.Y) - (a.Y - c.Y) * (x - c.X);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        /// <summary>True when (x, y) lies in any triangle.</summary>
        public static bool Covers(LocalGeometry g, double x, double y)
        {
            ReadOnlySpan<Float2> p = g.Positions;
            ReadOnlySpan<int> idx = g.Indices;
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                if (InTriangle(p[idx[t]], p[idx[t + 1]], p[idx[t + 2]], x, y)) return true;
            }
            return false;
        }

        public static double ShoelaceArea(ReadOnlySpan<ShapePoint> pts)
        {
            double a = 0;
            for (int i = 0; i < pts.Length; i++)
            {
                double x0 = Q.DequantizePosition(pts[i].X), y0 = Q.DequantizePosition(pts[i].Y);
                double x1 = Q.DequantizePosition(pts[(i + 1) % pts.Length].X), y1 = Q.DequantizePosition(pts[(i + 1) % pts.Length].Y);
                a += x0 * y1 - x1 * y0;
            }
            return Math.Abs(a) * 0.5;
        }

        public static RenderFrame Frame(params BodyState[] bodies)
        {
            var f = new RenderFrame();
            foreach (BodyState b in bodies) f.AddBody(b);
            f.Sort();
            return f;
        }

        public static List<ShapeDefinition> Shapes(params ShapeDefinition[] shapes) => new List<ShapeDefinition>(shapes);
    }
}
