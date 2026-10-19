using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Generation;
using Ballverse.Core.Modes;
using Ballverse.Core.Modes.RingEscape;
using Ballverse.Core.Params;

namespace Ballverse.App
{
    /// <summary>A mode-owned hint about the current parameter set, with actions that change the set (spec §2.9 M01 card).</summary>
    public sealed class SetupHint
    {
        public SetupHint(string modeId, string textKey, IReadOnlyList<SetupHintAction> actions)
        {
            ModeId = modeId ?? throw new ArgumentNullException(nameof(modeId));
            TextKey = textKey ?? throw new ArgumentNullException(nameof(textKey));
            Actions = actions ?? Array.Empty<SetupHintAction>();
        }

        public string ModeId { get; }
        public string TextKey { get; }
        public IReadOnlyList<SetupHintAction> Actions { get; }
    }

    /// <summary>One action of a <see cref="SetupHint"/> (e.g. «Шире прорези»): a pure change of the parameter set.</summary>
    public sealed class SetupHintAction
    {
        public SetupHintAction(string id, string labelKey, Func<ParamSet, ParamSet> apply)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            LabelKey = labelKey ?? throw new ArgumentNullException(nameof(labelKey));
            Apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public string Id { get; }
        public string LabelKey { get; }
        /// <summary>Returns the changed raw set (the caller prepares it for the mode and tier).</summary>
        public Func<ParamSet, ParamSet> Apply { get; }
    }

    /// <summary>
    /// A mode's background check of the parameter set chosen in the settings sheet. Runs on a worker thread; must be pure and
    /// stop when <paramref name="stop"/> returns true or <paramref name="maxTotalTicks"/> are simulated.
    /// </summary>
    public interface ISetupHintProvider
    {
        /// <param name="values">Prepared parameters of the mode (as for a run).</param>
        /// <returns>The hint to show, or null.</returns>
        SetupHint Evaluate(ParamSet values, int maxTotalTicks, Func<bool> stop);
    }

    /// <summary>
    /// Setup hints by mode id (next to the Sim runtime's <c>ArenaInputMapperRegistry</c>), so the simulation view model has no
    /// mode-specific branches: a mode that ships a hint registers a provider here.
    /// </summary>
    public sealed class SetupHintRegistry
    {
        /// <summary>
        /// Tick cap of a probe: 3 × 180 s of game time (3 probe seeds run to their end). The probe is limited by the tier's 🎲
        /// time budget on the caller's clock (spec §2.9 M01: «останавливает пробу по своим часам»), not by a fixed game-time
        /// ratio, so a fast device probes as far as its budget allows; the cap only bounds a probe whose clock never runs out.
        /// </summary>
        public const int MaxProbeTicks = 3 * SimConstants.MaxDurationTicks;

        readonly Dictionary<string, ISetupHintProvider> _providers = new Dictionary<string, ISetupHintProvider>(StringComparer.Ordinal);

        /// <summary>Release 1: the M01 duration hint.</summary>
        public static SetupHintRegistry CreateRelease1()
        {
            var r = new SetupHintRegistry();
            r.Register(RingEscapeDurationHintProvider.ModeId, new RingEscapeDurationHintProvider());
            return r;
        }

        public void Register(string modeId, ISetupHintProvider provider)
        {
            if (string.IsNullOrEmpty(modeId)) throw new ArgumentException("modeId must not be empty", nameof(modeId));
            _providers[modeId] = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public ISetupHintProvider Find(string modeId)
        {
            return modeId != null && _providers.TryGetValue(modeId, out ISetupHintProvider p) ? p : null;
        }

        /// <summary>The structural (non-live) values of a set, without the seed: a hint depends only on these.</summary>
        public static ParamSet StructuralPart(IModeDefinition mode, ParamSet values)
        {
            if (mode == null || values == null) return ParamSet.Empty;
            var parts = new List<KeyValuePair<string, ParamValue>>();
            foreach (ParamDef def in mode.Schema.Params)
            {
                if (def.Live || def.Id == CommonParams.Seed) continue;
                if (values.TryGet(def.Id, out ParamValue v)) parts.Add(new KeyValuePair<string, ParamValue>(def.Id, v));
            }
            return new ParamSet(parts);
        }
    }

