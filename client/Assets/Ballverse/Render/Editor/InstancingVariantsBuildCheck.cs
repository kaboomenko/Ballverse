using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ballverse.Render.Editor
{
    /// <summary>
    /// Keeps the GPU-instancing shader variants of the renderer in player builds. The SDF balls and the particles are
    /// drawn only with <c>CommandBuffer.DrawMeshInstanced</c>; with the default Graphics setting "Instancing Variants:
    /// Strip Unused" Unity removes the <c>INSTANCING_ON</c> variants unless a material included in the build enables GPU
    /// instancing, and materials created at runtime do not count. The renderer therefore clones the instancing-enabled
    /// templates in <c>Resources/BallverseMaterials</c> (always in the build). Before every player build this check
    /// <list type="bullet">
    /// <item>verifies both templates exist, reference their shader and have "Enable GPU Instancing" on — a missing or
    /// broken template is (re)created from the shader, so a bad merge cannot silently ship a build without balls;</item>
    /// <item>fails the build when "Instancing Variants" is set to "Strip All", which would strip them regardless.</item>
    /// </list>
    /// EditMode tests cannot observe variant stripping, so this is the static guard (see RenderResourcesTests).
    /// </summary>
    public sealed class InstancingVariantsBuildCheck : IPreprocessBuildWithReport
    {
        /// <summary>Asset folder of the templates (<see cref="RenderResources.MaterialFolder"/> under the Render Resources folder).</summary>
        public const string TemplateFolder = "Assets/Ballverse/Render/Resources/BallverseMaterials";

        /// <summary>GraphicsSettings <c>m_InstancingStripping</c> values (Project Settings > Graphics > Instancing Variants).</summary>
        public const int StripUnused = 0;
        public const int StripAll = 1;
        public const int KeepAll = 2;

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            string error = EnsureTemplates();
            if (error != null) throw new BuildFailedException("[Ballverse.Render] " + error);
            if (ReadInstancingStripping() == StripAll)
            {
                throw new BuildFailedException("[Ballverse.Render] Project Settings > Graphics > Instancing Variants is 'Strip All': " +
                    "the renderer's balls and particles need the GPU-instancing variants. Use 'Strip Unused' (default) or 'Keep All'.");
            }
        }

        /// <summary>Asset path of a template by its Resources name (e.g. <see cref="RenderResources.BallTemplate"/>).</summary>
        public static string TemplatePath(string resourcesPath)
        {
            string name = resourcesPath.Substring(resourcesPath.LastIndexOf('/') + 1);
            return TemplateFolder + "/" + name + ".mat";
        }

        /// <summary>
        /// Checks both templates and recreates a missing or broken one. Returns null when both are valid afterwards,
        /// otherwise the reason.
        /// </summary>
        public static string EnsureTemplates()
        {
            string a = EnsureTemplate(TemplatePath(RenderResources.BallTemplate), RenderResources.BallShaderName);
            string b = EnsureTemplate(TemplatePath(RenderResources.ParticleTemplate), RenderResources.ParticleShaderName);
            return a ?? b;
        }

        /// <summary>Null when the template at <paramref name="assetPath"/> is valid, otherwise the reason (no repair).</summary>
        public static string Validate(string assetPath, string shaderName)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (m == null) return "Material template '" + assetPath + "' is missing";
            if (m.shader == null || !string.Equals(m.shader.name, shaderName, StringComparison.Ordinal))
            {
                return "Material template '" + assetPath + "' does not use shader '" + shaderName + "'";
            }
            if (!m.enableInstancing) return "Material template '" + assetPath + "' has GPU instancing off";
            return null;
        }

        static string EnsureTemplate(string assetPath, string shaderName)
        {
            string problem = Validate(assetPath, shaderName);
            if (problem == null) return null;
            Shader shader = Shader.Find(shaderName);
            if (shader == null) return problem + " and shader '" + shaderName + "' was not found";
            Debug.LogWarning("[Ballverse.Render] " + problem + "; recreating it with GPU instancing on.");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null)
            {
                existing.shader = shader;
                existing.enableInstancing = true;
                EditorUtility.SetDirty(existing);
            }
            else
            {
                if (!AssetDatabase.IsValidFolder(TemplateFolder)) AssetDatabase.CreateFolder("Assets/Ballverse/Render/Resources", "BallverseMaterials");
                AssetDatabase.CreateAsset(new Material(shader) { enableInstancing = true }, assetPath);
            }
            AssetDatabase.SaveAssets();
            return Validate(assetPath, shaderName);
        }

        /// <summary>The project's "Instancing Variants" stripping mode (<see cref="StripUnused"/>, <see cref="StripAll"/>, <see cref="KeepAll"/>), or −1.</summary>
        public static int ReadInstancingStripping()
        {
            UnityEngine.Object settings = GraphicsSettings.GetGraphicsSettings();
            if (settings == null) return -1;
            using (var so = new SerializedObject(settings))
            {
                SerializedProperty p = so.FindProperty("m_InstancingStripping");
                return p != null ? p.intValue : -1;
            }
        }
    }
}
