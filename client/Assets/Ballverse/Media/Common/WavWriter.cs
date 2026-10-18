using System;
using System.IO;

namespace Ballverse.Media
{
    /// <summary>Converts a <see cref="PcmFile"/> (f32 stereo 48 kHz) to a 16-bit PCM WAV file. Pure C#.</summary>
    public static class WavWriter
    {
        /// <summary>Writes at most <paramref name="maxFrames"/> frames (negative = all); returns frames written.</summary>
        public static long FromPcmFile(string pcmPath, string wavPath, long maxFrames)
        {
            PcmFile.EnsureLittleEndian();
            long frames = PcmFile.CountFrames(pcmPath);
            if (maxFrames >= 0) frames = Math.Min(frames, maxFrames);
            long dataBytes = frames * PcmFile.Channels * 2;
            if (dataBytes > uint.MaxValue - 36) throw new IOException("audio too long for WAV");
            using (var input = frames > 0 ? new FileStream(pcmPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16) : null)
            using (var output = new FileStream(wavPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            using (var w = new BinaryWriter(output))
            {
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                w.Write((uint)(36 + dataBytes));
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(16u);
                w.Write((ushort)1);
                w.Write((ushort)PcmFile.Channels);
                w.Write((uint)PcmFile.SampleRate);
                w.Write((uint)(PcmFile.SampleRate * PcmFile.Channels * 2));
                w.Write((ushort)(PcmFile.Channels * 2));
                w.Write((ushort)16);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                w.Write((uint)dataBytes);
                if (input != null)
                {
                    var bytes = new byte[8192 * PcmFile.BytesPerFrame];
                    var floats = new float[8192 * PcmFile.Channels];
                    long left = frames;
                    while (left > 0)
                    {
                        int n = (int)Math.Min(8192, left);
                        int got = PcmFile.ReadFully(input, bytes, n * PcmFile.BytesPerFrame);
                        if (got != n * PcmFile.BytesPerFrame) throw new EndOfStreamException("PCM file truncated");
                        Buffer.BlockCopy(bytes, 0, floats, 0, got);
                        for (int i = 0; i < n * PcmFile.Channels; i++)
                        {
                            float v = floats[i];
                            if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                            w.Write((short)Math.Round(v * 32767f));
                        }
                        left -= n;
                    }
                }
            }
            return frames;
        }
    }
}
