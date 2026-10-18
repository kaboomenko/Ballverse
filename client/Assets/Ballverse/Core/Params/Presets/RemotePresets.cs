using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Params.Presets
{
    /// <summary>
    /// Validation of presets delivered by Remote Config <c>remote_presets</c> (spec §2.3, §9.3): a JSON array of presets.
    /// Never throws. A preset is rejected (with a reason) when it is not an object, its id / mode / name is invalid, its
    /// id repeats an earlier one, its mode has no schema here, or a value has the wrong type, is non-finite, names an
    /// unknown option or is an unparseable colour / seed. Unknown parameters are dropped and out-of-range, off-step or
    /// over-long values are fixed; both are reported as warnings. Accepted presets hold sanitized partial values
    /// (tier caps are applied later, by <see cref="Preset.Apply"/>), source Remote and <c>isNew</c> true unless the
    /// payload says otherwise.
    /// </summary>
    public static class RemotePresets
    {
        /// <param name="json">Payload; null or blank means "no remote presets" (the default <c>[]</c>).</param>
        /// <param name="schemaForMode">Complete schema of a mode id, or null for a mode this build does not know.</param>
        public static RemotePresetLoadResult Load(string json, Func<string, ParamSchema> schemaForMode)
        {
            var accepted = new List<Preset>();
            var rejected = new List<PresetRejection>();
            var warnings = new List<PresetWarning>();
            try
            {
                if (schemaForMode == null)
                {
                    rejected.Add(new PresetRejection(-1, null, "No schema provider"));
                }
                else if (!string.IsNullOrWhiteSpace(json))
                {
                    LoadCore(json, schemaForMode, accepted, rejected, warnings);
                }
            }
            catch (Exception e)
            {
                rejected.Add(new PresetRejection(-1, null, "Unexpected error: " + e.Message));
            }
            return new RemotePresetLoadResult(accepted, rejected, warnings);
        }

        static void LoadCore(string json, Func<string, ParamSchema> schemaForMode,
            List<Preset> accepted, List<PresetRejection> rejected, List<PresetWarning> warnings)
        {
            if (!PresetJson.TryParseToken(json, out JToken token, out string error))
            {
                rejected.Add(new PresetRejection(-1, null, error));
                return;
            }
            if (!(token is JArray array))
            {
                rejected.Add(new PresetRejection(-1, null, "Expected a JSON array of presets"));
                return;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                string rawId = (array[i] as JObject)?["id"]?.Type == JTokenType.String ? (string)array[i]["id"] : null;
                if (!PresetJson.TryFromToken(array[i], PresetSource.Remote, out Preset preset, out error))
                {
                    rejected.Add(new PresetRejection(i, rawId, error));
                    continue;
                }
                if (ids.Contains(preset.Id))
                {
                    rejected.Add(new PresetRejection(i, preset.Id, "Duplicate preset id"));
                    continue;
                }

                ParamSchema schema = null;
                try
                {
                    schema = schemaForMode(preset.ModeId);
                }
                catch (Exception e)
                {
                    rejected.Add(new PresetRejection(i, preset.Id, "Schema lookup failed for mode '" + preset.ModeId + "': " + e.Message));
                    continue;
                }
                if (schema == null || schema.IsLayer)
                {
                    rejected.Add(new PresetRejection(i, preset.Id, "Unknown mode '" + preset.ModeId + "'"));
                    continue;
                }

                var sanitized = preset.Values.SanitizePartial(schema);
                ParamIssue fatal = null;
                foreach (var issue in sanitized.Issues)
                {
                    if (IsFatal(issue.Code))
                    {
                        fatal = issue;
                        break;
                    }
                }
                if (fatal != null)
                {
                    rejected.Add(new PresetRejection(i, preset.Id, "Invalid value: " + fatal));
                    continue;
                }

                foreach (var issue in sanitized.Issues) warnings.Add(new PresetWarning(preset.Id, issue));
                ids.Add(preset.Id);
                accepted.Add(preset.WithValues(sanitized.Values));
            }
        }

        static bool IsFatal(ParamIssueCode code)
        {
            switch (code)
            {
                case ParamIssueCode.TypeMismatch:
                case ParamIssueCode.NonFinite:
                case ParamIssueCode.InvalidOption:
                case ParamIssueCode.InvalidValue:
                    return true;
                default:
                    return false;
            }
        }
    }
}
