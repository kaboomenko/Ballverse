using System;
using Ballverse.Platform;
using Ballverse.UI.Fakes;
using Ballverse.UI.Layout;
using Ballverse.UI.Screens;
using Ballverse.UI.Theming;
using Ballverse.UI.ViewModels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.EditorTests
{
    /// <summary>
    /// EditMode checks of the QA round 3 fixes on a detached element tree: the timeline is built for a given track width
    /// (no panel layout needed) and the safe-area controller is fed by a fake platform provider.
    /// </summary>
    public sealed class QaRound3Tests
    {
        const long S = TimelineGeometry.MicrosPerSecond;
        const float TrackWidth = 350f;

        FakeViewModels _vms;
        UiShell _shell;

        [SetUp]
        public void SetUp()
        {
            _vms = new FakeViewModels();
            _shell = new UiShell(new VisualElement(), _vms);
            _vms.SetLocalizer(_shell.Localizer.Get);
        }

        [TearDown]
        public void TearDown()
        {
            _shell?.Dispose();
            _vms?.Dispose();
        }

        static StudioTimelineData Data(long videoStartUs, params TimelineTextItem[] texts)
        {
            return new StudioTimelineData(30 * S,
                new TimelineTrackData(true, 30 * S, 0, 30 * S, 0, Array.Empty<Texture>()),
                new TimelineTrackData(true, 20 * S, 0, 20 * S, videoStartUs, Array.Empty<Texture>()),
                texts, null, 60);
        }

        TimelineView Build(StudioTimelineData data)
        {
            var view = new TimelineView(_shell.Context);
            view.SetData(data, 0);
            view.Build(TrackWidth);
            return view;
        }

        static float Left(VisualElement e)
        {
            Assert.AreEqual(StyleKeyword.Undefined, e.style.left.keyword, e.name + " has no inline left (never placed)");
            return e.style.left.value.value;
        }

        static float Width(VisualElement e)
        {
            Assert.AreEqual(StyleKeyword.Undefined, e.style.width.keyword);
            return e.style.width.value.value;
        }

        [Test]
        public void ShortTextSpanHandlesArePlacedAndLeaveTheBodyFree()
        {
            // QA round 3: a 2 s text on a 30 s reel (28 pt pill) kept the USS handle offsets and was fully covered.
            var view = Build(Data(0, new TimelineTextItem(0, 10 * S, 12 * S, "#satisfying")));
            var track = view.Q<VisualElement>(className: "bv-timeline__track--text");
            var pill = track.Q<VisualElement>(className: "bv-timeline__text");
            Assert.IsNotNull(pill);
            Assert.AreSame(track, pill.parent);
            var left = pill.Q<VisualElement>(className: TimelineView.LeftHandleClass);
            var right = pill.Q<VisualElement>(className: TimelineView.RightHandleClass);
            float x = Left(pill);
            float w = Width(pill);
            view.HandleBand(track, out float bandMin, out float bandMax);
            var p = TimelineGeometry.PlaceHandles(x, w, bandMin, bandMax);
            Assert.AreEqual(p.LeftX, Left(left), 1e-3f);
            Assert.AreEqual(p.RightX, Left(right), 1e-3f);
            Assert.GreaterOrEqual(p.ExposedBody, TimelineGeometry.MinExposedBody - 1e-3f);
            // The label sits in the pill's body, which carries the drag; the pill itself is not hit.
            var body = pill.Q<VisualElement>(className: TimelineView.BodyClass);
            Assert.IsNotNull(body.Q<Label>(className: "bv-timeline__text-label"));
            Assert.AreEqual(PickingMode.Ignore, pill.pickingMode);
            Assert.AreEqual(PickingMode.Position, body.pickingMode);
            Assert.AreEqual(0f, Left(body), 1e-3f);
            Assert.AreEqual(w, Width(body), 1e-3f);
        }

        [Test]
        public void AVideoClipBeforeSIsDrawnFromTheTrackStart()
        {
            // QA round 3: with V at −10 s the clip painted 32 pt over the badge column and the padding.
            var view = Build(Data(-10 * S));
            var seg = view.Q<VisualElement>(className: "bv-timeline__track--video").Q<VisualElement>(className: "bv-timeline__segment");
            var body = seg.Q<VisualElement>(className: TimelineView.BodyClass);
            float x = Left(seg);
            Assert.Less(x, 0f);
            Assert.AreEqual(0f, x + Left(body), 1e-3f);
            Assert.AreEqual(x + Width(seg), x + Left(body) + Width(body), 1e-3f);
            Assert.IsTrue(body.ClassListContains("bv-timeline__body--cut-start"));
            Assert.IsFalse(body.ClassListContains("bv-timeline__body--cut-end"));
            Assert.AreEqual(PickingMode.Ignore, seg.pickingMode);
            // The handles are the segment's children beside the body, never inside the clipped body.
            var left = seg.Q<VisualElement>(className: TimelineView.LeftHandleClass);
            Assert.AreSame(seg, left.parent);
            Assert.IsNull(body.Q<VisualElement>(className: "bv-timeline__handle"));
        }

        sealed class FakeSafeArea : ISafeAreaProvider
        {
            public SafeAreaInsets Current { get; set; }
            public event Action<SafeAreaInsets> Changed;
            public bool HasListeners => Changed != null;

            public void Raise(SafeAreaInsets insets)
            {
                Current = insets;
                Changed?.Invoke(insets);
            }
        }

        [Test]
        public void SafeAreaSpacersFollowThePlatformProvider()
        {
            var provider = new FakeSafeArea { Current = new SafeAreaInsets(1170, 2532, 0, 141, 0, 102) };
            var root = new VisualElement();
            var top = new VisualElement();
            top.AddToClassList("bv-safe-top");
            var bottom = new VisualElement();
            bottom.AddToClassList("bv-safe-bottom");
            root.Add(top);
            root.Add(bottom);
            var controller = new SafeAreaController(root, provider);
            Assert.IsTrue(provider.HasListeners);
            Assert.IsFalse(controller.Recompute(float.NaN), "waits for the panel layout");
            Assert.IsTrue(controller.Recompute(390f));
            Assert.AreEqual(47f, top.style.height.value.value, 0.01f);
            Assert.AreEqual(34f, bottom.style.height.value.value, 0.01f);

            provider.Raise(new SafeAreaInsets(1170, 2532, 0, 0, 0, 0));
            Assert.AreEqual(provider.Current, controller.DeviceInsets);
            controller.Recompute(390f);
            Assert.AreEqual(0f, top.style.height.value.value, 0.01f);

            controller.Dispose();
            Assert.IsFalse(provider.HasListeners);
        }

        [Test]
        public void TheShellUnsubscribesFromTheProviderOnDispose()
        {
            var provider = new FakeSafeArea { Current = new SafeAreaInsets(1170, 2532, 0, 141, 0, 102) };
            var vms = new FakeViewModels();
            var shell = new UiShell(new VisualElement(), vms, provider);
            try
            {
                Assert.IsTrue(provider.HasListeners);
                Assert.AreEqual(provider.Current, shell.Context.SafeArea.DeviceInsets);
            }
            finally
            {
                shell.Dispose();
                vms.Dispose();
            }
            Assert.IsFalse(provider.HasListeners);
        }
    }
}
