using System;

namespace Ballverse.Core.Params.Presets
{
    /// <summary>
    /// Named partial parameter set of one mode (spec §2.3, §7.6 <c>Preset</c>). Built-in presets usually carry a
    /// localization <see cref="NameKey"/>; user presets carry a <see cref="Name"/> (trimmed, 1–32 characters).
    /// Values are partial: applying a preset means defaults + preset values, sanitized (<see cref="Apply"/>).
    /// </summary>
    public sealed class Preset
    {
        public const int MaxNameLength = 32;
        public const int MaxIdLength = 64;
        public const int MaxNameKeyLength = 128;

        /// <summary>Stable id, 1–64 chars of [A-Za-z0-9_.-]; supplied by the caller (Core creates no ids).</summary>
        public string Id { get; }
        /// <summary>Display name, or null when only <see cref="NameKey"/> is set.</summary>
        public string Name { get; }
        /// <summary>Localization key of the name, or null.</summary>
        public string NameKey { get; }
        /// <summary>Mode id, e.g. "ring_escape" (spec §2.8).</summary>
        public string ModeId { get; }
        /// <summary>Partial values; ids absent here take schema defaults when applied.</summary>
        public ParamSet Values { get; }
        public PresetSource Source { get; }
        /// <summary>"Новое" badge (spec §2.3: presets from Remote Config).</summary>
        public bool IsNew { get; }

        Preset(string id, string name, string nameKey, string modeId, ParamSet values, PresetSource source, bool isNew)
        {
            Id = id;
            Name = name;
            NameKey = nameKey;
            ModeId = modeId;
            Values = values;
            Source = source;
            IsNew = isNew;
        }

        /// <summary>Creates a validated preset; throws <see cref="PresetException"/> (see <see cref="TryCreate"/> for the rules).</summary>
        public static Preset Create(string id, string modeId, ParamSet values, PresetSource source,
            string name = null, string nameKey = null, bool isNew = false)
        {
            if (!TryCreate(id, modeId, values, source, name, nameKey, isNew, out Preset p, out string error))
                throw new PresetException(error);
            return p;
        }

        /// <summary>
        /// Validates and creates a preset: id 1–64 chars of [A-Za-z0-9_.-]; mode id 1–64 chars of [a-z0-9_]; name trimmed
        /// and 1–32 code points when given; user presets need a name, others a name or a name key. Null values = empty set.
        /// </summary>
        public static bool TryCreate(string id, string modeId, ParamSet values, PresetSource source,
            string name, string nameKey, bool isNew, out Preset preset, out string error)
        {
            preset = null;
            if (!IsValidId(id))
            {
                error = "Invalid preset id " + TextRules.Quote(id) + " (1-" + MaxIdLength + " chars of A-Z a-z 0-9 _ . -)";
                return false;
            }
            if (!IsValidModeId(modeId))
            {
                error = "Preset '" + id + "': invalid mode id " + TextRules.Quote(modeId);
                return false;
            }
            string normalizedName = null;
            if (name != null)
            {
                normalizedName = NormalizeName(name);
                if (normalizedName == null)
                {
                    error = "Preset '" + id + "': name must be 1-" + MaxNameLength + " characters after trimming";
                    return false;
                }
            }
            if (nameKey != null && (nameKey.Length == 0 || nameKey.Length > MaxNameKeyLength))
            {
                error = "Preset '" + id + "': name key must be 1-" + MaxNameKeyLength + " characters";
                return false;
            }
            if (source == PresetSource.User && normalizedName == null)
            {
                error = "Preset '" + id + "': user presets need a name";
                return false;
            }
            if (normalizedName == null && nameKey == null)
            {
                error = "Preset '" + id + "': a name or a name key is required";
                return false;
            }
            error = null;
            preset = new Preset(id, normalizedName, nameKey, modeId, values ?? ParamSet.Empty, source, isNew);
            return true;
        }

        /// <summary>
        /// Trimmed name if it has 1–32 code points after trimming, otherwise null (spec §2.3 rename rules). Unpaired
        /// surrogates are replaced with U+FFFD so the name survives the UTF-8 preset file.
        /// </summary>
        public static string NormalizeName(string name)
        {
            if (name == null) return null;
            string t = TextRules.ReplaceLoneSurrogates(name.Trim());
            int n = TextRules.CodePointCount(t);
            return n >= 1 && n <= MaxNameLength ? t : null;
        }

        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength) return false;
            foreach (char c in id)
            {
                if (!TextRules.IsAsciiLetter(c) && !(c >= '0' && c <= '9') && c != '_' && c != '.' && c != '-') return false;
            }
            return true;
        }

        public static bool IsValidModeId(string modeId)
        {
            if (string.IsNullOrEmpty(modeId) || modeId.Length > 64) return false;
            foreach (char c in modeId)
            {
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') return false;
            }
            return true;
        }

        /// <summary>Renamed copy; throws <see cref="PresetException"/> when the name is invalid.</summary>
        public Preset WithName(string name)
        {
            string n = NormalizeName(name);
            if (n == null) throw new PresetException("Preset name must be 1-" + MaxNameLength + " characters after trimming");
            return new Preset(Id, n, NameKey, ModeId, Values, Source, IsNew);
        }

        public Preset WithValues(ParamSet values)
        {
            return new Preset(Id, Name, NameKey, ModeId, values ?? throw new ArgumentNullException(nameof(values)), Source, IsNew);
        }

        public Preset WithIsNew(bool isNew)
        {
            return new Preset(Id, Name, NameKey, ModeId, Values, Source, isNew);
        }

        /// <summary>
        /// Parameters of this preset for <paramref name="schema"/> on <paramref name="tier"/>: defaults overlaid with the
        /// preset values, sanitized (spec §2.3). Issues concern preset values only (defaults never report "missing").
        /// </summary>
        public SanitizeResult Apply(ParamSchema schema, DeviceTier tier)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            return schema.CreateDefaults(tier).Overlay(Values).Sanitize(schema, tier);
        }

        /// <summary>
        /// "Сбросить к пресету" (spec §2.2 panel rules): the preset's parameters as in <see cref="Apply"/>, except that
        /// seed parameters the preset does not set keep their current value, so a reset does not re-roll the scene.
        /// </summary>
        public SanitizeResult ResetFrom(ParamSet current, ParamSchema schema, DeviceTier tier)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            var values = schema.CreateDefaults(tier);
            foreach (var def in schema.Params)
            {
                if (def.Type == ParamType.Seed && !Values.Contains(def.Id) && current.TryGet(def.Id, out ParamValue seed))
                    values = values.With(def.Id, seed);
            }
            return values.Overlay(Values).Sanitize(schema, tier);
        }

        public override string ToString()
        {
            return Source + " preset " + Id + " (" + (Name ?? NameKey) + ", " + ModeId + ")";
        }
    }
}
