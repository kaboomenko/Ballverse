using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;
using Xunit;

namespace Ballverse.Core.Tests.Modes
{
    /// <summary>
    /// Generic checks of the mode contract (Framework/CONTRACT.md §6) for any <see cref="IModeDefinition"/>: M01 uses them here;
    /// the M02 / M05 tracks call them from their own test folders.
    /// </summary>
    public static class ModeConformance
    {
        /// <summary>A recorded run: replay bytes, render stream bytes, hashes, render events, outcome.</summary>
        public sealed class Recording
        {
            public byte[] Replay;
            public byte[] RenderStream;
            public List<ReplayStateHash> Hashes;
            public RenderEventLog RenderEvents;
            public List<Ballverse.Core.Audio.SoundEvent> SoundEvents;
            public ModeSessionOutcome Outcome;
            public ModeSession Session;
        }

        /// <summary>
        /// Runs a session with every sink for up to <paramref name="maxTicks"/> ticks (or to the end), calling
        /// <paramref name="beforeTick"/> before each tick (live edits, inputs).
        /// </summary>
        public static Recording Record(IModeDefinition mode, ParamSet values, ulong seed, int maxTicks, Action<ModeSession> beforeTick = null)
        {
            var replay = new MemoryStream();
            var bvs = new MemoryStream();
            var rec = new Recording
            {
                Hashes = new List<ReplayStateHash>(),
                RenderEvents = new RenderEventLog(),
                SoundEvents = new List<Ballverse.Core.Audio.SoundEvent>(),
            };
            var session = new ModeSession(mode, values, seed, new ModeSessionOptions
            {
                Replay = replay,
                RenderStream = bvs,
                HashLog = rec.Hashes,
                RenderEvents = rec.RenderEvents,
                SoundEvents = rec.SoundEvents,
                AppVersion = "test",
            });
            session.Start();
            while (session.Tick < maxTicks && !session.IsFinished)
            {
                beforeTick?.Invoke(session);
                if (!session.Step()) break;
            }
            rec.Outcome = session.Complete();
            rec.Session = session;
            rec.Replay = replay.ToArray();
            rec.RenderStream = bvs.ToArray();
            return rec;
        }

        /// <summary>Same values and seed twice → identical hashes, replay bytes and render-stream bytes (spec §7.4 rule 8).</summary>
        public static Recording AssertDeterministic(IModeDefinition mode, ParamSet values, ulong seed, int maxTicks, Action<ModeSession> beforeTick = null)
        {
            Recording a = Record(mode, values, seed, maxTicks, beforeTick);
            Recording b = Record(mode, values, seed, maxTicks, beforeTick);
            Assert.NotEmpty(a.Hashes);
            Assert.Equal(a.Hashes, b.Hashes);
            Assert.Equal(a.Replay, b.Replay);
            Assert.Equal(a.RenderStream, b.RenderStream);
            Assert.Equal(a.RenderEvents.Count, b.RenderEvents.Count);
            return a;
        }

        /// <summary>Re-simulating the recorded replay verifies every HASH and regenerates an identical render stream.</summary>
        public static ModeReplayResult AssertReplayRoundTrip(IModeDefinition mode, Recording rec)
        {
            ReplayFile file = ReplayReader.Read(rec.Replay);
            Assert.Equal(mode.Info.Id, file.Header.ModeId);
            Assert.Equal(mode.SimVersion, file.Header.ModeSimVersion);
            Assert.Equal(PhysicsBackend.CoreSim, file.Header.PhysicsBackend);
            Assert.Equal(rec.Hashes, file.Hashes);
            var bvs = new MemoryStream();
            var log = new RenderEventLog();
            var sound = new List<Ballverse.Core.Audio.SoundEvent>();
            ModeReplayResult r = ModeReplay.Resimulate(mode, file, new ModeReplayOptions { RenderStream = bvs, RenderEvents = log, SoundEvents = sound });
            Assert.True(r.IsVerified, r.Message);
            Assert.True(r.ParamsExact);
            Assert.True(r.ResultMatches);
            Assert.Equal(rec.Hashes.Count, r.Verification.VerifiedCount);
            Assert.Equal(rec.RenderStream, bvs.ToArray());
            Assert.Equal(rec.RenderEvents.Count, log.Count);
            Assert.Equal(rec.SoundEvents, sound);
            Assert.Equal((int)file.Header.DurationTicks, r.Ticks);
            return r;
        }

        /// <summary>The render stream reads back with one frame per tick and the scene's quantization.</summary>
        public static RenderStreamReader AssertRenderStreamReadable(IModeDefinition mode, Recording rec)
        {
            var reader = new RenderStreamReader(new MemoryStream(rec.RenderStream));
            Assert.Equal(mode.Info.Id, reader.Header.ModeId);
            Assert.Equal(rec.Session.Scene.Quantization, reader.Header.Quantization);
            Assert.Equal(rec.Session.Tick + 1, reader.TickCount);
            Assert.Equal(rec.Session.Visuals.ShapeCount, reader.Shapes.Count);
            for (int i = 0; i < reader.Shapes.Count; i++) Assert.Equal(rec.Session.Visuals.GetShape(i), reader.Shapes[i]);
            RenderFrame last = reader.ReadFrame(reader.TickCount - 1);
            var expected = new RenderFrame();
            expected.CopyFrom(rec.Session.Frame);
            expected.Sort();
            Assert.True(expected.ContentEquals(last), "last frame differs from the live frame");
            return reader;
        }

