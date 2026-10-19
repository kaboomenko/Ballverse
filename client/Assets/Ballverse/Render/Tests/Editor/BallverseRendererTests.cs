using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ballverse.Render.Tests
{
    /// <summary>
    /// Unity-side checks of the simulation renderer. The geometry, VFX, camera and theme logic is covered by the Core tests
    /// (dotnet); these verify shader inclusion and that a frame really reaches the pixels. GPU tests are ignored on a
    /// machine without a graphics device (CI started with -nographics).
    /// </summary>
    public sealed class BallverseRendererTests
    {
        static readonly string[] s_shaders =
        {
            "BallverseBatch", "BallverseSdfBall", "BallverseParticle", "BallverseText", "BallverseBackground", "BallversePost",
        };

        [Test]
        public void ShadersAreShippedInResources()
        {
            foreach (string name in s_shaders)
            {
                Shader shader = Resources.Load<Shader>(RenderResources.ShaderFolder + name);
                Assert.IsNotNull(shader, name + " is missing from Resources/" + RenderResources.ShaderFolder);
            }
        }

        static void RequireGpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device (-nographics).");
        }

        static RenderTexture NewTarget(int w, int h)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            return rt;
        }

        static Color32[] ReadBack(RenderTexture rt)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            Color32[] pixels = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return pixels;
        }

        static RenderContext RingEscapeContext(out RenderFrame frame)
        {
            RenderQuantization q = RenderQuantization.Default;
            var shapes = new List<ShapeDefinition>
            {
                ShapeDefinition.Circle,
                // Gap on the left (around 180°), so the ring is solid at 0° (to the right of the centre).
                ShapeDefinition.Ring(q.QuantizeScale(3f), q.QuantizeScale(0.2f), new[] { RingGap.FromRadians(Mathf.PI - 0.3f, Mathf.PI + 0.3f) }),
            };
            frame = new RenderFrame();
            frame.AddBody(q.QuantizeBody(1, 0, 0f, 0f, 0f, 1.2f, 0, BodyFlags.Visible));
            frame.AddBody(q.QuantizeBody(2, 1, 0f, 0f, 0f, 1f, PaletteSlots.Arena, BodyFlags.Visible));
            frame.AddHud(0, 7);
            frame.Sort();
            var ctx = new RenderContext
            {
                Quantization = q,
                Shapes = shapes,
                ModeId = "ring_escape",
                Tick = 0,
                Quality = QualityProfile.A,
            };
            ctx.Look.Theme = new RenderThemeCatalog().GetOrDefault("Mono");   // black background, white balls
            ctx.Look.Trail = TrailStyle.Off;
            return ctx;
        }

        [Test]
        public void RingEscapeFrameReachesThePixelsDeterministically()
        {
            RequireGpu();
            RenderContext ctx = RingEscapeContext(out RenderFrame frame);
            RenderTexture rt = NewTarget(180, 320);
            using (var renderer = new BallverseRenderer())
            {
                renderer.Render(frame, ctx, rt);
                Color32[] first = ReadBack(rt);
                // Centre: the white ball (Mono palette entry 0). Corner: the black background.
                Color32 centre = first[160 * 180 + 90];
                Color32 corner = first[2 * 180 + 2];
                Assert.Greater(centre.r, 200, "ball not drawn: " + centre);
                Assert.Less(corner.r + corner.g + corner.b, 60, "background not drawn: " + corner);
                // Ring at 3 m: 3 m × 20 px/m = 60 px right of the centre is on the ring (white).
                Color32 ring = first[160 * 180 + 90 + 60];
                Assert.Greater(ring.r, 150, "ring not drawn: " + ring);

                renderer.Render(frame, ctx, rt);
                Color32[] second = ReadBack(rt);
                CollectionAssert.AreEqual(first, second);

                Assert.AreEqual(1, renderer.Hud.Length);
                Assert.AreEqual(7, renderer.Hud[0].Value);
            }
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        [Test]
        public void OfflineRenderUsesTheExportProfileAndRestoresTheContext()
        {
            RequireGpu();
            RenderContext ctx = RingEscapeContext(out RenderFrame frame);
            ctx.Quality = QualityProfile.C;
            RenderTexture rt = NewTarget(108, 192);
            using (var renderer = new BallverseRenderer())
            {
                renderer.RenderOffline(frame, ctx, rt);
                Assert.AreSame(QualityProfile.C, ctx.Quality);
            }
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        [Test]
        public void HelixFrameRendersPlatformsAroundTheBall()
        {
            RequireGpu();
            var q = new RenderQuantization(64f, 8f);
            var shapes = new List<ShapeDefinition>
            {
                ShapeDefinition.Circle,
                ShapeDefinition.Ring(q.QuantizeScale(1.5f), q.QuantizeScale(1.8f), new[] { RingGap.FromRadians(-0.5f, 0.5f) }),
            };
            var frame = new RenderFrame();
            for (int k = 0; k < 8; k++) frame.AddBody(q.QuantizeBody(10 + k, 1, 0f, -k * 1.8f, k * 0.6f, 1f, PaletteSlots.Arena, BodyFlags.Visible));
            frame.AddBody(q.QuantizeBody(1, 0, 0f, -3.3f, 0f, 0.25f, 0, BodyFlags.Visible));
            frame.Sort();
            var ctx = new RenderContext { Quantization = q, Shapes = shapes, ModeId = "helix_drop", Tick = 0, Quality = QualityProfile.B };
            RenderTexture rt = NewTarget(180, 320);
            using (var renderer = new BallverseRenderer())
            {
                renderer.Render(frame, ctx, rt);
                Assert.Greater(renderer.Composer.Under.IndexCount, 0);
                Assert.Greater(renderer.Composer.Over.IndexCount, 0);
                Assert.AreEqual(1, renderer.Composer.Balls.Length);
            }
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
