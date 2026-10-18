using System;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>
    /// Pseudo-3D projection of the Helix Drop tower (M05 card "Камера и рендер": platforms drawn as tilted rings, elliptic
    /// projection with tilt <c>viewTilt</c>, back sectors darker, a column in the centre; no real 3D).
    /// <para>
    /// Tower space: the tower axis is vertical at x = <see cref="AxisX"/>; a point at radius ρ, world azimuth ψ and height
    /// h is (x, y, z) = (AxisX + ρ·sin ψ, h, −ρ·cos ψ): ψ = 0 faces the viewer (z &lt; 0 is towards the viewer) and ψ grows
    /// counter-clockwise seen from above. This is the convention of the M05 solver, whose vertical cut maps a local x to
    /// the azimuth asin(x / R_path) − θ_k: a platform drawn from a ring body with angle θ puts the ring's local angle a at
    /// ψ = a + θ, and a ball at stream x lies on the path circle at ψ = asin((x − AxisX) / R_path) on the front side.
    /// </para>
    /// <para>
    /// Camera: a pinhole at distance <see cref="CameraDistance"/> from the focus point (AxisX, <see cref="FocusHeight"/>, 0),
    /// looking down at <see cref="Tilt"/>. The focus point maps to itself with scale 1; heights around it compress by
    /// ≈ cos(tilt) and a ring of radius ρ becomes an ellipse of half-height ≈ ρ·sin(tilt). A non-positive or infinite
    /// distance gives the orthographic limit. The result is in drawing-plane metres, which the 2D view maps to pixels.
    /// </para>
    /// </summary>
    public struct HelixProjection : IPointProjector
    {
        public const float DefaultTiltDegrees = 28f;
        public const float DefaultCameraDistance = 14f;
        public const float DefaultBallPathRadius = 1.5f;

        float _sin;
        float _cos;
        float _distance;

        public HelixProjection(float axisX, float focusHeight, float tiltRadians, float cameraDistance, float ballPathRadius)
        {
            AxisX = axisX;
            FocusHeight = focusHeight;
            Tilt = tiltRadians;
            BallPathRadius = ballPathRadius > 0f ? ballPathRadius : DefaultBallPathRadius;
            _sin = MathF.Sin(tiltRadians);
            _cos = MathF.Cos(tiltRadians);
            _distance = cameraDistance > 0f && !float.IsInfinity(cameraDistance) ? cameraDistance : 0f;
            CameraDistance = _distance;
        }

        public float AxisX { get; }
        public float FocusHeight { get; }
        /// <summary>Downward view tilt, radians (M05 <c>viewTilt</c>, 15–40°, default 28°).</summary>
        public float Tilt { get; }
        /// <summary>Camera distance from the focus point, metres; 0 = orthographic.</summary>
        public float CameraDistance { get; }
        /// <summary>Radius of the ball's path circle (M05 R_path = 1.5 m).</summary>
        public float BallPathRadius { get; }

        /// <summary>Projects tower-space (x, y, z); <paramref name="depth"/> grows away from the viewer.</summary>
        public void Project(float x, float y, float z, out float sx, out float sy, out float scale, out float depth)
        {
            float rx = x - AxisX;
            float ry = y - FocusHeight;
            // Camera basis: right (1,0,0), up (0, cos, sin), forward (0, −sin, cos).
            float yc = ry * _cos + z * _sin;
            float zc = -ry * _sin + z * _cos;
            if (_distance > 0f)
            {
                float d = _distance + zc;
                if (d < 0.1f * _distance) d = 0.1f * _distance;
                scale = _distance / d;
            }
            else
            {
                scale = 1f;
            }
            sx = AxisX + rx * scale;
            sy = FocusHeight + yc * scale;
            depth = zc;
        }

        /// <summary>Projects the point at radius <paramref name="rho"/>, world azimuth <paramref name="azimuth"/> and height <paramref name="height"/>.</summary>
        public void ProjectCylindrical(float rho, float azimuth, float height, out float sx, out float sy, out float scale, out float depth)
        {
            Project(AxisX + rho * MathF.Sin(azimuth), height, -rho * MathF.Cos(azimuth), out sx, out sy, out scale, out depth);
        }

        /// <summary>A ball at stream (x, y): on the front half of the path circle (x is clamped to ±R_path).</summary>
        public void ProjectBody(float x, float y, out float sx, out float sy, out float scale)
        {
            float lateral = x - AxisX;
            float r = BallPathRadius;
            if (lateral > r) lateral = r;
            if (lateral < -r) lateral = -r;
            float z = -MathF.Sqrt(Math.Max(0f, r * r - lateral * lateral));
            Project(AxisX + lateral, y, z, out sx, out sy, out scale, out _);
        }

        public void ProjectPoint(float x, float y, float z, out float sx, out float sy, out float scale)
        {
            Project(x, y, z, out sx, out sy, out scale, out _);
        }

        /// <summary>Brightness factor of a surface at azimuth ψ: 1 at the front, <paramref name="backShade"/> at the back.</summary>
        public static float Shade(float azimuth, float backShade)
        {
            float front = 0.5f + 0.5f * MathF.Cos(azimuth);
            return backShade + (1f - backShade) * front;
        }

        /// <summary>True when azimuth ψ is on the half facing the viewer (cos ψ ≥ 0).</summary>
        public static bool IsFront(float azimuth) => MathF.Cos(azimuth) >= 0f;
    }

    /// <summary>Helix Drop view settings (M05 card); defaults match the card.</summary>
    public sealed class HelixViewSettings
    {
        /// <summary>View tilt (<c>viewTilt</c>), degrees, 15–40.</summary>
        public float TiltDegrees { get; set; } = HelixProjection.DefaultTiltDegrees;
        /// <summary>Camera distance, metres (0 = orthographic).</summary>
        public float CameraDistance { get; set; } = HelixProjection.DefaultCameraDistance;
        /// <summary>Ball path radius R_path, metres.</summary>
        public float BallPathRadius { get; set; } = HelixProjection.DefaultBallPathRadius;
        /// <summary>Platform slab thickness T, metres (the platform occupies heights [y − T, y]).</summary>
        public float PlatformThickness { get; set; } = 0.15f;
        /// <summary>Column radius, metres (0 = the smallest platform inner radius).</summary>
        public float ColumnRadius { get; set; }
        /// <summary>Column extends this far above the top platform, metres.</summary>
        public float ColumnTopExtra { get; set; } = 0.6f;
        /// <summary>Column extends this far below the lowest platform, metres.</summary>
        public float ColumnBottomExtra { get; set; } = 30f;
        /// <summary>Brightness of the outer rim relative to the top face.</summary>
        public float RimShade { get; set; } = 0.62f;
        /// <summary>Brightness of radial gap walls relative to the top face.</summary>
        public float GapWallShade { get; set; } = 0.78f;

        /// <summary>Platforms the default Helix camera shows (M05 card «видно до 12 платформ», §7.4 «в кадре 12 платформ»).</summary>
        public const int VisiblePlatforms = 12;

        /// <summary>
        /// Platform spacing of the tower (M05 <c>platformSpacing</c>), metres; 0 = unknown (the default camera then keeps
        /// the arena's 16 m view height).
        /// </summary>
        public float PlatformSpacing { get; set; }

        public float TiltRadians => Math.Max(5f, Math.Min(60f, TiltDegrees)) * (MathF.PI / 180f);

        /// <summary>
        /// Height of <see cref="VisiblePlatforms"/> platform spacings as projected (heights compress by cos(tilt)), metres;
        /// 0 when the spacing is unknown. The default camera uses <see cref="ViewHeightFor"/>, which caps this.
        /// </summary>
        public float VisiblePlatformsHeight => PlatformSpacing > 0f && !float.IsInfinity(PlatformSpacing)
            ? VisiblePlatforms * PlatformSpacing * MathF.Cos(TiltRadians)
            : 0f;

        /// <summary>
        /// View height of the default Helix camera on a <paramref name="width"/> × <paramref name="height"/> canvas, metres:
        /// the zoom-1 view (<see cref="ViewMapping.Fixed"/>, 16 m on 9:16) capped by <see cref="VisiblePlatformsHeight"/>.
        /// «Видно до 12 платформ» is an upper bound: small spacings zoom in so that no more than 12 are visible, large
        /// spacings keep the zoom-1 view (fewer platforms, never smaller than the arena scale).
        /// </summary>
        public float ViewHeightFor(int width, int height)
        {
            float zoomOne = ViewMapping.Fixed(width, height).Height;
            float twelve = VisiblePlatformsHeight;
            return twelve > 0f && twelve < zoomOne ? twelve : zoomOne;
        }
    }
}
