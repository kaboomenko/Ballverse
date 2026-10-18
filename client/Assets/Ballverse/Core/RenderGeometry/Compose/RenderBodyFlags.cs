using System;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>
    /// Renderer conventions for the free <see cref="BodyFlags"/> bits 5–7 (the render stream carries them bit-exactly; see
    /// RenderEventContract.md §5). Modes set them on bodies like the named flags; bit 7 stays free.
    /// </summary>
    public static class RenderBodyFlags
    {
        /// <summary>
        /// AutoZoom fits only the bodies with this flag when any visible body in the frame has it (otherwise every visible
        /// body). M01 «Отдаление камеры»: the ball and the next 4 rings carry it.
        /// </summary>
        public const BodyFlags CameraTarget = (BodyFlags)(1 << 5);

        /// <summary>
        /// The body fades with its extent beyond the safe zone (<see cref="SafeZoneFade"/>). M01 «Сжатие»: «Кольца за краем
        /// безопасной зоны рисуются с затуханием» — the mode sets it on every ring.
        /// </summary>
        public const BodyFlags FadeOutsideSafeZone = (BodyFlags)(1 << 6);
    }

    /// <summary>
    /// Fade of a body flagged <see cref="RenderBodyFlags.FadeOutsideSafeZone"/>: fully opaque while its bounding circle stays
    /// inside the circle inscribed in the safe zone (radius <see cref="StartRadius"/> = half the safe-zone width, 4 m, around
    /// the arena centre), then the alpha falls linearly to <see cref="MinAlpha"/> over <see cref="Length"/> metres of
    /// overhang. Pure function of the frame, so preview and export agree.
    /// </summary>
    public static class SafeZoneFade
    {
        /// <summary>Radius around the arena centre that is always fully visible: half the safe-zone width (4 m).</summary>
        public const float StartRadius = SimConstants.SafeZoneWidth * 0.5f;
        /// <summary>Overhang beyond <see cref="StartRadius"/> at which the fade reaches <see cref="MinAlpha"/>, metres.</summary>
        public const float Length = 4f;
        /// <summary>Alpha of bodies far outside the safe zone (they stay faintly visible, as a hint of what comes next).</summary>
        public const float MinAlpha = 0.2f;

        /// <summary>Alpha factor in [<see cref="MinAlpha"/>, 1] for a body at (x, y) with bounding radius <paramref name="extent"/>.</summary>
        public static float Alpha(float x, float y, float extent)
        {
            float reach = MathF.Sqrt(x * x + y * y) + Math.Max(0f, extent);
            float over = reach - StartRadius;
            if (!(over > 0f)) return 1f;
            float t = over >= Length ? 1f : over / Length;
            return 1f - (1f - MinAlpha) * t;
        }
    }
}
