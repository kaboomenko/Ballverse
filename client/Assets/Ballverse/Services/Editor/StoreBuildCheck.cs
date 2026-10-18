using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ballverse.Services.Editor
{
    /// <summary>
    /// Fails a Store build whose AdMob ad units are missing, malformed or Google's test units (spec §8.9: Store contains the
    /// real ids and no test ones). Without this the Store player would still be safe — <see cref="ServicesPolicy.SelectAdUnits"/>
    /// switches ads off rather than serving test ads — but it would earn nothing.
    /// <para>
    /// The variant is passed to the player build only as a compile symbol, which the Editor assembly cannot see, so this
    /// step reads <c>-ballverseVariant Store</c> from the command line (CI). The bootstrap build script, which knows the variant
    /// for menu builds and its defaults too, can call <see cref="StoreAdUnitsProblem"/> next to its own AdMob app id check.
    /// </para>
    /// </summary>
    public sealed class StoreBuildCheck : IPreprocessBuildWithReport
    {
        public const string VariantOption = "-ballverseVariant";

        public int callbackOrder => 310;

        public void OnPreprocessBuild(BuildReport report)
        {
            var target = report.summary.platform;
            if (target != BuildTarget.Android && target != BuildTarget.iOS) return;
            if (!IsStoreVariant(Environment.GetCommandLineArgs())) return;
            var config = Resources.Load<ServicesConfig>(ServicesConfig.ResourcePath);
            string problem = config == null
                ? "Store build without Resources/" + ServicesConfig.ResourcePath + ".asset: set the real AdMob ad unit ids there (spec §8.9)."
                : target == BuildTarget.iOS
                    ? StoreAdUnitsProblem(config.iosRewardedAdUnit, config.iosInterstitialAdUnit)
                    : StoreAdUnitsProblem(config.androidRewardedAdUnit, config.androidInterstitialAdUnit);
            if (problem != null) throw new BuildFailedException("[Ballverse] " + problem);
        }

        /// <summary>True when the command line selects the Store variant (<c>-ballverseVariant Store</c>, case-insensitive).</summary>
        public static bool IsStoreVariant(string[] args)
        {
            if (args == null) return false;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], VariantOption, StringComparison.OrdinalIgnoreCase))
                    return string.Equals(args[i + 1]?.Trim(), "Store", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        /// <summary>Why these ad units cannot ship in a Store build, or null when they can.</summary>
        public static string StoreAdUnitsProblem(string rewarded, string interstitial)
        {
            ServicesPolicy.SelectAdUnits(ServicesBuildVariant.Store, ServicesConfig.UnitsOrNull(rewarded, interstitial), null, out string problem);
            return problem;
        }
    }
}
