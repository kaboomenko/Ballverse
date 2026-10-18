using System.Collections.Generic;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Ballverse.Render.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Ballverse.Render.Tests
{
    /// <summary>
    /// Player-build guards that EditMode can check without a GPU (variant stripping itself is invisible to EditMode tests,
    /// see <see cref="InstancingVariantsBuildCheck"/>), and the toolkit-independent presenter.
    /// </summary>
    public sealed class RenderBuildAndPresenterTests
    {
        [Test]
        public void InstancedMaterialTemplatesShipWithGpuInstancingOn()
        {
            Assert.IsNull(InstancingVariantsBuildCheck.Validate(InstancingVariantsBuildCheck.TemplatePath(RenderResources.BallTemplate),
                RenderResources.BallShaderName));
            Assert.IsNull(InstancingVariantsBuildCheck.Validate(InstancingVariantsBuildCheck.TemplatePath(RenderResources.ParticleTemplate),
                RenderResources.ParticleShaderName));
            // The runtime loads them through Resources (always in the build).
            Assert.IsNotNull(RenderResources.LoadTemplate(RenderResources.BallTemplate, RenderResources.BallShaderName));
            Assert.IsNotNull(RenderResources.LoadTemplate(RenderResources.ParticleTemplate, RenderResources.ParticleShaderName));
        }

        [Test]
        public void TemplatesLiveInTheRenderResourcesFolder()
        {
            StringAssert.StartsWith("Assets/Ballverse/Render/Resources/", InstancingVariantsBuildCheck.TemplatePath(RenderResources.BallTemplate));
            StringAssert.EndsWith("/BallverseSdfBallInstanced.mat", InstancingVariantsBuildCheck.TemplatePath(RenderResources.BallTemplate));
        }

        [Test]
        public void ProjectDoesNotStripAllInstancingVariants()
        {
            Assert.AreNotEqual(InstancingVariantsBuildCheck.StripAll, InstancingVariantsBuildCheck.ReadInstancingStripping());
        }

        [Test]
        public void RendererMaterialsAreClonedFromTheTemplates()
        {
            LogAssert.ignoreFailingMessages = true;   // shaders may be unsupported without a graphics device
            using (var resources = new RenderResources())
            {
                if (resources.BallMaterial == null || resources.ParticleMaterial == null) Assert.Ignore("Shaders unsupported on this device.");
                Assert.IsTrue(resources.TemplatesValid);
                Assert.IsTrue(resources.BallMaterial.enableInstancing);
                Assert.IsTrue(resources.ParticleMaterial.enableInstancing);
            }
        }

        [Test]
        public void PresenterClampsSizesAndNeedsAProviderAndAStream()
        {
            Assert.AreEqual(BallversePresenter.MinSize, BallversePresenter.ClampSize(3));
            Assert.AreEqual(BallversePresenter.MaxSize, BallversePresenter.ClampSize(10000));
            Assert.AreEqual(540, BallversePresenter.ClampSize(540));
            Assert.IsFalse(BallversePresenter.TryGetPixelSize(null, 0f, out _, out _));
            Assert.IsFalse(BallversePresenter.TryGetPixelSize(new UnityEngine.UIElements.VisualElement(), 0f, out _, out _));   // not in a panel
            LogAssert.ignoreFailingMessages = true;
            using (var presenter = new BallversePresenter())
            {
                Assert.IsFalse(presenter.Present(540, 960, 1f / 60f));   // no provider, no shape table
                Assert.IsNull(presenter.Target);
            }
        }

        sealed class RingProvider : IBallverseFrameProvider
        {
            public int Tick;
            public bool TryGetFrame(out RenderFrame frame, out int tick)
            {
                frame = null;   // read from the context's frame source
                tick = Tick;
                return true;
            }
        }

        [Test]
        public void PresenterRendersFromTheFrameSourceAndRaisesTargetChanged()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device (-nographics).");
            RenderQuantization q = RenderQuantization.Default;
            var shapes = new List<ShapeDefinition> { ShapeDefinition.Circle };
            var ring = new RenderFrameRing(8);
            for (int t = 0; t < 4; t++)
            {
                var f = new RenderFrame();
                f.AddBody(q.QuantizeBody(1, 0, 0.5f * t, 0f, 0f, 1f, 0, BodyFlags.Visible | BodyFlags.Trail));
                ring.Push(t, f);
            }
            using (var presenter = new BallversePresenter())
            {
                RenderTexture changed = null;
                presenter.TargetChanged += rt => changed = rt;
                presenter.Context.Quantization = q;
                presenter.Context.Shapes = shapes;
                presenter.Context.ModeId = "geometry_bounce";
                presenter.Context.FrameSource = ring;
                presenter.Provider = new RingProvider { Tick = 3 };
                Assert.IsTrue(presenter.Present(90, 160, 1f / 60f));
                Assert.IsNotNull(presenter.Target);
                Assert.AreSame(presenter.Target, changed);
                Assert.AreEqual(90, presenter.Target.width);
                Assert.AreEqual(1, presenter.Renderer.Composer.Balls.Length);
                Assert.AreEqual(1.5f, presenter.Renderer.Composer.Balls[0].X, 0.01f);
                Assert.IsTrue(presenter.Present(180, 320, 1f / 60f));
                Assert.AreSame(presenter.Target, changed);
                Assert.AreEqual(180, presenter.Target.width);
            }
        }
    }
}
