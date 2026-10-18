namespace Ballverse.Core.Audio
{
    /// <summary>
    /// A <see cref="SoundEvent"/> with an explicit start frame on the output timeline, for playback that is not 1 tick =
    /// 800 frames: realtime timeScale 0.25–3× (spec §2.2), studio speed S 0.5–2× and finale slow-mo (spec §3.4).
    /// <see cref="SoundEvent.Tick"/> stays the simulation tick (sample-variant hashing); rate limits use
    /// <see cref="Frame"/>. Frames before 0 start the sound part-way through (see <see cref="EventMixer"/>).
    /// </summary>
    public struct ScheduledSoundEvent
    {
        public SoundEvent Event;

        /// <summary>Output frame (48 kHz) where the sound starts.</summary>
        public long Frame;

        public ScheduledSoundEvent(SoundEvent e, long frame)
        {
            Event = e;
            Frame = frame;
        }
    }
}
