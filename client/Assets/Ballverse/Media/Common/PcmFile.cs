using System;
using System.IO;
using Ballverse.Core.Audio;

namespace Ballverse.Media
{
    /// <summary>
    /// The raw PCM interchange format between the studio audio mix and the native encoders/decoders:
    /// headerless 32-bit float, little-endian, interleaved stereo (L, R), 48 000 Hz. Frame count = file length / 8.
    /// Pure C#, usable from any thread. Rate and channel count are the Core mix format
    /// (<see cref="AudioConstants.SampleRate"/>, <see cref="AudioConstants.Channels"/>), so the offline mix (Core) and the
    /// native encoders can never disagree; the Java (<c>AacPcmEncoder.SAMPLE_RATE</c>) and Objective-C++ plugins hard-code
    /// the same 48 kHz stereo.
    /// </summary>
    public static class PcmFile
    {
        /// <summary>48 000 Hz (<see cref="AudioConstants.SampleRate"/>).</summary>
        public const int SampleRate = AudioConstants.SampleRate;
        /// <summary>2, interleaved L/R (<see cref="AudioConstants.Channels"/>).</summary>
        public const int Channels = AudioConstants.Channels;
        public const int BytesPerFrame = Channels * sizeof(float);
        /// <summary>Suffix of the temp file used for atomic writes (deleted on any failure).</summary>
        public const string TempSuffix = ".tmp";

        /// <summary>Frames in an existing PCM file (0 if it does not exist).</summary>
        public static long CountFrames(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;
            return new FileInfo(path).Length / BytesPerFrame;
        }

        /// <summary>Writes a 48 kHz stereo buffer (e.g. the offline mix) to <paramref name="path"/> atomically.</summary>
        public static void Write(string path, PcmBuffer buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (buffer.SampleRate != SampleRate || buffer.Channels != Channels)
                throw new ArgumentException("PcmFile holds 48 kHz stereo only; convert first (got " + buffer.SampleRate + " Hz x" + buffer.Channels + ")", nameof(buffer));
            Write(path, buffer.Samples, 0, buffer.Samples.Length);
        }

        /// <summary>Writes interleaved stereo samples [offset, offset + count) atomically (temp file + rename).</summary>
        public static void Write(string path, float[] samples, int offset, int count)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (offset < 0 || count < 0 || offset + count > samples.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (count % Channels != 0) throw new ArgumentException("count must be a multiple of 2 (stereo)", nameof(count));
            EnsureLittleEndian();
            string tmp = path + TempSuffix;
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    var bytes = new byte[1 << 16];
                    int floatsPerChunk = bytes.Length / sizeof(float);
                    for (int i = 0; i < count; i += floatsPerChunk)
                    {
                        int n = Math.Min(floatsPerChunk, count - i);
                        Buffer.BlockCopy(samples, (offset + i) * sizeof(float), bytes, 0, n * sizeof(float));
                        fs.Write(bytes, 0, n * sizeof(float));
                    }
                }
                ReplaceWith(tmp, path);
            }
            catch
            {
                DeleteQuietly(tmp);
                throw;
            }
        }

        /// <summary>Replaces <paramref name="path"/> with <paramref name="tmp"/> (delete + rename).</summary>
        internal static void ReplaceWith(string tmp, string path)
        {
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>Best-effort delete used by cleanup paths; never throws.</summary>
        internal static void DeleteQuietly(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // best effort: the original exception matters more
            }
            catch (UnauthorizedAccessException)
            {
                // best effort
            }
        }

        /// <summary>Reads a whole PCM file into memory.</summary>
        public static PcmBuffer Read(string path)
        {
            EnsureLittleEndian();
            long frames = CountFrames(path);
            if (frames * Channels > int.MaxValue / 2) throw new IOException("PCM file too large to load into memory: " + path);
            var samples = new float[frames * Channels];
            if (frames > 0)
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                {
                    var bytes = new byte[1 << 16];
                    long totalBytes = frames * BytesPerFrame;
                    long done = 0;
                    while (done < totalBytes)
                    {
                        int want = (int)Math.Min(bytes.Length, totalBytes - done);
                        int got = ReadFully(fs, bytes, want);
                        if (got != want) throw new EndOfStreamException("PCM file truncated: " + path);
                        Buffer.BlockCopy(bytes, 0, samples, (int)done, got);
                        done += got;
                    }
                }
            }
            return new PcmBuffer(samples, SampleRate, Channels);
        }

        internal static int ReadFully(Stream s, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = s.Read(buffer, total, count - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }

        internal static void EnsureLittleEndian()
        {
            if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("PcmFile requires a little-endian CPU");
        }
    }
}
