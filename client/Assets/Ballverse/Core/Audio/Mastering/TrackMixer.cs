using System;

namespace Ballverse.Core.Audio.Mastering
{
    /// <summary>
    /// Export track mix (spec §3.6, §7.8 "mix with the video and music tracks, then EBU R128 and limiter"):
    /// out = sim·simGain + user·userGain + music·musicGain·fade·duck. All tracks are interleaved stereo float at 48 kHz on
    /// the output timeline (sim and user start at output frame 0, music at <see cref="TrackMixSettings.MusicStartFrame"/>);
    /// a track shorter than the output is silent after its end, and an empty span means "no track".
    /// <para>Ducking (simple envelope follower): detector d = max(|L|, |R|) of the user track after its gain; envelope with
    /// 5 ms attack / 250 ms release; the user is active while the envelope exceeds the threshold (−40 dBFS) and for a
    /// 300 ms hold afterwards; the duck gain moves toward the depth (−10 dB) with a 50 ms time constant and back to 1 with
    /// 500 ms. Simulation sounds are never ducked (spec §3.6).</para>
    /// Deterministic; sums in double, output rounded to float once. No clipping here — <see cref="MixAndMaster"/> limits.
    /// <para>Memory (spec §7.11, 180 s ≈ 66 MB per stereo float track): <c>output</c> may be the same memory as <c>sim</c>
    /// or <c>user</c> (same start; each frame is read before it is written), so the export path needs one buffer fewer;
    /// it must not overlap <c>music</c>, which is read at a different offset. Mastering scratch does not grow with length.</para>
    /// </summary>
    public static class TrackMixer
    {
        public const double DetectorAttackMs = 5;
        public const double DetectorReleaseMs = 250;
        public const double DuckHoldMs = 300;
        public const double DuckAttackMs = 50;
        public const double DuckReleaseMs = 500;

        /// <summary>Mixes the three tracks into <paramref name="output"/> (its length sets the duration; must be even).</summary>
        public static void Mix(ReadOnlySpan<float> sim, ReadOnlySpan<float> user, ReadOnlySpan<float> music, Span<float> output,
            TrackMixSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            CheckStereo(sim.Length, nameof(sim));
            CheckStereo(user.Length, nameof(user));
            CheckStereo(music.Length, nameof(music));
            CheckStereo(output.Length, nameof(output));

            float simGain = AudioMath.SanitizeGain(settings.SimGain, 0f, TrackMixSettings.MaxTrackGain, 1f);
            float userGain = settings.UserEnabled ? AudioMath.SanitizeGain(settings.UserGain, 0f, TrackMixSettings.MaxTrackGain, 1f) : 0f;
            float musicGain = AudioMath.SanitizeGain(settings.MusicGain, 0f, TrackMixSettings.MaxTrackGain, 1f);
            int fadeIn = AudioConstants.SecondsToFrames(AudioMath.SanitizeGain(settings.MusicFadeInSeconds, 0f, TrackMixSettings.MaxFadeSeconds, 0f));
            int fadeOut = AudioConstants.SecondsToFrames(AudioMath.SanitizeGain(settings.MusicFadeOutSeconds, 0f, TrackMixSettings.MaxFadeSeconds, 0f));
            bool duck = settings.DuckMusicUnderUser && userGain > 0f && user.Length > 0 && music.Length > 0;
            double duckTarget = AudioMath.DbToGain(AudioMath.SanitizeGain(settings.DuckDepthDb, -40f, 0f, -10f));
            double threshold = AudioMath.DbToGain(AudioMath.SanitizeGain(settings.DuckThresholdDb, -90f, 0f, -40f));

            double detAtt = OnePole(DetectorAttackMs);
            double detRel = OnePole(DetectorReleaseMs);
            double duckAtt = OnePole(DuckAttackMs);
            double duckRel = OnePole(DuckReleaseMs);
            int holdFrames = (int)(DuckHoldMs * AudioConstants.SampleRate / 1000);

            int frames = output.Length / 2;
            int simFrames = sim.Length / 2;
            int userFrames = user.Length / 2;
            long musicFrames = music.Length / 2;
            long musicStart = settings.MusicStartFrame;
            long audibleStart = Math.Max(musicStart, 0);
            long musicEnd = Math.Min(frames, musicStart + musicFrames);

            double env = 0;
            double duckGain = 1.0;
            int hold = 0;

            for (int n = 0; n < frames; n++)
            {
                int o = n * 2;
                double l = 0, r = 0;
                if (n < simFrames)
                {
                    l += sim[o] * (double)simGain;
                    r += sim[o + 1] * (double)simGain;
                }

                double ul = 0, ur = 0;
                if (n < userFrames && userGain > 0f)
                {
                    ul = user[o] * (double)userGain;
                    ur = user[o + 1] * (double)userGain;
                    l += ul;
                    r += ur;
                }

                if (duck)
                {
                    double d = Math.Max(Math.Abs(ul), Math.Abs(ur));
                    env += (d > env ? detAtt : detRel) * (d - env);
                    if (env > threshold) hold = holdFrames;
                    else if (hold > 0) hold--;
                    double target = (env > threshold || hold > 0) ? duckTarget : 1.0;
                    duckGain += (target < duckGain ? duckAtt : duckRel) * (target - duckGain);
                }

                long k = n - musicStart;
                if (k >= 0 && k < musicFrames && musicGain > 0f)
                {
                    double g = musicGain * duckGain;
                    if (fadeIn > 0 && n - audibleStart < fadeIn) g *= (double)(n - audibleStart) / fadeIn;
                    if (fadeOut > 0 && musicEnd - n <= fadeOut) g *= (double)(musicEnd - n - 1) / fadeOut;
                    int mo = (int)(k * 2);
                    l += music[mo] * g;
                    r += music[mo + 1] * g;
                }

                output[o] = (float)l;
                output[o + 1] = (float)r;
            }
        }

        /// <summary>
        /// Full export audio path: <see cref="Mix"/>, then (if <see cref="TrackMixSettings.Normalize"/>) −14 LUFS
        /// normalization with the −1 dBTP limiter (<see cref="LoudnessNormalizer"/>). Without normalization the −1 dBTP
        /// limiter still runs (no loudness gain), because tracks at up to 200 % can exceed full scale and the encoder would
        /// hard-clip them (spec §3.6). <paramref name="output"/> may alias <paramref name="sim"/> or <paramref name="user"/>.
        /// </summary>
        public static NormalizationReport MixAndMaster(ReadOnlySpan<float> sim, ReadOnlySpan<float> user, ReadOnlySpan<float> music,
            Span<float> output, TrackMixSettings settings, NormalizationSettings normalization = null)
        {
            Mix(sim, user, music, output, settings);
            if (settings.Normalize) return LoudnessNormalizer.Process(output, normalization);
            normalization = normalization ?? new NormalizationSettings();
            var limiter = new PeakLimiter(normalization.CeilingDbtp, normalization.LookAheadMs, normalization.ReleaseMs);
            var report = new NormalizationReport { Input = LoudnessMeter.Measure(output) };
            var meter = new LoudnessMeter();
            report.LimiterReductionDb = limiter.CreatePipeline(AudioConstants.Channels).Run(output, 1.0, output, meter);
            report.Output = meter.GetResult();
            return report;
        }

        static double OnePole(double ms)
        {
            return 1.0 - Math.Exp(-1000.0 / (ms * AudioConstants.SampleRate));
        }

        static void CheckStereo(int length, string name)
        {
            if ((length & 1) != 0) throw new ArgumentException("interleaved stereo length must be even", name);
        }
    }
}
