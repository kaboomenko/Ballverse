using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

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
        /// Parses and normalizes: drops credits without a string project id, with an unknown source, or with an
        /// <c>expiresAtMs</c> that is negative or not a number (a negative value beyond the <see cref="long"/> range
        /// saturates and is dropped; a positive one saturates to a credit that never expires in practice). A missing or
        /// wrong-typed <c>claimed</c> reads as false and the credit is kept. Keeps the most generous credit per project (held
        /// beats timed, later expiry beats earlier); sorts by project id. A timed credit is never claimed. The JSON literal
        /// <c>null</c> yields a fresh state. Runs in O(n log n) for n stored credits.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
        /// <exception cref="System.IO.InvalidDataException">
        /// The text is not JSON, or its root is not an object. An integer literal longer than 380 characters anywhere in
        /// the document is reported this way too (a Newtonsoft reader limit), so it fails the whole state, not one credit.
        /// </exception>
        public static RewardedGateState FromJson(string json)
        {
            var obj = AdJson.ParseObject(json, "rewarded gate state");
            var state = new RewardedGateState();
            if (obj == null) return state;
            if (AdJson.TryGetInt(AdJson.Get(obj, "version"), out int version)) state.Version = version;
            if (AdJson.TryGetString(AdJson.Get(obj, "openOfferProjectId"), out string open)) state.OpenOfferProjectId = open;
            if (AdJson.Get(obj, "credits") is JArray credits)
            {
                foreach (var item in credits)
                {
                    var credit = ReadCredit(item as JObject);
                    if (credit != null) state.Credits.Add(credit);
                }
            }
            state.Normalize();
            return state;
        }

        static RewardedExportCredit ReadCredit(JObject o)
        {
            if (o == null) return null;
            if (!AdJson.TryGetString(AdJson.Get(o, "projectId"), out string projectId)) return null;
            if (!AdJson.TryGetEnum(AdJson.Get(o, "source"), out RewardedCreditSource source)) return null;
            long? expires = null;
            var expiresToken = AdJson.Get(o, "expiresAtMs");
            if (expiresToken != null)
            {
                if (!AdJson.TryGetLong(expiresToken, out long e)) return null;
                expires = e;
            }
            // A missing or wrong-typed "claimed" reads as false (unclaimed); the credit itself is kept.
            if (!AdJson.TryGetBool(AdJson.Get(o, "claimed"), out bool claimed)) claimed = false;
            return new RewardedExportCredit(projectId, source, expires, claimed);
        }

        internal void Normalize()
        {
            if (Version < CurrentVersion) Version = CurrentVersion;
            if (string.IsNullOrEmpty(OpenOfferProjectId)) OpenOfferProjectId = null;
            var source = Credits ?? new List<RewardedExportCredit>();
            var valid = new List<RewardedExportCredit>(source.Count);
            foreach (var c in source)
            {
                if (c == null || string.IsNullOrEmpty(c.ProjectId)) continue;
                if (c.ExpiresAtMs.HasValue && c.ExpiresAtMs.Value < 0) continue;
                if (!Enum.IsDefined(typeof(RewardedCreditSource), c.Source)) continue;
                var copy = c.Clone();
                if (copy.ExpiresAtMs.HasValue) copy.Claimed = false;
                valid.Add(copy);
            }
            // Most generous first within each project, then keep the first of each project: one linear pass.
            valid.Sort(CompareForDedupe);
            var result = new List<RewardedExportCredit>(valid.Count);
            foreach (var c in valid)
            {
                if (result.Count > 0 && string.Equals(result[result.Count - 1].ProjectId, c.ProjectId, StringComparison.Ordinal)) continue;
                result.Add(c);
            }
            Credits = result;
        }

        /// <summary>Index of the credit of <paramref name="projectId"/> in a list sorted by project id (ordinal), or -1.</summary>
        internal static int IndexOf(List<RewardedExportCredit> credits, string projectId)
        {
            int i = LowerBound(credits, projectId);
            return i < credits.Count && string.Equals(credits[i].ProjectId, projectId, StringComparison.Ordinal) ? i : -1;
        }

        /// <summary>First index whose project id is ordinal-greater than or equal to <paramref name="projectId"/>.</summary>
        internal static int LowerBound(List<RewardedExportCredit> credits, string projectId)
        {
            int lo = 0, hi = credits.Count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (string.CompareOrdinal(credits[mid].ProjectId, projectId) < 0) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// Project id (ordinal), then most generous first: held before timed, later expiry first; ties prefer a claimed
        /// credit, then <see cref="RewardedCreditSource.Reward"/>. Total over distinct contents, so the result never
        /// depends on file order.
        /// </summary>
        static int CompareForDedupe(RewardedExportCredit a, RewardedExportCredit b)
        {
            int c = string.CompareOrdinal(a.ProjectId, b.ProjectId);
            if (c != 0) return c;
            if (a.ExpiresAtMs.HasValue != b.ExpiresAtMs.HasValue) return a.ExpiresAtMs.HasValue ? 1 : -1;
            if (a.ExpiresAtMs.HasValue && a.ExpiresAtMs.Value != b.ExpiresAtMs.Value) return b.ExpiresAtMs.Value.CompareTo(a.ExpiresAtMs.Value);
            if (a.Claimed != b.Claimed) return a.Claimed ? -1 : 1;
            return ((int)a.Source).CompareTo((int)b.Source);
        }
    }
}
