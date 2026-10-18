using System;
using System.Collections.Generic;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;

namespace Ballverse.Core.Modes
{
    /// <summary>
    /// One validation path for every source of mode parameters (spec §2.9 "Валидация одинакова для панели, пресетов, Remote
    /// Config и генератора"): schema sanitizing (ranges, steps, options, tier caps) followed by the mode's cross-parameter rules
    /// (<see cref="IModeDefinition.Constrain"/>). The result can go straight into a run.
    /// </summary>
    public static class ModeParams
    {
        /// <summary>Sanitized and constrained set for <paramref name="mode"/> on <paramref name="tier"/>; issues report every fix.</summary>
        public static SanitizeResult Prepare(IModeDefinition mode, ParamSet raw, DeviceTier tier)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            SanitizeResult s = raw.Sanitize(mode.Schema, tier);
            var issues = new List<ParamIssue>(s.Issues);
            ParamSet constrained = mode.Constrain(s.Values, tier, issues);
            return new SanitizeResult(constrained, issues);
        }

        /// <summary>Defaults of the mode (schema defaults, constrained) on <paramref name="tier"/>.</summary>
        public static ParamSet Defaults(IModeDefinition mode, DeviceTier tier = DeviceTier.A)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            return mode.Constrain(mode.Schema.CreateDefaults(tier), tier, new List<ParamIssue>());
        }

        /// <summary>A preset's parameters (defaults overlaid with the preset values, spec §2.3), sanitized and constrained.</summary>
        public static SanitizeResult ApplyPreset(IModeDefinition mode, Preset preset, DeviceTier tier)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            SanitizeResult s = preset.Apply(mode.Schema, tier);
            var issues = new List<ParamIssue>(s.Issues);
            ParamSet constrained = mode.Constrain(s.Values, tier, issues);
            return new SanitizeResult(constrained, issues);
        }

        /// <summary>
        /// True when <paramref name="values"/> is complete, sanitized and satisfies every cross-parameter rule on tier A (the
        /// widest caps): what <see cref="ModeSession"/> requires of its start values.
        /// </summary>
        public static bool IsRunnable(IModeDefinition mode, ParamSet values, out string problem)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            if (values == null) throw new ArgumentNullException(nameof(values));
            SanitizeResult s = values.Sanitize(mode.Schema, DeviceTier.A);
            if (s.HasIssues)
            {
                problem = "not sanitized: " + s.Issues[0];
                return false;
            }
            var issues = new List<ParamIssue>();
            mode.Constrain(s.Values, DeviceTier.A, issues);
            if (issues.Count > 0)
            {
                problem = "violates a mode rule: " + issues[0];
                return false;
            }
            problem = null;
            return true;
        }
    }
}
