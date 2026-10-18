using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Audio;

namespace Ballverse.Core.Tests.Audio
{
    internal static class AudioTestUtil
    {
        /// <summary>Simple test pack: every event type plays "t/{type}" at root pitch; i0 = 1, i1 = 11.</summary>
        public static SoundPack SimplePack(float i0 = 1f, float i1 = 11f, string sample = "t/tone")
        {
            var p = new SoundPack { Id = SoundPackId.Notes, I0 = i0, I1 = i1 };
            p.Rules.Add(new SoundRule(SoundEventType.Hit, sample) { RootMidi = 60 });
            p.Rules.Add(new SoundRule(SoundEventType.Finish, sample) { RootMidi = 60, ImpulseVolume = false });
            return p;
        }

        /// <summary>A bank with a decaying sine for every sample id used by the built-in packs plus the test ids.</summary>
        public static InMemorySampleBank FullBank(int baseFrames = 2400)
        {
            var bank = new InMemorySampleBank();
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var id in SoundPackCatalog.Ids)
            {
                foreach (var r in SoundPackCatalog.Get(id).Rules)
                {
                    foreach (var s in r.Samples) ids.Add(s);
                }
            }
            int k = 0;
            foreach (var id in ids)
            {
                bool stereo = (k % 3) == 2;
                bank.Add(id, DecayingSine(220.0 + 37.0 * k, baseFrames + 300 * (k % 7), stereo));
                k++;
            }
            bank.Add("t/tone", DecayingSine(440.0, 4800, false));
            return bank;
        }

        public static AudioSample DecayingSine(double freq, int frames, bool stereo)
        {
            int ch = stereo ? 2 : 1;
            var d = new float[frames * ch];
            for (int i = 0; i < frames; i++)
            {
                double env = 1.0 - (double)i / frames;
                double s = Math.Sin(2 * Math.PI * freq * i / AudioConstants.SampleRate) * env * 0.5;
                if (stereo)
                {
                    d[2 * i] = (float)s;
                    d[2 * i + 1] = (float)(s * 0.7);
                }
                else d[i] = (float)s;
            }
            return new AudioSample(d, ch);
        }

        /// <summary>Constant sample (all ones) of the given length — makes gains directly observable.</summary>
        public static AudioSample Constant(int frames, float value = 1f)
        {
            var d = new float[frames];
            for (int i = 0; i < frames; i++) d[i] = value;
            return new AudioSample(d, 1);
        }

        public static List<SoundEvent> RandomEvents(ref Rng rng, int count, int maxTick, int bodies)
        {
            var list = new List<SoundEvent>(count);
            double t = 0;
            for (int i = 0; i < count; i++)
            {
                t += rng.NextDouble() * 2.0 * maxTick / count;
                int tick = Math.Min(maxTick, (int)t);
                var type = (SoundEventType)rng.NextInt(0, 7);
                list.Add(new SoundEvent(tick, type, rng.NextInt(-1, bodies), rng.NextFloat(0f, 8f), rng.NextFloat(0.05f, 1.6f), rng.NextFloat(-1.2f, 1.2f)));
            }
            return list;
        }

        public static float[] Sine(double freq, double amplitude, int frames, int channels, bool[] activeChannels = null, double phase = 0)
        {
            var d = new float[frames * channels];
            for (int i = 0; i < frames; i++)
            {
                float s = (float)(amplitude * Math.Sin(2 * Math.PI * freq * i / AudioConstants.SampleRate + phase));
                for (int c = 0; c < channels; c++)
                {
                    if (activeChannels == null || activeChannels[c]) d[i * channels + c] = s;
                }
            }
            return d;
        }

        public static double DbToAmp(double db)
        {
            return Math.Pow(10, db / 20);
        }

        public static void AssertBitIdentical(float[] expected, float[] actual)
        {
            Xunit.Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                if (BitConverter.SingleToInt32Bits(expected[i]) != BitConverter.SingleToInt32Bits(actual[i]))
                    throw new Xunit.Sdk.XunitException("Sample " + i + " differs: " + expected[i] + " vs " + actual[i]);
            }
        }
    }
}
