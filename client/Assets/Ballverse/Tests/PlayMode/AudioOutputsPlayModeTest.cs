using System;
using System.Collections;
using Ballverse.App;
using Ballverse.Sim;
using Ballverse.Studio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ballverse.PlayModeTests
{
    /// <summary>
    /// The live sound output and the studio preview output running together in Play Mode, wired the way DeviceEnvironment
    /// wires them. Their Awake / OnEnable run here, the sources Play(), and Unity inserts each script's OnAudioFilterRead into
    /// the DSP chain of the AudioSource on its object. The first PlayMode smoke run failed at boot at exactly this point:
    /// "GameObject has multiple AudioSources and/or AudioListeners attached", because LiveAudioOutput had put the app's
    /// AudioListener on its own object. Any error log fails the test (Unity Test Framework's default LogAssert behaviour),
    /// and the app's only listener must be on an object of its own (<see cref="DeviceAudio.FindPlacementProblem"/>).
    /// </summary>
    public sealed class AudioOutputsPlayModeTest
    {
        IDisposable _live;
        PreviewAudioOutput _preview;

        [TearDown]
        public void TearDown()
        {
            _live?.Dispose();
            _live = null;
            if (_preview != null) Object.Destroy(_preview.gameObject);
            _preview = null;
            DeviceAudio.ReleaseListener();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator LiveAndPreviewOutputsPlayWithOneListenerOnItsOwnObject()
        {
            // Objects an earlier test destroyed (Object.Destroy is deferred to the end of the frame) are gone after one frame.
            yield return null;

            _live = DeviceAudio.CreateLiveOutput(new LiveAudioEngine()); // DeviceEnvironment.CreateAudioOutput: Awake + Play
            DeviceAudio.EnsureListener();                                // DeviceEnvironment.CreateStudio
            _preview = PreviewAudioOutput.Create(null);                  // StudioPreviewPlayer
            _preview.SetMix(new float[48000 * 2]);                       // one second of silent stereo mix
            _preview.Play(0);
            Assert.IsNotNull(Object.FindAnyObjectByType<LiveAudioOutput>(), "live output");

            for (int i = 0; i < 10; i++) yield return null;

            string problem = DeviceAudio.FindPlacementProblem();
            Assert.IsNull(problem, problem);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
