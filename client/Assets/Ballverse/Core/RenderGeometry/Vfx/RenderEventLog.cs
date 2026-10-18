using System;
using System.Buffers.Binary;
using System.IO;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>
    /// All render events of a run in tick order (see RenderEventContract.md). The renderer asks for the events whose
    /// effects can still be visible at a tick (<see cref="GetWindow"/>, binary search, no allocation). The log can be
    /// saved next to the render stream (<see cref="WriteTo"/> / <see cref="ReadFrom"/>, format "BVE1") or rebuilt by
    /// re-simulating the replay, like the stream itself (spec §7.4).
    /// </summary>
    public sealed class RenderEventLog
    {
        /// <summary>Format magic "BVE1".</summary>
        public static readonly byte[] Magic = { (byte)'B', (byte)'V', (byte)'E', (byte)'1' };
        public const ushort FormatVersion = 1;
        /// <summary>Bytes per serialized event.</summary>
        public const int RecordSize = 68;
        /// <summary>Largest event count accepted when reading (allocation guard: 180 s at ~2 800 events per second).</summary>
        public const int MaxEvents = 500_000;

        RenderEvent[] _events;
        int _count;

        public RenderEventLog(int capacity = 256)
        {
            _events = new RenderEvent[Math.Max(1, capacity)];
        }

        public int Count => _count;

        public ReadOnlySpan<RenderEvent> Events => new ReadOnlySpan<RenderEvent>(_events, 0, _count);

        public void Clear() => _count = 0;

        /// <summary>
        /// Appends an event. Ticks must be non-decreasing; within a tick, ids must be unique (they seed the effect).
        /// Throws <see cref="ArgumentException"/> otherwise.
        /// </summary>
        public void Add(in RenderEvent e)
        {
            if (e.Tick < 0) throw new ArgumentOutOfRangeException(nameof(e), "Event tick must be non-negative");
            if (_count > 0)
            {
                RenderEvent last = _events[_count - 1];
                if (e.Tick < last.Tick) throw new ArgumentException("Render events must be added in tick order", nameof(e));
                if (e.Tick == last.Tick)
                {
                    for (int i = _count - 1; i >= 0 && _events[i].Tick == e.Tick; i--)
                    {
                        if (_events[i].Id == e.Id) throw new ArgumentException("Duplicate render event id " + e.Id + " at tick " + e.Tick, nameof(e));
                    }
                }
            }
            if (_count == _events.Length) Array.Resize(ref _events, _events.Length * 2);
            _events[_count++] = e;
        }

        /// <summary>Removes the events at and after <paramref name="tick"/> (a live run restarted from a tick).</summary>
        public void TruncateFrom(int tick)
        {
            _count = LowerBound(tick);
        }

        /// <summary>
        /// Events with <paramref name="tick"/> − <paramref name="lookbackTicks"/> ≤ event tick ≤ <paramref name="tick"/>.
        /// </summary>
        public ReadOnlySpan<RenderEvent> GetWindow(int tick, int lookbackTicks)
        {
            if (lookbackTicks < 0) throw new ArgumentOutOfRangeException(nameof(lookbackTicks));
            int start = LowerBound(tick - lookbackTicks);
            int end = LowerBound(tick + 1);
            return new ReadOnlySpan<RenderEvent>(_events, start, Math.Max(0, end - start));
        }

        int LowerBound(int tick)
        {
            int lo = 0, hi = _count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_events[mid].Tick < tick) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// Writes the log: magic "BVE1", u16 version, u32 count, count × 68-byte little-endian records, u64 XXH64 of all
        /// preceding bytes.
        /// </summary>
        public void WriteTo(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var hash = new XxHash64();
            var header = new byte[10];
            Magic.CopyTo(header, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), FormatVersion);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(6), (uint)_count);
            stream.Write(header, 0, header.Length);
            hash.Append(header);
            var record = new byte[RecordSize];
            for (int i = 0; i < _count; i++)
            {
                Encode(_events[i], record);
                stream.Write(record, 0, RecordSize);
                hash.Append(record);
            }
            var tail = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(tail, hash.Digest());
            stream.Write(tail, 0, 8);
        }

        /// <summary>Reads a log written by <see cref="WriteTo"/>; corrupt data throws <see cref="InvalidDataException"/>.</summary>
        public static RenderEventLog ReadFrom(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var hash = new XxHash64();
            var header = new byte[10];
            ReadExactly(stream, header);
            for (int i = 0; i < 4; i++)
            {
                if (header[i] != Magic[i]) throw new InvalidDataException("Not a render event log (bad magic)");
            }
            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4));
            if (version != FormatVersion) throw new InvalidDataException("Unsupported render event log version " + version);
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(6));
            if (count > MaxEvents) throw new InvalidDataException("Render event log too large: " + count);
            hash.Append(header);
            var log = new RenderEventLog((int)Math.Max(1, count));
            var record = new byte[RecordSize];
            for (int i = 0; i < count; i++)
            {
                ReadExactly(stream, record);
                hash.Append(record);
                RenderEvent e = Decode(record);
                if (!Enum.IsDefined(typeof(RenderEventType), e.Type)) throw new InvalidDataException("Unknown render event type " + (int)e.Type);
                try
                {
                    log.Add(e);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidDataException("Render event log is not in tick order: " + ex.Message, ex);
                }
            }
            var tail = new byte[8];
            ReadExactly(stream, tail);
            if (BinaryPrimitives.ReadUInt64LittleEndian(tail) != hash.Digest()) throw new InvalidDataException("Render event log checksum mismatch");
            return log;
        }

        static void ReadExactly(Stream s, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = s.Read(buffer, read, buffer.Length - read);
                if (n <= 0) throw new InvalidDataException("Render event log is truncated");
                read += n;
            }
        }

        static void Encode(in RenderEvent e, byte[] b)
        {
            Span<byte> s = b;
            BinaryPrimitives.WriteInt32LittleEndian(s, e.Tick);
            s[4] = (byte)e.Type;
            s[5] = e.ColorIndex;
            s[6] = 0;
            s[7] = 0;
            BinaryPrimitives.WriteInt32LittleEndian(s.Slice(8), e.Id);
            WriteFloat(s.Slice(12), e.X);
            WriteFloat(s.Slice(16), e.Y);
            WriteFloat(s.Slice(20), e.NormalX);
            WriteFloat(s.Slice(24), e.NormalY);
            WriteFloat(s.Slice(28), e.Intensity);
            WriteFloat(s.Slice(32), e.Size);
            BinaryPrimitives.WriteInt32LittleEndian(s.Slice(36), e.BodyId);
            BinaryPrimitives.WriteInt32LittleEndian(s.Slice(40), e.TargetBodyId);
            BinaryPrimitives.WriteInt32LittleEndian(s.Slice(44), e.Feature);
            WriteFloat(s.Slice(48), e.LocalAngle);
            BinaryPrimitives.WriteInt32LittleEndian(s.Slice(52), e.ShapeId);
            WriteFloat(s.Slice(56), e.Angle);
            WriteFloat(s.Slice(60), e.Scale);
            BinaryPrimitives.WriteInt32LittleEndian(s.Slice(64), 0);
        }

        static RenderEvent Decode(byte[] b)
        {
            ReadOnlySpan<byte> s = b;
            return new RenderEvent
            {
                Tick = BinaryPrimitives.ReadInt32LittleEndian(s),
                Type = (RenderEventType)s[4],
                ColorIndex = s[5],
                Id = BinaryPrimitives.ReadInt32LittleEndian(s.Slice(8)),
                X = ReadFloat(s.Slice(12)),
                Y = ReadFloat(s.Slice(16)),
                NormalX = ReadFloat(s.Slice(20)),
                NormalY = ReadFloat(s.Slice(24)),
                Intensity = ReadFloat(s.Slice(28)),
                Size = ReadFloat(s.Slice(32)),
                BodyId = BinaryPrimitives.ReadInt32LittleEndian(s.Slice(36)),
                TargetBodyId = BinaryPrimitives.ReadInt32LittleEndian(s.Slice(40)),
                Feature = BinaryPrimitives.ReadInt32LittleEndian(s.Slice(44)),
                LocalAngle = ReadFloat(s.Slice(48)),
                ShapeId = BinaryPrimitives.ReadInt32LittleEndian(s.Slice(52)),
                Angle = ReadFloat(s.Slice(56)),
                Scale = ReadFloat(s.Slice(60)),
            };
        }

        static void WriteFloat(Span<byte> s, float v) => BinaryPrimitives.WriteInt32LittleEndian(s, BitConverter.SingleToInt32Bits(v));

        static float ReadFloat(ReadOnlySpan<byte> s) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(s));
    }
}
