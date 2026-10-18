using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ballverse.Editor
{
    /// <summary>
    /// Command-line options of a batch-mode build ("-name value" pairs). Understands the arguments GameCI's
    /// unity-builder passes to a custom build method (-customBuildPath, -buildVersion, -androidVersionCode,
    /// -androidKeystoreName/-Pass, -androidKeyaliasName/-Pass, -androidTargetSdkVersion) plus Ballverse options
    /// passed through customParameters (-iosTeamId, -iosProvisioningProfileId, -iosProvisioningProfileType,
    /// -iosBuildNumber, -ballverseVariant). Empty values (GameCI passes "" for unset
    /// inputs) count as absent.
    /// <para>The options in <see cref="IsValueOption"/> always take the next argument as their value, even when it starts
    /// with '-' (a keystore password such as "-Secr3t"). Other options take the next argument unless it looks like an
    /// option itself, so bare flags (-batchmode, -quit) work.</para>
    /// </summary>
    public sealed class BuildArgs
    {
        static readonly HashSet<string> s_valueOptions = new HashSet<string>(StringComparer.Ordinal)
        {
            // GameCI unity-builder (passed as separate argv entries, values may be "" or start with '-').
            "customBuildName", "customBuildPath", "customBuildTarget", "customBuildProfile", "buildVersion",
            "androidVersionCode", "androidKeystoreName", "androidKeystorePass", "androidKeyaliasName",
            "androidKeyaliasPass", "androidTargetSdkVersion", "androidExportType", "androidSymbolType",
            // Ballverse customParameters.
            "iosTeamId", "iosProvisioningProfileId", "iosProvisioningProfileType", "iosBuildNumber",
            "ballverseVariant",
        };

        /// <summary>True for options that always consume the following argument as their value.</summary>
        public static bool IsValueOption(string key) => key != null && s_valueOptions.Contains(key);

        readonly Dictionary<string, string> _values;

        BuildArgs(Dictionary<string, string> values)
        {
            _values = values;
        }

        public static BuildArgs FromCommandLine() => Parse(Environment.GetCommandLineArgs());

        public static BuildArgs Parse(IReadOnlyList<string> args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (args != null)
            {
                for (int i = 0; i < args.Count; i++)
                {
                    string token = args[i];
                    if (string.IsNullOrEmpty(token) || token.Length < 2 || token[0] != '-') continue;
                    string key = token.Substring(1);
                    string value = string.Empty;
                    if (i + 1 < args.Count && (IsValueOption(key) || !IsOption(args[i + 1])))
                    {
                        value = args[i + 1] ?? string.Empty;
                        i++;
                    }
                    // Later occurrences win, like most CLIs.
                    values[key] = value.Trim();
                }
            }
            return new BuildArgs(values);
        }

        static bool IsOption(string token) =>
            token != null && token.Length >= 2 && token[0] == '-' && !char.IsDigit(token[1]);

        /// <summary>Non-empty value of an option, or null.</summary>
        public string Get(string key) =>
            _values.TryGetValue(key, out string v) && v.Length > 0 ? v : null;

        public bool Has(string key) => _values.ContainsKey(key);

        /// <summary>Positive integer value of an option, or null when absent or not a positive integer.</summary>
        public int? GetPositiveInt(string key)
        {
            string v = Get(key);
            if (v != null && int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0) return n;
            return null;
        }

        /// <summary>Boolean option: "true/false/1/0/yes/no"; a bare flag (present without value) is true.</summary>
        public bool? GetBool(string key)
        {
            if (!_values.TryGetValue(key, out string v)) return null;
            if (v.Length == 0) return true;
            switch (v.ToLowerInvariant())
            {
                case "true": case "1": case "yes": return true;
                case "false": case "0": case "no": return false;
                default: return null;
            }
        }
    }
}
