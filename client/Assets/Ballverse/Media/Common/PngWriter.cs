using System;
using System.IO;
using System.IO.Compression;

namespace Ballverse.Media
{
    /// <summary>
    /// Minimal RGBA8 PNG encoder (filter 0, zlib via DeflateStream) used by the fake media backend to dump frames.
    /// Pure C#, thread-safe (no shared state besides an immutable CRC table).
    /// </summary>
    public static class PngWriter
    {
        static readonly uint[] CrcTable = BuildCrcTable();

        /// <summary>Writes a top-down, tightly packed RGBA32 image.</summary>
        public static void WriteRgba(string path, byte[] rgba, int width, int height)
        {
            if (rgba == null) throw new ArgumentNullException(nameof(rgba));
            if (width <= 0 || height <= 0 || rgba.Length < width * height * 4) throw new ArgumentException("buffer too small for the image size");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                var ihdr = new byte[13];
                PutU32(ihdr, 0, (uint)width);
                PutU32(ihdr, 4, (uint)height);
                ihdr[8] = 8;  // bit depth
                ihdr[9] = 6;  // colour type RGBA
                WriteChunk(fs, "IHDR", ihdr, ihdr.Length);
                byte[] idat = Compress(rgba, width, height);
                WriteChunk(fs, "IDAT", idat, idat.Length);
                WriteChunk(fs, "IEND", new byte[0], 0);
            }
        }

        static byte[] Compress(byte[] rgba, int width, int height)
        {
            int rowBytes = width * 4;
            uint a = 1, b = 0; // Adler-32 of the uncompressed (filtered) stream
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78);
                ms.WriteByte(0x01);
                using (var deflate = new DeflateStream(ms, CompressionLevel.Fastest, true))
                {
                    var filter = new byte[1];
                    for (int y = 0; y < height; y++)
                    {
                        deflate.Write(filter, 0, 1);
                        b = (b + a) % 65521; // adler of the 0 filter byte: a += 0
                        deflate.Write(rgba, y * rowBytes, rowBytes);
                        int end = y * rowBytes + rowBytes;
                        for (int i = y * rowBytes; i < end; i++)
                        {
                            a += rgba[i];
                            if (a >= 65521) a -= 65521;
                            b += a;
                            if (b >= 65521) b -= 65521;
                        }
                    }
                }
                uint adler = (b << 16) | a;
                ms.WriteByte((byte)(adler >> 24));
                ms.WriteByte((byte)(adler >> 16));
                ms.WriteByte((byte)(adler >> 8));
                ms.WriteByte((byte)adler);
                return ms.ToArray();
            }
        }

        static void WriteChunk(Stream s, string type, byte[] data, int length)
        {
            var head = new byte[8];
            PutU32(head, 0, (uint)length);
            for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
            s.Write(head, 0, 8);
            s.Write(data, 0, length);
            uint crc = 0xFFFFFFFFu;
            for (int i = 4; i < 8; i++) crc = CrcTable[(crc ^ head[i]) & 0xFF] ^ (crc >> 8);
            for (int i = 0; i < length; i++) crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            crc ^= 0xFFFFFFFFu;
            var tail = new byte[4];
            PutU32(tail, 0, crc);
            s.Write(tail, 0, 4);
        }

        static void PutU32(byte[] b, int at, uint v)
        {
            b[at] = (byte)(v >> 24);
            b[at + 1] = (byte)(v >> 16);
            b[at + 2] = (byte)(v >> 8);
            b[at + 3] = (byte)v;
        }

        static uint[] BuildCrcTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }
    }
}
