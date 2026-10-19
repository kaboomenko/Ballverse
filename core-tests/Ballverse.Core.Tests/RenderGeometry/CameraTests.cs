using System;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Xunit;
using static Ballverse.Core.Tests.RenderGeometry.GeometryTestUtil;

namespace Ballverse.Core.Tests.RenderGeometry
{
    public class CameraTests
    {
        static readonly System.Collections.Generic.List<ShapeDefinition> S = Shapes(ShapeDefinition.Circle);

        [Fact]
        public void NineBySixteenCanvasShowsTheArenaExactly()
        {
            ViewRect v = ViewMapping.Fixed(1080, 1920);
            Assert.Equal(9f, v.Width, 4);
            Assert.Equal(16f, v.Height, 4);
            Assert.Equal(120f, ViewMapping.ArenaPixelsPerMetre(1080, 1920), 3);
            Assert.Equal(80f, ViewMapping.ArenaPixelsPerMetre(720, 1280), 3);
        }

        [Theory]
        [InlineData(1080, 960)]    // L1 band (half height)
        [InlineData(540, 1920)]    // L2 half (half width)
        [InlineData(1920, 1080)]   // landscape
        [InlineData(1080, 1080)]
        [InlineData(300, 2000)]
        public void AnyCanvasKeepsTheSafeZoneVisible(int w, int h)
        {
            ViewRect v = ViewMapping.Fixed(w, h);
            Assert.True(v.Width >= SimConstants.SafeZoneWidth - 1e-3f, "width " + v.Width);
            Assert.True(v.Height >= SimConstants.SafeZoneHeight - 1e-3f, "height " + v.Height);
            Assert.Equal((float)w / h, v.Width / v.Height, 3);
            // Cover when possible: no empty bars unless the safe zone forbids cropping further.
            bool covers = v.Width <= SimConstants.ArenaWidth + 1e-3f || v.Height <= SimConstants.ArenaHeight + 1e-3f;
            bool safeLimited = Math.Abs(v.Width - SimConstants.SafeZoneWidth) < 1e-3f || Math.Abs(v.Height - SimConstants.SafeZoneHeight) < 1e-3f;
            Assert.True(covers || safeLimited);
        }

        [Fact]
        public void PixelWorldRoundTripAndOrthographicMatrix()
        {
            ViewRect v = ViewMapping.Fixed(1080, 1920);
            ViewMapping.WorldToPixel(v, 1080, 1920, 0f, 0f, out float px, out float py);
            Assert.Equal(540f, px, 3);
            Assert.Equal(960f, py, 3);
            ViewMapping.PixelToWorld(v, 1080, 1920, 1080f, 1920f, out float x, out float y);
            Assert.Equal(4.5f, x, 3);
            Assert.Equal(8f, y, 3);
            Span<float> m = stackalloc float[16];
            ViewMapping.Orthographic(v, m);
            // (4.5, 8) → clip (1, 1).
            Assert.Equal(1f, m[0] * 4.5f + m[12], 4);
            Assert.Equal(1f, m[5] * 8f + m[13], 4);
        }

        static RenderFrame Ball(float x, float y, BodyFlags extra = BodyFlags.None, int id = 1)
        {
            return Frame(Q.QuantizeBody(id, 0, x, y, 0f, 0.25f, 0, BodyFlags.Visible | extra));
        }

        [Fact]
        public void FixedCameraIgnoresBodies()
        {
            var cam = new CameraTrack();
            cam.Record(0, Ball(3f, 5f), Q, S);
            Assert.Equal(ViewMapping.Fixed(1080, 1920), cam.Evaluate(0, new CameraSettings(), 1080, 1920, out _));
        }

