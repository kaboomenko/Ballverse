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
        /// <summary>Game time the probe may simulate per real second of the tier's 🎲 budget (a conservative 10× real time).</summary>
        public const int GameSecondsPerBudgetSecond = 10;

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

        /// <summary>
        /// The probe's tick budget for a 🎲 time budget: <see cref="GameSecondsPerBudgetSecond"/> game seconds per budget second
        /// (tier B 2.5 s → 25 s of game time over the probe seeds, the Core acceptance budget). The caller also stops the probe
        /// on its own clock when the time budget runs out (a device slower than 10× real time).
        /// </summary>
        public static int TickBudget(GenerationBudget budget)
        {
            long ticks = budget.TimeMs * GameSecondsPerBudgetSecond * SimConstants.TickRate / 1000L;
            return (int)Math.Max(SimConstants.TickRate, Math.Min(int.MaxValue, ticks));
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
    /// </summary>
    public sealed class RingEscapeDurationHintProvider : ISetupHintProvider
    {
        public const string ModeId = "ring_escape";
        public const string WiderGapsAction = "wider_gaps";
        public const string FewerRingsAction = "fewer_rings";
        /// <summary>Probe base seed: fixed, so the verdict depends only on the parameter set.</summary>
        public const ulong ProbeSeed = 0x4D3031UL;
        public const double GapStepDeg = 10.0;

        static readonly SetupHintAction[] s_actions =
        {
            new SetupHintAction(WiderGapsAction, AppStrings.HintWiderGaps, WidenGaps),
            new SetupHintAction(FewerRingsAction, AppStrings.HintFewerRings, FewerRings),
        };

        public SetupHint Evaluate(ParamSet values, int maxTotalTicks, Func<bool> stop)
        {
            RingEscapeDurationEstimate estimate = RingEscapeDurationHint.Estimate(values, ProbeSeed, Math.Max(0, maxTotalTicks), stop);
            return estimate.ShowHint ? new SetupHint(ModeId, AppStrings.HintRingEscapeDuration, s_actions) : null;
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
