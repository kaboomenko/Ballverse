using System;
using System.IO;
using System.Runtime.InteropServices;
using Ballverse.Core.Studio;

namespace Ballverse.Studio
{
    /// <summary>
    /// <see cref="IPcmSource"/> over a raw PCM file in the Media <c>PcmFile</c> format (f32le, interleaved stereo, 48 kHz), read on
    /// demand so a decoded 3-minute user track (≈ 69 MB) never has to sit in memory next to the mix. Not thread-safe (one reader,
    /// e.g. the mix worker thread); dispose to close the file. Little-endian platforms only (all release-1 targets).
    /// </summary>
    public sealed class PcmFileSource : IPcmSource, IDisposable
    {
        const int BytesPerFrame = 8;
        readonly FileStream _stream;

        public PcmFileSource(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path required", nameof(path));
            if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("PCM files are little-endian");
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
            FrameCount = _stream.Length / BytesPerFrame;
        }

        public long FrameCount { get; }

        public int Read(long frame, Span<float> destination)
        {
            if (frame < 0 || frame >= FrameCount || destination.Length < 2) return 0;
            int frames = (int)Math.Min(destination.Length / 2, FrameCount - frame);
            Span<byte> bytes = MemoryMarshal.AsBytes(destination.Slice(0, frames * 2));
            long position = frame * BytesPerFrame;
            if (_stream.Position != position) _stream.Position = position;
            int total = 0;
            while (total < bytes.Length)
            {
                int n = _stream.Read(bytes.Slice(total));
                if (n <= 0) break;
                total += n;
            }
            return total / BytesPerFrame;
        }

        public void Dispose()
        {
            _stream.Dispose();
        }
    }
}
