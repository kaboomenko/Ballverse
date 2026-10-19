using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.UI.Localization;
using Ballverse.UI.Theming;
using Ballverse.UI.ViewModels;
using Newtonsoft.Json;

namespace Ballverse.App
{
    /// <summary>
    /// Everything the app remembers between launches besides projects and purchases (spec §2.2 "последние параметры
    /// каждого режима", §4.3 first run, §4.3 Settings). Plain DTO serialized with <see cref="CoreJson"/>.
    /// </summary>
    public sealed class AppSettingsData
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;

        // Settings screen.
        public bool SoundEnabled { get; set; } = true;
        public bool HapticsEnabled { get; set; } = true;
        public QualityPreference Quality { get; set; } = QualityPreference.Auto;
        public LanguagePreference Language { get; set; } = LanguagePreference.System;
        public ThemePreference Theme { get; set; } = ThemePreference.Dark;
        public bool SafeFlashes { get; set; }
        public ReduceMotionPreference ReduceMotion { get; set; } = ReduceMotionPreference.System;
        public bool ReduceTransparency { get; set; }
        public bool HighContrast { get; set; }
        public bool BallPatterns { get; set; }
        /// <summary>Keep the "Made with Ballverse" mark although NO_WATERMARK is owned (spec §3.10 opt-in, default off).</summary>
        public bool KeepWatermark { get; set; }
        public bool AutoSaveToGallery { get; set; } = true;
        public bool CopyCaptionOnShare { get; set; } = true;

        // First run (spec §4.3).
        public bool FirstRunDone { get; set; }
        /// <summary><see cref="HintStep"/> as int; Generate first.</summary>
        public int HintStep { get; set; } = (int)UI.ViewModels.HintStep.Generate;
        public bool QuestRan { get; set; }
        public bool QuestGenerated { get; set; }
        public bool QuestExported { get; set; }
        public bool QuestCollapsed { get; set; }
        public bool QuestCelebrated { get; set; }
        /// <summary>Modes whose photosensitivity warning was shown (spec §4.8: once).</summary>
        public List<string> PhotosensitivityWarned { get; set; } = new List<string>();

        public int ExportCount { get; set; }

        // Simulation.
        public string LastModeId { get; set; }
        /// <summary>Mode id → last parameters as Params JSON (Requirement 10).</summary>
        public Dictionary<string, string> LastParams { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Mode id → selected preset id.</summary>
        public Dictionary<string, string> LastPreset { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public string GenerationStyle { get; set; } = "any";
        public string GenerationDuration { get; set; } = "auto";

        // Studio.
        public string TemplateLanguage { get; set; }
    }

    /// <summary>
    /// Loads and saves <see cref="AppSettingsData"/> as <c>settings/app_settings.json</c> (Android Auto Backup includes the
    /// settings folder, spec §3.9). Writes are atomic (temp file + replace); a corrupt or unreadable file starts from the
    /// defaults (reported, never thrown). Main thread.
    /// </summary>
    public sealed class AppSettingsStore
    {
        public const string FileName = "app_settings.json";

        readonly string _path;
        readonly Action<string, Exception> _reportError;
        bool _dirty;

        public AppSettingsStore(string settingsDirectory, Action<string, Exception> reportError)
        {
            if (string.IsNullOrEmpty(settingsDirectory)) throw new ArgumentException("Settings directory is required", nameof(settingsDirectory));
            _path = Path.Combine(settingsDirectory, FileName);
            _reportError = reportError ?? ((c, e) => { });
            Data = Load();
        }

        public AppSettingsData Data { get; private set; }

        /// <summary>True when the file did not exist (first launch).</summary>
        public bool IsNew { get; private set; }

        public bool IsDirty => _dirty;

        /// <summary>Marks the data changed; it is written by <see cref="Save"/> (called on pause, on leaving screens and on a short timer).</summary>
        public void MarkDirty() => _dirty = true;

        public void Update(Action<AppSettingsData> change)
        {
            change(Data);
            _dirty = true;
        }

        /// <summary>Writes the file if it changed. Returns false on an I/O error (reported).</summary>
        public bool Save()
        {
            if (!_dirty) return true;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                string temp = _path + ".tmp";
                File.WriteAllText(temp, CoreJson.Serialize(Data));
                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
                _dirty = false;
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException)
            {
                _reportError("settings.save", e);
                return false;
            }
        }

        AppSettingsData Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    IsNew = true;
                    return new AppSettingsData();
                }
                AppSettingsData data = CoreJson.Deserialize<AppSettingsData>(File.ReadAllText(_path)) ?? new AppSettingsData();
                if (data.LastParams == null) data.LastParams = new Dictionary<string, string>(StringComparer.Ordinal);
                if (data.LastPreset == null) data.LastPreset = new Dictionary<string, string>(StringComparer.Ordinal);
                if (data.PhotosensitivityWarned == null) data.PhotosensitivityWarned = new List<string>();
                return data;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                _reportError("settings.load", e);
                return new AppSettingsData();
            }
        }
    }
}
