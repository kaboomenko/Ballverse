using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Modes;
using Ballverse.Core.Modes.RingEscape;
using Ballverse.Core.Params;
using Xunit;

namespace Ballverse.Core.Tests.Modes.RingEscape
{
    /// <summary>QA round 2 minors: maxRoundBalls validation, corrupt .bvr bytes give a status. Sound: RingEscapeSoundMixTests.</summary>
    public class RingEscapeQaRound2Tests
    {
        static readonly IModeDefinition Mode = RingEscapeMode.Instance;

        static ParamSet NewBall(int ballCount, int maxRoundBalls)
        {
            return ModeParams.Defaults(Mode, DeviceTier.A)
                .With(CommonParams.BallCount, ParamValue.Int(ballCount))
                .With(RingEscapeParams.MaxRoundBalls, ParamValue.Int(maxRoundBalls))
                .With(RingEscapeParams.AfterEscape, ParamValue.String(RingEscapeParams.AfterNewBall));
        }

        [Fact]
        public void MaxRoundBallsBelowBallCountIsRaisedWithAnIssue()
        {
            SanitizeResult r = ModeParams.Prepare(Mode, NewBall(10, 4), DeviceTier.A);
            int balls = r.Values.GetInt(CommonParams.BallCount);
            Assert.True(balls >= 2);
            Assert.Equal(balls, r.Values.GetInt(RingEscapeParams.MaxRoundBalls));
            Assert.Contains(r.Issues, i => i.ParamId == RingEscapeParams.MaxRoundBalls);

            // Idempotent; a valid set has no issue.
            SanitizeResult again = ModeParams.Prepare(Mode, r.Values, DeviceTier.A);
            Assert.Empty(again.Issues);
            Assert.True(r.Values.Equals(again.Values));

            // Other variants do not read it: no issue there.
            ParamSet finale = NewBall(10, 4).With(RingEscapeParams.AfterEscape, ParamValue.String(RingEscapeParams.AfterFinale));
            Assert.DoesNotContain(ModeParams.Prepare(Mode, finale, DeviceTier.A).Issues, i => i.ParamId == RingEscapeParams.MaxRoundBalls);
        }

        [Fact]
        public void MaxRoundBallsSliderStartsAtBallCount()
        {
            ParamSet v = ModeParams.Prepare(Mode, NewBall(5, 8), DeviceTier.A).Values;
            int balls = v.GetInt(CommonParams.BallCount);
            Assert.True(Mode.TryGetDynamicRange(RingEscapeParams.MaxRoundBalls, v, DeviceTier.A, out double min, out double max));
            Assert.Equal(balls, min);
            var def = (IntParam)Mode.Schema.Get(RingEscapeParams.MaxRoundBalls);
            Assert.Equal(def.EffectiveMax(DeviceTier.A), (int)max);
            Assert.True(Mode.TryGetDynamicRange(RingEscapeParams.MaxRoundBalls, v, DeviceTier.C, out _, out double maxC));
            Assert.Equal(def.EffectiveMax(DeviceTier.C), (int)maxC);
            ParamSet finale = v.With(RingEscapeParams.AfterEscape, ParamValue.String(RingEscapeParams.AfterFinale));
            Assert.False(Mode.TryGetDynamicRange(RingEscapeParams.MaxRoundBalls, finale, DeviceTier.A, out _, out _));
        }

        [Fact]
        public void CorruptOrTruncatedReplayBytesGiveAStatusNotAnException()
        {
            var garbage = new byte[200];
            uint x = 7;
            for (int i = 0; i < garbage.Length; i++)
            {
                x = x * 1664525u + 1013904223u;
                garbage[i] = (byte)(x >> 24);
            }
            ModeReplayResult g = ModeReplay.Resimulate(Mode, garbage);
            Assert.Equal(ModeReplayStatus.Unreadable, g.Status);
            Assert.False(g.IsVerified);

            ParamSet values = ModeParams.Prepare(Mode, ModeParams.Defaults(Mode, DeviceTier.A), DeviceTier.A).Values;
            ModeConformance.Recording rec = ModeConformance.Record(Mode, values, 9, 300);
            Assert.True(ModeReplay.Resimulate(Mode, rec.Replay).Status != ModeReplayStatus.Unreadable);
            foreach (int cut in new[] { 1, 7, rec.Replay.Length / 2, rec.Replay.Length - 3 })
            {
                byte[] truncated = new byte[cut];
                Array.Copy(rec.Replay, truncated, cut);
                Assert.Equal(ModeReplayStatus.Unreadable, ModeReplay.Resimulate(Mode, truncated).Status);
            }
            Assert.Equal(ModeReplayStatus.Unreadable, ModeReplay.Resimulate(Mode, Array.Empty<byte>()).Status);
        }
    }
}
