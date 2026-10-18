using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ballverse.App;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Ballverse.Editor
{
    /// <summary>
    /// Brings the Unity project to the state release 1 needs, idempotently: URP 2D pipeline asset assigned in
    /// Graphics and Quality settings, the boot scene with its Bootstrap object, build scene list, player settings
    /// (identity, portrait only, linear colour, GPU minimum, Android/iOS targets) and the AdMob app ids the ads plugin
    /// requires to build (sample ids for development builds; see <see cref="AdMobDefaults"/>). Every build runs it first, so a fresh checkout (no generated assets committed yet) builds in CI.
    /// Run it in the Editor with Ballverse > Project Setup and commit what it creates.
    /// </summary>
    public static class ProjectSetup
    {
        public const string CompanyName = "Ballverse";
        public const string ProductName = "Ballverse";
        /// <summary>Placeholder application id (owner may change it before store setup).</summary>
        public const string ApplicationId = "com.ballverse.app";

        public const string ScenesFolder = "Assets/Scenes";
        public const string BootScenePath = ScenesFolder + "/Boot.unity";
        public const string BootObjectName = "Bootstrap";

        public const string RenderingFolder = "Assets/Ballverse/App/Settings";
        public const string PipelineAssetPath = RenderingFolder + "/Ballverse-URP-2D.asset";
        public const string RendererAssetPath = RenderingFolder + "/Ballverse-URP-2D-Renderer.asset";

        /// <summary>Android 8.0 (spec §8.1).</summary>
        public const int AndroidMinSdk = 26;
        /// <summary>android:appCategory; "game" keeps the portrait lock on Android 16 tablets and foldables (spec §8.1).</summary>
        public const string AndroidCategory = "game";
        /// <summary>iOS 16 (spec §8.1).</summary>
        public const string IosMinVersion = "16.0";

        const string DefaultPostProcessDataPath = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

        [MenuItem("Ballverse/Project Setup", priority = 0)]
        public static void RunFromMenu()
        {
            Run();
            Debug.Log("[Ballverse] Project setup complete. Commit the new assets, .meta files and ProjectSettings changes.");
        }

        /// <summary>Entry point for "-executeMethod Ballverse.Editor.ProjectSetup.RunFromCommandLine" (exits the Editor in batch mode).</summary>
        public static void RunFromCommandLine()
        {
            int exitCode = 0;
            try
            {
                Run();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                exitCode = 1;
            }
            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }

        /// <summary>Applies every setup step. Safe to call repeatedly; throws when a step cannot be completed.</summary>
        public static void Run()
        {
            UniversalRenderPipelineAsset pipeline = EnsureRenderPipeline();
            AssignRenderPipeline(pipeline);
            EnsureBootScene();
            EnsureBuildScenes();
            ApplyPlayerSettings();
            AdMobDefaults.EnsureAppIds();
            AssetDatabase.SaveAssets();
        }

        // ---------- Rendering ----------

        /// <summary>Loads the URP 2D pipeline asset, creating it (and its 2D renderer) when missing. Existing assets are not modified.</summary>
        public static UniversalRenderPipelineAsset EnsureRenderPipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (pipeline != null) return pipeline;

            EnsureAssetFolder(RenderingFolder);

            var renderer = AssetDatabase.LoadAssetAtPath<Renderer2DData>(RendererAssetPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<Renderer2DData>();
                AssetDatabase.CreateAsset(renderer, RendererAssetPath);
                AssignDefaultPostProcessData(renderer);
                ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
                EditorUtility.SetDirty(renderer);
            }

            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            // HDR colour buffer for glow (spec §7.7); MSAA off: SDF balls are anti-aliased in the shader.
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 1;
            pipeline.renderScale = 1f;
            AssetDatabase.CreateAsset(pipeline, PipelineAssetPath);
            AssetDatabase.SaveAssets();

            pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (pipeline == null) throw new InvalidOperationException("Failed to create the URP pipeline asset at " + PipelineAssetPath);
            return pipeline;
        }

        // Renderer2DData.postProcessData is internal; the menu command that creates a 2D renderer assigns URP's
        // default PostProcessData, so do the same through the serialized field (needed for bloom and colour grading).
        static void AssignDefaultPostProcessData(Renderer2DData renderer)
        {
            var postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(DefaultPostProcessDataPath);
            if (postProcessData == null)
            {
                Debug.LogWarning("[Ballverse] URP default PostProcessData not found at " + DefaultPostProcessDataPath + "; post-processing stays off until it is assigned on " + RendererAssetPath);
                return;
            }
            var so = new SerializedObject(renderer);
            SerializedProperty prop = so.FindProperty("m_PostProcessData");
            if (prop == null)
            {
                Debug.LogWarning("[Ballverse] Renderer2DData has no m_PostProcessData field in this URP version; assign post-process data on " + RendererAssetPath + " manually.");
                return;
            }
            prop.objectReferenceValue = postProcessData;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Makes <paramref name="pipeline"/> the default pipeline and the pipeline of every quality level.</summary>
        public static void AssignRenderPipeline(UniversalRenderPipelineAsset pipeline)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (GraphicsSettings.defaultRenderPipeline != pipeline) GraphicsSettings.defaultRenderPipeline = pipeline;

            int current = QualitySettings.GetQualityLevel();
            int count = QualitySettings.names.Length;
            for (int i = 0; i < count; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                if (QualitySettings.renderPipeline != pipeline) QualitySettings.renderPipeline = pipeline;
            }
            if (count > 0) QualitySettings.SetQualityLevel(current, false);
        }

        // ---------- Scenes ----------

        /// <summary>Creates Assets/Scenes/Boot.unity with one Bootstrap object, or adds the object to an existing boot scene that lacks it.</summary>
        public static void EnsureBootScene()
        {
            EnsureAssetFolder(ScenesFolder);
            if (File.Exists(BootScenePath)) RepairBootScene();
            else CreateBootScene();
        }

        static void CreateBootScene()
        {
            // A new scene cannot be added next to an untitled one, so it is created as the single open scene and the
            // user's scene setup is restored afterwards (unsaved changes are offered for saving first).
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("Project setup cancelled: the open scenes have unsaved changes.");
            SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AddBootstrap(scene);
            bool saved = EditorSceneManager.SaveScene(scene, BootScenePath);

            if (previous.Length > 0 && previous.All(s => !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(previous);
            if (!saved) throw new InvalidOperationException("Failed to save the boot scene to " + BootScenePath);
        }

        static void RepairBootScene()
        {
            Scene loaded = SceneManager.GetSceneByPath(BootScenePath);
            bool wasLoaded = loaded.IsValid() && loaded.isLoaded;
            Scene scene = wasLoaded ? loaded : EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Additive);
            try
            {
                if (SceneHasBootstrap(scene)) return;
                AddBootstrap(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("Failed to save the boot scene to " + BootScenePath);
            }
            finally
            {
                if (!wasLoaded && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void AddBootstrap(Scene scene)
        {
            var go = new GameObject(BootObjectName);
            go.AddComponent<Bootstrap>();
            if (go.scene != scene) SceneManager.MoveGameObjectToScene(go, scene);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        static bool SceneHasBootstrap(Scene scene) =>
            scene.GetRootGameObjects().Any(root => root.GetComponentInChildren<Bootstrap>(true) != null);

        /// <summary>Puts the boot scene first in the build scene list (enabled), keeping any other scenes after it.</summary>
        public static void EnsureBuildScenes()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Length > 0 && scenes[0].path == BootScenePath && scenes[0].enabled) return;

            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(BootScenePath, true) };
            list.AddRange(scenes.Where(s => s.path != BootScenePath));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ---------- Player settings ----------

        /// <summary>
        /// Identity, orientation, colour space and per-platform targets. Build-specific values (version, signing,
        /// development flag, APK vs AAB) are applied by <see cref="BuildScript"/>.
        /// </summary>
        public static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, ApplicationId);

            PlayerSettings.colorSpace = ColorSpace.Linear;

            // Portrait only (spec §8.1).
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            // A portrait-only iPad app must opt out of multitasking (App Store requirement).
            PlayerSettings.iOS.requiresFullScreen = true;

            // Unity 6 lets every plan disable the splash screen; it only delays testing.
            PlayerSettings.SplashScreen.show = false;

            // Android: IL2CPP, ARM64 only, Android 8.0+. Unity 6.3 links IL2CPP libraries with 16 KB page alignment by default.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)AndroidMinSdk;
            // Minimum GPU (spec §8.1): OpenGL ES 3.1 (or Vulkan 1.1); keeps ES 3.0-only devices out of the Play listing.
            PlayerSettings.openGLRequireES31 = true;
            // Android 16 (target API 36) ignores the portrait lock on large screens unless the app is a game.
            PlayerSettings.Android.appCategory = AndroidCategory;

            // iOS: IL2CPP (the only backend), iOS 16+, device SDK.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.targetOSVersionString = IosMinVersion;
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
        }

        // ---------- Helpers ----------

        /// <summary>Creates an "Assets/..." folder chain through the AssetDatabase so assets can be created in it immediately.</summary>
        public static void EnsureAssetFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets") throw new ArgumentException("Folder must start with Assets/: " + folder, nameof(folder));
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