        [Fact]
        public void HelixFollowKeepsTheBallAt38PercentAndNeverMovesUp()
        {
            var cam = new CameraTrack();
            CameraSettings s = CameraSettings.ForHelix();
            // Ball falls from 0 to −10 m over 3 s, then bounces up 1 m.
            for (int t = 0; t < 180; t++) cam.Record(t, Ball(0f, -t / 18f), Q, S);
            ViewRect v = cam.Evaluate(179, s, 1080, 1920, out float target);
            Assert.InRange(target, -9.94f + 0.4f, -9.94f + 0.6f);   // exponential smoothing lags speed × τ = 0.5 m behind a falling ball
            float anchorY = v.MaxY - 0.38f * v.Height;
            Assert.Equal(target, anchorY, 3);
            for (int t = 180; t < 200; t++) cam.Record(t, Ball(0f, -10f + (t - 180) * 0.05f), Q, S);
            ViewRect up = cam.Evaluate(199, s, 1080, 1920, out float target2);
            Assert.True(target2 <= target + 1e-4f, "camera moved up: " + target + " → " + target2);
            Assert.True(up.CenterY <= v.CenterY + 1e-4f);
            Assert.Equal(0f, up.CenterX);   // vertical only
        }

        [Fact]
        public void EvaluationDependsOnlyOnRecordedTicksNotOnTheCallPattern()
        {
            CameraSettings s = CameraSettings.ForHelix();
            var a = new CameraTrack();
            var b = new CameraTrack();
            Func<int, RenderFrame> frameAt = t => Ball(0.3f * MathF.Sin(t * 0.1f), -t * 0.03f + 0.5f * MathF.Abs(MathF.Sin(t * 0.2f)));
            // a: sequential with an evaluation every tick; b: only the window before tick 250 recorded.
            ViewRect va = default;
            for (int t = 0; t <= 250; t++)
            {
                a.Record(t, frameAt(t), Q, S);
                va = a.Evaluate(t, s, 1080, 1920, out _);
            }
            for (int t = 250 - CameraTrack.Capacity + 1; t <= 250; t++) b.Record(t, frameAt(t), Q, S);
            ViewRect vb = b.Evaluate(250, s, 1080, 1920, out _);
            Assert.Equal(va, vb);
        }

        [Fact]
        public void AutoZoomFitsBallsAndNeverZoomsInPastTheArena()
        {
            var cam = new CameraTrack();
            var s = new CameraSettings { Mode = CameraMode.AutoZoom, AutoZoomCentered = true, SmoothingSeconds = 0f, AutoZoomMargin = 1f };
            cam.Record(0, Ball(0.5f, 0.2f), Q, S);
            ViewRect near = cam.Evaluate(0, s, 1080, 1920, out _);
            Assert.Equal(16f, near.Height, 3);   // min height = the zoom-1 view
            var far = Frame(Q.QuantizeBody(1, 0, 10f, 0f, 0f, 0.25f, 0, BodyFlags.Visible), Q.QuantizeBody(2, 0, -2f, 3f, 0f, 0.25f, 0, BodyFlags.Visible));
            cam.Record(1, far, Q, S);
            ViewRect v = cam.Evaluate(1, s, 1080, 1920, out _);
            Assert.Equal(0f, v.CenterX);
            Assert.True(v.MaxX >= 10.25f + 1f - 1e-3f, "ball outside: maxX " + v.MaxX);
            Assert.Equal(9f / 16f, v.Width / v.Height, 3);
        }

        [Fact]
        public void HighlightedBallIsTheFollowTarget()
        {
            var cam = new CameraTrack();
            var f = Frame(Q.QuantizeBody(1, 0, 0f, 5f, 0f, 0.25f, 0, BodyFlags.Visible), Q.QuantizeBody(2, 0, 0f, -3f, 0f, 0.25f, 0, BodyFlags.Visible | BodyFlags.Highlight));
            cam.Record(0, f, Q, S);
            var s = new CameraSettings { Mode = CameraMode.Follow, SmoothingSeconds = 0f };
            cam.Evaluate(0, s, 1080, 1920, out float target);
            Assert.Equal(-3f, target, 2);
        }
    }
}
