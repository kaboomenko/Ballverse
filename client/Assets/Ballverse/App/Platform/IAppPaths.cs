namespace Ballverse.Platform
{
    /// <summary>
    /// App storage roots (spec §3.9). Persistent data (projects, settings, presets, entitlement cache) lives under
    /// <see cref="PersistentRoot"/>, which iOS includes in device backups; Android Auto Backup is limited to
    /// <see cref="Settings"/> and <see cref="Presets"/> by the build's backup rules. Regenerable data (render-stream
    /// cache, thumbnails, export scratch files) lives under <see cref="CacheRoot"/>, which the OS may purge and never
    /// backs up.
    /// </summary>
    public interface IAppPaths
    {
        string PersistentRoot { get; }
        string CacheRoot { get; }

        /// <summary>Project store root; matches the "projects" folder of the project layout (spec §7.4).</summary>
        string Projects { get; }
        string Settings { get; }
        string Presets { get; }
        string RenderStreamCache { get; }
        string Thumbnails { get; }
        string ExportScratch { get; }
        string Logs { get; }

        /// <summary>Full path of a named folder under <see cref="PersistentRoot"/>, created if missing.</summary>
        string EnsurePersistentDir(string name);

        /// <summary>Full path of a named folder under <see cref="CacheRoot"/>, created if missing.</summary>
        string EnsureCacheDir(string name);
    }
}
