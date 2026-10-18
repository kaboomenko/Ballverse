using System;
using System.Collections.Generic;

namespace Ballverse.Core.Ads
{
    /// <summary>
    /// Persistent state of <see cref="RewardedExportGate"/> (spec §5.3.1, §3.8). Credits are kept sorted by project id
    /// (ordinal) so serialized bytes never depend on insertion order. Stored as JSON via <see cref="CoreJson"/>.
    /// </summary>
    public sealed class RewardedGateState
    {
        public const int CurrentVersion = 1;

        /// <summary>Format version for future migrations.</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>Export credits, at most one per project, sorted by <see cref="RewardedExportCredit.ProjectId"/>.</summary>
        public List<RewardedExportCredit> Credits { get; set; } = new List<RewardedExportCredit>();

        /// <summary>Project whose rewarded offer dialog is open (outcomes are accepted only for it), or null.</summary>
        public string OpenOfferProjectId { get; set; }

        public RewardedGateState Clone()
        {
            var copy = new RewardedGateState
            {
                Version = Version,
                OpenOfferProjectId = OpenOfferProjectId,
                Credits = new List<RewardedExportCredit>(),
            };
            if (Credits != null)
            {
                foreach (var c in Credits) copy.Credits.Add(c?.Clone());
            }
            return copy;
        }

        /// <summary>Serializes with <see cref="CoreJson"/> (compact by default).</summary>
        public string ToJson(bool indented = false)
        {
            return CoreJson.Serialize(this, indented);
        }

        /// <summary>
        /// Parses with <see cref="CoreJson"/> and normalizes: drops credits without a project id or with a negative expiry,
        /// keeps the most generous credit per project (held beats timed, later expiry beats earlier), sorts by project id.
        /// The JSON literal <c>null</c> yields a fresh state.
        /// </summary>
        public static RewardedGateState FromJson(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var state = CoreJson.Deserialize<RewardedGateState>(json) ?? new RewardedGateState();
            state.Normalize();
            return state;
        }

        internal void Normalize()
        {
            if (Version < CurrentVersion) Version = CurrentVersion;
            if (string.IsNullOrEmpty(OpenOfferProjectId)) OpenOfferProjectId = null;
            var source = Credits ?? new List<RewardedExportCredit>();
            var result = new List<RewardedExportCredit>(source.Count);
            foreach (var c in source)
            {
                if (c == null || string.IsNullOrEmpty(c.ProjectId)) continue;
                if (c.ExpiresAtMs.HasValue && c.ExpiresAtMs.Value < 0) continue;
                if (!Enum.IsDefined(typeof(RewardedCreditSource), c.Source)) continue;
                int i = IndexOf(result, c.ProjectId);
                if (i < 0)
                {
                    result.Add(c.Clone());
                }
                else if (IsMoreGenerous(c, result[i]))
                {
                    result[i] = c.Clone();
                }
            }
            result.Sort((a, b) => string.CompareOrdinal(a.ProjectId, b.ProjectId));
            Credits = result;
        }

        internal static int IndexOf(List<RewardedExportCredit> credits, string projectId)
        {
            for (int i = 0; i < credits.Count; i++)
            {
                if (string.Equals(credits[i].ProjectId, projectId, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        static bool IsMoreGenerous(RewardedExportCredit a, RewardedExportCredit b)
        {
            if (!b.ExpiresAtMs.HasValue) return false;
            if (!a.ExpiresAtMs.HasValue) return true;
            return a.ExpiresAtMs.Value > b.ExpiresAtMs.Value;
        }
    }
}
