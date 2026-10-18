using System.Collections.Generic;

namespace Ballverse.Services
{
    /// <summary>
    /// Remote Config without a backend: the release-1 production path while Firebase is not in the build (every variant,
    /// Store included), and the editor and tests. It serves the build defaults (<c>RemoteConfigValues.Defaults</c>), which is
    /// exactly what the app runs on offline (spec §9.3, §7.9); it is not a test fake and holds no test values.
    /// <para>
    /// Tests and the developer menu can push raw values with <see cref="SetRaw"/>, exactly as Firebase would deliver them;
    /// nothing in the app does so, so a Store build always keeps the defaults.
    /// </para>
    /// </summary>
    public sealed class BuildDefaultsRemoteConfigService : RemoteConfigServiceBase
    {
        readonly IReadOnlyDictionary<string, string> _initialRaw;

        /// <param name="initialRaw">
        /// Raw values applied by <see cref="Initialize"/> as a fetch (tests: <see cref="ServicesOptions.InitialRemoteConfig"/>);
        /// null keeps the build defaults.
        /// </param>
        public BuildDefaultsRemoteConfigService(IServiceLog log = null, IReadOnlyDictionary<string, string> initialRaw = null) : base(log)
        {
            _initialRaw = initialRaw;
        }

        public bool Initialized { get; private set; }

        public override void Initialize()
        {
            if (Initialized) return;
            Initialized = true;
            if (_initialRaw != null) Apply(_initialRaw, RemoteConfigSource.Fetched);
            MarkFetchSettled();
        }

        /// <summary>Applies <paramref name="raw"/> as if a fetch had delivered it (tests, developer menu).</summary>
        public void SetRaw(IReadOnlyDictionary<string, string> raw)
        {
            Apply(raw, RemoteConfigSource.Fetched);
        }
    }
}
