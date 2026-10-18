using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core.Replay;
using Xunit;

namespace Ballverse.Core.Tests.Replay
{
    /// <summary>Residual QA findings of the merged Replay module.</summary>
    public class ReplayResidualQaTests
    {
        /// <summary>A broken collection whose Count is negative.</summary>
        sealed class NegativeCountList : IReadOnlyList<int>
        {
            public int Count => -1;
            public int this[int index] => throw new InvalidOperationException("must not be read");
            public IEnumerator<int> GetEnumerator() { yield break; }
            IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        }

        static ReplayFile RoundTrip(Action<ReplayWriter> write, uint duration = 10)
        {
            var ms = new MemoryStream();
            var w = new ReplayWriter(ms, ReplayTestUtil.Header());
            write(w);
            w.Finish(duration);
            return ReplayReader.Read(ms.ToArray());
        }

        [Fact]
        public void NegativeCountIsRejectedAndTheFileStaysReadable()
        {
            var bad = new NegativeCountList();
            ReplayFile file = RoundTrip(w =>
            {
                w.AddInput(0, 1, new[] { 5 });
                Assert.Throws<ArgumentException>(() => w.AddInput(1, 2, bad));
                w.AddInput(2, 3, new[] { 7 });
            });
            Assert.Equal(2, file.Inputs.Count);
            Assert.Equal(7, file.Inputs[1].Values[0]);
            Assert.Throws<ArgumentException>(() => new ReplayInput(0, 1, bad));
            Assert.Throws<ArgumentException>(() => new ReplayResult("", -1, bad, 0));
        }

        [Fact]
        public void MaxInputValuesBoundaryOnWriteAndRead()
        {
            var max = new int[BvrFormat.MaxInputValues];
            for (int i = 0; i < max.Length; i++) max[i] = i * 1000 - 7;
            ReplayFile file = RoundTrip(w =>
            {
                w.AddInput(0, 1, max);
                w.AddInput(new ReplayInput(1, 2, max));
                var tooMany = new List<int>(new int[BvrFormat.MaxInputValues + 1]);
                Assert.Throws<ArgumentException>(() => w.AddInput(2, 3, tooMany));
            });
            Assert.Equal(2, file.Inputs.Count);
            Assert.Equal(max, file.Inputs[0].Values);
            Assert.Equal(max, file.Inputs[1].Values);
        }

        [Fact]
        public void FeedReportsASkippedRecordedTickImmediately()
        {
            var recorded = new List<ReplayStateHash> { new ReplayStateHash(0, 10), new ReplayStateHash(60, 11), new ReplayStateHash(120, 12) };
            var v = new HashVerifier(recorded);
            Assert.True(v.Feed(0, 10));
            Assert.False(v.Feed(61, 999)); // tick 60 was passed over without a recomputed hash
            Assert.False(v.IsOk);
            HashVerificationResult r = v.Finish();
            Assert.Equal(HashMismatchKind.Missing, r.Kind);
            Assert.Equal(60u, r.Tick);
        }

        [Fact]
        public void AppVersionRejectsDel()
        {
            Assert.Throws<ArgumentException>(() => new ReplayHeader("ring_escape", 1, "1.0\u007F", PhysicsBackend.CoreSim, 1, "{}"));
            Assert.Throws<ArgumentException>(() => new ReplayHeader("ring_escape", 1, "1.0\u001F", PhysicsBackend.CoreSim, 1, "{}"));
            new ReplayHeader("ring_escape", 1, " ~", PhysicsBackend.CoreSim, 1, "{}");
        }

        [Fact]
        public void CoreSimBackendRoundTrips()
        {
            var ms = new MemoryStream();
            var header = new ReplayHeader("helix_drop", 2, "1.0.0", PhysicsBackend.CoreSim, 9, "{}");
            new ReplayWriter(ms, header).Finish(0);
            Assert.Equal(PhysicsBackend.CoreSim, ReplayReader.Read(ms.ToArray()).Header.PhysicsBackend);
        }

        [Fact]
        public void PhysicsBackendNumberingMatchesTheSpec()
        {
            // Spec §7.4 (docs/tz/07_architecture.md): "physicsBackend u8 (0 = нет, 1 = Box2D v3 — резерв волны 3, 2 = Core.Sim)";
            // the values "никогда не перенумеровываются": Box2DV3 = 1 is the value of the accepted Replay module, CoreSim = 2 was
            // added for release 1, release-1 replays write 2. §8.5: "байт physicsBackend 0 / 1 / 2 зафиксирован". QA round 2 of
            // core-integration: round 1 had renumbered these from a superseded spec draft; this test pins the bytes.
            Assert.Equal(0, (int)PhysicsBackend.None);
            Assert.Equal(1, (int)PhysicsBackend.Box2DV3);
            Assert.Equal(2, (int)PhysicsBackend.CoreSim);
            var pinned = new[] { (PhysicsBackend.None, (byte)0), (PhysicsBackend.Box2DV3, (byte)1), (PhysicsBackend.CoreSim, (byte)2) };
            foreach (var (backend, value) in pinned)
            {
                var ms = new MemoryStream();
                new ReplayWriter(ms, new ReplayHeader("m", 1, "", backend, 0, "{}")).Finish(0);
                byte[] b = ms.ToArray();
                // magic 4 + formatVersion 2 + modeId (1 + 1) + modeSimVersion 2 + appVersion (1 + 0) = offset 11.
                Assert.Equal(value, b[11]);
                Assert.Equal(backend, ReplayReader.Read(b).Header.PhysicsBackend);
            }
            // A header byte written by another build reads as the pinned enum value.
            var raw = new MemoryStream();
            new ReplayWriter(raw, new ReplayHeader("m", 1, "", PhysicsBackend.None, 0, "{}")).Finish(0);
            byte[] bytes = raw.ToArray();
            bytes[11] = 2;
            Assert.Equal(PhysicsBackend.CoreSim, ReplayReader.Read(bytes).Header.PhysicsBackend);
            bytes[11] = 1;
            Assert.Equal(PhysicsBackend.Box2DV3, ReplayReader.Read(bytes).Header.PhysicsBackend);
            // The accepted module's golden file (written from ReplayTestUtil.Header) keeps byte 1 = Box2D v3.
            Assert.Equal(PhysicsBackend.Box2DV3, ReplayTestUtil.Header().PhysicsBackend);
        }
    }
}
