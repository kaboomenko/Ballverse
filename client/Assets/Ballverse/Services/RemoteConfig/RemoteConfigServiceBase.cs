using System;
using System.Collections.Generic;
using Ballverse.Core.Config;

namespace Ballverse.Services
{
    /// <summary>Shared parsing and change notification of the Remote Config implementations.</summary>
    public abstract class RemoteConfigServiceBase : IRemoteConfigService
    {
        /// <summary>Spec §7.9: the start-up fetch is waited for at most 3 s.</summary>
        public const int FetchTimeoutMs = 3000;

        const string Tag = "RemoteConfig";

        protected readonly IServiceLog Log;

        protected RemoteConfigServiceBase(IServiceLog log)
        {
            Log = log ?? NullServiceLog.Instance;
            Current = RemoteConfigValues.Defaults;
            Issues = Array.Empty<RemoteConfigIssue>();
        }

        public RemoteConfigValues Current { get; private set; }

        public IReadOnlyList<RemoteConfigIssue> Issues { get; private set; }

        public RemoteConfigSource Source { get; private set; }

        public bool FetchSettled { get; private set; }

        public event Action<RemoteConfigValues> Updated;

        public abstract void Initialize();

        /// <summary>Parses <paramref name="raw"/> (never throws) and publishes the result.</summary>
        protected void Apply(IReadOnlyDictionary<string, string> raw, RemoteConfigSource source)
        {
            var result = RemoteConfigParser.Parse(raw);
            Current = result.Values;
            Issues = result.Issues;
            Source = source;
            foreach (var issue in result.Issues)
            {
                if (issue.Severity == RemoteConfigIssueSeverity.Error) Log.Warning(Tag, issue.ToString());
            }
            Log.Info(Tag, "Applied " + source + " config (" + result.Issues.Count + " issues).");
            var handler = Updated;
            if (handler != null) handler(Current);
        }

        protected void MarkFetchSettled()
        {
            FetchSettled = true;
        }
    }
}
