using System;
using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Params.Presets
{
    /// <summary>
    /// The user's own presets for all modes (spec §2.3): save up to <see cref="MaxPerMode"/> per mode, rename, delete.
    /// Names are trimmed and must be 1–32 characters; ids come from the caller. Insertion order is kept and is the
    /// list order. Stored as JSON <c>{"schemaVersion":1,"presets":[...]}</c> (spec §3.9 settings, kept apart from projects).
    /// </summary>
    public sealed class UserPresetLibrary
    {
        public const int MaxPerMode = 20;
        public const int SchemaVersion = 1;

        readonly List<Preset> _presets = new List<Preset>();

        /// <summary>
        /// <c>schemaVersion</c> of the JSON this library was loaded from (<see cref="SchemaVersion"/> for a new library
        /// or when the file had none or an invalid one; <see cref="int.MaxValue"/> for a version too large for an int).
        /// </summary>
        public int LoadedSchemaVersion { get; private set; } = SchemaVersion;

        /// <summary>
        /// True when the library was loaded from a file written by a newer build (<see cref="LoadedSchemaVersion"/> &gt;
        /// <see cref="SchemaVersion"/>). Its presets were read best-effort; saving it with <see cref="ToJson"/> would
        /// stamp version 1 and drop fields this build does not know, so <see cref="ToJson"/> refuses unless forced.
        /// </summary>
        public bool IsFromNewerVersion => LoadedSchemaVersion > SchemaVersion;

        /// <summary>All presets in insertion order (read-only view).</summary>
        public IReadOnlyList<Preset> All => _presets.AsReadOnly();

        public IReadOnlyList<Preset> ForMode(string modeId)
        {
            var result = new List<Preset>();
            foreach (var p in _presets)
                if (p.ModeId == modeId) result.Add(p);
            return result;
        }

        public int CountForMode(string modeId)
        {
            int n = 0;
            foreach (var p in _presets)
                if (p.ModeId == modeId) n++;
            return n;
        }

        /// <summary>Preset with this id, or null.</summary>
        public Preset Find(string id)
        {
            int i = IndexOf(id);
            return i < 0 ? null : _presets[i];
        }

        /// <summary>Saves a new user preset with the given values (typically the current sanitized parameters).</summary>
        public UserPresetError TryAdd(string id, string modeId, string name, ParamSet values, out Preset preset)
        {
            preset = null;
            if (!Preset.IsValidId(id)) return UserPresetError.InvalidId;
            if (!Preset.IsValidModeId(modeId)) return UserPresetError.InvalidMode;
            if (Preset.NormalizeName(name) == null) return UserPresetError.InvalidName;
            if (IndexOf(id) >= 0) return UserPresetError.DuplicateId;
            if (CountForMode(modeId) >= MaxPerMode) return UserPresetError.LimitReached;
            preset = Preset.Create(id, modeId, values ?? ParamSet.Empty, PresetSource.User, name: name);
            _presets.Add(preset);
            return UserPresetError.None;
        }

        public UserPresetError TryRename(string id, string newName, out Preset preset)
        {
            preset = null;
            int i = IndexOf(id);
            if (i < 0) return UserPresetError.NotFound;
            if (Preset.NormalizeName(newName) == null) return UserPresetError.InvalidName;
            preset = _presets[i].WithName(newName);
            _presets[i] = preset;
            return UserPresetError.None;
        }

        /// <summary>Replaces the values of an existing preset ("save over").</summary>
        public UserPresetError TryUpdateValues(string id, ParamSet values, out Preset preset)
        {
            preset = null;
            int i = IndexOf(id);
            if (i < 0) return UserPresetError.NotFound;
            preset = _presets[i].WithValues(values ?? ParamSet.Empty);
            _presets[i] = preset;
            return UserPresetError.None;
        }

        public bool Delete(string id)
        {
            int i = IndexOf(id);
            if (i < 0) return false;
            _presets.RemoveAt(i);
            return true;
        }

        /// <summary>
        /// Serializes the library as version <see cref="SchemaVersion"/>. Throws <see cref="InvalidOperationException"/>
        /// when <see cref="IsFromNewerVersion"/> and <paramref name="allowDowngrade"/> is false, so a save does not
        /// silently overwrite a newer build's file with a downgraded one (the caller decides, e.g. after asking the user).
        /// </summary>
        public string ToJson(bool indented = true, bool allowDowngrade = false)
        {
            if (IsFromNewerVersion && !allowDowngrade)
                throw new InvalidOperationException("The preset library was written by a newer build (schemaVersion "
                    + LoadedSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "); saving it as version " + SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " would drop data. Pass allowDowngrade: true to save anyway.");
            var dto = new LibraryDto { SchemaVersion = SchemaVersion, Presets = new List<PresetDto>() };
            foreach (var p in _presets) dto.Presets.Add(PresetJson.ToDto(p, null));
            return CoreJson.Serialize(dto, indented);
        }

        /// <summary>
        /// Loads a library; never throws. Invalid entries, duplicate ids and presets beyond <see cref="MaxPerMode"/> for
        /// their mode are skipped and described in <paramref name="problems"/>. Malformed JSON yields an empty library.
        /// <c>schemaVersion</c> is checked: a missing one is read as version 1; a version newer than
        /// <see cref="SchemaVersion"/> (written by a newer build) or an invalid one is reported, and the presets are
        /// still read best-effort so the user does not lose them (values are sanitized when applied). The version read
        /// is exposed as <see cref="LoadedSchemaVersion"/> / <see cref="IsFromNewerVersion"/>.
        /// </summary>
        public static UserPresetLibrary FromJson(string json, out IReadOnlyList<string> problems)
        {
            var lib = new UserPresetLibrary();
            var list = new List<string>();
            problems = list;
            if (!PresetJson.TryParseToken(json, out JToken token, out string error))
            {
                list.Add(error);
                return lib;
            }
            if (!(token is JObject root) || !(root["presets"] is JArray array))
            {
                list.Add("Expected an object with a 'presets' array");
                return lib;
            }
            var version = root["schemaVersion"];
            if (version != null)
            {
                object raw = (version as JValue)?.Value;
                if (version.Type != JTokenType.Integer)
                    list.Add("Invalid schemaVersion; reading as version " + SchemaVersion);
                else
                {
                    if (raw is long v) lib.LoadedSchemaVersion = v > int.MaxValue ? int.MaxValue : v < int.MinValue ? int.MinValue : (int)v;
                    else if (raw is BigInteger big) lib.LoadedSchemaVersion = big.Sign > 0 ? int.MaxValue : int.MinValue;
                    if (lib.LoadedSchemaVersion < 1 || lib.LoadedSchemaVersion > SchemaVersion)
                        list.Add("Unsupported schemaVersion " + version.ToString(Newtonsoft.Json.Formatting.None) + " (this build reads "
                            + SchemaVersion + "); presets read best-effort");
                }
            }
            for (int i = 0; i < array.Count; i++)
            {
                if (!PresetJson.TryFromToken(array[i], PresetSource.User, out Preset p, out error))
                {
                    list.Add("Preset #" + i + ": " + error);
                    continue;
                }
                if (lib.IndexOf(p.Id) >= 0)
                {
                    list.Add("Preset #" + i + ": duplicate id '" + p.Id + "'");
                    continue;
                }
                if (lib.CountForMode(p.ModeId) >= MaxPerMode)
                {
                    list.Add("Preset #" + i + ": more than " + MaxPerMode + " presets for mode '" + p.ModeId + "'");
                    continue;
                }
                lib._presets.Add(p);
            }
            return lib;
        }

        sealed class LibraryDto
        {
            public int SchemaVersion { get; set; }
            public List<PresetDto> Presets { get; set; }
        }

        int IndexOf(string id)
        {
            for (int i = 0; i < _presets.Count; i++)
                if (string.Equals(_presets[i].Id, id, StringComparison.Ordinal)) return i;
            return -1;
        }
    }
}