    /// <summary>
    /// M01 «Побег может не успеть за 180 с — ролик закончится по счёту колец» (spec §2.9 M01 «Длительность не ограничивается
    /// валидацией»): Core's <see cref="RingEscapeDurationHint.Estimate"/> on 3 probe seeds within the tier's 🎲 budget, with the
    /// actions «Шире прорези» (gapSize +10°) and «Меньше колец» (ringCount −25 %, at least one ring fewer).
    /// <para>
    /// A probe that ran to its end (an escape, or every seed at the 180 s cap) is conclusive. A probe stopped by the clock
    /// first (a slow device) rests on Core's rhythm extrapolation, which overshoots after the first rings (the early rings
    /// take longer than the rest, then rings fall in combos): stopped after every round, Core alone showed a false hint at
    /// some stop for 29 of 104 sets in the QA sweep and for 241 of 516 in a wider one (10–100 rings × gaps 26–58° × random /
    /// aligned / spiral placement on tiers A and C, plus 2–4 balls with pulse / reverse speed laws), even when the full probe
    /// escapes in 113 s. So such a verdict is trusted only when it is clear: every seed passed a ring, each seed ran at
    /// least <see cref="MinExtrapolatedSecondsPerSeed"/> s of game time, and the projection is at least
    /// <see cref="ExtrapolationMargin"/> × 180 s. On the QA sweep that shows no false hint at any stop; on the wider sweep
    /// one parameter set (60 rings at 29°, random placement, whose best seed escapes at 175 s) still shows it at stops from
    /// 37 s per seed, and 36 of the 92 sets that need the hint get it by 40 s per seed (100 rings at gapMin projects
    /// 600–1000 s from 30 s per seed on). Not a rule of the spec: §2.9 M01 shows Core's raw verdict, so its test (д) (100
    /// rings at gapMin with 25 s of game time over 3 seeds) gives no hint here — no extrapolation rule tells such a set from
    /// one that escapes in time that early (README «Placeholders and gaps»). A missed hint only means the run ends by the
    /// 180 s cap, as without the hint.
    /// </para>
    /// </summary>
    public sealed class RingEscapeDurationHintProvider : ISetupHintProvider
    {
        public const string ModeId = "ring_escape";
        public const string WiderGapsAction = "wider_gaps";
        public const string FewerRingsAction = "fewer_rings";
        /// <summary>Probe base seed: fixed, so the verdict depends only on the parameter set.</summary>
        public const ulong ProbeSeed = 0x4D3031UL;
        public const double GapStepDeg = 10.0;
        /// <summary>
        /// An extrapolated verdict (probe stopped by the clock) needs a projection of at least this × 180 s. 1.5 (QA round 3)
        /// still showed a false hint for 20 rings at 26°, spiral placement, stopped after 35 rounds (350 s projected, escapes
        /// in 164 s) and for 4 more parameter sets of the wider sweep.
        /// </summary>
        public const float ExtrapolationMargin = 3f;
        /// <summary>An extrapolated verdict needs at least this much game time simulated per probe seed, seconds.</summary>
        public const int MinExtrapolatedSecondsPerSeed = 35;

        static readonly SetupHintAction[] s_actions =
        {
            new SetupHintAction(WiderGapsAction, AppStrings.HintWiderGaps, WidenGaps),
            new SetupHintAction(FewerRingsAction, AppStrings.HintFewerRings, FewerRings),
        };

        public SetupHint Evaluate(ParamSet values, int maxTotalTicks, Func<bool> stop)
        {
            RingEscapeDurationEstimate estimate = RingEscapeDurationHint.Estimate(values, ProbeSeed, Math.Max(0, maxTotalTicks), stop);
            return ShowsHint(estimate) ? new SetupHint(ModeId, AppStrings.HintRingEscapeDuration, s_actions) : null;
        }

        /// <summary>
        /// Core's verdict, with an extrapolated one (probe stopped before every seed finished) kept only when clear (see the
        /// class remarks).
        /// </summary>
        public static bool ShowsHint(RingEscapeDurationEstimate estimate)
        {
            if (estimate == null || !estimate.ShowHint) return false;
            if (!estimate.BudgetExhausted) return true;
            const int minTicks = RingEscapeDurationHint.ProbeSeeds * MinExtrapolatedSecondsPerSeed * SimConstants.TickRate;
            return estimate.SeedsInconclusive == 0
                && estimate.TicksRun >= minTicks
                && estimate.ProjectedSeconds >= ExtrapolationMargin * SimConstants.MaxDurationSeconds;
        }

        public static ParamSet WidenGaps(ParamSet values)
        {
            if (values == null || !values.TryGet(RingEscapeParams.GapSize, out ParamValue gap)) return values;
            return values.With(RingEscapeParams.GapSize, ParamValue.Float(gap.AsFloat + GapStepDeg));
        }

        public static ParamSet FewerRings(ParamSet values)
        {
            if (values == null || !values.TryGet(RingEscapeParams.RingCount, out ParamValue rings)) return values;
            long count = rings.AsInt;
            long next = Math.Max(1, count - Math.Max(1, count / 4));
            return values.With(RingEscapeParams.RingCount, ParamValue.Int((int)next));
        }
    }
}
