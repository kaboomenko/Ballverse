using System;
using UnityEngine;

namespace Ballverse.Render
{
    /// <summary>Shader property ids used by the renderer (resolved once).</summary>
    static class ShaderIds
    {
        public static readonly int ViewProj = Shader.PropertyToID("_BvViewProj");
        public static readonly int SkinTex = Shader.PropertyToID("_SkinTex");
        public static readonly int GlowMargin = Shader.PropertyToID("_GlowMargin");
        public static readonly int BallColor = Shader.PropertyToID("_BallColor");
        public static readonly int BallParams = Shader.PropertyToID("_BallParams");
        public static readonly int BallSkinRect = Shader.PropertyToID("_BallSkinRect");
        public static readonly int ParticleColor = Shader.PropertyToID("_PColor");
        public static readonly int ParticleParams = Shader.PropertyToID("_PParams");
        public static readonly int MainTex = Shader.PropertyToID("_MainTex");
        public static readonly int BgTop = Shader.PropertyToID("_BgTop");
        public static readonly int BgBottom = Shader.PropertyToID("_BgBottom");
        public static readonly int BgGrid = Shader.PropertyToID("_BgGrid");
        public static readonly int BgStar = Shader.PropertyToID("_BgStar");
        public static readonly int BgParams = Shader.PropertyToID("_BgParams");
        public static readonly int BgView = Shader.PropertyToID("_BgView");
        public static readonly int BgPixels = Shader.PropertyToID("_BgPixels");
        public static readonly int Source = Shader.PropertyToID("_BvSource");
        public static readonly int SourceTexelSize = Shader.PropertyToID("_BvSource_TexelSize");
        public static readonly int Scene = Shader.PropertyToID("_BvScene");
        public static readonly int Bloom = Shader.PropertyToID("_BvBloom");
        public static readonly int BloomParams = Shader.PropertyToID("_BvBloomParams");
    }

    /// <summary>
    /// Shaders, materials and the shared quad of the renderer. Shaders live in <c>Ballverse/Render/Resources/BallverseShaders</c>
    /// so they are always included in player builds (no "Always Included Shaders" setup needed). One instance can be shared
    /// by several <see cref="BallverseRenderer"/>s (live preview and export).
    /// </summary>
    public sealed class RenderResources : IDisposable
    {
        public const string ShaderFolder = "BallverseShaders/";

        /// <summary>Glow margin of the ball quad: the quad extends this fraction of the radius beyond the disc for the halo.</summary>
        public const float BallGlowMargin = 0.6f;

        bool _disposed;

        public RenderResources()
        {
            BatchMaterial = CreateMaterial("BallverseBatch", "Hidden/Ballverse/Batch", false);
            BallMaterial = CreateMaterial("BallverseSdfBall", "Hidden/Ballverse/SdfBall", true);
            ParticleMaterial = CreateMaterial("BallverseParticle", "Hidden/Ballverse/Particle", true);
            TextMaterial = CreateMaterial("BallverseText", "Hidden/Ballverse/Text", false);
            BackgroundMaterial = CreateMaterial("BallverseBackground", "Hidden/Ballverse/Background", false);
            PostMaterial = CreateMaterial("BallversePost", "Hidden/Ballverse/Post", false);
            if (BallMaterial != null) BallMaterial.SetFloat(ShaderIds.GlowMargin, BallGlowMargin);
            Quad = CreateQuad();
            InstancingSupported = SystemInfo.supportsInstancing;
        }

        public Material BatchMaterial { get; }
        public Material BallMaterial { get; }
        public Material ParticleMaterial { get; }
        public Material TextMaterial { get; }
        public Material BackgroundMaterial { get; }
        public Material PostMaterial { get; }

        /// <summary>Unit quad [-0.5, 0.5]² in the XY plane (instanced balls and particles).</summary>
        public Mesh Quad { get; }

        public bool InstancingSupported { get; }

        /// <summary>True when every shader loaded and is supported on this GPU.</summary>
        public bool IsComplete => BatchMaterial != null && BallMaterial != null && ParticleMaterial != null && TextMaterial != null
            && BackgroundMaterial != null && PostMaterial != null;

        static Material CreateMaterial(string resourceName, string shaderName, bool instanced)
        {
            Shader shader = Resources.Load<Shader>(ShaderFolder + resourceName);
            if (shader == null) shader = Shader.Find(shaderName);
            if (shader == null || !shader.isSupported)
            {
                Debug.LogError("[Ballverse.Render] Shader '" + shaderName + "' is missing or unsupported on this device; that layer is skipped.");
                return null;
            }
            var m = new Material(shader)
            {
                name = resourceName,
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = instanced,
            };
            return m;
        }

        static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "BallverseQuad", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            });
            mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0, true);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, 1f, 1f));
            mesh.UploadMeshData(true);
            return mesh;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Destroy(BatchMaterial);
            Destroy(BallMaterial);
            Destroy(ParticleMaterial);
            Destroy(TextMaterial);
            Destroy(BackgroundMaterial);
            Destroy(PostMaterial);
            Destroy(Quad);
        }

        internal static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }

    /// <summary>sRGB byte → linear float lookup (colours are authored in sRGB; the scene buffer is linear HDR).</summary>
    static class ColorConvert
    {
        static readonly float[] s_toLinear = BuildTable();

        static float[] BuildTable()
        {
            var t = new float[256];
            for (int i = 0; i < 256; i++) t[i] = Ballverse.Core.RenderGeometry.ColorMath.ToLinear((byte)i);
            return t;
        }

        /// <summary>0xRRGGBBAA → colour in the project's working space (linear in a linear project), alpha linear.</summary>
        public static Vector4 ToWorking(uint rgba)
        {
            byte r = (byte)(rgba >> 24), g = (byte)(rgba >> 16), b = (byte)(rgba >> 8), a = (byte)rgba;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                return new Vector4(s_toLinear[r], s_toLinear[g], s_toLinear[b], a / 255f);
            }
            return new Vector4(r / 255f, g / 255f, b / 255f, a / 255f);
        }
    }
}