        /// <summary>Every built-in preset applies without a cross-parameter fix and is runnable.</summary>
        public static void AssertPresetsValid(IModeDefinition mode)
        {
            Assert.InRange(mode.BuiltInPresets.Count, 4, 8);
            var ids = new HashSet<string>();
            foreach (var preset in mode.BuiltInPresets)
            {
                Assert.True(ids.Add(preset.Id), "duplicate preset id " + preset.Id);
                Assert.Equal(mode.Info.Id, preset.ModeId);
                foreach (DeviceTier tier in new[] { DeviceTier.A, DeviceTier.B, DeviceTier.C })
                {
                    SanitizeResult r = ModeParams.ApplyPreset(mode, preset, tier);
                    foreach (ParamIssue issue in r.Issues)
                    {
                        // Tier caps on lower tiers are expected (budgets); anything else means the preset is invalid as authored.
                        if (tier == DeviceTier.A || issue.Code != ParamIssueCode.TierCapped)
                            Assert.True(issue.Code == ParamIssueCode.Missing, preset.Id + " on " + tier + ": " + issue);
                    }
                    Assert.True(ModeParams.IsRunnable(mode, r.Values, out string problem), preset.Id + ": " + problem);
                }
            }
        }

        /// <summary>
        /// <paramref name="count"/> random raw sets (uniform over every number range and option) after <see cref="ModeParams.Prepare"/>
        /// are runnable, and preparing them again changes nothing (idempotent). Returns the prepared sets for further rule checks.
        /// </summary>
        public static List<ParamSet> AssertRandomSetsPrepare(IModeDefinition mode, int count, ulong seed)
        {
            var rng = Rng.FromSeed(seed, 99);
            var result = new List<ParamSet>(count);
            for (int i = 0; i < count; i++)
            {
                ParamSet raw = RandomSet(mode.Schema, ref rng);
                var tier = (DeviceTier)rng.NextInt(0, 3);
                SanitizeResult prepared = ModeParams.Prepare(mode, raw, tier);
                Assert.True(ModeParams.IsRunnable(mode, prepared.Values, out string problem), "set " + i + ": " + problem + " " + prepared.Values);
                SanitizeResult again = ModeParams.Prepare(mode, prepared.Values, tier);
                Assert.False(again.HasIssues, "not idempotent for set " + i + ": " + (again.HasIssues ? again.Issues[0].ToString() : ""));
                Assert.Equal(prepared.Values, again.Values);
                result.Add(prepared.Values);
            }
            return result;
        }

        /// <summary>Uniform random raw values for every float / int / bool / enum parameter of a schema (others keep their default).</summary>
        public static ParamSet RandomSet(ParamSchema schema, ref Rng rng)
        {
            var pairs = new List<KeyValuePair<string, ParamValue>>();
            foreach (ParamDef d in schema.Params)
            {
                ParamValue v;
                switch (d)
                {
                    case FloatParam f: v = ParamValue.Float(f.Min + (f.Max - f.Min) * rng.NextDouble()); break;
                    case IntParam n:
                        v = n.AllowUnlimited && rng.NextInt(0, 4) == 0 ? ParamValue.Unlimited : ParamValue.Int(rng.NextInt(n.Min, n.Max + 1));
                        break;
                    case BoolParam _: v = ParamValue.Bool(rng.NextBool()); break;
                    case OptionParam o: v = ParamValue.String(o.Options[rng.NextInt(0, o.Options.Count)]); break;
                    default: v = d.Default; break;
                }
                pairs.Add(new KeyValuePair<string, ParamValue>(d.Id, v));
            }
            return new ParamSet(pairs);
        }

        /// <summary>
        /// Steady-state live stepping (frames, render events, sound events, hashes; no stream sinks) allocates nothing: measured over
        /// <paramref name="measureTicks"/> ticks after <paramref name="warmupTicks"/>.
        /// </summary>
        public static long MeasureSteadyStateAllocation(IModeDefinition mode, ParamSet values, ulong seed, int warmupTicks, int measureTicks)
        {
            // A per-tick allocation shows in every attempt; a stray runtime allocation on this thread (tiered JIT, GC
            // bookkeeping under a loaded CI machine) does not repeat, so the smallest of three fresh runs is the measurement.
            long best = long.MaxValue;
            for (int attempt = 0; attempt < 3 && best != 0; attempt++)
                best = Math.Min(best, MeasureOnce(mode, values, seed, warmupTicks, measureTicks));
            return best;
        }

        static long MeasureOnce(IModeDefinition mode, ParamSet values, ulong seed, int warmupTicks, int measureTicks)
        {
            var session = new ModeSession(mode, values, seed, new ModeSessionOptions
            {
                RenderEvents = new RenderEventLog(100000),
                SoundEvents = new List<Ballverse.Core.Audio.SoundEvent>(100000),
                HashLog = new List<ReplayStateHash>(1000),
            });
            for (int i = 0; i < warmupTicks; i++) session.AdvanceDisplayFrame(60);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < measureTicks; i++) session.AdvanceDisplayFrame(60);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }
}
