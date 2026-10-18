using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// A sound pack as data (spec §2.7): impulse thresholds i0/i1 of the volume curve, size range for pitch rules and one
    /// <see cref="SoundRule"/> per event type. Event types without a rule are silent (the <c>None</c> pack has no rules).
    /// Built-in packs come from <see cref="SoundPackCatalog"/>; remote/content packs are loaded with <see cref="SoundPackJson"/>.
    /// The mixer snapshots the pack when it is created, so later edits of this object do not affect a running mixer.
    /// </summary>
    public sealed class SoundPack
    {
        public const float MaxGain = 4f;
        public const int MaxSamplesPerRule = 32;

        public SoundPackId Id { get; set; }

        /// <summary>Impulse below which hits are silent; volume 0.15 at exactly i0 (spec §2.7).</summary>
        public float I0 { get; set; }

        /// <summary>Impulse at which the volume reaches 1 (spec §2.7). Must be greater than <see cref="I0"/>.</summary>
        public float I1 { get; set; } = 1f;

        /// <summary>Pack gain, 0…4.</summary>
        public float Gain { get; set; } = 1f;

        /// <summary>Body size mapped to the highest pitch (default 0.1 m — lower end of <c>ballSize</c>, spec §2.2).</summary>
        public float SizeMin { get; set; } = 0.1f;

        /// <summary>Body size mapped to the lowest pitch (default 1.5 m — upper end of <c>ballSize</c>, spec §2.2).</summary>
        public float SizeMax { get; set; } = 1.5f;

        public List<SoundRule> Rules { get; set; } = new List<SoundRule>();

        /// <summary>The rule for <paramref name="type"/>, or null (silent). First match wins; validation forbids duplicates.</summary>
        public SoundRule GetRule(SoundEventType type)
        {
            if (Rules == null) return null;
            for (int i = 0; i < Rules.Count; i++)
            {
                var r = Rules[i];
                if (r != null && r.EventType == type) return r;
            }
            return null;
        }

        /// <summary>Checks every field; returns an empty list when the pack is usable. Never throws.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (!Enum.IsDefined(typeof(SoundPackId), Id)) errors.Add("id: unknown sound pack " + (int)Id);
            if (!IsFinite(I0) || I0 < 0f) errors.Add("i0 must be a finite number >= 0");
            if (!IsFinite(I1) || !(I1 > I0)) errors.Add("i1 must be a finite number greater than i0");
            if (!IsFinite(Gain) || Gain < 0f || Gain > MaxGain) errors.Add("gain must be within 0..4");
            if (!IsFinite(SizeMin) || SizeMin < 0f) errors.Add("sizeMin must be a finite number >= 0");
            if (!IsFinite(SizeMax) || !(SizeMax > SizeMin)) errors.Add("sizeMax must be a finite number greater than sizeMin");
            if (Rules == null)
            {
                errors.Add("rules must be an array");
                return errors;
            }

            var seen = new bool[7];
            for (int i = 0; i < Rules.Count; i++)
            {
                string p = "rules[" + i.ToString(CultureInfo.InvariantCulture) + "].";
                var r = Rules[i];
                if (r == null)
                {
                    errors.Add(p.TrimEnd('.') + " is null");
                    continue;
                }
                if (!Enum.IsDefined(typeof(SoundEventType), r.EventType)) errors.Add(p + "eventType is unknown");
                else
                {
                    int t = (int)r.EventType;
                    if (seen[t]) errors.Add(p + "eventType " + r.EventType + " is duplicated");
                    seen[t] = true;
                }
                if (r.Samples == null || r.Samples.Length == 0) errors.Add(p + "samples must list at least one sample id");
                else if (r.Samples.Length > MaxSamplesPerRule) errors.Add(p + "samples: at most 32 ids");
                else
                {
                    for (int s = 0; s < r.Samples.Length; s++)
                    {
                        if (string.IsNullOrEmpty(r.Samples[s])) errors.Add(p + "samples[" + s.ToString(CultureInfo.InvariantCulture) + "] is empty");
                    }
                }
                if (!Enum.IsDefined(typeof(SampleSelection), r.Selection)) errors.Add(p + "selection is unknown");
                if (!Enum.IsDefined(typeof(PitchMode), r.Pitch)) errors.Add(p + "pitch is unknown");
                if (r.RootMidi < 0 || r.RootMidi > 127) errors.Add(p + "rootMidi must be within 0..127");
                if (r.LowMidi < 0 || r.LowMidi > 127) errors.Add(p + "lowMidi must be within 0..127");
                if (r.Steps < 0 || r.Steps > 60) errors.Add(p + "steps must be within 0..60");
                if (r.SemitoneRange < 0 || r.SemitoneRange > 24) errors.Add(p + "semitoneRange must be within 0..24");
                if (r.Semitones < -AudioMath.MaxSemitoneShift || r.Semitones > AudioMath.MaxSemitoneShift) errors.Add(p + "semitones must be within -48..48");
                if (!IsFinite(r.Gain) || r.Gain < 0f || r.Gain > MaxGain) errors.Add(p + "gain must be within 0..4");
            }
            return errors;
        }

        static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }
    }
}
