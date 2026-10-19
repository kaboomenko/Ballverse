using System;
using System.Collections.Generic;
using Ballverse.App;
using Ballverse.Core.Params;
using Ballverse.UI.Localization;
using AppContext = Ballverse.App.AppContext;

namespace Ballverse.PlayModeTests
{
    /// <summary>
    /// What the live run looked like at one moment, for the 🎲 post-condition: the applied parameter set (a new instance for
    /// every started run), the live run's seed, and the selected preset.
    /// </summary>
    public readonly struct LiveRunSnapshot
    {
        public LiveRunSnapshot(ParamSet values, ulong seed, string presetId)
        {
            Values = values;
            Seed = seed;
            PresetId = presetId;
        }

        public ParamSet Values { get; }
        public ulong Seed { get; }
        public string PresetId { get; }

        public static LiveRunSnapshot Of(SimulationViewModel sim, AppContext ctx) =>
            new LiveRunSnapshot(sim.CurrentValues, ctx.Live.Seed, sim.PresetsModel.SelectedId);

        public override string ToString() => "seed " + Seed + ", preset " + PresetId;
    }

    /// <summary>
    /// Post-conditions of smoke steps that need no engine, kept apart from the Unity flow so CompileCheck~/LogicTests can run
    /// them against the real view models (Tests/EditMode/App TestApp) under plain .NET.
    /// </summary>
    public static class SmokeChecks
    {
        /// <summary>Notices after which 🎲 started nothing (SimulationViewModel.ApplyGenerated / FinishGeneration).</summary>
        public static readonly IReadOnlyList<string> GenerateFailureKeys = new[] { AppStrings.GenerateNone, AppStrings.GenerateFailed };

        /// <summary>
        /// True when <paramref name="shown"/> is the notice <paramref name="key"/>. "app.*" keys reach the screen as resolved
        /// literals (AppNotices.TextOf → AppText), the others as keys.
        /// </summary>
        public static bool IsNotice(LocalizedText shown, string key, Func<string, LocalizedText> textOf)
        {
            if (shown.Key == key) return true;
            string literal = textOf?.Invoke(key).Literal;
            return literal != null && shown.Literal == literal;
        }

        /// <summary>
        /// Null when a 🎲 that ended (not generating, no winner choice pending) applied a generated run, else why not. A run
        /// started by ApplyGenerated has a new parameter set, a new seed and the «Свои» preset (PresetsViewModel.MarkCustom).
        /// <paramref name="declinedChoice"/>: the forced-winner choice was closed with back, which keeps the current run
        /// (SimulationViewModel.DeclineDecision), so then the run must be unchanged. <paramref name="notices"/>: every notice
        /// shown since the 🎲 (toasts and the simulation screen's notices).
        /// </summary>
        public static string DiceFailure(LiveRunSnapshot before, LiveRunSnapshot after, bool declinedChoice,
            IEnumerable<LocalizedText> notices, Func<string, LocalizedText> textOf)
        {
            if (notices != null)
            {
                foreach (LocalizedText shown in notices)
                {
                    foreach (string key in GenerateFailureKeys)
                    {
                        if (IsNotice(shown, key, textOf)) return "🎲 ended without a generated run: notice " + key + " («" + (shown.Literal ?? shown.Key) + "»)";
                    }
                }
            }
            if (declinedChoice)
            {
                return ReferenceEquals(before.Values, after.Values) && before.Seed == after.Seed
                    ? null
                    : "closing the winner choice must keep the current run (" + before + " → " + after + ")";
            }
            if (ReferenceEquals(before.Values, after.Values)) return "🎲 applied no generated run: the parameter set is unchanged (" + after + ")";
            if (before.Seed == after.Seed) return "🎲 applied no generated run: the live run's seed is unchanged (" + after + ")";
            if (after.PresetId != PresetsViewModel.CustomId)
                return "a generated run selects the «" + PresetsViewModel.CustomId + "» preset, got " + after.PresetId;
            return null;
        }
    }
}
