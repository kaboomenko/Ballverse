using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.Timeline;
using Ballverse.UI.Gestures;
using Ballverse.UI.Layout;
using Ballverse.UI.Navigation;
using Ballverse.UI.Studio;
using Xunit;

namespace Ballverse.UI.Tests
{
    public class StudioValuesTests
    {
        [Fact]
        public void EverySplitPercentStaysReachable()
        {
            // The old UI magnet (±2%) made 12 of the 41 steps unreachable; Core RatioMagnets keeps all of them.
            for (int p = StudioValues.RatioMinPercent; p <= StudioValues.RatioMaxPercent; p++)
            {
                float ratio = StudioValues.RatioFromPercent(p);
                Assert.Equal(p, StudioValues.PercentFromRatio(ratio));
                Assert.Equal(RatioMagnets.Snap(p / 100f), ratio);
            }
        }

        [Fact]
        public void MagnetStepsCommitExactThirdsAndHalf()
        {
            Assert.Equal(1f / 3f, StudioValues.RatioFromPercent(33));
            Assert.Equal(0.5f, StudioValues.RatioFromPercent(50));
            Assert.Equal(2f / 3f, StudioValues.RatioFromPercent(67));
            Assert.Equal(0.30f, StudioValues.RatioFromPercent(10));
            Assert.Equal(0.70f, StudioValues.RatioFromPercent(95));
        }

        [Theory]
        [InlineData(false, 2.0, 0f)]
        [InlineData(true, 0.5, 1f)]
        [InlineData(true, 0.0, 1f)]
        [InlineData(true, 1.5, 1.5f)]
        [InlineData(true, 3.0, 3f)]
        [InlineData(true, 9.0, 3f)]
        public void SlowMoIsOffOrOneToThreeSeconds(bool enabled, double slider, float expected)
        {
            Assert.Equal(expected, StudioValues.SlowMoSeconds(enabled, slider));
        }

        [Fact]
        public void EverySlowMoValueTheUiCanSendIsAcceptedByCore()
        {
            var values = new List<float> { StudioValues.SlowMoSeconds(false, 1) };
            for (double v = StudioValues.SlowMoMinSeconds; v <= StudioValues.SlowMoMaxSeconds + 1e-9; v += StudioValues.SlowMoStepSeconds)
                values.Add(StudioValues.SlowMoSeconds(true, v));
            foreach (float seconds in values)
            {
                int ticks = ClipTiming.TicksFromSeconds(seconds);
                var timing = new ClipTiming(0, 600, 600, 100, ticks, 50);
                Assert.Equal(ticks, timing.SlowMoTicks);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClipTiming(0, 600, 600, 100, ClipTiming.TicksFromSeconds(0.5), 50));
        }

        [Fact]
        public void SegmentsMapToCoreEnumsByName()
        {
            Assert.Equal(TextAlign.Left, StudioValues.At(StudioValues.AlignOrder, 0));
            Assert.Equal(TextAlign.Center, StudioValues.At(StudioValues.AlignOrder, 1));
            Assert.Equal(TextAlign.Right, StudioValues.At(StudioValues.AlignOrder, 2));
            Assert.Equal(1, StudioValues.IndexIn(StudioValues.AlignOrder, TextAlign.Center));
            Assert.Equal(TextPlate.Glass, StudioValues.At(StudioValues.PlateOrder, 2));
            Assert.Equal(PipShape.Circle, StudioValues.At(StudioValues.PipShapeOrder, 1));
            AssertCovers(StudioValues.AlignOrder);
            AssertCovers(StudioValues.PlateOrder);
            AssertCovers(StudioValues.PipShapeOrder);
            AssertCovers(StudioValues.FitBackgroundOrder);
            AssertCovers(StudioValues.VideoAfterEndOrder);
            AssertCovers(StudioValues.DurationModeOrder);
        }

        static void AssertCovers<T>(T[] order) where T : struct
        {
            foreach (T v in (T[])Enum.GetValues(typeof(T))) Assert.Contains(v, order);
            Assert.Equal(Enum.GetValues(typeof(T)).Length, order.Length);
        }

