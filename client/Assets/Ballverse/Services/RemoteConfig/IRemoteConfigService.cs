using System;
using System.Collections.Generic;
using Ballverse.Core.Config;

namespace Ballverse.Services
{
    public enum RemoteConfigSource
    {
        /// <summary>Build defaults (<see cref="RemoteConfigValues.Defaults"/>): no config activated yet.</summary>
        Defaults = 0,
        /// <summary>Values fetched in an earlier session and activated at start.</summary>
        Cached = 1,
        /// <summary>Values fetched in this session.</summary>
        Fetched = 2,
    }

    /// <summary>
    /// Remote Config (spec §9.3, §7.9): raw key → string values are typed by Core <see cref="RemoteConfigParser"/>; the app
    /// works fully on <see cref="RemoteConfigValues.Defaults"/> without network. Main thread only.
    /// </summary>
    public interface IRemoteConfigService
    {
        RemoteConfigValues Current { get; }

        IReadOnlyList<RemoteConfigIssue> Issues { get; }

        RemoteConfigSource Source { get; }

        /// <summary>
        /// True once the start-up fetch finished or gave up after <see cref="RemoteConfigServiceBase.FetchTimeoutMs"/>
        /// (spec §7.9: 3 s, never blocking). A fetch that completes later still applies.
        /// </summary>
        bool FetchSettled { get; }

        /// <summary>Raised on the main thread whenever <see cref="Current"/> changes.</summary>
        event Action<RemoteConfigValues> Updated;

        /// <summary>Activates cached values and starts a background fetch. Returns immediately.</summary>
        void Initialize();
    }
}
