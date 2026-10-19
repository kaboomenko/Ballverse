using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core.Audio;
using Ballverse.Core.Integration;
using Ballverse.Core.Params;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.Replay;
using Ballverse.Core.Sim;

namespace Ballverse.Core.Modes
{
    /// <summary>How a re-simulation ended.</summary>
    public enum ModeReplayStatus
    {
        /// <summary>Every recorded HASH matched.</summary>
        Verified = 0,
        /// <summary>A HASH differed or was not reached (see <see cref="ModeReplayResult.Verification"/>).</summary>
        HashMismatch = 1,
        /// <summary>The replay belongs to another mode.</summary>
        WrongMode = 2,
        /// <summary>
        /// The replay was written by another <see cref="IModeDefinition.SimVersion"/>: it cannot be re-simulated, its saved render
        /// stream stays the source of the clip (spec §3.9).
        /// </summary>
        SimVersionMismatch = 3,
        /// <summary>Not a Core.Sim replay (spec §7.4 physicsBackend byte).</summary>
        UnsupportedBackend = 4,
        /// <summary>The stored parameters cannot start a run (hand-made or corrupted file).</summary>
        InvalidParams = 5,
    }

    /// <summary>Sinks of a re-simulation (all optional): the regenerated render stream and visual caches.</summary>
    public sealed class ModeReplayOptions
    {
        /// <summary>Destination of the regenerated <c>.bvs</c> (spec §7.4: the stream is a cache rebuilt from the replay).</summary>
        public Stream RenderStream;
        public bool LeaveRenderStreamOpen = true;
        public RenderEventLog RenderEvents;
        public List<SoundEvent> SoundEvents;
        /// <summary>Stop at the first mismatching hash (default) instead of simulating to the end.</summary>
        public bool StopAtFirstMismatch = true;
        /// <summary>Re-simulate only up to this tick (e.g. a clip's trim-out); null = the whole replay.</summary>
        public int? LastTick;
    }

    /// <summary>Result of <see cref="ModeReplay.Resimulate"/>.</summary>
    public sealed class ModeReplayResult
    {
        internal ModeReplayResult(ModeReplayStatus status, string message, HashVerificationResult verification, bool paramsExact,
            int ticks, SimResult result, ReplayResult replayResult, bool resultMatches)
        {
            Status = status;
            Message = message;
            Verification = verification;
            ParamsExact = paramsExact;
            Ticks = ticks;
            Result = result;
            ReplayResult = replayResult;
            ResultMatches = resultMatches;
        }

        public ModeReplayStatus Status { get; }

        public bool IsVerified => Status == ModeReplayStatus.Verified;

        /// <summary>Human-readable reason when not verified (for logs), else null.</summary>
        public string Message { get; }

        public HashVerificationResult Verification { get; }

        /// <summary>The header's paramsHash equals the re-canonicalized parameters (the schema did not change since recording).</summary>
        public bool ParamsExact { get; }

        public int Ticks { get; }

        public SimResult Result { get; }

        /// <summary>RSLT of the re-simulation (null when the run had not reached its finale at the last tick).</summary>
        public ReplayResult ReplayResult { get; }

        /// <summary>The re-simulated RSLT equals the recorded one (true when the file has none and the run produced none).</summary>
        public bool ResultMatches { get; }
    }

    /// <summary>
    /// Replay re-simulation (spec §7.4 "восстанавливать его из реплея пересчётом с проверкой хэшей", §8.5): rebuilds the run
    /// from the header's parameters and seed, applies the recorded INPT inputs and PARM changes at their ticks, verifies every
    /// HASH record with <see cref="HashVerifier"/> and optionally regenerates the render stream, render events and sound events.
    /// </summary>
    public static class ModeReplay
    {
        public static ModeReplayResult Resimulate(IModeDefinition mode, ReplayFile replay, ModeReplayOptions options = null)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            if (replay == null) throw new ArgumentNullException(nameof(replay));
            options = options ?? new ModeReplayOptions();
            ReplayHeader header = replay.Header;
            var none = new HashVerificationResult(HashMismatchKind.None, 0, 0, 0, 0);
            if (!string.Equals(header.ModeId, mode.Info.Id, StringComparison.Ordinal))
                return Fail(ModeReplayStatus.WrongMode, "replay of '" + header.ModeId + "', not '" + mode.Info.Id + "'", none);
            if (header.ModeSimVersion != mode.SimVersion)
                return Fail(ModeReplayStatus.SimVersionMismatch, "sim version " + header.ModeSimVersion + " != " + mode.SimVersion, none);
            if (header.PhysicsBackend != PhysicsBackend.CoreSim)
                return Fail(ModeReplayStatus.UnsupportedBackend, "physics backend " + (int)header.PhysicsBackend, none);

