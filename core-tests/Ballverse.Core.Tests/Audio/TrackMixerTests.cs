using System;
using Ballverse.Core.Audio;
using Ballverse.Core.Audio.Mastering;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    public class TrackMixerTests
    {
        const int Sr = AudioConstants.SampleRate;

        static float[] Const(int frames, float l, float r)
        {
            var x = new float[frames * 2];
            for (int i = 0; i < frames; i++)
            {
                x[2 * i] = l;
                x[2 * i + 1] = r;
            }
            return x;
        }

        [Fact]
        public void TrackGainsAreAppliedAndClampedTo200Percent()
        {
            var sim = Const(100, 0.1f, 0.2f);
            var user = Const(100, 0.01f, 0.02f);
            var o = new float[200];
            TrackMixer.Mix(sim, user, default, o, new TrackMixSettings { SimGain = 1.5f, UserGain = 2f });
            Assert.Equal(0.1 * 1.5 + 0.01 * 2, o[0], 6);
            Assert.Equal(0.2 * 1.5 + 0.02 * 2, o[1], 6);
            TrackMixer.Mix(sim, user, default, o, new TrackMixSettings { SimGain = 9f, UserGain = -1f });
            Assert.Equal(0.2, o[0], 6);
            TrackMixer.Mix(sim, user, default, o, new TrackMixSettings { SimGain = float.NaN, UserEnabled = false });
            Assert.Equal(0.1, o[0], 6);
        }

        [Fact]
        public void ShortTracksAreSilentAfterTheirEnd()
        {
            var o = new float[2 * 50];
            TrackMixer.Mix(Const(10, 1f, 1f), Const(20, 0.5f, 0.5f), default, o, new TrackMixSettings());
            Assert.Equal(1.5f, o[2 * 9]);
            Assert.Equal(0.5f, o[2 * 10]);
            Assert.Equal(0f, o[2 * 20]);
        }

        [Fact]
        public void MusicStartAndLinearFades()
        {
            int frames = 4 * Sr;
            var music = Const(10 * Sr, 0.5f, 0.5f);
            var o = new float[frames * 2];
            var s = new TrackMixSettings { MusicStartFrame = 1000, MusicFadeInSeconds = 1f, MusicFadeOutSeconds = 0.5f, MusicGain = 2f };
            TrackMixer.Mix(default, default, music, o, s);
            Assert.Equal(0f, o[2 * 999]);
            Assert.Equal(0f, o[2 * 1000]);
            Assert.Equal(0.5, o[2 * (1000 + 24000)], 5);
            Assert.Equal(1.0, o[2 * (1000 + 48000)], 5);
            Assert.Equal(1.0, o[2 * (frames - 24001)], 5);
            Assert.Equal(0.5, o[2 * (frames - 12001)], 4);
            Assert.Equal(0f, o[2 * (frames - 1)]);
        }

        [Fact]
        public void FadesAreClampedToTwoSecondsAndMusicCanStartInsideTheTrack()
        {
            var ramp = new float[2 * 1000];
            for (int i = 0; i < 1000; i++)
            {
                ramp[2 * i] = i / 1000f;
                ramp[2 * i + 1] = i / 1000f;
            }
            var o = new float[2 * 500];
            TrackMixer.Mix(default, default, ramp, o, new TrackMixSettings { MusicStartFrame = -10 });
            Assert.Equal(10 / 1000f, o[0]);

            var music = Const(10 * Sr, 1f, 1f);
            var o2 = new float[2 * 5 * Sr];
            TrackMixer.Mix(default, default, music, o2, new TrackMixSettings { MusicFadeInSeconds = 10f });
            Assert.Equal(0.5, o2[2 * Sr], 5); // fade clamped to 2 s
        }

        [Fact]
        public void UserVoiceDucksMusicAndReleases()
        {
            int frames = 5 * Sr;
            var music = Const(frames, 0.5f, 0.5f);
            var user = new float[frames * 2];
            for (int i = Sr; i < 3 * Sr; i++)
            {
                float v = (float)(0.1 * Math.Sin(2 * Math.PI * 300 * i / Sr));
                user[2 * i] = v;
                user[2 * i + 1] = v;
            }
            var o = new float[frames * 2];
            TrackMixer.Mix(default, user, music, o, new TrackMixSettings());

            double MusicAt(int f) => o[2 * f] - user[2 * f];
            Assert.Equal(0.5, MusicAt(Sr / 2), 5); // before the voice
            double ducked = 0.5 * Math.Pow(10, -10.0 / 20);
            Assert.InRange(MusicAt(2 * Sr), ducked * 0.98, ducked * 1.02); // during the voice
            Assert.InRange(MusicAt(3 * Sr + Sr / 10), ducked * 0.95, ducked * 1.05); // hold after the voice
            // Envelope falls under −40 dBFS ≈ 0.57 s after the voice, 0.3 s hold, then 500 ms release toward full level.
            Assert.True(MusicAt(4 * Sr) > ducked * 1.05);
            Assert.True(MusicAt(5 * Sr - 1) > MusicAt(4 * Sr));
            Assert.InRange(MusicAt(5 * Sr - 1), 0.44, 0.5);
        }

        [Fact]
        public void NoDuckingWhenDisabledOrUserMuted()
        {
            int frames = 2 * Sr;
            var music = Const(frames, 0.5f, 0.5f);
            var user = Const(frames, 0.2f, 0.2f);
            var o = new float[frames * 2];
            TrackMixer.Mix(default, user, music, o, new TrackMixSettings { DuckMusicUnderUser = false });
            Assert.Equal(0.7f, o[2 * Sr], 5);
            TrackMixer.Mix(default, user, music, o, new TrackMixSettings { UserGain = 0f });
            Assert.Equal(0.5f, o[2 * Sr], 5);
            TrackMixer.Mix(default, user, music, o, new TrackMixSettings { UserEnabled = false });
            Assert.Equal(0.5f, o[2 * Sr], 5);
        }

        [Fact]
        public void MixAndMasterNormalizesOrOnlyMeasures()
        {
            var sim = AudioTestUtil.Sine(1000, 0.1, 5 * Sr, 2);
            var o = new float[sim.Length];
            var plain = TrackMixer.MixAndMaster(sim, default, default, o, new TrackMixSettings { Normalize = false });
            Assert.InRange(plain.Output.IntegratedLufs, -20.1, -19.9);
            Assert.Equal(0.0, plain.GainDb);
            var mastered = TrackMixer.MixAndMaster(sim, default, default, o, new TrackMixSettings());
            Assert.InRange(mastered.Output.IntegratedLufs, -14.1, -13.9);
        }

        [Fact]
        public void InvalidArguments()
        {
            Assert.Throws<ArgumentException>(() => TrackMixer.Mix(new float[3], default, default, new float[4], new TrackMixSettings()));
            Assert.Throws<ArgumentException>(() => TrackMixer.Mix(default, default, default, new float[3], new TrackMixSettings()));
            Assert.Throws<ArgumentNullException>(() => TrackMixer.Mix(default, default, default, new float[4], null));
        }
    }
}
