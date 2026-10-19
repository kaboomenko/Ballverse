// Stand-ins for the Unity 6 / Unity Test Framework / editor / UI runtime APIs the PlayMode smoke test uses beyond the App
// harness's stand-ins (Tests/AppHarness~/Stubs/UnityStubs*.cs and UiRuntimeStubs.cs, compiled here with their classes made
// partial by HarnessStubs.targets so the members below can extend them). Signatures follow the Unity 6000.3 scripting reference and com.unity.test-framework 1.6
// (UnityEngine.TestRunner); bodies are benign. Not part of the Unity project (the parent folder ends in "~").
#pragma warning disable CS0067, CS1591
using System;
using System.Text.RegularExpressions;

namespace UnityEngine
{
    /// <summary>UnityEngine.LogType (values as in UnityEngine.CoreModule).</summary>
    public enum LogType
    {
        Error = 0,
        Assert = 1,
        Warning = 2,
        Log = 3,
        Exception = 4,
    }

    public static partial class Application
    {
        public delegate void LogCallback(string condition, string stackTrace, LogType type);

        public static event LogCallback logMessageReceived;
        public static event LogCallback logMessageReceivedThreaded;
        public static bool isBatchMode => true;
        public static string unityVersion => "6000.3.26f1";
    }

    public static partial class SystemInfo
    {
        public static UnityEngine.Rendering.GraphicsDeviceType graphicsDeviceType => UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore;
        public static string graphicsDeviceVersion => "stub";
    }

    public class YieldInstruction
    {
    }

    public class AsyncOperation : YieldInstruction
    {
        public bool isDone => true;
        public float progress => 1f;
        public bool allowSceneActivation { get => true; set { } }
    }
}

namespace UnityEngine.Rendering
{
    /// <summary>UnityEngine.Rendering.GraphicsDeviceType (subset; values as in UnityEngine.CoreModule).</summary>
    public enum GraphicsDeviceType
    {
        Direct3D11 = 2,
        Null = 4,
        OpenGLES3 = 11,
        Metal = 16,
        OpenGLCore = 17,
        Direct3D12 = 18,
        Vulkan = 21,
    }
}

namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode
    {
        Single = 0,
        Additive = 1,
    }

    public enum LocalPhysicsMode
    {
        None = 0,
        Physics2D = 1,
        Physics3D = 2,
    }

    public struct LoadSceneParameters
    {
        public LoadSceneMode loadSceneMode;
        public LocalPhysicsMode localPhysicsMode;

        public LoadSceneParameters(LoadSceneMode mode)
        {
            loadSceneMode = mode;
            localPhysicsMode = LocalPhysicsMode.None;
        }

        public LoadSceneParameters(LoadSceneMode mode, LocalPhysicsMode physicsMode)
        {
            loadSceneMode = mode;
            localPhysicsMode = physicsMode;
        }
    }

    public class SceneManager
    {
        public static AsyncOperation LoadSceneAsync(string sceneName, LoadSceneMode mode) => new AsyncOperation();
        public static AsyncOperation LoadSceneAsync(string sceneName, LoadSceneParameters parameters) => new AsyncOperation();
    }
}

namespace UnityEditor.SceneManagement
{
    public sealed class EditorSceneManager : UnityEngine.SceneManagement.SceneManager
    {
        public static UnityEngine.AsyncOperation LoadSceneAsyncInPlayMode(string path, UnityEngine.SceneManagement.LoadSceneParameters parameters) =>
            new UnityEngine.AsyncOperation();
    }
}

namespace UnityEditor
{
    /// <summary>Used by the real Media/Fake/FakeMediaBridge.cs under UNITY_EDITOR (the editor branch is compiled here).</summary>
    public static class EditorUtility
    {
        public static void RevealInFinder(string path) { }
    }
}

namespace UnityEngine.TestTools
{
    [AttributeUsage(AttributeTargets.Method)]
    public class UnityTestAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class UnitySetUpAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class UnityTearDownAttribute : Attribute
    {
    }

    public interface IPrebuildSetup
    {
        void Setup();
    }

    public interface IPostBuildCleanup
    {
        void Cleanup();
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class PrebuildSetupAttribute : Attribute
    {
        public PrebuildSetupAttribute(Type setupType) { }
        public PrebuildSetupAttribute(string targetClassName) { }
    }

    public static class LogAssert
    {
        public static bool ignoreFailingMessages { get; set; }
        public static void Expect(LogType type, string message) { }
        public static void Expect(LogType type, Regex message) { }
        public static void NoUnexpectedReceived() { }
    }
}

namespace Ballverse.Editor
{
    /// <summary>
    /// Signature stand-in for the members of Assets/Ballverse/Editor/ProjectSetup.cs the test uses (the editor assembly needs
    /// UnityEditor / URP and is compiled by Unity only); ../run.sh checks the copied lines against the real file.
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenesFolder = "Assets/Scenes";
        public const string BootScenePath = ScenesFolder + "/Boot.unity";

        public static void Run() { }
    }
}

namespace Ballverse.UI.Navigation
{
    /// <summary>
    /// The navigator members the smoke test drives beyond the App harness's UiRuntimeStubs.cs (which has Current, Pop and the
    /// events). Copied from UI/Runtime/Navigation/UiNavigator.cs; ../run.sh checks each line against that file.
    /// </summary>
    public sealed partial class UiNavigator
    {
        public NavigationStack Stack => default;
        public void Push(ScreenId id) { }
        public void SelectTab(TabId tab) { }
        public BackOutcome Back() => default;
    }
}
