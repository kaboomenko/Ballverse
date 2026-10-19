using System;
using Ballverse.Core.Params;
using Ballverse.Core.Sim;

namespace Ballverse.Core.Modes.RingEscape
{
    /// <summary>Outcome of <see cref="RingEscapeDurationHint.Estimate"/>.</summary>
    public sealed class RingEscapeDurationEstimate
    {
        internal RingEscapeDurationEstimate(bool showHint, int seedsEscaped, int seedsFinished, float projectedSeconds, int ticksRun, bool budgetExhausted,
            int seedsInconclusive)
        {
            SeedsInconclusive = seedsInconclusive;
            ShowHint = showHint;
            SeedsEscaped = seedsEscaped;
            SeedsFinished = seedsFinished;
            ProjectedSeconds = projectedSeconds;
            TicksRun = ticksRun;
            BudgetExhausted = budgetExhausted;
        }

        /// <summary>Show «Побег может не успеть за 180 с — ролик закончится по счёту колец» with «Шире прорези» / «Меньше колец».</summary>
        public bool ShowHint { get; }

        /// <summary>Probe seeds that escaped within the budget (any escape hides the hint).</summary>
        public int SeedsEscaped { get; }

        /// <summary>Probe seeds whose run ended (escape or the 180 s cap) within the budget.</summary>
        public int SeedsFinished { get; }

        /// <summary>
        /// Shortest escape time among the seeds, seconds: measured for an escaped seed, otherwise extrapolated as
        /// countdown + (elapsed − countdown) · ringCount / max(1, rings passed) — with no ring yet, as if the first one passed now
        /// (a lower bound) — and ∞ for a seed that ended at the cap without an escape.
        /// </summary>
        public float ProjectedSeconds { get; }

        /// <summary>Probe seeds still running that had not passed a ring when the budget ended (projected optimistically).</summary>
        public int SeedsInconclusive { get; }

        /// <summary>Ticks simulated over all seeds.</summary>
        public int TicksRun { get; }

        /// <summary>The budget ran out before every seed finished: the verdict rests on the extrapolation.</summary>
        public bool BudgetExhausted { get; }
    }

    /// <summary>
    /// The M01 panel hint "the escape may not make it in 180 s" (spec §2.9 M01 «Длительность не ограничивается валидацией», docs QA
    /// residual): in <c>Setup</c> the current set is run headless on 3 probe seeds, round-robin in 1 s slices, within the tier's
    /// 🎲 budget. Any escape hides the hint. When the budget runs out first, the escape time of each seed is extrapolated from its
    /// rhythm after the countdown (rings passed per game second, like the generator's T̄; a seed with no ring yet counts as if
    /// its first ring passed now, an optimistic bound) and the hint shows when every projection exceeds 180 s. So a short
    /// budget or one spent in the countdown never shows the hint on its own.
    /// Pure: the caller converts its time budget into ticks (or passes a stop callback that reads its clock).
    /// </summary>
    public static class RingEscapeDurationHint
    {
        public const int ProbeSeeds = 3;
        /// <summary>Round-robin slice per seed, ticks.</summary>
        public const int SliceTicks = SimConstants.TickRate;
        const ulong HintStream = 0x4D30315F48494E54UL; // "M01_HINT"

        /// <param name="values">Prepared M01 parameters (as for a run).</param>
        /// <param name="seed">Base seed of the probe (the probe seeds derive from it).</param>
        /// <param name="maxTotalTicks">Tick budget over all seeds (e.g. tier time budget × measured ticks per ms).</param>
        /// <param name="stop">Optional: polled between slices; true ends the probe (time budget, user left Setup).</param>
        public static RingEscapeDurationEstimate Estimate(ParamSet values, ulong seed, int maxTotalTicks, Func<bool> stop = null)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (maxTotalTicks < 0) throw new ArgumentOutOfRangeException(nameof(maxTotalTicks));
            IModeDefinition mode = RingEscapeMode.Instance;
            var worlds = new PhysicsWorld[ProbeSeeds];
            var scenes = new RingEscapeScene[ProbeSeeds];
            var runners = new SimRunner[ProbeSeeds];
            for (int k = 0; k < ProbeSeeds; k++)
            {
                ulong s = Rng.FromSeed(seed, HintStream + (ulong)k).NextULong();
                var events = new ModeEventBuffer { WantsRender = false, WantsSound = false, WantsReplay = false };
                worlds[k] = new PhysicsWorld();
                scenes[k] = (RingEscapeScene)mode.BuildScene(worlds[k], new ModeSceneContext(mode, values, s, new ModeVisualTable(), events));
                runners[k] = new SimRunner(worlds[k], scenes[k]);
                runners[k].Start();
            }

            int ticks = 0;
            bool exhausted = false;
            while (true)
            {
                bool anyRunning = false;
                for (int k = 0; k < ProbeSeeds; k++)
                {
                    if (scenes[k].EscapeTick >= 0) return Result(scenes, runners, ticks, false);
                    if (!Running(runners[k])) continue;
                    anyRunning = true;
                    for (int i = 0; i < SliceTicks && Running(runners[k]) && ticks < maxTotalTicks; i++)
                    {
                        runners[k].Step();
                        ticks++;
                    }
                    if (scenes[k].EscapeTick >= 0) return Result(scenes, runners, ticks, false);
                }
                if (!anyRunning) break;
                if (ticks >= maxTotalTicks || (stop != null && stop()))
                {
                    exhausted = true;
                    break;
                }
            }
            return Result(scenes, runners, ticks, exhausted);
        }

        static bool Running(SimRunner r)
        {
            return r.Phase == SimPhase.Running && !r.IsFinished;
        }

        static RingEscapeDurationEstimate Result(RingEscapeScene[] scenes, SimRunner[] runners, int ticks, bool exhausted)
        {
            int escaped = 0, finished = 0, inconclusive = 0;
            float best = float.PositiveInfinity;
            for (int k = 0; k < scenes.Length; k++)
            {
                RingEscapeScene s = scenes[k];
                float projected;
                if (s.EscapeTick >= 0)
                {
                    escaped++;
                    projected = s.EscapeTick / (float)SimConstants.TickRate;
                }
                else if (runners[k].TimeLimitReached)
                {
                    // The run reached the 180 s cap without an escape: conclusive.
                    projected = float.PositiveInfinity;
                }
                else
                {
                    // Rhythm after the countdown (the balls are held for its 3 s), extrapolated to every ring. A seed with no
                    // ring yet is projected optimistically, as if its first ring passed right now: a lower bound, so a short
                    // budget (or one spent in the countdown) never shows the hint, while a long wait for the first ring does.
                    float countdown = s.Config.Countdown ? RingEscapeScene.CountdownTicks / (float)SimConstants.TickRate : 0f;
                    float elapsed = MathF.Max(0f, runners[k].Tick / (float)SimConstants.TickRate - countdown);
                    if (s.RingsPassed == 0) inconclusive++;
                    projected = countdown + elapsed * s.Config.RingCount / Math.Max(1, s.RingsPassed);
                }
                if (!Running(runners[k])) finished++;
                if (projected < best) best = projected;
            }
            bool show = escaped == 0 && best > SimConstants.MaxDurationSeconds;
            return new RingEscapeDurationEstimate(show, escaped, finished, best, ticks, exhausted && escaped == 0, inconclusive);
        }
    }
}
