using Ballverse.Core.Audio;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.Modes
{
    /// <summary>
    /// Sound events with body size and pan taken from the render stream's quantized values (spec §7.8; Audio
    /// <see cref="SoundEvent"/> remarks): the live run and the export (which rebuilds sound from the replay EVNT records and the
    /// stream) then compute identical pitch and pan.
    /// </summary>
    public static class ModeSound
    {
        /// <summary>Half the arena width: x at ±4.5 m pans fully left / right.</summary>
        public const float PanHalfWidth = SimConstants.ArenaWidth * 0.5f;

        /// <summary>Diameter of a ball of <paramref name="radius"/> after render-stream quantization, metres.</summary>
        public static float BodySize(RenderQuantization q, float radius)
        {
            return 2f * q.DequantizeScale(q.QuantizeScale(radius));
        }

        /// <summary>Pan −1…1 of a body at world <paramref name="x"/> after render-stream quantization.</summary>
        public static float Pan(RenderQuantization q, float x)
        {
            float p = q.DequantizePosition(q.QuantizePosition(x)) / PanHalfWidth;
            if (p < -1f) return -1f;
            return p > 1f ? 1f : p;
        }

        /// <summary>A sound event of a ball (render id <paramref name="bodyId"/>, radius, x) with the given impulse (N·s).</summary>
        public static SoundEvent ForBall(SoundEventType type, int bodyId, float impulse, RenderQuantization q, float radius, float x)
        {
            return new SoundEvent(0, type, bodyId, impulse, BodySize(q, radius), Pan(q, x));
        }

        /// <summary>A sound event without an emitting body (finale, escape chord): body id −1, centre pan.</summary>
        public static SoundEvent Bodiless(SoundEventType type, float impulse, float bodySize)
        {
            return new SoundEvent(0, type, -1, impulse, bodySize, 0f);
        }
    }
}
