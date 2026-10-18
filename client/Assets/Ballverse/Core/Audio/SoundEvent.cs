namespace Ballverse.Core.Audio
{
    /// <summary>
    /// One sound-producing simulation event (spec §2.7, §7.8). The same events drive realtime playback and the offline
    /// export render, so changing the sound pack never requires re-simulation (spec §3.6, §7.8).
    /// This is the audio module's own model; the replay module's event records are mapped onto it by an adapter:
    /// <list type="bullet">
    /// <item><see cref="Type"/> converts by value (the codes equal the replay EVNT type codes).</item>
    /// <item><see cref="Tick"/> is always the simulation tick (sample-variant hashing uses it); where the sound plays on the
    /// output timeline is a separate frame (see <see cref="EventMixer"/>, <see cref="PlaybackTimeMap"/>).</item>
    /// <item>A replay event without a body (the mode's convention, replay body ids are non-negative) maps to
    /// <see cref="BodyId"/> −1.</item>
    /// <item><see cref="BodySize"/> and <see cref="Pan"/> are not in the EVNT record: realtime and export must take them from
    /// the same quantized source (render stream radius and x position, spec §7.4), otherwise size-dependent pitch can
    /// round differently and the export would not sound like the app (spec §7.8).</item>
    /// </list>
    /// </summary>
    public struct SoundEvent
    {
        /// <summary>Simulation tick (1/60 s). Starts exactly at frame <c>tick × 800</c> (spec §7.8).</summary>
        public int Tick;
        public SoundEventType Type;
        /// <summary>Emitting body. Negative = no body (exempt from the per-body rate limit, spec §2.7).</summary>
        public int BodyId;
        /// <summary>Impact impulse; drives volume via the pack's i0/i1 (spec §2.7). NaN is treated as silent.</summary>
        public float Impulse;
        /// <summary>Body size (diameter, m); drives pitch in size-dependent packs (bigger = lower).</summary>
        public float BodySize;
        /// <summary>Stereo position −1 (left) … 1 (right). Out-of-range values are clamped, NaN is centre.</summary>
        public float Pan;

        public SoundEvent(int tick, SoundEventType type, int bodyId, float impulse, float bodySize, float pan)
        {
            Tick = tick;
            Type = type;
            BodyId = bodyId;
            Impulse = impulse;
            BodySize = bodySize;
            Pan = pan;
        }

        /// <summary>Convenience for a collision.</summary>
        public static SoundEvent Hit(int tick, int bodyId, float impulse, float bodySize = 0.5f, float pan = 0f)
        {
            return new SoundEvent(tick, SoundEventType.Hit, bodyId, impulse, bodySize, pan);
        }

        public override string ToString()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return Type + "@" + Tick.ToString(c) + " body=" + BodyId.ToString(c) + " imp=" + Impulse.ToString(c)
                + " size=" + BodySize.ToString(c) + " pan=" + Pan.ToString(c);
        }
    }
}