        [Fact]
        public void UiDeclaresNoCopiesOfCoreProjectEnums()
        {
            // QA: UI had its own TextAlign (Left = 0) while Core.Projects has Center = 0.
            var ui = typeof(StudioValues).Assembly;
            foreach (var t in ui.GetTypes())
            {
                if (t.Namespace == null || !t.Namespace.StartsWith("Ballverse.UI", StringComparison.Ordinal) || !t.IsEnum) continue;
                Assert.DoesNotContain(t.Name, new[] { "TextAlign", "TextPlate", "PipShape", "FitBackgroundKind" });
            }
        }
    }

    public class CurrentRefTests
    {
        sealed class Sheet
        {
        }

        [Fact]
        public void LateReleaseOfAReplacedInstanceKeepsTheSuccessor()
        {
            var slot = new CurrentRef<Sheet>();
            var a = new Sheet();
            var b = new Sheet();
            slot.Set(a);
            slot.Set(b); // tool switch: b opens while a animates out
            Assert.False(slot.Release(a)); // a's Closed callback after its 0.22 s animation
            Assert.Same(b, slot.Value);
            Assert.True(slot.Release(b));
            Assert.False(slot.HasValue);
            Assert.False(slot.Release(null));
        }
    }

    public class TimelineEditTests
    {
        const long S = TimelineGeometry.MicrosPerSecond;

        [Fact]
        public void VideoMayStartBeforeTheSimulation()
        {
            // Core UserVideoTiming allows a negative offset (V before S); the UI keeps at least 1 s of overlap.
            Assert.Equal(-5 * S, TimelineGeometry.ClampVideoOffset(-5 * S, 20 * S, 10 * S));
            Assert.Equal(-9 * S, TimelineGeometry.ClampVideoOffset(-30 * S, 20 * S, 10 * S));
            Assert.Equal(19 * S, TimelineGeometry.ClampVideoOffset(40 * S, 20 * S, 10 * S));
            Assert.Equal(0, TimelineGeometry.ClampVideoOffset(0, 20 * S, 10 * S));
        }

        [Fact]
        public void TextSpansMoveInsideTheReel()
        {
            var span = new TrimRange(2 * S, 6 * S);
            Assert.Equal(new TrimRange(5 * S, 9 * S), TimelineGeometry.ShiftSpan(span, 3 * S, 20 * S));
            Assert.Equal(new TrimRange(0, 4 * S), TimelineGeometry.ShiftSpan(span, -10 * S, 20 * S));
            Assert.Equal(new TrimRange(16 * S, 20 * S), TimelineGeometry.ShiftSpan(span, 30 * S, 20 * S));
            // Edges use the trim math with the reel as the source: ≥ 1 s long, inside [0, reel].
            Assert.Equal(new TrimRange(5 * S, 6 * S), TimelineGeometry.DragStart(span, 4 * S, 20 * S));
            Assert.Equal(new TrimRange(2 * S, 20 * S), TimelineGeometry.DragEnd(span, 40 * S, 20 * S));
        }
    }

    public class PreviewGestureTests
    {
        sealed class Sink : IPreviewGestureSink
        {
            public readonly List<string> Log = new List<string>();
            public FloatVec2 LastPip;
            public float Zoom = 1f;
            public FloatVec2 Pan;

            public void MovePip(FloatVec2 center, bool final)
            {
                LastPip = center;
                Log.Add(final ? "pip-final" : "pip");
            }

            public void PanZoom(SlotKind slot, FloatVec2 panDelta, float zoomFactor, bool final)
            {
                Zoom *= zoomFactor;
                Pan = new FloatVec2(Pan.X + panDelta.X, Pan.Y + panDelta.Y);
                Log.Add((final ? "commit-" : "panzoom-") + slot);
            }

            public void ResetSlot(SlotKind slot)
            {
                Log.Add("reset-" + slot);
            }
        }

        // 200 × 356 px preview: S fills the frame, V is a PiP window in the top-right quarter.
        static readonly GestureSlot[] Slots =
        {
            new GestureSlot(SlotKind.Simulation, new FloatRect(0f, 0f, 1f, 1f), false),
            new GestureSlot(SlotKind.Video, new FloatRect(0.55f, 0.05f, 0.4f, 0.25f), true),
        };

        static FloatVec2 P(float x, float y) => new FloatVec2(x, y);

