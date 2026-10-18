using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Config
{
    /// <summary>
    /// Turns the raw Firebase Remote Config strings into a typed <see cref="RemoteConfigValues"/> (spec §9.3).
    /// <para>Value formats: booleans "true"/"false" (case-insensitive; "1"/"0" also accepted); integers in invariant culture
    /// (integral forms such as "3.0" or "1e2" accepted); lists and maps as JSON.</para>
    /// <para>Per key: missing or blank → default; malformed JSON / wrong type → default + Error issue; out-of-range numbers →
    /// clamped + Warning; invalid elements of lists and maps → that element dropped + Warning; unknown keys → Info.</para>
    /// <para>Merge rules: lists (mode lists, presets, melodies) replace the default as a whole. Fixed tables
    /// (<c>export_bitrates</c>, <c>export_default_fps_by_tier</c>, <c>generation_budget_by_tier</c>) merge field by field over the
    /// defaults. Open maps (<c>generation_weights</c>, <c>tier_overrides</c>, <c>platform_safe_zones</c>, <c>text_templates</c>)
    /// merge entry by entry: a delivered entry replaces the default entry with the same key, other default entries stay
    /// (so a payload that only adds "de" templates does not delete "en"/"ru"; send an empty list to clear an entry).</para>
    /// <para>JSON objects with duplicate property names are rejected as invalid JSON (ambiguous).</para>
    /// Never throws; output and issue order are deterministic and independent of the dictionary's enumeration order.
    /// </summary>
    public static class RemoteConfigParser
    {
        static readonly string[] s_bitrateKeys =
        {
            ExportBitrates.KeyOf(ExportResolution.P720, 30),
            ExportBitrates.KeyOf(ExportResolution.P720, 60),
            ExportBitrates.KeyOf(ExportResolution.P1080, 30),
            ExportBitrates.KeyOf(ExportResolution.P1080, 60),
        };

        static readonly HashSet<string> s_known = new HashSet<string>(RemoteConfigKeys.All, StringComparer.Ordinal);

        /// <summary>Parses raw key → string values (spec §9.3). <paramref name="raw"/> may be null (→ defaults).</summary>
        public static RemoteConfigParseResult Parse(IReadOnlyDictionary<string, string> raw)
        {
            var issues = new List<RemoteConfigIssue>();
            var builder = new RemoteConfigBuilder();
            if (raw != null)
            {
                foreach (string key in RemoteConfigKeys.All)
                {
                    var ctx = new Context(key, issues);
                    string text;
                    try
                    {
                        if (!raw.TryGetValue(key, out text)) continue;
                    }
                    catch (Exception e)
                    {
                        ctx.Error(RemoteConfigIssueKind.InternalError, "lookup failed: " + e.Message);
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        ctx.Info(RemoteConfigIssueKind.EmptyValue, "empty value, default kept");
                        continue;
                    }
                    try
                    {
                        ParseKey(key, text, builder, ctx);
                    }
                    catch (Exception e)
                    {
                        ctx.Error(RemoteConfigIssueKind.InternalError, "unexpected failure, default kept: " + e.Message);
                    }
                }
                ReportUnknownKeys(raw, issues);
            }

            RemoteConfigValues values;
            try
            {
                values = builder.Build();
            }
            catch (Exception e)
            {
                issues.Add(new RemoteConfigIssue(string.Empty, RemoteConfigIssueSeverity.Error, RemoteConfigIssueKind.InternalError,
                    "snapshot build failed, all defaults used: " + e.Message));
                values = RemoteConfigValues.Defaults;
            }
            return new RemoteConfigParseResult(values, issues.AsReadOnly());
        }

        static void ReportUnknownKeys(IReadOnlyDictionary<string, string> raw, List<RemoteConfigIssue> issues)
        {
            var unknown = new List<string>();
            try
            {
                foreach (var kv in raw)
                {
                    if (kv.Key != null && !s_known.Contains(kv.Key)) unknown.Add(kv.Key);
                }
            }
            catch (Exception e)
            {
                issues.Add(new RemoteConfigIssue(string.Empty, RemoteConfigIssueSeverity.Error, RemoteConfigIssueKind.InternalError,
                    "enumeration failed: " + e.Message));
            }
            // Ordinal sort: the issue list must not depend on dictionary enumeration order.
            unknown.Sort(StringComparer.Ordinal);
            foreach (var key in unknown)
            {
                issues.Add(new RemoteConfigIssue(key, RemoteConfigIssueSeverity.Info, RemoteConfigIssueKind.UnknownKey, "unknown key ignored"));
            }
        }

        static void ParseKey(string key, string text, RemoteConfigBuilder b, Context ctx)
        {
            int n;
            bool flag;
            switch (key)
            {
                case RemoteConfigKeys.FreeModes:
                {
                    var list = ParseModeList(text, ctx);
                    if (list == null) return;
                    var missing = new List<string>();
                    foreach (var m in ModeCatalog.All)
                    {
                        if (m.FreeByDefault && !list.Contains(m.Code)) missing.Add(m.Code.ToString());
                    }
                    if (missing.Count > 0)
                    {
                        ctx.Warning(RemoteConfigIssueKind.NarrowedFreeModes,
                            "free set narrowed (spec §5.1 intends extension only), not free: " + string.Join(",", missing));
                    }
                    b.FreeModes = list;
                    return;
                }
                case RemoteConfigKeys.ModesDisabled:
                {
                    var list = ParseModeList(text, ctx);
                    if (list != null) b.ModesDisabled = list;
                    return;
                }
                case RemoteConfigKeys.GalleryOrder:
                {
                    var list = ParseModeList(text, ctx);
                    if (list == null) return;
                    if (list.Count < ModeCatalog.All.Count)
                    {
                        var completed = RemoteConfigBuilder.CompleteGalleryOrder(list);
                        var appended = new List<string>();
                        for (int i = list.Count; i < completed.Count; i++) appended.Add(completed[i].ToString());
                        ctx.Warning(RemoteConfigIssueKind.IncompleteGalleryOrder, "missing modes appended in catalog order: " + string.Join(",", appended));
                    }
                    b.GalleryOrder = list;
                    return;
                }
                case RemoteConfigKeys.InterstitialEveryNGames:
                    if (TryParseInt(text, RemoteConfigLimits.InterstitialEveryNGamesMin, RemoteConfigLimits.InterstitialEveryNGamesMax, ctx, out n)) b.InterstitialEveryNGames = n;
                    return;
                case RemoteConfigKeys.InterstitialMinIntervalSec:
                    if (TryParseInt(text, RemoteConfigLimits.InterstitialMinIntervalSecMin, RemoteConfigLimits.InterstitialMinIntervalSecMax, ctx, out n)) b.InterstitialMinIntervalSec = n;
                    return;
                case RemoteConfigKeys.InterstitialGraceGames:
                    if (TryParseInt(text, RemoteConfigLimits.InterstitialGraceGamesMin, RemoteConfigLimits.InterstitialGraceGamesMax, ctx, out n)) b.InterstitialGraceGames = n;
                    return;
                case RemoteConfigKeys.RewardedExportEnabled:
                    if (TryParseBool(text, ctx, out flag)) b.RewardedExportEnabled = flag;
                    return;
                case RemoteConfigKeys.RewardedWaitTimeoutSec:
                    if (TryParseInt(text, RemoteConfigLimits.RewardedWaitTimeoutSecMin, RemoteConfigLimits.RewardedWaitTimeoutSecMax, ctx, out n)) b.RewardedWaitTimeoutSec = n;
                    return;
                case RemoteConfigKeys.RewardedRetryWindowMin:
                    if (TryParseInt(text, RemoteConfigLimits.RewardedRetryWindowMinMin, RemoteConfigLimits.RewardedRetryWindowMinMax, ctx, out n)) b.RewardedRetryWindowMin = n;
                    return;
                case RemoteConfigKeys.PaidModeTrialEnabled:
                    if (TryParseBool(text, ctx, out flag)) b.PaidModeTrialEnabled = flag;
                    return;
                case RemoteConfigKeys.PaidModeTrialSeconds:
                    if (TryParseInt(text, RemoteConfigLimits.PaidModeTrialSecondsMin, RemoteConfigLimits.PaidModeTrialSecondsMax, ctx, out n)) b.PaidModeTrialSeconds = n;
                    return;
                case RemoteConfigKeys.ExportBitrates:
                {
                    var v = ParseBitrates(text, b.ExportBitrates, ctx);
                    if (v != null) b.ExportBitrates = v;
                    return;
                }
                case RemoteConfigKeys.ExportDefaultFpsByTier:
                {
                    var v = ParseFpsByTier(text, b.ExportDefaultFpsByTier, ctx);
                    if (v != null) b.ExportDefaultFpsByTier = v;
                    return;
                }
                case RemoteConfigKeys.GenerationWeights:
                {
                    var v = ParseWeights(text, b.GenerationWeights, ctx);
                    if (v != null) b.GenerationWeights = v;
                    return;
                }
                case RemoteConfigKeys.GenerationBudgetByTier:
                {
                    var v = ParseBudget(text, b.GenerationBudgetByTier, ctx);
                    if (v != null) b.GenerationBudgetByTier = v;
                    return;
                }
                case RemoteConfigKeys.TierOverrides:
                {
                    var v = ParseTierOverrides(text, b.TierOverrides, ctx);
                    if (v != null) b.TierOverrides = v;
                    return;
                }
                case RemoteConfigKeys.PlatformSafeZones:
                {
                    var v = ParseSafeZones(text, b.PlatformSafeZones, ctx);
                    if (v != null) b.PlatformSafeZones = v;
                    return;
                }
                case RemoteConfigKeys.RemotePresets:
                {
                    var v = ParsePresets(text, ctx);
                    if (v != null) b.RemotePresets = v;
                    return;
                }
                case RemoteConfigKeys.RemoteMelodies:
                {
                    var v = ParseMelodies(text, ctx);
                    if (v != null) b.RemoteMelodies = v;
                    return;
                }
                case RemoteConfigKeys.TextTemplates:
                {
                    var v = ParseTextTemplates(text, b.TextTemplates, ctx);
                    if (v != null) b.TextTemplates = v;
                    return;
                }
                case RemoteConfigKeys.WatermarkEnabled:
                    if (TryParseBool(text, ctx, out flag)) b.WatermarkEnabled = flag;
                    return;
                case RemoteConfigKeys.RatePromptAfterExports:
                    if (TryParseInt(text, RemoteConfigLimits.RatePromptAfterExportsMin, RemoteConfigLimits.RatePromptAfterExportsMax, ctx, out n)) b.RatePromptAfterExports = n;
                    return;
            }
        }

        // ---- scalars ----

        static bool TryParseInt(string text, int min, int max, Context ctx, out int value)
        {
            value = 0;
            string s = text.Trim();
            if (!long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n))
            {
                const NumberStyles floatStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
                if (!double.TryParse(s, floatStyle, CultureInfo.InvariantCulture, out double d) || !RemoteConfigJson.TryIntegral(d, out n))
                {
                    ctx.Error(RemoteConfigIssueKind.InvalidType, "expected an integer, got " + RemoteConfigJson.Excerpt(text) + "; default kept");
                    return false;
                }
            }
            value = Clamp(n, min, max, ctx, "value");
            return true;
        }

        static bool TryParseBool(string text, Context ctx, out bool value)
        {
            string s = text.Trim();
            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1")
            {
                value = true;
                return true;
            }
            if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) || s == "0")
            {
                value = false;
                return true;
            }
            value = false;
            ctx.Error(RemoteConfigIssueKind.InvalidType, "expected true/false, got " + RemoteConfigJson.Excerpt(text) + "; default kept");
            return false;
        }

        static int Clamp(long n, int min, int max, Context ctx, string what)
        {
            if (n < min)
            {
                ctx.Warning(RemoteConfigIssueKind.OutOfRange, what + " " + n.ToString(CultureInfo.InvariantCulture) + " below " + min + ", clamped");
                return min;
            }
            if (n > max)
            {
                ctx.Warning(RemoteConfigIssueKind.OutOfRange, what + " " + n.ToString(CultureInfo.InvariantCulture) + " above " + max + ", clamped");
                return max;
            }
            return (int)n;
        }

        // ---- JSON shells ----

        static JToken ParseJson(string text, JTokenType expected, Context ctx)
        {
            if (!RemoteConfigJson.TryParse(text, out JToken token, out string error))
            {
                ctx.Error(RemoteConfigIssueKind.InvalidJson, "malformed JSON, default kept: " + error);
                return null;
            }
            if (token.Type != expected)
            {
                ctx.Error(RemoteConfigIssueKind.InvalidType, "expected a JSON " + (expected == JTokenType.Array ? "array" : "object")
                    + ", got " + token.Type + "; default kept");
                return null;
            }
            return token;
        }

        /// <summary>Accepts a catalog code ("M01") or a stable mode id ("ring_escape"), surrounding whitespace ignored.</summary>
        internal static bool TryResolveMode(string s, out ModeCode code)
        {
            code = default;
            if (s == null) return false;
            string t = s.Trim();
            if (ModeCatalog.TryParseCode(t, out code)) return true;
            if (ModeCatalog.TryGetById(t, out var info))
            {
                code = info.Code;
                return true;
            }
            return false;
        }

        static bool TryParseTier(string s, out DeviceTier tier)
        {
            tier = default;
            if (s == null) return false;
            switch (s.Trim().ToUpperInvariant())
            {
                case "A": tier = DeviceTier.A; return true;
                case "B": tier = DeviceTier.B; return true;
                case "C": tier = DeviceTier.C; return true;
                default: return false;
            }
        }

        // ---- mode lists ----

        static List<ModeCode> ParseModeList(string text, Context ctx)
        {
            var token = ParseJson(text, JTokenType.Array, ctx);
            if (token == null) return null;
            var result = new List<ModeCode>();
            var seen = new bool[ModeCatalog.All.Count + 1];
            int index = 0;
            foreach (var el in (JArray)token)
            {
                string s = RemoteConfigJson.GetString(el);
                if (s == null)
                {
                    ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "element #" + index + " is not a string, ignored");
                }
                else if (!TryResolveMode(s, out ModeCode code))
                {
                    ctx.Warning(RemoteConfigIssueKind.UnknownMode, "unknown mode " + RemoteConfigJson.Excerpt(s) + " ignored");
                }
                else if (seen[(int)code])
                {
                    ctx.Warning(RemoteConfigIssueKind.DuplicateEntry, "duplicate mode " + code + " ignored");
                }
                else
                {
                    seen[(int)code] = true;
                    result.Add(code);
                }
                index++;
            }
            return result;
        }

        // ---- fixed tables (field-wise merge) ----

        static ExportBitrates ParseBitrates(string text, ExportBitrates current, Context ctx)
        {
            var obj = (JObject)ParseJson(text, JTokenType.Object, ctx);
            if (obj == null) return null;
            int[] v = { current.P720Fps30Kbps, current.P720Fps60Kbps, current.P1080Fps30Kbps, current.P1080Fps60Kbps };
            var seen = new bool[v.Length];
            foreach (var prop in obj.Properties())
            {
                int idx = Array.FindIndex(s_bitrateKeys, k => string.Equals(k, prop.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (idx < 0)
                {
                    ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "unknown bitrate key " + RemoteConfigJson.Excerpt(prop.Name) + " ignored");
                    continue;
                }
                if (seen[idx])
                {
                    ctx.Warning(RemoteConfigIssueKind.DuplicateEntry, "duplicate bitrate key " + s_bitrateKeys[idx] + " ignored");
                    continue;
                }
                seen[idx] = true;
                if (!RemoteConfigJson.TryGetInteger(prop.Value, out long n))
                {
                    ctx.Warning(RemoteConfigIssueKind.InvalidEntry, s_bitrateKeys[idx] + " is not an integer, default kept");
                    continue;
                }
                v[idx] = Clamp(n, RemoteConfigLimits.BitrateKbpsMin, RemoteConfigLimits.BitrateKbpsMax, ctx, s_bitrateKeys[idx]);
            }
            return new ExportBitrates(v[0], v[1], v[2], v[3]);
        }

        static TierValues<int> ParseFpsByTier(string text, TierValues<int> current, Context ctx)
        {
            var obj = (JObject)ParseJson(text, JTokenType.Object, ctx);
            if (obj == null) return null;
            var result = current;
            var seen = new bool[3];
            foreach (var prop in obj.Properties())
            {
                if (!TryParseTier(prop.Name, out DeviceTier tier))
                {
                    ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "unknown tier " + RemoteConfigJson.Excerpt(prop.Name) + " ignored");
                    continue;
                }
                if (seen[(int)tier])
                {
                    ctx.Warning(RemoteConfigIssueKind.DuplicateEntry, "duplicate tier " + tier + " ignored");
                    continue;
                }
                seen[(int)tier] = true;
                if (!RemoteConfigJson.TryGetInteger(prop.Value, out long fps) || fps > int.MaxValue || fps < int.MinValue || !RemoteConfigLimits.IsValidFps((int)fps))
                {
                    ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "tier " + tier + ": fps must be 30 or 60, default kept");
                    continue;
                }
                result = result.With(tier, (int)fps);
            }
            return result;
        }

        static TierValues<GenerationBudget> ParseBudget(string text, TierValues<GenerationBudget> current, Context ctx)
        {
            var obj = (JObject)ParseJson(text, JTokenType.Object, ctx);
            if (obj == null) return null;
            var result = current;
            var seen = new bool[3];
            foreach (var prop in obj.Properties())
            {
                if (!TryParseTier(prop.Name, out DeviceTier tier))
                {
                    ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "unknown tier " + RemoteConfigJson.Excerpt(prop.Name) + " ignored");
                    continue;
                }
                if (seen[(int)tier])
                {
                    ctx.Warning(RemoteConfigIssueKind.DuplicateEntry, "duplicate tier " + tier + " ignored");
                    continue;
                }
                seen[(int)tier] = true;
                if (prop.Value.Type != JTokenType.Object)
                {
                    ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "tier " + tier + ": expected {candidates, ms}, default kept");
                    continue;
                }
                var old = result.Get(tier);
                int candidates = old.Candidates;
                int ms = old.Milliseconds;
                foreach (var field in ((JObject)prop.Value).Properties())
                {
                    string name = field.Name.Trim();
                    bool isCandidates = string.Equals(name, "candidates", StringComparison.OrdinalIgnoreCase);
                    bool isMs = string.Equals(name, "ms", StringComparison.OrdinalIgnoreCase);
                    if (!isCandidates && !isMs)
                    {
                        ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "tier " + tier + ": unknown field " + RemoteConfigJson.Excerpt(field.Name) + " ignored");
                        continue;
                    }
                    if (!RemoteConfigJson.TryGetInteger(field.Value, out long n))
                    {
                        ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "tier " + tier + ": " + name + " is not an integer, default kept");
                        continue;
                    }
                    if (isCandidates) candidates = Clamp(n, RemoteConfigLimits.BudgetCandidatesMin, RemoteConfigLimits.BudgetCandidatesMax, ctx, "tier " + tier + " candidates");
                    else ms = Clamp(n, RemoteConfigLimits.BudgetMsMin, RemoteConfigLimits.BudgetMsMax, ctx, "tier " + tier + " ms");
                }
                result = result.With(tier, new GenerationBudget(candidates, ms));
            }
            return result;
        }

        // ---- open maps (entry-wise merge) ----

        static Dictionary<ModeCode, GenerationWeights> ParseWeights(string text, Dictionary<ModeCode, GenerationWeights> current, Context ctx)
        {
            var obj = (JObject)ParseJson(text, JTokenType.Object, ctx);
            if (obj == null) return null;
            var result = new Dictionary<ModeCode, GenerationWeights>(current);
            var seen = new bool[ModeCatalog.All.Count + 1];
            foreach (var prop in obj.Properties())
            {
                if (!TryResolveMode(prop.Name, out ModeCode code))
                {
                    ctx.Warning(RemoteConfigIssueKind.UnknownMode, "unknown mode " + RemoteConfigJson.Excerpt(prop.Name) + " ignored");
                    continue;
                }
                if (seen[(int)code])
                {
                    ctx.Warning(RemoteConfigIssueKind.DuplicateEntry, "duplicate mode " + code + " ignored");
                    continue;
                }
                seen[(int)code] = true;
                var w = ParseWeightSet(prop.Value, code, ctx);
                if (w != null) result[code] = w;
            }
            return result;
        }

        static GenerationWeights ParseWeightSet(JToken token, ModeCode code, Context ctx)
        {
            var w = new double[5];
            if (token.Type == JTokenType.Array)
            {
                var arr = (JArray)token;
                if (arr.Count != 5)
                {
                    ctx.Warning(RemoteConfigIssueKind.InvalidEntry, code + ": expected 5 weights, got " + arr.Count + "; built-in weights kept");
                    return null;
                }
                for (int i = 0; i < 5; i++)
                {
                    if (!RemoteConfigJson.TryGetNumber(arr[i], out w[i]) || !GenerationWeights.IsValidWeight(w[i]))
                    {
                        ctx.Warning(RemoteConfigIssueKind.InvalidEntry, code + ": w" + (i + 1) + " must be a finite number >= 0; built-in weights kept");
                        return null;
                    }
                }
            }
            else if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                foreach (var field in obj.Properties())
                {
                    if (!IsWeightName(field.Name))
                        ctx.Warning(RemoteConfigIssueKind.InvalidEntry, code + ": unknown field " + RemoteConfigJson.Excerpt(field.Name) + " ignored");
                }
                for (int i = 0; i < 5; i++)
                {
                    string name = "w" + (i + 1);
                    if (!RemoteConfigJson.TryGetNumber(obj[name], out w[i]) || !GenerationWeights.IsValidWeight(w[i]))
                    {
                        ctx.Warning(RemoteConfigIssueKind.InvalidEntry, code + ": " + name + " missing or not a finite number >= 0; built-in weights kept");
                        return null;
                    }
                }
            }
            else
            {
                ctx.Warning(RemoteConfigIssueKind.InvalidEntry, code + ": expected {w1..w5} or [5 numbers]; built-in weights kept");
                return null;
            }
            return new GenerationWeights((float)w[0], (float)w[1], (float)w[2], (float)w[3], (float)w[4]);
        }

        static bool IsWeightName(string name)
        {
            return name == "w1" || name == "w2" || name == "w3" || name == "w4" || name == "w5";
        }

        static List<TierOverride> ParseTierOverrides(string text, List<TierOverride> current, Context ctx)
        {
            var obj = (JObject)ParseJson(text, JTokenType.Object, ctx);
            if (obj == null) return null;
            var delivered = new List<TierOverride>();
            foreach (var prop in obj.Properties())
            {
                string model = prop.Name.Trim();
                if (model.Length == 0)
                {
                    ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "empty device model ignored");
                    continue;
                }
                if (delivered.Exists(o => string.Equals(o.DeviceModel, model, StringComparison.OrdinalIgnoreCase)))
                {
                    ctx.Warning(RemoteConfigIssueKind.DuplicateEntry, "duplicate device model " + RemoteConfigJson.Excerpt(model) + " ignored");
                    continue;
                }
                if (!TryParseTier(RemoteConfigJson.GetString(prop.Value), out DeviceTier tier))
                {
                    ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "device model " + RemoteConfigJson.Excerpt(model) + ": tier must be \"A\", \"B\" or \"C\"; ignored");
                    continue;
                }
                delivered.Add(new TierOverride(model, tier));
            }
            return MergeByKey(delivered, current, o => o.DeviceModel);
        }

        static List<PlatformSafeZone> ParseSafeZones(string text, List<PlatformSafeZone> current, Context ctx)
        {
            var obj = (JObject)ParseJson(text, JTokenType.Object, ctx);
            if (obj == null) return null;
            var delivered = new List<PlatformSafeZone>();
            foreach (var prop in obj.Properties())
            {
                string platform = prop.Name.Trim();
                if (platform.Length == 0)
                {
                    ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "empty platform id ignored");
                    continue;
                }
                if (delivered.Exists(p => string.Equals(p.Platform, platform, StringComparison.OrdinalIgnoreCase)))
                {
                    ctx.Warning(RemoteConfigIssueKind.DuplicateEntry, "duplicate platform " + RemoteConfigJson.Excerpt(platform) + " ignored");
                    continue;
                }
                if (prop.Value.Type != JTokenType.Array)
                {
                    ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "platform " + RemoteConfigJson.Excerpt(platform) + ": expected an array of rects; default kept");
                    continue;
                }
                var rects = new List<NormalizedRect>();
                int index = 0;
                foreach (var el in (JArray)prop.Value)
                {
                    var r = ParseRect(el, platform + " #" + index, ctx);
                    if (r != null) rects.Add(r);
                    index++;
                }
                delivered.Add(new PlatformSafeZone(platform, rects));
            }
            return MergeByKey(delivered, current, p => p.Platform);
        }

        /// <summary>
        /// Rect {x,y,w,h}. Non-numbers, non-positive size → dropped. A rect leaving the frame is clamped to its intersection with
        /// the frame (Warning); an empty intersection is dropped.
        /// </summary>
        static NormalizedRect ParseRect(JToken el, string where, Context ctx)
        {
            if (el.Type != JTokenType.Object)
            {
                ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "rect " + where + " is not an object, ignored");
                return null;
            }
            var obj = (JObject)el;
            if (!RemoteConfigJson.TryGetNumber(obj["x"], out double x) || !RemoteConfigJson.TryGetNumber(obj["y"], out double y)
                || !RemoteConfigJson.TryGetNumber(obj["w"], out double w) || !RemoteConfigJson.TryGetNumber(obj["h"], out double h))
            {
                ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "rect " + where + " needs numeric x, y, w, h; ignored");
                return null;
            }
            if (w <= 0.0 || h <= 0.0)
            {
                ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "rect " + where + " has non-positive size, ignored");
                return null;
            }
            if (NormalizedRect.IsValid((float)x, (float)y, (float)w, (float)h))
            {
                // Already inside the frame at float precision: keep exactly (writer → parser round trip is lossless).
                return new NormalizedRect((float)x, (float)y, (float)w, (float)h);
            }
            double x0 = Math.Max(0.0, x), y0 = Math.Max(0.0, y);
            double x1 = Math.Min(1.0, x + w), y1 = Math.Min(1.0, y + h);
            if (x1 <= x0 || y1 <= y0)
            {
                ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "rect " + where + " lies outside the frame, ignored");
                return null;
            }
            float fx = (float)x0, fy = (float)y0;
            float fw = FitSize(fx, (float)(x1 - x0));
            float fh = FitSize(fy, (float)(y1 - y0));
            if (!NormalizedRect.IsValid(fx, fy, fw, fh))
            {
                ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "rect " + where + " is degenerate, ignored");
                return null;
            }
            // Decimal round-off (0.33 + 0.67 = 1.0000000000000002) is clamped silently.
            const double eps = 1e-6;
            if (x0 - x > eps || y0 - y > eps || (x + w) - x1 > eps || (y + h) - y1 > eps)
                ctx.Warning(RemoteConfigIssueKind.OutOfRange, "rect " + where + " clamped to the frame");
            return new NormalizedRect(fx, fy, fw, fh);
        }

        /// <summary>Shrinks <paramref name="size"/> by float ulps until origin + size ≤ 1 (guards float rounding).</summary>
        static float FitSize(float origin, float size)
        {
            for (int i = 0; i < 8 && size > 0f && origin + size > 1f; i++)
            {
                size = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(size) - 1);
            }
            return size;
        }

        static List<TextTemplateSet> ParseTextTemplates(string text, List<TextTemplateSet> current, Context ctx)
        {
            var obj = (JObject)ParseJson(text, JTokenType.Object, ctx);
            if (obj == null) return null;
            var delivered = new List<TextTemplateSet>();
            foreach (var prop in obj.Properties())
            {
                string language = prop.Name.Trim();
                if (language.Length == 0)
                {
                    ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "empty language tag ignored");
                    continue;
                }
                if (delivered.Exists(s => string.Equals(s.Language, language, StringComparison.OrdinalIgnoreCase)))
                {
                    ctx.Warning(RemoteConfigIssueKind.DuplicateEntry, "duplicate language " + RemoteConfigJson.Excerpt(language) + " ignored");
                    continue;
                }
                if (prop.Value.Type != JTokenType.Array)
                {
                    ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "language " + RemoteConfigJson.Excerpt(language) + ": expected an array of {id, text}; default kept");
                    continue;
                }
                var templates = new List<TextTemplate>();
                int index = 0;
                foreach (var el in (JArray)prop.Value)
                {
                    string where = language + " #" + index;
                    index++;
                    string id = el.Type == JTokenType.Object ? RemoteConfigJson.GetString(el["id"]) : null;
                    string body = el.Type == JTokenType.Object ? RemoteConfigJson.GetString(el["text"]) : null;
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(body))
                    {
                        ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "template " + where + " needs non-empty string id and text; ignored");
                        continue;
                    }
                    string trimmedId = id.Trim();
                    if (templates.Exists(t => t.Id == trimmedId))
                    {
                        ctx.Warning(RemoteConfigIssueKind.DuplicateEntry, "template " + where + ": duplicate id " + RemoteConfigJson.Excerpt(trimmedId) + " ignored");
                        continue;
                    }
                    templates.Add(new TextTemplate(trimmedId, body));
                }
                delivered.Add(new TextTemplateSet(language, templates));
            }
            return MergeByKey(delivered, current, s => s.Language);
        }

        /// <summary>Delivered entries plus the current entries whose key (ignoring case) was not delivered.</summary>
        static List<T> MergeByKey<T>(List<T> delivered, List<T> current, Func<T, string> key)
        {
            var result = new List<T>(delivered);
            foreach (var c in current)
            {
                string k = key(c);
                if (!delivered.Exists(d => string.Equals(key(d), k, StringComparison.OrdinalIgnoreCase))) result.Add(c);
            }
            return result;
        }

        // ---- lists (whole replacement) ----

        static List<string> ParsePresets(string text, Context ctx)
        {
            var token = ParseJson(text, JTokenType.Array, ctx);
            if (token == null) return null;
            var result = new List<string>();
            int index = 0;
            foreach (var el in (JArray)token)
            {
                if (el.Type == JTokenType.Object) result.Add(RemoteConfigJson.Compact(el));
                else ctx.Warning(RemoteConfigIssueKind.InvalidEntry, "preset #" + index + " is not a JSON object, ignored");
                index++;
            }
            return result;
        }

        static List<RemoteMelody> ParseMelodies(string text, Context ctx)
        {
            var token = ParseJson(text, JTokenType.Array, ctx);
            if (token == null) return null;
            var result = new List<RemoteMelody>();
            int index = 0;
            foreach (var el in (JArray)token)
            {
                var m = ParseMelody(el, index, ctx);
                if (m != null)
                {
                    if (result.Exists(x => x.Id == m.Id))
                        ctx.Warning(RemoteConfigIssueKind.DuplicateEntry, "melody #" + index + ": duplicate id " + RemoteConfigJson.Excerpt(m.Id) + " ignored");
                    else
                        result.Add(m);
                }
                index++;
            }
            return result;
        }

        /// <summary>A melody with any invalid note is dropped as a whole: a partial melody would play the wrong tune.</summary>
        static RemoteMelody ParseMelody(JToken el, int index, Context ctx)
        {
            string where = "melody #" + index;
            if (el.Type != JTokenType.Object)
            {
                ctx.Warning(RemoteConfigIssueKind.InvalidEntry, where + " is not an object, ignored");
                return null;
            }
            var obj = (JObject)el;
            string id = RemoteConfigJson.GetString(obj["id"]);
            string name = RemoteConfigJson.GetString(obj["name"]);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                ctx.Warning(RemoteConfigIssueKind.InvalidEntry, where + " needs non-empty string id and name; ignored");
                return null;
            }
            var notesToken = obj["notes"];
            if (notesToken == null || notesToken.Type != JTokenType.Array || ((JArray)notesToken).Count == 0)
            {
                ctx.Warning(RemoteConfigIssueKind.InvalidEntry, where + " needs a non-empty notes array; ignored");
                return null;
            }
            var notes = new List<MelodyNote>();
            int noteIndex = 0;
            foreach (var n in (JArray)notesToken)
            {
                if (n.Type != JTokenType.Object
                    || !RemoteConfigJson.TryGetInteger(n["pitch"], out long pitch)
                    || pitch < RemoteConfigLimits.PitchMin || pitch > RemoteConfigLimits.PitchMax
                    || !RemoteConfigJson.TryGetNumber(n["durWeight"], out double dur) || !MelodyNote.IsValidDurWeight(dur)
                    || !RemoteConfigJson.TryGetNumber(n["velocity"], out double vel) || !MelodyNote.IsValidVelocity(vel))
                {
                    ctx.Warning(RemoteConfigIssueKind.InvalidEntry, where + " note #" + noteIndex
                        + " needs integer pitch 0-127, durWeight > 0, velocity 0-1; melody ignored");
                    return null;
                }
                notes.Add(new MelodyNote((int)pitch, (float)dur, (float)vel));
                noteIndex++;
            }
            return new RemoteMelody(id, name, notes);
        }

        sealed class Context
        {
            readonly string _key;
            readonly List<RemoteConfigIssue> _issues;

            public Context(string key, List<RemoteConfigIssue> issues)
            {
                _key = key;
                _issues = issues;
            }

            public void Info(RemoteConfigIssueKind kind, string message)
            {
                _issues.Add(new RemoteConfigIssue(_key, RemoteConfigIssueSeverity.Info, kind, message));
            }

            public void Warning(RemoteConfigIssueKind kind, string message)
            {
                _issues.Add(new RemoteConfigIssue(_key, RemoteConfigIssueSeverity.Warning, kind, message));
            }

            public void Error(RemoteConfigIssueKind kind, string message)
            {
                _issues.Add(new RemoteConfigIssue(_key, RemoteConfigIssueSeverity.Error, kind, message));
            }
        }
    }
}
