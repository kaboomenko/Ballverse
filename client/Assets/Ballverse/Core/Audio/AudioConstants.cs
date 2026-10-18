namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Fixed audio constants and time conversions (spec §2.7 mixer rules, §7.8 sample bank 48 kHz).
    /// All Core audio is interleaved stereo float PCM at 48 kHz; time is measured in frames (one frame = one sample per channel).
    /// </summary>
    public static class AudioConstants
    {
        /// <summary>Sample rate of the sample bank, the mixer and the export mix (spec §7.8).</summary>
        public const int SampleRate = 48000;

        /// <summary>Output channel count of the mixer and the export mix (interleaved L, R).</summary>
        public const int Channels = 2;

        /// <summary>Frames per simulation tick: 48000 / 60 = 800 (spec §2.1, §7.8 "exact tick timestamps").</summary>
        public const int FramesPerTick = SampleRate / SimConstants.TickRate;

        /// <summary>Maximum simultaneous voices (spec §2.7).</summary>
        public const int MaxVoices = 32;

        /// <summary>At most one sound per body per 50 ms = 2400 frames (spec §2.7).</summary>
        public const int PerBodyMinIntervalFrames = SampleRate / 20;

        /// <summary>Length of the sliding rate window: 1 s (spec §2.7).</summary>
        public const int RateWindowFrames = SampleRate;

        /// <summary>At most 40 sounds per sliding second overall (spec §2.7).</summary>
        public const int MaxSoundsPerWindow = 40;

        /// <summary>At most 20 melody notes per sliding second in the Melody pack (spec §2.7).</summary>
        public const int MaxMelodyNotesPerWindow = 20;

        /// <summary>Haptic feedback at most once per 80 ms = 3840 frames (spec §2.7).</summary>
        public const int HapticsMinIntervalFrames = SampleRate * 80 / 1000;

        /// <summary>Lower clamp of the impulse volume curve v = clamp((impulse − i0) / (i1 − i0), 0.15, 1) (spec §2.7).</summary>
        public const float MinAudibleVolume = 0.15f;

        /// <summary>Frame index of the start of <paramref name="tick"/>: tick × 800.</summary>
        public static long TickToFrame(long tick)
        {
            return tick * FramesPerTick;
        }

        /// <summary>Frames for a duration in microseconds (project files use µs, spec §7.4), rounded down.</summary>
        public static long MicrosToFrames(long micros)
        {
            // 48000 / 1e6 = 6 / 125; floor division also for negative values.
            long n = micros * 6;
            long q = n / 125;
            if (n % 125 != 0 && n < 0) q--;
            return q;
        }

        /// <summary>Frames for a duration in seconds, rounded to the nearest frame; non-finite or negative input gives 0.</summary>
        public static int SecondsToFrames(double seconds)
        {
            if (double.IsNaN(seconds) || seconds <= 0) return 0;
            double f = seconds * SampleRate;
            if (f >= int.MaxValue) return int.MaxValue;
            return (int)System.Math.Floor(f + 0.5);
        }
    }
}