        [Fact]
        public void DraggingThePipMovesItAndCommitsOnRelease()
        {
            var sink = new Sink();
            var g = new PreviewGestures(sink);
            Assert.True(g.Down(1, P(150, 50), 200, 356, Slots, 0f));
            g.Move(1, P(120, 120));
            g.Move(1, P(60, 250));
            g.Up(1, P(60, 250), 0.5f);
            Assert.Equal("pip-final", sink.Log[sink.Log.Count - 1]);
            Assert.Contains("pip", sink.Log);
            // Grab offset is kept: the window centre moved by the finger movement.
            Assert.Equal(0.75f - 0.45f, sink.LastPip.X, 3);
            Assert.False(g.Active);
        }

        [Fact]
        public void OneFingerPansAndTwoFingersZoomTheSlot()
        {
            var sink = new Sink();
            var g = new PreviewGestures(sink);
            Assert.True(g.Down(1, P(50, 200), 200, 356, Slots, 0f));
            g.Move(1, P(70, 200));
            Assert.Equal(20f / 200f, sink.Pan.X, 3);
            Assert.True(g.Down(2, P(150, 200), 200, 356, Slots, 0.1f));
            g.Move(2, P(170, 200)); // distance 80 → 100
            Assert.Equal(1.25f, sink.Zoom, 3);
            g.Up(2, P(170, 200), 0.3f);
            g.Up(1, P(70, 200), 0.4f);
            Assert.Equal("commit-Simulation", sink.Log[sink.Log.Count - 1]);
            Assert.False(g.Active);
        }

        [Fact]
        public void DoubleTapResetsTheSlotUnderTheFinger()
        {
            var sink = new Sink();
            var g = new PreviewGestures(sink);
            g.Down(1, P(40, 300), 200, 356, Slots, 0f);
            g.Up(1, P(41, 300), 0.08f);
            g.Down(1, P(44, 302), 200, 356, Slots, 0.2f);
            g.Up(1, P(44, 302), 0.26f);
            Assert.Equal(new[] { "reset-Simulation" }, sink.Log.ToArray());
        }

        [Fact]
        public void SlowTapsAreNotADoubleTap()
        {
            var sink = new Sink();
            var g = new PreviewGestures(sink);
            g.Down(1, P(40, 300), 200, 356, Slots, 0f);
            g.Up(1, P(40, 300), 0.1f);
            g.Down(1, P(40, 300), 200, 356, Slots, 0.9f);
            g.Up(1, P(40, 300), 1.0f);
            Assert.Empty(sink.Log);
        }

        [Fact]
        public void PressOutsideEverySlotIsNotUsed()
        {
            var g = new PreviewGestures(new Sink());
            var only = new[] { new GestureSlot(SlotKind.Simulation, new FloatRect(0f, 0f, 1f, 0.5f), false) };
            Assert.False(g.Down(1, P(100, 300), 200, 356, only, 0f));
            Assert.False(g.Active);
        }
    }

    public class ArenaAndEdgeGestureTests
    {
        [Theory]
        [InlineData(10f, 400f, false)] // left edge zone (spec §2.6: 24 pt)
        [InlineData(380f, 400f, false)] // right edge zone
        [InlineData(200f, 830f, false)] // bottom edge zone
        [InlineData(200f, 80f, false)] // inside the top bar strip
        [InlineData(200f, 760f, false)] // inside the bottom bar strip
        [InlineData(30f, 400f, true)]
        [InlineData(200f, 400f, true)]
        public void ArenaDropsEdgeAndBarTouches(float x, float y, bool accepted)
        {
            Assert.Equal(accepted, ArenaInputPolicy.Accepts(x, y, 390f, 844f, 110f, 740f));
        }

        [Fact]
        public void SwipeFromTheLeftEdgeIsBack()
        {
            var s = new EdgeSwipeBack();
            Assert.True(s.Down(1, 8f, 400f, 390f));
            Assert.True(s.Up(1, 120f, 420f));
        }

        [Fact]
        public void ShortVerticalOrNonEdgeSwipesAreNotBack()
        {
            var s = new EdgeSwipeBack();
            Assert.False(s.Down(1, 60f, 400f, 390f)); // not from the edge
            s.Down(2, 5f, 400f, 390f);
            Assert.False(s.Up(2, 50f, 400f)); // too short
            s.Down(3, 5f, 400f, 390f);
            Assert.False(s.Up(3, 120f, 600f)); // mostly vertical
            s.Down(4, 5f, 400f, 390f);
            s.Cancel(4);
            Assert.False(s.Up(4, 200f, 400f));
        }
    }
}
