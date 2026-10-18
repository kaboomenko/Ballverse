using System.Collections.Generic;

namespace Ballverse.Services
{
    /// <summary>
    /// Remote Config without a backend (editor, tests, builds without Firebase). Starts on the build defaults; tests and the
    /// developer menu push raw values with <see cref="SetRaw"/>, exactly as Firebase would deliver them.
    /// </summary>
    public sealed class FakeRemoteConfigService : RemoteConfigServiceBase
    {
        readonly IReadOnlyDictionary<string, string> _initialRaw;

        /// <param name="initialRaw">Raw values applied by <see cref="Initialize"/> as a fetch; null keeps the defaults.</param>
        public FakeRemoteConfigService(IServiceLog log = null, IReadOnlyDictionary<string, string> initialRaw = null) : base(log)
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

        /// <summary>Simulates a fetch that delivered <paramref name="raw"/>.</summary>
        public void SetRaw(IReadOnlyDictionary<string, string> raw)
        {
            Apply(raw, RemoteConfigSource.Fetched);
        }
    }
}
