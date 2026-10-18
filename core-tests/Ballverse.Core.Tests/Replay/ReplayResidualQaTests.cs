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
            Assert.Equal(2, (int)PhysicsBackend.CoreSim);
        }
    }
}
