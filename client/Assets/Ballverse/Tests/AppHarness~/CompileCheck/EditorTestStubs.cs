// Stand-ins for the Unity editor / Test Framework APIs used only by the App EditMode tests that need a real scene
// (Tests/EditMode/App/Unity/*.cs). Compile-only: those tests run in GameCI's EditMode job, not in ../Runtime (whose Unity
// stand-ins have no scene). Signatures follow the Unity 6000.x scripting reference and Unity Test Framework 1.x.
#pragma warning disable CS1591
namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string path => default;
        public bool IsValid() => false;
    }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene = 0, DefaultGameObjects = 1 }
    public enum NewSceneMode { Single = 0, Additive = 1 }

    public sealed class SceneSetup
    {
        public string path { get; set; }
        public bool isLoaded { get; set; }
        public bool isActive { get; set; }
    }

    public static class EditorSceneManager
    {
        public static SceneSetup[] GetSceneManagerSetup() => new SceneSetup[0];
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => default;
        public static void RestoreSceneManagerSetup(SceneSetup[] value) { }
    }
}

namespace UnityEngine.TestTools
{
    public static class LogAssert
    {
        public static void NoUnexpectedReceived() { }
    }
}
