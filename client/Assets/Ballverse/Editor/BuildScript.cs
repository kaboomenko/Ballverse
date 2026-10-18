using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ballverse.Editor
{
    public enum BuildKind
    {
        /// <summary>Development APK for sideloading on a test phone (debug keystore unless a keystore is passed).</summary>
        AndroidApk = 0,
        /// <summary>Release Android App Bundle for Google Play (needs an upload keystore to be accepted by Play).</summary>
        AndroidAab = 1,
        /// <summary>Xcode project; signing and the actual iPhone build happen in Xcode or the macOS CI job.</summary>
        IosXcode = 2,
    }

    /// <summary>
    /// Player builds for CI (GameCI unity-builder "buildMethod") and the Ballverse > Build menu.
    /// Each build runs <see cref="ProjectSetup.Run"/> first, so a fresh checkout builds without generated assets.
    /// <para>CI: <c>-executeMethod Ballverse.Editor.BuildScript.BuildAndroidApk</c> (or BuildAndroidAab, BuildIosXcode).
    /// Options are listed on <see cref="BuildArgs"/>. In batch mode the Editor exits with 0 on success and
    /// 101 (failed), 102 (cancelled) or 103 (unknown) otherwise.</para>
    /// </summary>
    public static class BuildScript
    {
        public const string DefaultAndroidApkPath = "Builds/Android/Ballverse.apk";
        public const string DefaultAndroidAabPath = "Builds/Android/Ballverse.aab";
        public const string DefaultIosPath = "Builds/iOS/Ballverse";

        public static void BuildAndroidApk() => RunAndExit(BuildKind.AndroidApk);

        public static void BuildAndroidAab() => RunAndExit(BuildKind.AndroidAab);

        public static void BuildIosXcode() => RunAndExit(BuildKind.IosXcode);

        [MenuItem("Ballverse/Build/Android APK (development)", priority = 20)]
        static void MenuAndroidApk() => RunAndExit(BuildKind.AndroidApk);

        [MenuItem("Ballverse/Build/Android App Bundle (release)", priority = 21)]
        static void MenuAndroidAab() => RunAndExit(BuildKind.AndroidAab);

        [MenuItem("Ballverse/Build/iOS Xcode project", priority = 22)]
        static void MenuIosXcode() => RunAndExit(BuildKind.IosXcode);

        static void RunAndExit(BuildKind kind)
        {
            BuildResult result;
            try
            {
                BuildReport report = Build(kind, BuildArgs.FromCommandLine());
                result = report.summary.result;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                result = BuildResult.Failed;
            }

            if (Application.isBatchMode) EditorApplication.Exit(ExitCode(result));
            else if (result == BuildResult.Succeeded) Debug.Log("[Ballverse] " + kind + " build succeeded.");
            else Debug.LogError("[Ballverse] " + kind + " build finished with result " + result + ". See the Console for details.");
        }

        public static int ExitCode(BuildResult result)
        {
            switch (result)
            {
                case BuildResult.Succeeded: return 0;
                case BuildResult.Failed: return 101;
                case BuildResult.Cancelled: return 102;
                default: return 103;
            }
        }

        /// <summary>Applies setup and build settings, then builds. Throws when the build cannot start.</summary>
        public static BuildReport Build(BuildKind kind, BuildArgs args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            BuildTarget target = kind == BuildKind.IosXcode ? BuildTarget.iOS : BuildTarget.Android;
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);

            if (!BuildPipeline.IsBuildTargetSupported(group, target))
                throw new InvalidOperationException(target + " build support is not installed in this Editor.");
            if (EditorUserBuildSettings.activeBuildTarget != target &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                throw new InvalidOperationException("Could not switch the active build target to " + target + ".");

            ProjectSetup.Run();
            ApplyVersion(args);
            if (target == BuildTarget.Android) ApplyAndroid(kind, args);
            else ApplyIos(args);
            AssetDatabase.SaveAssets();

            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0 || scenes[0] != ProjectSetup.BootScenePath)
                throw new InvalidOperationException("The boot scene must be the first enabled build scene: " + ProjectSetup.BootScenePath);

            string output = args.Get("customBuildPath") ?? DefaultOutput(kind);
            string parent = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            bool development = args.GetBool("ballverseDevelopment") ?? kind == BuildKind.AndroidApk;
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = target,
                targetGroup = group,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };

            Debug.Log($"[Ballverse] Building {kind}: version {PlayerSettings.bundleVersion}, development {development}, output {output}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            LogSummary(kind, report);
            return report;
        }

        static string DefaultOutput(BuildKind kind)
        {
            switch (kind)
            {
                case BuildKind.AndroidApk: return DefaultAndroidApkPath;
                case BuildKind.AndroidAab: return DefaultAndroidAabPath;
                default: return DefaultIosPath;
            }
        }

        static void ApplyVersion(BuildArgs args)
        {
            string version = args.Get("buildVersion");
            if (version != null && !string.Equals(version, "none", StringComparison.OrdinalIgnoreCase))
                PlayerSettings.bundleVersion = version;

            int? versionCode = args.GetPositiveInt("androidVersionCode");
            if (versionCode.HasValue) PlayerSettings.Android.bundleVersionCode = versionCode.Value;

            int? iosBuild = args.GetPositiveInt("iosBuildNumber") ?? versionCode;
            if (iosBuild.HasValue) PlayerSettings.iOS.buildNumber = iosBuild.Value.ToString(CultureInfo.InvariantCulture);
        }

        static void ApplyAndroid(BuildKind kind, BuildArgs args)
        {
            EditorUserBuildSettings.buildAppBundle = kind == BuildKind.AndroidAab;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;

            // Target API: an explicit -androidTargetSdkVersion ("36" or "AndroidApiLevel36"), otherwise the highest
            // SDK platform installed with the Editor (Unity 6.3 ships the level Google Play currently requires).
            PlayerSettings.Android.targetSdkVersion = ParseTargetSdk(args.Get("androidTargetSdkVersion"));

            string keystore = args.Get("androidKeystoreName");
            if (keystore != null)
            {
                if (!File.Exists(keystore))
                    throw new FileNotFoundException("Android keystore not found (path is relative to the Unity project folder).", keystore);
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystore;
                PlayerSettings.Android.keystorePass = args.Get("androidKeystorePass") ?? string.Empty;
                PlayerSettings.Android.keyaliasName = args.Get("androidKeyaliasName") ?? string.Empty;
                PlayerSettings.Android.keyaliasPass = args.Get("androidKeyaliasPass") ?? string.Empty;
            }
            else
            {
                PlayerSettings.Android.useCustomKeystore = false;
                if (kind == BuildKind.AndroidAab)
                    Debug.LogWarning("[Ballverse] Building the App Bundle with the debug keystore; Google Play only accepts bundles signed with your upload key.");
            }
        }

        public static AndroidSdkVersions ParseTargetSdk(string value)
        {
            if (string.IsNullOrEmpty(value)) return AndroidSdkVersions.AndroidApiLevelAuto;
            const string prefix = "AndroidApiLevel";
            string number = value.StartsWith(prefix, StringComparison.Ordinal) ? value.Substring(prefix.Length) : value;
            if (int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int level) && level >= ProjectSetup.AndroidMinSdk)
                return (AndroidSdkVersions)level;
            Debug.LogWarning("[Ballverse] Ignoring androidTargetSdkVersion '" + value + "'; using the highest installed SDK.");
            return AndroidSdkVersions.AndroidApiLevelAuto;
        }

        static void ApplyIos(BuildArgs args)
        {
            string teamId = args.Get("iosTeamId");
            string profileId = args.Get("iosProvisioningProfileId");
            if (teamId != null) PlayerSettings.iOS.appleDeveloperTeamID = teamId;

            if (teamId != null && profileId != null)
            {
                // Manual signing for CI archives: Unity writes these settings into the Unity-iPhone target only.
                PlayerSettings.iOS.appleEnableAutomaticSigning = false;
                PlayerSettings.iOS.iOSManualProvisioningProfileID = profileId;
                PlayerSettings.iOS.iOSManualProvisioningProfileType =
                    string.Equals(args.Get("iosProvisioningProfileType"), "Distribution", StringComparison.OrdinalIgnoreCase)
                        ? ProvisioningProfileType.Distribution
                        : ProvisioningProfileType.Development;
            }
            else
            {
                // Automatic signing: Xcode picks the team's certificate and profile. Without a team id the developer
                // selects a team (a free Apple ID works) in Xcode > Signing & Capabilities.
                PlayerSettings.iOS.appleEnableAutomaticSigning = true;
                PlayerSettings.iOS.iOSManualProvisioningProfileID = string.Empty;
            }
        }

        static void LogSummary(BuildKind kind, BuildReport report)
        {
            BuildSummary s = report.summary;
            Debug.Log($"[Ballverse] {kind} build result: {s.result}; errors {s.totalErrors}, warnings {s.totalWarnings}, " +
                      $"size {s.totalSize / (1024 * 1024)} MB, time {s.totalTime:hh\\:mm\\:ss}, output {s.outputPath}");
        }
    }
}