            ReplayParamsResult stored = ReplayParams.ReadParams(header, mode.Schema);
            if (!ModeParams.IsRunnable(mode, stored.Values, out string problem))
                return Fail(ModeReplayStatus.InvalidParams, "stored parameters are " + problem, none);

            int duration = (int)Math.Min(header.DurationTicks, (uint)SimConstants.MaxDurationTicks);
            if (options.LastTick.HasValue) duration = Math.Max(0, Math.Min(duration, options.LastTick.Value));

            var session = new ModeSession(mode, stored.Values, header.Seed, new ModeSessionOptions
            {
                RenderStream = options.RenderStream,
                LeaveRenderStreamOpen = options.LeaveRenderStreamOpen,
                RenderEvents = options.RenderEvents,
                SoundEvents = options.SoundEvents,
                BuildFrames = options.RenderStream != null,
            });
            foreach (ReplayInput input in replay.Inputs)
            {
                if (input.Tick > (uint)duration) break;
                IReadOnlyList<int> v = input.Values;
                session.EnqueueRecordedInput(new SimInput((int)input.Tick, input.Type,
                    v.Count > 0 ? v[0] : 0, v.Count > 1 ? v[1] : 0, v.Count > 2 ? v[2] : 0, v.Count > 3 ? v[3] : 0));
            }

            var verifier = new HashVerifier(replay.Hashes);
            IReadOnlyList<ReplayParamChange> changes = replay.ParamChanges;
            int nextChange = 0;
            session.Start();
            while (session.Tick < duration && !session.IsFinished)
            {
                while (nextChange < changes.Count && changes[nextChange].Tick <= (uint)session.Tick)
                {
                    ReplayParamChange c = changes[nextChange++];
                    if (c.Tick == (uint)session.Tick && ReplayParams.TryReadParamChange(c, mode.Schema, out ParamValue value, out _))
                        session.QueueRecordedParam(c.ParamId, value);
                }
                if (!session.Step()) break;
                if (session.LastHashTick == session.Tick)
                {
                    bool ok = verifier.Feed((uint)session.Tick, session.LastHash);
                    if (!ok && options.StopAtFirstMismatch) break;
                }
            }

            HashVerificationResult verification = verifier.Finish((uint)session.Tick);
            ModeSessionOutcome outcome = session.Complete();
            bool resultMatches = replay.Result == null ? outcome.ReplayResult == null : replay.Result.Equals(outcome.ReplayResult);
            // A partial re-simulation (trim-out before the end) cannot reproduce the final result; nothing to compare.
            if (session.Tick < (int)header.DurationTicks) resultMatches = verification.IsMatch;
            ModeReplayStatus status = verification.IsMatch ? ModeReplayStatus.Verified : ModeReplayStatus.HashMismatch;
            return new ModeReplayResult(status, verification.IsMatch ? null : verification.ToString(), verification, stored.IsExact,
                session.Tick, outcome.Result, outcome.ReplayResult, resultMatches);
        }

        /// <summary>Reads a <c>.bvr</c> and re-simulates it (see <see cref="Resimulate(IModeDefinition, ReplayFile, ModeReplayOptions)"/>).</summary>
        public static ModeReplayResult Resimulate(IModeDefinition mode, byte[] bvr, ModeReplayOptions options = null)
        {
            if (bvr == null) throw new ArgumentNullException(nameof(bvr));
            return Resimulate(mode, ReplayReader.Read(bvr), options);
        }

        static ModeReplayResult Fail(ModeReplayStatus status, string message, HashVerificationResult none)
        {
            return new ModeReplayResult(status, message, none, false, 0, SimResult.None, null, false);
        }
    }
}
