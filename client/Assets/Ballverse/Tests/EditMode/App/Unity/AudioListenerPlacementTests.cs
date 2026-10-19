using System;
using System.Linq;
using Ballverse.Sim;
using Ballverse.Studio;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ballverse.App.Tests
{
    /// <summary>
    /// The app's audio objects in a real Unity scene (GameCI EditMode run). The .NET App harness only compile-checks this
    /// folder, because its Unity stand-ins have no scene. The PlayMode smoke test failed at boot with "GameObject has multiple
    /// AudioSources and/or AudioListeners attached": LiveAudioOutput had added the listener to its own object, which carries the
    /// AudioSource and the OnAudioFilterRead script. The live output and the studio preview output are created here together,
    /// the way DeviceEnvironment wires them, in an empty (camera-less, like the boot scene) scene.
    /// <para>Edit Mode does not call Awake / OnEnable on these scripts (Unity 6 Scripting API, <c>ExecuteAlways</c>: "By default,
    /// MonoBehaviours are only executed in Play Mode"), so the sources do not play here. The placement does not depend on that:
    /// the listener comes from <see cref="DeviceAudio"/>, not from the outputs' Awake. Tests/PlayMode/AudioOutputsPlayModeTest
    /// covers the running path (Awake, Play, the filters attached).</para>
    /// </summary>
    public sealed class AudioListenerPlacementTests
    {
        SceneSetup[] _previous;
        IDisposable _live;
        PreviewAudioOutput _preview;
        GameObject _sceneListener;

        [SetUp]
        public void SetUp()
        {
            _previous = EditorSceneManager.GetSceneManagerSetup();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            DeviceAudio.ReleaseListener();
        }

        [TearDown]
        public void TearDown()
        {
            _live?.Dispose();
            _live = null;
            if (_preview != null) Object.DestroyImmediate(_preview.gameObject);
            _preview = null;
            if (_sceneListener != null) Object.DestroyImmediate(_sceneListener);
            _sceneListener = null;
            DeviceAudio.ReleaseListener();
            // An untitled scene cannot be restored (same rule as Ballverse.Editor.ProjectSetup.CreateBootScene).
            if (_previous != null && _previous.Length > 0 && _previous.All(s => !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(_previous);
        }

        void CreateBothOutputs()
        {
            _live = DeviceAudio.CreateLiveOutput(new LiveAudioEngine()); // DeviceEnvironment.CreateAudioOutput
            DeviceAudio.EnsureListener();                                // DeviceEnvironment.CreateStudio
            _preview = PreviewAudioOutput.Create(null);                  // StudioPreviewPlayer (under the studio runner on a device)
        }

        static void AssertNoListenerNextToAnAudioSource()
        {
            foreach (AudioSource source in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                Assert.IsNull(source.GetComponent<AudioListener>(), source.gameObject.name + " has an AudioSource and an AudioListener");
            }
            foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            {
                Assert.IsNull(listener.GetComponent<LiveAudioOutput>(), "listener next to LiveAudioOutput (OnAudioFilterRead)");
                Assert.IsNull(listener.GetComponent<PreviewAudioOutput>(), "listener next to PreviewAudioOutput (OnAudioFilterRead)");
            }
        }

        [Test]
        public void LiveAndPreviewOutputsShareOneListenerOnItsOwnObject()
        {
            CreateBothOutputs();

            Assert.IsNotNull(Object.FindAnyObjectByType<LiveAudioOutput>(), "live output");
            Assert.IsNotNull(Object.FindAnyObjectByType<PreviewAudioOutput>(), "studio preview output");
            AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            Assert.AreEqual(1, listeners.Length, "exactly one AudioListener in the app");
            Assert.AreSame(DeviceAudio.OwnedListener, listeners[0]);
            Assert.AreEqual(DeviceAudio.ListenerObjectName, listeners[0].gameObject.name);
            Assert.IsTrue(listeners[0].enabled);
            Assert.IsNull(listeners[0].GetComponent<AudioSource>(), "the listener's object has no AudioSource");
            AssertNoListenerNextToAnAudioSource();
            Assert.IsNull(DeviceAudio.FindPlacementProblem());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AnEnabledSceneListenerIsReusedAndNoSecondOneIsAdded()
        {
            _sceneListener = new GameObject("Main Camera");
            AudioListener existing = _sceneListener.AddComponent<AudioListener>();

            CreateBothOutputs();

            Assert.AreSame(existing, DeviceAudio.EnsureListener());
            Assert.IsNull(DeviceAudio.OwnedListener, "no listener of the app's own");
            Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length);
            AssertNoListenerNextToAnAudioSource();
            Assert.IsNull(DeviceAudio.FindPlacementProblem());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void TheAppListenerIsCreatedOnceReenabledAndReleased()
        {
            AudioListener first = DeviceAudio.EnsureListener();
            first.enabled = false;
            AudioListener again = DeviceAudio.EnsureListener();
            Assert.AreSame(first, again, "no second listener");
            Assert.IsTrue(again.enabled, "re-enabled");
            Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length);

            DeviceAudio.ReleaseListener();
            Assert.AreEqual(0, Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length);
            Assert.IsNull(DeviceAudio.OwnedListener);
            StringAssert.Contains("found 0", DeviceAudio.FindPlacementProblem());
            LogAssert.NoUnexpectedReceived();
        }
    }
}
