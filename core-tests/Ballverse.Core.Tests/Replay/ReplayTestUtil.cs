using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ballverse.Core;
using Ballverse.Core.Replay;

namespace Ballverse.Core.Tests.Replay
{
    /// <summary>Builders and stream wrappers shared by the replay tests.</summary>
    internal static class ReplayTestUtil
    {
        internal const string ParamsJson = "{\"ballCount\":12,\"speed\":1.5,\"theme\":\"neon\"}";

        internal static ReplayHeader Header(uint duration = 0)
        {
            return new ReplayHeader("ring_escape", 3, "1.0.0 (42)", PhysicsBackend.Box2DV3, 0x0123456789ABCDEFUL, ParamsJson, duration);
        }

        /// <summary>A small replay that uses every record type, with negative values, equal ticks and edge values.</summary>
        internal static ReplayFile Sample()
        {
            var inputs = new List<ReplayInput>
            {
                new ReplayInput(0, 1, new[] { 0 }),
                new ReplayInput(10, 2, new[] { -4500, 8000, int.MinValue, int.MaxValue }),
                new ReplayInput(10, 3, null),
                new ReplayInput(600, 255, new[] { 65535 }),
            };
            var parms = new List<ReplayParamChange>
            {
                new ReplayParamChange(5, "speed", "2.0"),
                new ReplayParamChange(5, "theme", "\"retro\""),
                new ReplayParamChange(700, "gravity", "{\"x\":0,\"y\":-9.81}"),
            };
            var hashes = new List<ReplayStateHash>
            {
                new ReplayStateHash(0, 0UL),
                new ReplayStateHash(60, ulong.MaxValue),
                new ReplayStateHash(120, 0xDEADBEEFCAFEBABEUL),
            };
            var events = new List<ReplayEvent>
            {
                new ReplayEvent(1, ReplayEventType.Hit, 0, 100, 0),
                new ReplayEvent(1, ReplayEventType.Break, 7, 0, -1),
                new ReplayEvent(2, ReplayEventType.Eliminate, int.MaxValue, ushort.MaxValue, long.MinValue),
                new ReplayEvent(3, ReplayEventType.Finish, 3, 1, long.MaxValue),
                new ReplayEvent(4, ReplayEventType.Spawn, 300, 2, 5),
                new ReplayEvent(5, ReplayEventType.Despawn, 300, 3, 6),
                new ReplayEvent(6, ReplayEventType.Score, 1, 4, 10),
                new ReplayEvent(800, ReplayEventType.Custom, 2, 5, 42),
            };
            var result = new ReplayResult("team_red", 2, new[] { 3, -1, 17 }, 790);
            return new ReplayFile(Header(800), inputs, parms, hashes, events, result);
        }

