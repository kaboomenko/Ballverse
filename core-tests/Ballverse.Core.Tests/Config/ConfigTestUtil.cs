using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Config;
using Xunit;

namespace Ballverse.Core.Tests.Config
{
    internal static class ConfigTestUtil
    {
        public static RemoteConfigParseResult Parse(string key, string value)
        {
            return RemoteConfigParser.Parse(new Dictionary<string, string> { { key, value } });
        }

        public static RemoteConfigParseResult Parse(params (string key, string value)[] pairs)
        {
            var d = new Dictionary<string, string>();
            foreach (var (k, v) in pairs) d[k] = v;
            return RemoteConfigParser.Parse(d);
        }

        public static void AssertNoIssues(RemoteConfigParseResult r)
        {
            Assert.True(r.Issues.Count == 0, "unexpected issues: " + string.Join("; ", r.Issues.Select(i => i.ToString())));
        }

        public static RemoteConfigIssue Single(RemoteConfigParseResult r, RemoteConfigIssueKind kind, RemoteConfigIssueSeverity severity)
        {
            var match = r.Issues.Where(i => i.Kind == kind).ToList();
            Assert.True(match.Count == 1, "expected one " + kind + ", got: " + string.Join("; ", r.Issues.Select(i => i.ToString())));
            Assert.Equal(severity, match[0].Severity);
            return match[0];
        }

        public static void AssertHas(RemoteConfigParseResult r, RemoteConfigIssueKind kind, RemoteConfigIssueSeverity severity)
        {
            Assert.True(r.Issues.Any(i => i.Kind == kind && i.Severity == severity),
                "expected " + severity + " " + kind + ", got: " + string.Join("; ", r.Issues.Select(i => i.ToString())));
        }

        public static string Raw(RemoteConfigValues v, string key)
        {
            return RemoteConfigWriter.ToRaw(v).Single(kv => kv.Key == key).Value;
        }

        public static Dictionary<string, string> RawDictionary(RemoteConfigValues v)
        {
            return RemoteConfigWriter.ToRaw(v).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public static ModeCode[] Codes(params int[] n)
        {
            return n.Select(i => (ModeCode)i).ToArray();
        }

        public static ModeCode[] AllCodes()
        {
            return ModeCatalog.All.Select(m => m.Code).ToArray();
        }

        /// <summary>The modes of this build's release in catalog order (release 1: M01, M02, M05).</summary>
        public static ModeCode[] ReleasedCodes()
        {
            return ModeCatalog.Released.Select(m => m.Code).ToArray();
        }

        /// <summary>The default free modes (release 1: M01, M02, M05).</summary>
        public static ModeCode[] DefaultFree()
        {
            return ModeCatalog.All.Where(m => m.FreeByDefault).Select(m => m.Code).ToArray();
        }
    }
}
