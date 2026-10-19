using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Ballverse.Core.Integration;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Modes
{
    /// <summary>
    /// QA round 1: runner options and the replay (ContinueAfterFinish stops every recording sink at Finished, non-default
    /// limits are refused with a replay sink, a re-simulation that ends before the recorded duration is not verified),
    /// stored data that cannot be applied is a status, and the visual table never throws on live label data.
    /// </summary>
    public class ModeRecordingLimitsTests
    {
        static readonly IModeDefinition Fake = FakeBoxMode.Instance;

        static ParamSet Lifetime(int ticks) => Fake.Schema.CreateDefaults().With(FakeBoxMode.Lifetime, ParamValue.Int(ticks));

        [Fact]
        public void ContinueAfterFinishRecordsOnlyUpToFinished()
        {
            // Spec §2.1: Finished closes the replay; the background play-out is live-only.
            var replay = new MemoryStream();
            var bvs = new MemoryStream();
            var hashes = new List<ReplayStateHash>();
            var log = new RenderEventLog();
            var sound = new List<SoundEvent>();
            var s = new ModeSession(Fake, Lifetime(300), 4, new ModeSessionOptions
            {
                Replay = replay,
                RenderStream = bvs,
                HashLog = hashes,
                RenderEvents = log,
                SoundEvents = sound,
                Runner = new SimRunnerOptions { ContinueAfterFinish = true },
            });
            s.RunHeadless(900);
            Assert.Equal(330, s.Runner.FinishedTick);
            Assert.Equal(900, s.Tick);
            Assert.False(s.IsRecording);
            Assert.False(s.IsFinished);
            ModeSessionOutcome o = s.Complete();
            Assert.Equal(330u, o.ReplayHeader.DurationTicks);
            Assert.Equal(330, o.Run.DurationTicks);
            Assert.All(hashes, h => Assert.True(h.Tick <= 330));
            Assert.All(sound, e => Assert.True(e.Tick <= 330));
            foreach (RenderEvent e in log.Events) Assert.True(e.Tick <= 330);
            Assert.Equal(331, new RenderStreamReader(new MemoryStream(bvs.ToArray())).TickCount);

            ReplayFile file = ReplayReader.Read(replay.ToArray());
            Assert.Equal(hashes, file.Hashes);
            Assert.All(file.Events, e => Assert.True(e.Tick <= 330));
            var regenerated = new MemoryStream();
            ModeReplayResult r = ModeReplay.Resimulate(Fake, file, new ModeReplayOptions { RenderStream = regenerated });
            Assert.Equal(ModeReplayStatus.Verified, r.Status);
            Assert.Equal(330, r.Ticks);
            Assert.True(r.ResultMatches);
            Assert.Equal(bvs.ToArray(), regenerated.ToArray());
        }

        [Fact]
        public void LiveEditsAfterFinishedAreNotRecorded()
        {
            var replay = new MemoryStream();
            var s = new ModeSession(Fake, Lifetime(120), 4, new ModeSessionOptions
            {
                Replay = replay,
                BuildFrames = false,
                Runner = new SimRunnerOptions { ContinueAfterFinish = true },
            });
            s.RunHeadless(200);
            Assert.Equal(150, s.Runner.FinishedTick);
            s.SetLiveParam(FakeBoxMode.Kick, ParamValue.Float(2.0));
            s.EnqueueInput(FakeBoxMode.KickInput, 1000, 0);
            s.RunHeadless(10);
            s.Complete();
            ReplayFile file = ReplayReader.Read(replay.ToArray());
            Assert.Empty(file.ParamChanges);
            Assert.Empty(file.Inputs);
            Assert.Equal(ModeReplayStatus.Verified, ModeReplay.Resimulate(Fake, file).Status);
        }

        [Fact]
        public void ReplaySinkRequiresDefaultRunnerLimits()
        {
            Assert.Throws<ArgumentException>(() => new ModeSession(Fake, Lifetime(300), 1, new ModeSessionOptions
            {
                Replay = new MemoryStream(),
                Runner = new SimRunnerOptions { MaxTicks = 1800 },
            }));
            Assert.Throws<ArgumentException>(() => new ModeSession(Fake, Lifetime(300), 1, new ModeSessionOptions
            {
                Replay = new MemoryStream(),
                Runner = new SimRunnerOptions { FinaleReserveTicks = 60 },
            }));
            // Without a replay (generator candidates, previews) custom limits stay allowed, and so do pacing options.
            var headless = new ModeSession(Fake, Lifetime(300), 1, new ModeSessionOptions { Runner = new SimRunnerOptions { MaxTicks = 1800 } });
            headless.RunToEnd();
            new ModeSession(Fake, Lifetime(300), 1, new ModeSessionOptions
            {
                Replay = new MemoryStream(),
                Runner = new SimRunnerOptions { MaxStepsPerDisplayFrame = 4, ContinueAfterFinish = true },
            }).RunToEnd();
        }

        [Fact]
        public void ResimulationThatEndsBeforeTheRecordedDurationIsNotVerified()
        {
            // A file claiming 600 ticks for a run whose rules finish at 330 (e.g. recorded with other limits, or edited).
            ParamSet values = Lifetime(300);
            ReplayHeader h = ReplayParams.CreateHeader(Fake.Info.Id, Fake.SimVersion, "t", PhysicsBackend.CoreSim, 4, values, Fake.Schema);
            var ms = new MemoryStream();
            new ReplayWriter(ms, h).Finish(600);
            ModeReplayResult r = ModeReplay.Resimulate(Fake, ms.ToArray());
            Assert.Equal(ModeReplayStatus.DurationMismatch, r.Status);
            Assert.False(r.IsVerified);
            Assert.Equal(330, r.Ticks);
            Assert.NotNull(r.Message);

            // Recorded hashes after the point where the re-simulation stops are required (no silent partial verify).
            ModeConformance.Recording rec = ModeConformance.Record(Fake, Lifetime(900), 4, 10000);
            ReplayFile file = ReplayReader.Read(rec.Replay);
            string json300 = ReplayParams.CreateHeader(Fake.Info.Id, Fake.SimVersion, "t", PhysicsBackend.CoreSim, 4, Lifetime(300), Fake.Schema).ParamsJson;
            var shortened = new ReplayFile(new ReplayHeader(Fake.Info.Id, Fake.SimVersion, "t", PhysicsBackend.CoreSim, 4, json300, file.Header.DurationTicks),
                file.Inputs, file.ParamChanges, file.Hashes, file.Events, file.Result);
            ModeReplayResult m = ModeReplay.Resimulate(Fake, shortened);
            Assert.False(m.IsVerified);
        }

        [Fact]
        public void TrimOutVerifiesOnlyUpToTheLastTick()
        {
            ModeConformance.Recording rec = ModeConformance.Record(Fake, Lifetime(600), 4, 10000);
            ReplayFile file = ReplayReader.Read(rec.Replay);
            ModeReplayResult r = ModeReplay.Resimulate(Fake, file, new ModeReplayOptions { LastTick = 200 });
            Assert.Equal(ModeReplayStatus.Verified, r.Status);
            Assert.Equal(200, r.Ticks);
            Assert.Equal(3, r.Verification.VerifiedCount);
            Assert.True(r.ResultMatches);
        }

        [Fact]
        public void StoredDataTheRulesCannotApplyIsAStatusNotAnException()
        {
            ParamSet values = Lifetime(300);
            ReplayHeader h = ReplayParams.CreateHeader(Fake.Info.Id, Fake.SimVersion, "t", PhysicsBackend.CoreSim, 4, values, Fake.Schema);
            var ms = new MemoryStream();
            var w = new ReplayWriter(ms, h);
            w.AddInput(10, FakeBoxMode.PoisonInput, new[] { 0, 0, 0, 0 });
            w.Finish(330);
            var bvs = new MemoryStream();
            ModeReplayResult r = ModeReplay.Resimulate(Fake, ms.ToArray(), new ModeReplayOptions { RenderStream = bvs, LeaveRenderStreamOpen = true });
            Assert.Equal(ModeReplayStatus.InvalidData, r.Status);
            Assert.False(r.IsVerified);
            Assert.Contains("cannot apply", r.Message);
            Assert.True(bvs.CanWrite); // the caller's stream is left open
        }

        [Fact]
        public void TryDefineLabelRefusesInvalidStringsAndAFullTable()
        {
            var table = new ModeVisualTable();
            Assert.False(table.TryDefineLabel(null, out int none));
            Assert.Equal(BodyState.NoLabel, none);
            Assert.False(table.TryDefineLabel("", out _));
            Assert.False(table.TryDefineLabel(new string('x', 40), out _)); // longer than a render-stream label
            Assert.True(table.TryDefineLabel("a", out int a));
            Assert.Equal(1, a);
            Assert.True(table.TryDefineLabel("a", out int again));
            Assert.Equal(a, again);
            for (int i = table.LabelCount; i < RenderStreamLimits.MaxLabels; i++) Assert.True(table.TryDefineLabel("l" + i, out _));
            Assert.Equal(0, table.FreeLabelSlots);
            Assert.False(table.TryDefineLabel("one more", out int full));
            Assert.Equal(BodyState.NoLabel, full);
            Assert.True(table.TryDefineLabel("a", out int existing)); // existing labels still resolve
            Assert.Equal(a, existing);
            Assert.Throws<InvalidOperationException>(() => table.DefineLabel("one more"));
        }
    }
}
