namespace Ballverse.Core.Audio
{
    /// <summary>Construction options of <see cref="EventMixer"/> (spec §7.8). Read once at construction.</summary>
    public sealed class EventMixerOptions
    {
        /// <summary>Output gain applied to every voice, clamped to 0…4 (realtime <c>sfxVolume</c>, spec §2.2). Default 1.</summary>
        public float MasterGain { get; set; } = 1f;

        /// <summary>
        /// Tick rendered at output frame 0 (e.g. the clip's <c>trimInTick</c>, spec §7.4) for tick-placed events. Events before
        /// the origin update the rate limits and the melody position exactly as in a full render; one whose sound is still
        /// playing at the origin is heard from there on, part-way through its sample. Default 0.
        /// </summary>
        public int OriginTick { get; set; }

        /// <summary>Capacity of the realtime event queue (rounded up to a power of two). Default 4096.</summary>
        public int QueueCapacity { get; set; } = 4096;
    }
}
