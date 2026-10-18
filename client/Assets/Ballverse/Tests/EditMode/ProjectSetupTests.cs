using System.IO;
using System.Linq;
using Ballverse.App;
using Ballverse.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Ballverse.Tests
{
    /// <summary>
    /// After <see cref="ProjectSetup.Run"/> the project has the URP 2D pipeline assigned, the boot scene with its
    /// Bootstrap first in the build list and the release-1 player settings; running it again changes nothing.
    /// </summary>
    public sealed class ProjectSetupTests
    {
        [OneTimeSetUp]
        public void RunSetup() => ProjectSetup.Run();

        [Test]
        public void UrpPipelineAndRendererAssetsExist()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(ProjectSetup.PipelineAssetPath);
            Assert.IsNotNull(pipeline, ProjectSetup.PipelineAssetPath);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Renderer2DData>(ProjectSetup.RendererAssetPath), ProjectSetup.RendererAssetPath);
            Assert.IsTrue(pipeline.supportsHDR);
        }

        [Test]
        public void UrpPipelineIsAssignedInGraphicsAndEveryQualityLevel()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(ProjectSetup.PipelineAssetPath);
            Assert.AreSame(pipeline, GraphicsSettings.defaultRenderPipeline);

            int current = QualitySettings.GetQualityLevel();
            try
            {
                for (int i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    Assert.AreSame(pipeline, QualitySettings.renderPipeline, "quality level " + QualitySettings.names[i]);
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(current, false);
            }
        }

        [Test]
        public void BootSceneExistsWithBootstrapAndIsFirstBuildScene()
        {
            Assert.IsTrue(File.Exists(ProjectSetup.BootScenePath), ProjectSetup.BootScenePath);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(ProjectSetup.BootScenePath));

            EditorBuildSettingsScene first = EditorBuildSettings.scenes.FirstOrDefault();
            Assert.IsNotNull(first);
            Assert.AreEqual(ProjectSetup.BootScenePath, first.path);
            Assert.IsTrue(first.enabled);

            Scene scene = EditorSceneManager.OpenScene(ProjectSetup.BootScenePath, OpenSceneMode.Additive);
            try
            {
                Bootstrap[] found = scene.GetRootGameObjects()
                    .SelectMany(go => go.GetComponentsInChildren<Bootstrap>(true))
                    .ToArray();
                Assert.AreEqual(1, found.Length, "exactly one Bootstrap in the boot scene");
                Assert.AreEqual(ProjectSetup.BootObjectName, found[0].gameObject.name);
            }
            finally
            {
                if (SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void PlayerSettingsMatchReleaseOne()
        {
            Assert.AreEqual("Ballverse", PlayerSettings.companyName);
            Assert.AreEqual("Ballverse", PlayerSettings.productName);
            Assert.AreEqual(ProjectSetup.ApplicationId, PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android));
            Assert.AreEqual(ProjectSetup.ApplicationId, PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS));
            Assert.AreEqual(ColorSpace.Linear, PlayerSettings.colorSpace);
            Assert.AreEqual(UIOrientation.Portrait, PlayerSettings.defaultInterfaceOrientation);
            Assert.IsFalse(PlayerSettings.allowedAutorotateToLandscapeLeft);
            Assert.IsFalse(PlayerSettings.allowedAutorotateToLandscapeRight);
            Assert.AreEqual(ScriptingImplementation.IL2CPP, PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android));
            Assert.AreEqual(AndroidArchitecture.ARM64, PlayerSettings.Android.targetArchitectures);
            Assert.AreEqual((AndroidSdkVersions)ProjectSetup.AndroidMinSdk, PlayerSettings.Android.minSdkVersion);
            Assert.AreEqual(ProjectSetup.IosMinVersion, PlayerSettings.iOS.targetOSVersionString);
        }

        [Test]
        public void AdMobAppIdsAreSetWhenThePluginIsPresent()
        {
            if (!AdMobDefaults.PluginPresent) Assert.Ignore("Google Mobile Ads plugin is not installed.");
            var ids = AdMobDefaults.ReadAppIds();
            Assert.IsTrue(ids.HasValue);
            Assert.IsFalse(string.IsNullOrWhiteSpace(ids.Value.android));
            Assert.IsFalse(string.IsNullOrWhiteSpace(ids.Value.ios));
        }

        [Test]
        public void RunningSetupAgainKeepsTheSameAssets()
        {
            string pipelineGuid = AssetDatabase.AssetPathToGUID(ProjectSetup.PipelineAssetPath);
            string sceneGuid = AssetDatabase.AssetPathToGUID(ProjectSetup.BootScenePath);
            int sceneCount = EditorBuildSettings.scenes.Length;

            ProjectSetup.Run();

            Assert.AreEqual(pipelineGuid, AssetDatabase.AssetPathToGUID(ProjectSetup.PipelineAssetPath));
            Assert.AreEqual(sceneGuid, AssetDatabase.AssetPathToGUID(ProjectSetup.BootScenePath));
            Assert.AreEqual(sceneCount, EditorBuildSettings.scenes.Length);
        }
    }
}
