using System;
using System.IO;
using System.Threading;
using NUnit.Framework;

namespace Ballverse.Media.Tests
{
    public sealed class PcmTests
    {
        static void WriteF32(string path, float[] samples)
        {
            var bytes = new byte[samples.Length * 4];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(path, bytes);
        }

        static float[] Sine(int rate, int channels, int frames, double hz)
        {
            var s = new float[frames * channels];
            for (int i = 0; i < frames; i++)
            {
                float v = (float)(0.5 * Math.Sin(2 * Math.PI * hz * i / rate));
                for (int c = 0; c < channels; c++) s[i * channels + c] = v;
            }
            return s;
        }

        [Test]
        public void ConvertFile_44k1Mono_To48kStereo_HasExactLengthAndAccurateSine()
        {
            using (var dir = new TempDir())
            {
                string input = dir.File("in.f32");
                string output = dir.File("out.pcm");
                WriteF32(input, Sine(44100, 1, 44100, 440));
                long frames = PcmConverter.ConvertFile(input, 44100, 1, output, CancellationToken.None);
                Assert.AreEqual(48000, frames);
                Assert.AreEqual(48000, PcmFile.CountFrames(output));
                PcmBuffer pcm = PcmFile.Read(output);
                double maxError = 0;
                for (int i = 1000; i < 47000; i++)
                {
                    double expected = 0.5 * Math.Sin(2 * Math.PI * 440 * i / 48000.0);
                    maxError = Math.Max(maxError, Math.Abs(pcm.Samples[2 * i] - expected));
                    Assert.AreEqual(pcm.Samples[2 * i], pcm.Samples[2 * i + 1]);
                }
                Assert.Less(maxError, 1e-3);
                Assert.IsFalse(File.Exists(output + PcmFile.TempSuffix));
            }
        }

        // QA finding 3: a cancelled conversion left "<output>.tmp" behind.
        [Test]
        public void ConvertFile_Cancelled_LeavesNoTempOrOutput()
        {
            using (var dir = new TempDir())
            {
                string input = dir.File("in.f32");
                string output = dir.File("out.pcm");
                WriteF32(input, Sine(44100, 2, 100000, 1000));
                var cts = new CancellationTokenSource();
                cts.Cancel();
                Assert.Catch<OperationCanceledException>(() => PcmConverter.ConvertFile(input, 44100, 2, output, cts.Token));
                Assert.IsFalse(File.Exists(output + PcmFile.TempSuffix), "temp file left behind");
                Assert.IsFalse(File.Exists(output));
            }
        }

        [Test]
        public void ConvertFile_InputMissing_LeavesNoTemp()
        {
            using (var dir = new TempDir())
            {
                string output = dir.File("out.pcm");
                Assert.Catch<IOException>(() => PcmConverter.ConvertFile(dir.File("missing.f32"), 44100, 2, output, CancellationToken.None));
                Assert.IsFalse(File.Exists(output + PcmFile.TempSuffix));
            }
        }

        [Test]
        public void PcmFile_UsesCoreAudioFormat()
        {
            Assert.AreEqual(Ballverse.Core.Audio.AudioConstants.SampleRate, PcmFile.SampleRate);
            Assert.AreEqual(Ballverse.Core.Audio.AudioConstants.Channels, PcmFile.Channels);
            Assert.AreEqual(48000, VideoWriterOptions.AudioSampleRate);
            Assert.AreEqual(8, PcmFile.BytesPerFrame);
        }

        [Test]
        public void PcmFile_WriteRead_RoundTrip_IsAtomic()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("a.pcm");
                var samples = new float[] { 0.1f, -0.2f, 0.3f, -0.4f };
                PcmFile.Write(path, samples, 0, samples.Length);
                PcmFile.Write(path, samples, 2, 2);
                CollectionAssert.AreEqual(new[] { 0.3f, -0.4f }, PcmFile.Read(path).Samples);
                Assert.IsFalse(File.Exists(path + PcmFile.TempSuffix));
            }
        }
    }
}
