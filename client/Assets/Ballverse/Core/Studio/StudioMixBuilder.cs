using System;
using System.Collections.Generic;
using Ballverse.Core.Audio;
using Ballverse.Core.Audio.Mastering;
using Ballverse.Core.Integration;
using Ballverse.Core.Timeline;

namespace Ballverse.Core.Studio
{
    /// <summary>Inputs of <see cref="StudioMixBuilder.Render"/>.</summary>
    public sealed class StudioMixInputs
    {
        /// <summary>The compiled timeline of the preview or export (its fps sets the output length).</summary>
        public StudioTimeline Timeline { get; set; }
        /// <summary>Simulation sound events (replay EVNT records converted by <see cref="ReplaySoundEvents"/>), tick order.</summary>
        public IReadOnlyList<SoundEvent> SimEvents { get; set; }
        /// <summary>Sound pack of the clip (<c>clip.soundPack</c>); null = no simulation sounds.</summary>
        public SoundPack Pack { get; set; }
        /// <summary>Decoded samples of the pack; null = no simulation sounds.</summary>
        public ISampleBank Bank { get; set; }
        /// <summary>Melody of the Melody pack (<c>clip.melody</c>), or null.</summary>
        public Melody Melody { get; set; }
        /// <summary>Decoded user-video audio from <c>trimIn</c> (48 kHz stereo), or null.</summary>
        public IPcmSource UserAudio { get; set; }
        /// <summary>Decoded music track from its start (interleaved stereo 48 kHz), or null.</summary>
        public float[] Music { get; set; }
        /// <summary>Gains, fades, ducking, normalization (<see cref="ProjectComposition.BuildMixSettings"/>).</summary>
        public TrackMixSettings Settings { get; set; }
        /// <summary>Loudness target and limiter; null = −14 LUFS / −1 dBTP.</summary>
        public NormalizationSettings Normalization { get; set; }
    }

    /// <summary>Result of <see cref="StudioMixBuilder.Render"/>.</summary>
    public sealed class StudioMixResult
    {
        /// <summary>Interleaved stereo float at 48 kHz, exactly <see cref="ExportSoundSchedule.AudioFrameCount"/> frames.</summary>
        public float[] Samples { get; }
        public NormalizationReport Report { get; }
        /// <summary>Simulation events whose sample was missing from the bank (silent but counted toward the limits).</summary>
        public int MissingSamples { get; }

        public StudioMixResult(float[] samples, NormalizationReport report, int missingSamples)
        {
            Samples = samples;
            Report = report;
            MissingSamples = missingSamples;
        }

        public long FrameCount => Samples.Length / 2;
    }

    /// <summary>
    /// The studio's offline mix (spec §3.6, §7.5 step 3, §7.8): simulation events placed on the output timeline
    /// (<see cref="ExportSoundSchedule"/>) and rendered by the same <see cref="EventMixer"/> as realtime, the user track placed by
    /// <see cref="UserAudioPlacement"/>, music, then <see cref="TrackMixer.MixAndMaster"/> (−14 LUFS, −1 dBTP). The preview plays
    /// this very buffer and the export encodes it, so they sound the same. Pure, deterministic and thread-agnostic: run it on
    /// a worker thread (180 s at 48 kHz is ≈ 69 MB per stereo track; the simulation track doubles as the output to save one).
    /// </summary>
    public static class StudioMixBuilder
    {
        public static StudioMixResult Render(StudioMixInputs inputs)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            StudioTimeline timeline = inputs.Timeline ?? throw new ArgumentException("timeline required", nameof(inputs));
            TrackMixSettings settings = inputs.Settings ?? new TrackMixSettings();
            long frames = ExportSoundSchedule.AudioFrameCount(timeline);
            if (frames * 2 > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(inputs), "mix too long");
            var output = new float[frames * 2];
            int missing = 0;
            IReadOnlyList<SoundEvent> events = inputs.SimEvents;
            if (inputs.Pack != null && inputs.Bank != null && events != null && events.Count > 0 && settings.SimGain > 0f)
            {
                ScheduledSoundEvent[] scheduled = ExportSoundSchedule.Build(timeline, events);
                var mixer = new EventMixer(inputs.Pack, inputs.Bank, inputs.Melody);
                mixer.RenderOffline(scheduled, output);
                missing = mixer.MissingSamples;
            }
            float[] user = null;
            if (inputs.UserAudio != null && settings.UserEnabled && inputs.UserAudio.FrameCount > 0)
            {
                user = new float[frames * 2];
                UserAudioPlacement.Place(timeline, inputs.UserAudio, user);
            }
            float[] music = inputs.Music;
            if (music != null && (music.Length & 1) != 0) throw new ArgumentException("music must be interleaved stereo", nameof(inputs));
            NormalizationReport report = TrackMixer.MixAndMaster(output, user ?? Array.Empty<float>(), music ?? Array.Empty<float>(),
                output, settings, inputs.Normalization);
            return new StudioMixResult(output, report, missing);
        }
    }
}
