using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core.Params.Presets;

namespace Ballverse.App
{
    /// <summary>
    /// The user's presets (spec §2.3: up to 20 per mode) in <c>presets/user_presets.json</c> through Core
    /// <see cref="UserPresetLibrary"/>. A file written by a newer build is read but never overwritten (its unknown fields would
    /// be lost); saving then fails softly. Main thread.
    /// </summary>
    public sealed class UserPresetStore
    {
        public const string FileName = "user_presets.json";

        readonly string _path;
        readonly Action<string, Exception> _report;
        bool _dirty;

        public UserPresetStore(string presetsDirectory, Action<string, Exception> report)
        {
            _path = Path.Combine(presetsDirectory, FileName);
            _report = report ?? ((c, e) => { });
            Library = Load();
        }

        public UserPresetLibrary Library { get; }

        public void MarkDirty() => _dirty = true;

        public bool Save()
        {
            if (!_dirty) return true;
            if (Library.IsFromNewerVersion)
            {
                _dirty = false;
                return false;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                string temp = _path + ".tmp";
                File.WriteAllText(temp, Library.ToJson());
                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
                _dirty = false;
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                _report("presets.save", e);
                return false;
            }
        }

        UserPresetLibrary Load()
        {
            try
            {
                if (!File.Exists(_path)) return new UserPresetLibrary();
                UserPresetLibrary lib = UserPresetLibrary.FromJson(File.ReadAllText(_path), out IReadOnlyList<string> problems);
                if (problems != null && problems.Count > 0) _report("presets.load", new InvalidDataException(string.Join("; ", problems)));
                return lib ?? new UserPresetLibrary();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is PresetException || e is FormatException)
            {
                _report("presets.load", e);
                return new UserPresetLibrary();
            }
        }
    }
}
