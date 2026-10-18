using System;

namespace Ballverse.Core.Sim
{
    /// <summary>Vertex helpers for arena polygons (Geometry Bounce: square, hexagon, star…). Points are counter-clockwise.</summary>
    public static class ShapeBuilder
    {
        /// <summary>Regular polygon with <paramref name="sides"/> vertices on a circle of <paramref name="circumradius"/>.</summary>
        public static Vec2[] RegularPolygon(int sides, float circumradius, float rotation = 0f, Vec2 centre = default)
        {
            if (sides < 3) throw new ArgumentOutOfRangeException(nameof(sides), "A polygon needs at least 3 sides");
            if (!(circumradius > 0f) || !SimMath.IsFinite(circumradius)) throw new ArgumentOutOfRangeException(nameof(circumradius));
            var pts = new Vec2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = rotation + SimMath.TwoPi * i / sides;
                pts[i] = centre + Vec2.FromAngle(a) * circumradius;
            }
            return pts;
        }

        /// <summary>Star with alternating outer / inner radii (2·points vertices).</summary>
        public static Vec2[] Star(int points, float outerRadius, float innerRadius, float rotation = 0f, Vec2 centre = default)
        {
            if (points < 3) throw new ArgumentOutOfRangeException(nameof(points), "A star needs at least 3 points");
            if (!(outerRadius > 0f) || !(innerRadius > 0f) || innerRadius >= outerRadius)
                throw new ArgumentOutOfRangeException(nameof(innerRadius), "Radii must satisfy 0 < inner < outer");
            var pts = new Vec2[points * 2];
            for (int i = 0; i < points * 2; i++)
            {
                float a = rotation + SimMath.Pi * i / points;
                float r = (i & 1) == 0 ? outerRadius : innerRadius;
                pts[i] = centre + Vec2.FromAngle(a) * r;
            }
            return pts;
        }

        /// <summary>Axis-aligned rectangle (counter-clockwise from bottom-left).</summary>
        public static Vec2[] Rectangle(float width, float height, Vec2 centre = default)
        {
            if (!(width > 0f) || !(height > 0f)) throw new ArgumentOutOfRangeException(nameof(width), "Size must be > 0");
            float hw = width * 0.5f, hh = height * 0.5f;
            return new[]
            {
                centre + new Vec2(-hw, -hh),
                centre + new Vec2(hw, -hh),
                centre + new Vec2(hw, hh),
                centre + new Vec2(-hw, hh),
            };
        }

        /// <summary>Inradius (centre to edge midpoint) of a regular polygon.</summary>
        public static float Inradius(int sides, float circumradius)
        {
            if (sides < 3) throw new ArgumentOutOfRangeException(nameof(sides));
            return circumradius * SimMath.Cos(SimMath.Pi / sides);
        }
    }
}
