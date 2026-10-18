using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Config
{
    /// <summary>
    /// Serializes a snapshot back to the raw Firebase string form (spec §9.3), e.g. to register in-app defaults with the SDK
    /// or to author a console template. <c>RemoteConfigParser.Parse(ToRaw(v))</c> reproduces <c>v</c> without issues.
    /// </summary>
    public static class RemoteConfigWriter
    {
        /// <summary>Every key of <see cref="RemoteConfigKeys.All"/> with its value as a string, in table order.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> ToRaw(RemoteConfigValues v)
        {
            if (v == null) throw new System.ArgumentNullException(nameof(v));
            var list = new List<KeyValuePair<string, string>>(RemoteConfigKeys.All.Count);
            foreach (string key in RemoteConfigKeys.All)
            {
                list.Add(new KeyValuePair<string, string>(key, Write(key, v)));
            }
            return list.AsReadOnly();
        }

        static string Write(string key, RemoteConfigValues v)
        {
            switch (key)
            {
                case RemoteConfigKeys.FreeModes: return Modes(v.FreeModes);
                case RemoteConfigKeys.ModesDisabled: return Modes(v.ModesDisabled);
                case RemoteConfigKeys.GalleryOrder: return Modes(v.GalleryOrder);
                case RemoteConfigKeys.InterstitialEveryNGames: return Int(v.InterstitialEveryNGames);
                case RemoteConfigKeys.InterstitialMinIntervalSec: return Int(v.InterstitialMinIntervalSec);
                case RemoteConfigKeys.InterstitialGraceGames: return Int(v.InterstitialGraceGames);
                case RemoteConfigKeys.RewardedExportEnabled: return Bool(v.RewardedExportEnabled);
                case RemoteConfigKeys.RewardedWaitTimeoutSec: return Int(v.RewardedWaitTimeoutSec);
                case RemoteConfigKeys.RewardedRetryWindowMin: return Int(v.RewardedRetryWindowMin);
                case RemoteConfigKeys.PaidModeTrialEnabled: return Bool(v.PaidModeTrialEnabled);
                case RemoteConfigKeys.PaidModeTrialSeconds: return Int(v.PaidModeTrialSeconds);
                case RemoteConfigKeys.ExportBitrates:
                {
                    var b = v.ExportBitrates;
                    return Compact(new JObject
                    {
                        { ExportBitrates.KeyOf(ExportResolution.P720, 30), b.P720Fps30Kbps },
                        { ExportBitrates.KeyOf(ExportResolution.P720, 60), b.P720Fps60Kbps },
                        { ExportBitrates.KeyOf(ExportResolution.P1080, 30), b.P1080Fps30Kbps },
                        { ExportBitrates.KeyOf(ExportResolution.P1080, 60), b.P1080Fps60Kbps },
                    });
                }
                case RemoteConfigKeys.ExportDefaultFpsByTier:
                {
                    var f = v.ExportDefaultFpsByTier;
                    return Compact(new JObject { { "A", f.A }, { "B", f.B }, { "C", f.C } });
                }
                case RemoteConfigKeys.GenerationWeights:
                {
                    var o = new JObject();
                    foreach (var kv in v.GenerationWeights)
                    {
                        var w = kv.Value;
                        var entry = new JObject();
                        if (w.HasWeights)
                        {
                            entry.Add("w1", w.W1);
                            entry.Add("w2", w.W2);
                            entry.Add("w3", w.W3);
                            entry.Add("w4", w.W4);
                            entry.Add("w5", w.W5);
                        }
                        if (w.DensityMin.HasValue) entry.Add("densityMin", w.DensityMin.Value);
                        if (w.DensityMax.HasValue) entry.Add("densityMax", w.DensityMax.Value);
                        if (w.QualityThreshold.HasValue) entry.Add("qualityThreshold", w.QualityThreshold.Value);
                        o.Add(kv.Key.ToString(), entry);
                    }
                    return Compact(o);
                }
                case RemoteConfigKeys.GenerationBudgetByTier:
                {
                    var t = v.GenerationBudgetByTier;
                    return Compact(new JObject { { "A", Budget(t.A) }, { "B", Budget(t.B) }, { "C", Budget(t.C) } });
                }
                case RemoteConfigKeys.TierOverrides:
                {
                    var o = new JObject();
                    foreach (var t in v.TierOverrides) o.Add(t.DeviceModel, t.Tier.ToString());
                    return Compact(o);
                }
                case RemoteConfigKeys.PlatformSafeZones:
                {
                    var o = new JObject();
                    foreach (var p in v.PlatformSafeZones)
                    {
                        var arr = new JArray();
                        foreach (var r in p.Rects) arr.Add(new JObject { { "x", r.X }, { "y", r.Y }, { "w", r.W }, { "h", r.H } });
                        o.Add(p.Platform, arr);
                    }
                    return Compact(o);
                }
                case RemoteConfigKeys.RemotePresets:
                {
                    var arr = new JArray();
                    foreach (var p in v.RemotePresets)
                    {
                        RemoteConfigJson.TryParse(p, out JToken token, out _);
                        arr.Add(token);
                    }
                    return Compact(arr);
                }
                case RemoteConfigKeys.RemoteMelodies:
                {
                    var arr = new JArray();
                    foreach (var m in v.RemoteMelodies)
                    {
                        var notes = new JArray();
                        foreach (var n in m.Notes) notes.Add(new JObject { { "pitch", n.Pitch }, { "durWeight", n.DurWeight }, { "velocity", n.Velocity } });
                        arr.Add(new JObject { { "id", m.Id }, { "name", m.Name }, { "notes", notes } });
                    }
                    return Compact(arr);
                }
                case RemoteConfigKeys.TextTemplates:
                {
                    var o = new JObject();
                    foreach (var set in v.TextTemplates)
                    {
                        var arr = new JArray();
                        foreach (var t in set.Templates) arr.Add(new JObject { { "id", t.Id }, { "text", t.Text } });
                        o.Add(set.Language, arr);
                    }
                    return Compact(o);
                }
                case RemoteConfigKeys.WatermarkEnabled: return Bool(v.WatermarkEnabled);
                case RemoteConfigKeys.RatePromptAfterExports: return Int(v.RatePromptAfterExports);
                default: throw new System.InvalidOperationException("unhandled key " + key);
            }
        }

        static JObject Budget(GenerationBudgetConfig b)
        {
            return new JObject { { "candidates", b.Candidates }, { "ms", b.Milliseconds } };
        }

        static string Modes(IReadOnlyList<ModeCode> modes)
        {
            var arr = new JArray();
            foreach (var m in modes) arr.Add(m.ToString());
            return Compact(arr);
        }

        static string Int(int n)
        {
            return n.ToString(CultureInfo.InvariantCulture);
        }

        static string Bool(bool b)
        {
            return b ? "true" : "false";
        }

        static string Compact(JToken token)
        {
            return RemoteConfigJson.Compact(token);
        }
    }
}
