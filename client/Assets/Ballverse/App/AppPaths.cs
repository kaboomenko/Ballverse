using System;
using System.IO;
using Ballverse.Platform;

namespace Ballverse.App
{
    /// <summary>
    /// <see cref="IAppPaths"/> on the device file system. The folder names are part of the storage contract: the
    /// Android backup rules (Ballverse.Editor.AndroidBackupRules) include exactly <see cref="SettingsDirName"/> and
    /// <see cref="PresetsDirName"/>.
    /// </summary>
    public sealed class AppPaths : IAppPaths
    {
        /// <summary>Same value as the project store root of the Core project layout (spec §7.4).</summary>
        public const string ProjectsDirName = "projects";
        public const string SettingsDirName = "settings";
        public const string PresetsDirName = "presets";
        public const string LogsDirName = "logs";
        public const string RenderStreamCacheDirName = "render-streams";
        public const string ThumbnailsDirName = "thumbs";
        public const string ExportScratchDirName = "export";

        /// <param name="persistentRoot">Application.persistentDataPath on device.</param>
        /// <param name="cacheRoot">Application.temporaryCachePath on device.</param>
        public AppPaths(string persistentRoot, string cacheRoot)
        {
            if (string.IsNullOrEmpty(persistentRoot)) throw new ArgumentException("Persistent root is required", nameof(persistentRoot));
            if (string.IsNullOrEmpty(cacheRoot)) throw new ArgumentException("Cache root is required", nameof(cacheRoot));
            PersistentRoot = Path.GetFullPath(persistentRoot);
            CacheRoot = Path.GetFullPath(cacheRoot);
        }

        public static AppPaths ForDevice() =>
            new AppPaths(UnityEngine.Application.persistentDataPath, UnityEngine.Application.temporaryCachePath);

        public string PersistentRoot { get; }
        public string CacheRoot { get; }

        public string Projects => Path.Combine(PersistentRoot, ProjectsDirName);
        public string Settings => Path.Combine(PersistentRoot, SettingsDirName);
        public string Presets => Path.Combine(PersistentRoot, PresetsDirName);
        public string Logs => Path.Combine(PersistentRoot, LogsDirName);
        public string RenderStreamCache => Path.Combine(CacheRoot, RenderStreamCacheDirName);
        public string Thumbnails => Path.Combine(CacheRoot, ThumbnailsDirName);
        public string ExportScratch => Path.Combine(CacheRoot, ExportScratchDirName);

        public string EnsurePersistentDir(string name) => Ensure(PersistentRoot, name);

        public string EnsureCacheDir(string name) => Ensure(CacheRoot, name);

        /// <summary>Creates the well-known folders. Called once at boot; cheap when they already exist.</summary>
        public void EnsureStandardDirs()
        {
            Ensure(PersistentRoot, ProjectsDirName);
            Ensure(PersistentRoot, SettingsDirName);
            Ensure(PersistentRoot, PresetsDirName);
            Ensure(PersistentRoot, LogsDirName);
            Ensure(CacheRoot, RenderStreamCacheDirName);
            Ensure(CacheRoot, ThumbnailsDirName);
            Ensure(CacheRoot, ExportScratchDirName);
        }

        /// <summary>A folder name is one segment of [a-z0-9_-], 1..64 chars (no separators, dots or traversal).</summary>
        public static bool IsValidDirName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64) return false;
            foreach (char c in name)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        static string Ensure(string root, string name)
        {
            if (!IsValidDirName(name)) throw new ArgumentException("Invalid folder name: " + name, nameof(name));
            string path = Path.Combine(root, name);
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