        /// <summary>Random but valid replay driven by <see cref="Rng"/>.</summary>
        internal static ReplayFile Random(ulong seed, int records)
        {
            var rng = Rng.FromSeed(seed, 7);
            uint duration = (uint)rng.NextInt(1, SimConstants.MaxDurationTicks);
            var inputs = new List<ReplayInput>();
            var parms = new List<ReplayParamChange>();
            var hashes = new List<ReplayStateHash>();
            var events = new List<ReplayEvent>();
            uint ti = 0, tp = 0, th = 0, te = 0;
            for (int i = 0; i < records; i++)
            {
                switch (rng.NextInt(0, 4))
                {
                    case 0:
                    {
                        ti = Step(ref rng, ti, duration);
                        var vals = new int[rng.NextInt(0, 6)];
                        for (int k = 0; k < vals.Length; k++) vals[k] = (int)rng.NextUInt();
                        inputs.Add(new ReplayInput(ti, (byte)rng.NextUInt(256), vals));
                        break;
                    }
                    case 1:
                        tp = Step(ref rng, tp, duration);
                        parms.Add(new ReplayParamChange(tp, "p" + rng.NextInt(0, 10), rng.NextInt(-1000, 1000).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                        break;
                    case 2:
                        th = Step(ref rng, th, duration);
                        hashes.Add(new ReplayStateHash(th, rng.NextULong()));
                        break;
                    default:
                        te = Step(ref rng, te, duration);
                        events.Add(new ReplayEvent(te, (ReplayEventType)rng.NextInt(0, 8), rng.NextInt(0, 5000),
                            (ushort)rng.NextUInt(65536), (long)rng.NextULong()));
                        break;
                }
            }
            ReplayResult result = rng.NextBool()
                ? new ReplayResult("w" + rng.NextInt(0, 9), rng.NextInt(-1, 9), new[] { rng.NextInt(-5, 50), rng.NextInt(0, 50) }, duration)
                : null;
            var header = new ReplayHeader("marble_race", (ushort)rng.NextUInt(65536), "1.2.3", PhysicsBackend.Box2DV3, rng.NextULong(),
                "{\"n\":" + rng.NextInt(0, 100) + "}", duration);
            return new ReplayFile(header, inputs, parms, hashes, events, result);
        }

        static uint Step(ref Rng rng, uint tick, uint duration)
        {
            uint next = tick + rng.NextUInt(4);
            return next > duration ? duration : next;
        }

        /// <summary>Header-only file bytes (via the writer) with raw chunks appended.</summary>
        internal static byte[] WithRawChunks(uint duration, params byte[][] chunks)
        {
            var ms = new MemoryStream();
            new ReplayWriter(ms, Header()).Finish(duration);
            foreach (var c in chunks) ms.Write(c, 0, c.Length);
            return ms.ToArray();
        }

        internal static byte[] Chunk(string tag, byte[] payload)
        {
            var ms = new MemoryStream();
            ms.Write(Encoding.ASCII.GetBytes(tag), 0, 4);
            ms.Write(BitConverter.GetBytes((uint)payload.Length), 0, 4);
            ms.Write(payload, 0, payload.Length);
            return ms.ToArray();
        }

        internal static byte[] Bytes(params int[] values)
        {
            var b = new byte[values.Length];
            for (int i = 0; i < values.Length; i++) b[i] = (byte)values[i];
            return b;
        }

        internal static void AssertEqual(ReplayFile expected, ReplayFile actual)
        {
            Xunit.Assert.Equal(expected.Header.ModeId, actual.Header.ModeId);
            Xunit.Assert.Equal(expected.Header.ModeSimVersion, actual.Header.ModeSimVersion);
            Xunit.Assert.Equal(expected.Header.AppVersion, actual.Header.AppVersion);
            Xunit.Assert.Equal(expected.Header.PhysicsBackend, actual.Header.PhysicsBackend);
            Xunit.Assert.Equal(expected.Header.Seed, actual.Header.Seed);
            Xunit.Assert.Equal(expected.Header.ParamsJson, actual.Header.ParamsJson);
            Xunit.Assert.Equal(expected.Header.ParamsHash, actual.Header.ParamsHash);
            Xunit.Assert.Equal(expected.Header.DurationTicks, actual.Header.DurationTicks);
            Xunit.Assert.Equal(expected.Inputs, actual.Inputs);
            Xunit.Assert.Equal(expected.ParamChanges, actual.ParamChanges);
            Xunit.Assert.Equal(expected.Hashes, actual.Hashes);
            Xunit.Assert.Equal(expected.Events, actual.Events);
            Xunit.Assert.Equal(expected.Result, actual.Result);
        }
    }

    /// <summary>Wraps a stream and hides seeking; optionally returns at most one byte per Read.</summary>
    internal sealed class NonSeekableStream : Stream
    {
        readonly Stream _inner;
        readonly bool _trickle;

        internal NonSeekableStream(Stream inner, bool trickle = false)
        {
            _inner = inner;
            _trickle = trickle;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, _trickle ? Math.Min(count, 1) : count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
        }
    }
}
