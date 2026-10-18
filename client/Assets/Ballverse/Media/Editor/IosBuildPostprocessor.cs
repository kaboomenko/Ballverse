#if UNITY_IOS
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace Ballverse.Media.Editor
{
    /// <summary>
    /// Xcode project fix-ups for the MediaBridge iOS plugin (Assets/Plugins/iOS/BallverseMedia):
    /// links AVFoundation, CoreMedia, CoreVideo, Photos, Accelerate and (weakly, iOS 14+) PhotosUI into UnityFramework,
    /// compiles the plugin's .mm files with ARC (-fobjc-arc), and adds the photo-library usage descriptions to
    /// Info.plist when the project does not define them yet (texts are placeholders to localise before release).
    /// </summary>
    public static class IosBuildPostprocessor
    {
        const string PluginFolder = "Plugins/iOS/BallverseMedia";

        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;

            string projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string framework = project.GetUnityFrameworkTargetGuid();
            foreach (string name in new[] { "AVFoundation.framework", "CoreMedia.framework", "CoreVideo.framework", "Photos.framework", "Accelerate.framework" })
            {
                project.AddFrameworkToProject(framework, name, false);
            }
            project.AddFrameworkToProject(framework, "PhotosUI.framework", true);

            string sourceFolder = Path.Combine(Application.dataPath, PluginFolder);
            if (Directory.Exists(sourceFolder))
            {
                foreach (string file in Directory.GetFiles(sourceFolder, "*.mm"))
                {
                    string projectFile = "Libraries/" + PluginFolder + "/" + Path.GetFileName(file);
                    string guid = project.FindFileGuidByProjectPath(projectFile);
                    if (string.IsNullOrEmpty(guid))
                    {
                        Debug.LogWarning("[Media] " + projectFile + " not found in the Xcode project; ARC flag not set.");
                        continue;
                    }
                    List<string> flags = project.GetCompileFlagsForFile(framework, guid) ?? new List<string>();
                    if (!flags.Contains("-fobjc-arc"))
                    {
                        flags.Add("-fobjc-arc");
                        project.SetCompileFlagsForFile(framework, guid, flags);
                    }
                }
            }
            project.WriteToFile(projectPath);

            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            PlistElementDict root = plist.root;
            if (!root.values.ContainsKey("NSPhotoLibraryAddUsageDescription"))
                root.SetString("NSPhotoLibraryAddUsageDescription", "Ballverse saves the videos you export to your photo library.");
            if (!root.values.ContainsKey("NSPhotoLibraryUsageDescription"))
                root.SetString("NSPhotoLibraryUsageDescription", "Ballverse adds the videos you export to its Ballverse album.");
            plist.WriteToFile(plistPath);
        }
    }
}
#endif
