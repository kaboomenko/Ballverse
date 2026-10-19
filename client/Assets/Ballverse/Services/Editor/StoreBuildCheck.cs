using System;
using Ballverse.Platform;
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
    /// The variant reaches the player as its scripting symbol (<see cref="BuildVariants"/>). The bootstrap build script passes
    /// it in <c>BuildPlayerOptions.extraScriptingDefines</c>, for CI, menu builds and its defaults alike (an App Bundle is a
    /// Store build unless <c>-ballverseVariant</c> says otherwise); <see cref="BuildDefinesRecorder"/> records those defines
    /// when the build starts (<see cref="BuildPlayerProcessor"/>) and <see cref="ResolveVariant"/> reads them.
    /// </para>
    /// <para>
    /// An Android App Bundle that compiles without any variant symbol and was not started by the build script (Unity's Build
    /// Profiles window) would be a Dev player — Google's test ads and the fake store — in an upload format; it is refused
    /// with <see cref="UnmarkedAppBundleProblem"/> instead of being checked as a Store build it is not.
    /// </para>
    /// </summary>
    public sealed class StoreBuildCheck : IPreprocessBuildWithReport
    {
        public const string VariantOption = "-ballverseVariant";

        /// <summary>Prefix of the <c>-executeMethod</c> value of the bootstrap build script's CI entry points.</summary>
        public const string BuildScriptMethodPrefix = "Ballverse.Editor.BuildScript.";

        public int callbackOrder => 310;

        public void OnPreprocessBuild(BuildReport report)
        {
            string[] extraDefines = BuildDefinesRecorder.Take();
            var target = report.summary.platform;
            if (target != BuildTarget.Android && target != BuildTarget.iOS) return;
            NamedBuildTarget named = target == BuildTarget.iOS ? NamedBuildTarget.iOS : NamedBuildTarget.Android;
            string playerDefines = PlayerSettings.GetScriptingDefineSymbols(named);
            string[] commandLine = Environment.GetCommandLineArgs();
            bool appBundle = target == BuildTarget.Android && EditorUserBuildSettings.buildAppBundle;
            string unmarked = UnmarkedAppBundleProblem(extraDefines, playerDefines, commandLine, appBundle);
            if (unmarked != null) throw new BuildFailedException("[Ballverse] " + unmarked);
            BuildVariant? variant = ResolveVariant(extraDefines, playerDefines, commandLine, appBundle);
            if (variant != BuildVariant.Store) return;
            var config = Resources.Load<ServicesConfig>(ServicesConfig.ResourcePath);
            string problem = config == null
                ? "Store build without Resources/" + ServicesConfig.ResourcePath + ".asset: set the real AdMob ad unit ids there (spec §8.9)."
                : target == BuildTarget.iOS
                    ? StoreAdUnitsProblem(config.iosRewardedAdUnit, config.iosInterstitialAdUnit)
                    : StoreAdUnitsProblem(config.androidRewardedAdUnit, config.androidInterstitialAdUnit);
            if (problem != null)
                throw new BuildFailedException("[Ballverse] " + problem +
                                               " Build the OwnerTest variant (-ballverseVariant OwnerTest) to use Google's test ads.");
        }

        /// <summary>
        /// The variant a player build compiles as, or null when nothing says (a Dev player). In order:
        /// <list type="number">
        /// <item>a variant symbol among the symbols the player compiles with: the build's extra scripting defines (what the
        /// bootstrap build script passes) and the Player Settings defines (<paramref name="playerDefines"/>, ';'-separated);
        /// Store wins when several are present, as it does in <see cref="BuildVariants.Current"/>;</item>
        /// <item><c>-ballverseVariant</c> on the command line (<see cref="BuildVariants.TryParse"/>);</item>
        /// <item>an Android App Bundle started by the bootstrap build script (<c>-executeMethod Ballverse.Editor.BuildScript.…</c>)
        /// is a Store build, the script's default for an App Bundle (reached only when its defines were not recorded).</item>
        /// </list>
        /// Anything else — an APK, an Xcode project or an App Bundle from Unity's own build windows without a variant symbol —
        /// compiles as Dev and gets null.
        /// </summary>
        public static BuildVariant? ResolveVariant(string[] extraDefines, string playerDefines, string[] commandLine, bool androidAppBundle)
        {
            BuildVariant? fromExtra = VariantOfSymbols(extraDefines);
            BuildVariant? fromPlayer = VariantOfSymbols(playerDefines?.Split(';'));
            if (fromExtra == BuildVariant.Store || fromPlayer == BuildVariant.Store) return BuildVariant.Store;
            if (fromExtra == BuildVariant.OwnerTest || fromPlayer == BuildVariant.OwnerTest) return BuildVariant.OwnerTest;
            if (fromExtra.HasValue || fromPlayer.HasValue) return BuildVariant.Dev;
            if (TryGetCommandLineVariant(commandLine, out BuildVariant fromArgs)) return fromArgs;
            return androidAppBundle && IsBuildScriptBuild(commandLine) ? BuildVariant.Store : (BuildVariant?)null;
        }

        /// <summary>
        /// Why this Android App Bundle must not be built, or null: it compiles without any variant symbol (a Dev player with
        /// Google's test ads and the fake store) and was not started by the build script, whose App Bundle default is Store.
        /// </summary>
        public static string UnmarkedAppBundleProblem(string[] extraDefines, string playerDefines, string[] commandLine, bool androidAppBundle)
        {
            if (!androidAppBundle || ResolveVariant(extraDefines, playerDefines, commandLine, true) != null) return null;
            return "Android App Bundle without a Ballverse variant symbol: it would compile as a Dev player (Google's test ads, " +
                   "fake store). Build it with the Ballverse/Build menu or Ballverse.Editor.BuildScript.BuildAndroidAab " +
                   "(-ballverseVariant Store|OwnerTest|Dev), or add " + BuildVariants.StoreSymbol + ", " +
                   BuildVariants.OwnerTestSymbol + " or " + BuildVariants.DevSymbol + " to the Android scripting define symbols.";
        }

        /// <summary>True when the command line runs one of the bootstrap build script's entry points.</summary>
        public static bool IsBuildScriptBuild(string[] args)
        {
            if (args == null) return false;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], "-executeMethod", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1] != null && args[i + 1].StartsWith(BuildScriptMethodPrefix, StringComparison.Ordinal);
            }
            return false;
        }

        /// <summary>True when the command line selects the Store variant (<c>-ballverseVariant Store</c>).</summary>
        public static bool IsStoreVariant(string[] args) =>
            TryGetCommandLineVariant(args, out BuildVariant variant) && variant == BuildVariant.Store;

        static bool TryGetCommandLineVariant(string[] args, out BuildVariant variant)
        {
            variant = BuildVariant.Dev;
            if (args == null) return false;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], VariantOption, StringComparison.OrdinalIgnoreCase))
                    return BuildVariants.TryParse(args[i + 1], out variant);
            }
            return false;
        }

        static BuildVariant? VariantOfSymbols(string[] symbols)
        {
            if (symbols == null) return null;
            BuildVariant? found = null;
            foreach (string raw in symbols)
            {
                string symbol = raw?.Trim();
                if (symbol == BuildVariants.StoreSymbol) return BuildVariant.Store;
                if (symbol == BuildVariants.OwnerTestSymbol) found = BuildVariant.OwnerTest;
                else if (symbol == BuildVariants.DevSymbol && found == null) found = BuildVariant.Dev;
            }
            return found;
        }

        /// <summary>Why these ad units cannot ship in a Store build, or null when they can.</summary>
        public static string StoreAdUnitsProblem(string rewarded, string interstitial)
        {
            ServicesPolicy.SelectAdUnits(BuildVariant.Store, ServicesConfig.UnitsOrNull(rewarded, interstitial), null, out string problem);
            return problem;
        }
    }

    /// <summary>
    /// Records the extra scripting defines of the player build that is starting (<see cref="BuildPlayerContext.BuildPlayerOptions"/>),
    /// which the later <see cref="IPreprocessBuildWithReport"/> step cannot see, for <see cref="StoreBuildCheck"/>.
    /// </summary>
    public sealed class BuildDefinesRecorder : BuildPlayerProcessor
    {
        static string[] s_extraDefines;

        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            s_extraDefines = buildPlayerContext?.BuildPlayerOptions.extraScriptingDefines;
        }

        /// <summary>The recorded defines of the current build (null when none were recorded); clears them.</summary>
        public static string[] Take()
        {
            string[] defines = s_extraDefines;
            s_extraDefines = null;
            return defines;
        }
    }
}
