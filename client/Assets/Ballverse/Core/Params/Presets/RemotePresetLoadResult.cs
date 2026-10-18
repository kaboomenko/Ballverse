using System.Collections.Generic;

namespace Ballverse.Core.Params.Presets
{
    /// <summary>A remote preset that was not accepted, with the reason (spec §2.3, §9.3 <c>remote_presets</c>).</summary>
    public sealed class PresetRejection
    {
        /// <summary>Index in the payload array, or -1 when the whole payload was unusable.</summary>
        public int Index { get; }
        /// <summary>Preset id when it could be read, else null.</summary>
        public string PresetId { get; }
        public string Reason { get; }

        public PresetRejection(int index, string presetId, string reason)
        {
            Index = index;
            PresetId = presetId;
            Reason = reason;
        }

        public override string ToString()
        {
            return "#" + Index + " " + (PresetId ?? "?") + ": " + Reason;
        }
    }

    /// <summary>A fix applied to an accepted remote preset (dropped unknown parameter, clamped value…).</summary>
    public sealed class PresetWarning
    {
        public string PresetId { get; }
        public ParamIssue Issue { get; }

        public PresetWarning(string presetId, ParamIssue issue)
        {
            PresetId = presetId;
            Issue = issue;
        }

        public override string ToString()
        {
            return PresetId + ": " + Issue;
        }
    }

    /// <summary>Outcome of <see cref="RemotePresets.Load"/>; lists keep payload order.</summary>
    public sealed class RemotePresetLoadResult
    {
        public IReadOnlyList<Preset> Accepted { get; }
        public IReadOnlyList<PresetRejection> Rejected { get; }
        public IReadOnlyList<PresetWarning> Warnings { get; }

        public RemotePresetLoadResult(IReadOnlyList<Preset> accepted, IReadOnlyList<PresetRejection> rejected,
            IReadOnlyList<PresetWarning> warnings)
        {
            Accepted = accepted;
            Rejected = rejected;
            Warnings = warnings;
        }
    }
}
