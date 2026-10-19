using System;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using Ballverse.Editor;
#endif

namespace Ballverse.PlayModeTests
{
    /// <summary>
    /// Runs in the editor before Play Mode starts (Unity Test Framework <see cref="IPrebuildSetup"/>): applies
    /// <c>Ballverse.Editor.ProjectSetup.Run()</c>, the same step every device build runs first (BuildScript), so the play
    /// session has what the APK has: the URP 2D pipeline assigned in Graphics / Quality settings, the boot scene
    /// (Assets/Scenes/Boot.unity with its Bootstrap object) first in the build scene list, and the release-1 player settings
    /// (company / product name, hence the same persistentDataPath layout). A fresh checkout has none of these assets
    /// committed, so without this step there is no boot scene to load. Idempotent; a project that already ran Ballverse >
    /// Project Setup is left unchanged.
    /// </summary>
    public sealed class BootSceneSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            ProjectSetup.Run();
            if (!System.IO.File.Exists(ProjectSetup.BootScenePath))
                throw new InvalidOperationException("ProjectSetup.Run() did not create the boot scene at " + ProjectSetup.BootScenePath);
#endif
        }
    }
}
