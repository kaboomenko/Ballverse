using System;
using Ballverse.Core.Generation;
using Xunit;

namespace Ballverse.Core.Tests.Generation
{
    /// <summary>
    /// Release-1 generation rules found by QA of the core-integration track (round 1): the M05 auto/player split, w1 = 0 for the
    /// player control and the M01 "last second" director goal.
    /// </summary>
    public class GenerationReleaseOneTests
    {
        static CandidateEvaluation EvaluateOne(GenerationSessionOptions o, CandidateRunSummary summary)
        {
            var session = new GenerationSession(o);
            GenerationStep step = session.NextRequest();
            Assert.True(step.IsRun);
            session.Report(step.CandidateIndex, summary, 10);
            return Assert.Single(session.Evaluations);
        }

        // 3 events per second: inside the M05 density range 1.5–5/s and the M01 range 1.5–8/s.
        static CandidateRunSummary OffTarget => Runs.Make(durationTicks: 1200, eventEveryTicks: 20);

        [Fact]
        public void M05AutoControlScoresTheTargetDuration()
        {
            // Mutant I11 (QA): restoring "Interactive || mode.Interactive" made every M05 auto session ignore the target and survived.
            GenerationSessionOptions auto = GenerationSessionOptions.ForMode(ModeCode.M05, DeviceTier.B, 1UL, Runs.Target);
            Assert.False(auto.Interactive);
            Assert.False(new GenerationSession(auto).IsInteractive);
            Assert.True(EvaluateOne(auto, OffTarget).Breakdown.FitDuration < 1.0);

            GenerationSessionOptions player = GenerationSessionOptions.ForMode(ModeCode.M05, DeviceTier.B, 1UL, Runs.Target);
            player.Interactive = true;
            Assert.True(new GenerationSession(player).IsInteractive);
            Assert.Equal(1.0, EvaluateOne(player, OffTarget).Breakdown.FitDuration);
        }

        [Fact]
        public void InteractiveOnlyModeIsInteractiveWithoutTheFlag()
        {
            GenerationSessionOptions m04 = GenerationSessionOptions.ForMode(ModeCode.M04, DeviceTier.B, 1UL, Runs.Target);
            m04.Interactive = false;
            var session = new GenerationSession(m04);
            Assert.True(session.IsInteractive);
            Assert.Equal(1.0, EvaluateOne(m04, OffTarget).Breakdown.FitDuration);
        }

        [Fact]
        public void M05PlayerControlScoresWithW1Zero()
        {
            // Release-1 card M05: "Вес w1 … действует в «Авто-ролике» и «Боте» и равен 0 в «Игроке»".
            GenerationSessionOptions auto = GenerationSessionOptions.ForMode(ModeCode.M05, DeviceTier.B, 1UL, Runs.Target);
            GenerationSessionOptions player = GenerationSessionOptions.ForMode(ModeCode.M05, DeviceTier.B, 1UL, Runs.Target);
            player.Interactive = true;
            var playerSession = new GenerationSession(player);
            Assert.Equal(0.0, playerSession.ScoringWeights.W1);
            Assert.Equal(auto.Weights.W2, playerSession.ScoringWeights.W2);
            Assert.Equal(auto.Weights.W5, playerSession.ScoringWeights.W5);
            Assert.Equal(auto.QualityThreshold - auto.Weights.W1, playerSession.EffectiveQualityThreshold, 12);
            var autoSession = new GenerationSession(auto);
            Assert.Equal(auto.Weights, autoSession.ScoringWeights);
            Assert.Equal(auto.QualityThreshold, autoSession.EffectiveQualityThreshold);

            foreach (double tension in new[] { 0.0, 0.2, 0.5, 0.9 })
            {
                CandidateRunSummary s = Runs.Make(durationTicks: Runs.Target, eventEveryTicks: 20, tension: tension, coverage: tension);
                CandidateEvaluation a = EvaluateOne(auto, s);
                CandidateEvaluation p = EvaluateOne(player, s);
                Assert.Equal(1.0, a.Breakdown.FitDuration); // on target
                Assert.Equal(a.Breakdown.Total - auto.Weights.W1, p.Breakdown.Total, 9); // spec formula: no w1 term
                Assert.Equal(a.PassesThreshold, p.PassesThreshold); // same gate on the remaining terms
            }
        }

        [Fact]
        public void M01AcceptsTheLastSecondGoal()
        {
            // Release-1 spec §2.5: "«Победа в последнюю секунду» … в релизе 1 для M01 «Соревнование»" (QA probe G1).
            GenerationSessionOptions o = GenerationSessionOptions.ForMode(ModeCode.M01, DeviceTier.B, 1UL, Runs.Target);
            o.Goal = DirectorGoal.LastSecond;
            Assert.True(EvaluateOne(o, Runs.Make(tension: 0.9)).GoalMet);
            Assert.False(EvaluateOne(o, Runs.Make(tension: 0.3)).GoalMet);

            o.Goal = DirectorGoal.Comeback;
            Assert.Throws<ArgumentException>(() => new GenerationSession(o));
            foreach (ModeCode mode in new[] { ModeCode.M02, ModeCode.M05 })
            {
                foreach (DirectorGoal goal in new[] { DirectorGoal.LastSecond, DirectorGoal.Comeback })
                {
                    GenerationSessionOptions other = GenerationSessionOptions.ForMode(mode, DeviceTier.B, 1UL, Runs.Target);
                    other.Goal = goal;
                    Assert.Throws<ArgumentException>(() => new GenerationSession(other));
                }
            }
        }
    }
}
