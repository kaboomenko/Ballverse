using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ballverse.UI.Accessibility;
using Ballverse.UI.Gestures;
using Ballverse.UI.Glass;
using Ballverse.UI.Layout;
using Xunit;

namespace Ballverse.UI.Tests
{
    /// <summary>iOS edge swipe after QA round 2: a press whose release the shell never saw must not linger.</summary>
    public class EdgeSwipeRecoveryTests
    {
        const float Width = 390f;

        [Fact]
        public void CapturedPressWithoutReleaseDoesNotFireOnALaterTap()
        {
            // QA probe: the back button at x = 12 captures the press (its Up never reaches app-root); the next tap in the
            // upper right of the arena reuses touch id 1 and must not be measured from the old origin.
            var s = new EdgeSwipeBack();
            Assert.True(s.Down(1, 12f, 60f, Width));
            Assert.False(s.Down(1, 200f, 90f, Width)); // a new press of the same pointer: the old one ended
            Assert.False(s.Tracking);
            Assert.False(s.Up(1, 200f, 90f));
        }

        [Fact]
        public void AfterACapturedEdgePressAGenuineSwipeStillWorks()
        {
            var s = new EdgeSwipeBack();
            Assert.True(s.Down(1, 10f, 60f, Width)); // captured, no Up
            Assert.True(s.Down(1, 6f, 400f, Width));
            Assert.True(s.Up(1, 140f, 410f));
        }

        [Fact]
        public void ASecondFingerDoesNotRestartTheTrackedPress()
        {
            var s = new EdgeSwipeBack();
            Assert.True(s.Down(1, 6f, 400f, Width));
            Assert.False(s.Down(2, 8f, 100f, Width));
            Assert.True(s.IsTracking(1));
            Assert.False(s.IsTracking(2));
            Assert.False(s.Up(2, 200f, 100f));
            Assert.True(s.Up(1, 150f, 400f));
        }

        [Fact]
        public void MoveClaimsThePressOnlyWhenItIsClearlyASwipe()
        {
            var s = new EdgeSwipeBack();
            s.Down(1, 6f, 400f, Width);
            Assert.False(s.Move(1, 6f + EdgeSwipeBack.ClaimDistance - 1f, 400f)); // a tap's jitter
            Assert.False(s.Move(1, 30f, 440f)); // mostly vertical
            Assert.False(s.Move(2, 60f, 400f)); // another pointer
            Assert.True(s.Move(1, 6f + EdgeSwipeBack.ClaimDistance, 404f));
            s.Cancel(1);
            Assert.False(s.Move(1, 100f, 400f));
            Assert.False(s.Up(1, 200f, 400f));
        }
    }

    /// <summary>Trim handles keep a 44 pt hit area at the reel edges and leave short text spans selectable.</summary>
    public class TimelineHandlePlacementTests
    {
        // QA probe layout: 390-unit reference width, timeline host padding 8, badge column 24 → track x 32..382 (350 wide).
        const float TrackWidth = 350f;
        const float BandMin = -32f;
        const float BandMax = 358f;
        const float H = TimelineGeometry.HandleWidth;

        static float Pickable(float segX, float handleX)
        {
            float l = Math.Max(BandMin, segX + handleX);
            float r = Math.Min(BandMax, segX + handleX + H);
            return Math.Max(0f, r - l);
        }

        [Fact]
        public void HandlesAreCentredOnTheEdgesOfALongSegment()
        {
            var p = TimelineGeometry.PlaceHandles(100f, 150f, BandMin, BandMax);
            Assert.Equal(-H / 2f, p.LeftX);
            Assert.Equal(150f - H / 2f, p.RightX);
            Assert.Equal(H / 2f, p.LeftEdgeInHandle);
            Assert.Equal(H / 2f, p.RightEdgeInHandle);
        }

        [Fact]
        public void HandlesAtTheReelStartAndEndKeepTheFullHitArea()
        {
            // The S clip of an Auto-duration reel spans the whole track (QA: 22 pt pickable each as shipped).
            var p = TimelineGeometry.PlaceHandles(0f, TrackWidth, BandMin, BandMax);
            Assert.Equal(H, Pickable(0f, p.LeftX));
            Assert.Equal(H, Pickable(0f, p.RightX));
            Assert.True(p.RightX + H <= BandMax);
            // The end grip still marks the clip end inside the handle.
            Assert.Equal(TrackWidth - p.RightX, p.RightEdgeInHandle);
            // Even with no room outside the track at all (a clipping parent) each handle stays whole.
            var tight = TimelineGeometry.PlaceHandles(0f, TrackWidth, 0f, TrackWidth);
            Assert.Equal(0f, tight.LeftX);
            Assert.Equal(TrackWidth - H, tight.RightX);
        }

        [Theory]
        [InlineData(28f)] // 2 s text on a 30 s reel (QA: 0 px of body exposed)
        [InlineData(35f)] // 3 s text on a 30 s reel
        [InlineData(29.2f)] // 5 s text on a 60 s reel
        [InlineData(60f)]
        public void ShortSpansKeepPartOfTheirBodyFree(float width)
        {
            foreach (float x in new[] { 0f, 120f, TrackWidth - width })
            {
                var p = TimelineGeometry.PlaceHandles(x, width, BandMin, BandMax);
                // At the reel start too (the left handle reaches over the badge column); only against the right screen
                // edge (8 pt past the track) is there no room for a whole handle beside a short body.
                if (x < TrackWidth - width)
                    Assert.True(p.ExposedBody >= Math.Min(width, TimelineGeometry.MinExposedBody) - 1e-3f, x + ": " + p.ExposedBody);
                Assert.Equal(H, Pickable(x, p.LeftX));
                Assert.Equal(H, Pickable(x, p.RightX));
                Assert.True(p.LeftX + H <= p.RightX + 1e-3f);
                // The grip stays on the edge it trims.
                Assert.InRange(p.LeftEdgeInHandle, 0f, H);
                Assert.InRange(p.RightEdgeInHandle, 0f, H);
            }
        }

        [Fact]
        public void HandlesNeverOverlapEvenWhenTheBandIsTooNarrow()
        {
            var p = TimelineGeometry.PlaceHandles(0f, 10f, 0f, 60f);
            Assert.True(p.LeftX + H <= p.RightX + 1e-3f);
        }
    }

    /// <summary>When the screen-reader hierarchy is rebuilt (spec §4.8; QA round 2: rebuilt before layout).</summary>
    public class ScreenReaderSyncTests
    {
        [Fact]
        public void AChangeWaitsUntilTheNewElementsAreLaidOut()
        {
            var s = new ScreenReaderSync();
            Assert.True(s.Due(0.016f));
            Assert.Equal(ScreenReaderStep.PublishScreen, s.Decide(true, true));
            // A dialog opens: this frame it has no layout, next frame it has.
            s.MarkDirty();
            Assert.True(s.Due(0.016f));
            Assert.Equal(ScreenReaderStep.Wait, s.Decide(false, true));
            Assert.True(s.Dirty);
            Assert.True(s.Due(0.016f));
            Assert.Equal(ScreenReaderStep.PublishScreen, s.Decide(true, true));
            Assert.False(s.Dirty);
        }

        [Fact]
        public void AChangeIsPublishedAnywayAfterTheMaximumWait()
        {
            var s = new ScreenReaderSync();
            s.Due(0f);
            s.Decide(true, true);
            s.MarkDirty();
            float t = 0f;
            var step = ScreenReaderStep.Wait;
            while (step == ScreenReaderStep.Wait && t < 5f)
            {
                t += 0.1f;
                Assert.True(s.Due(0.1f));
                step = s.Decide(false, true);
            }
            Assert.Equal(ScreenReaderStep.PublishScreen, step);
            Assert.InRange(t, ScreenReaderSync.MaxLayoutWaitSeconds - 1e-3f, ScreenReaderSync.MaxLayoutWaitSeconds + 0.11f);
        }

        [Fact]
        public void TheVisibleUiIsReReadPeriodicallyAndRepublishedWhenItChanged()
        {
            var s = new ScreenReaderSync();
            s.Due(0f);
            s.Decide(true, true);
            Assert.False(s.Due(0.2f));
            Assert.False(s.Due(0.2f));
            Assert.True(s.Due(0.2f));
            Assert.Equal(ScreenReaderStep.RefreshFrames, s.Decide(true, false));
            Assert.True(s.Due(0.5f));
            // e.g. the result card appeared or a push transition ended: no event, but the nodes differ.
            Assert.Equal(ScreenReaderStep.PublishLayout, s.Decide(true, true));
            // A check during a transition waits one more period before publishing what it found.
            Assert.True(s.Due(0.5f));
            Assert.Equal(ScreenReaderStep.Idle, s.Decide(false, true));
            Assert.True(s.Due(0.5f));
            Assert.Equal(ScreenReaderStep.PublishLayout, s.Decide(false, true));
        }
    }

    /// <summary>Documented static checks for the Unity-side parts of the QA round 2 fixes.</summary>
    public class QaRound2StaticChecks
    {
        static string Ui(params string[] parts) => Path.Combine(new[] { Paths.UiRoot }.Concat(parts).ToArray());

        static string Strip(string src) => Regex.Replace(src, @"//.*?$|/\*.*?\*/", "", RegexOptions.Multiline | RegexOptions.Singleline);

        static string Body(string src, string signature)
        {
            var m = Regex.Match(src, Regex.Escape(signature) + @"\s*\{(.*?)\n        \}", RegexOptions.Singleline);
            Assert.True(m.Success, "missing " + signature);
            return m.Groups[1].Value;
        }

        /// <summary>(selector, declarations) of every USS rule, in file order.</summary>
        static List<KeyValuePair<string, Dictionary<string, string>>> UssRules()
        {
            var rules = new List<KeyValuePair<string, Dictionary<string, string>>>();
            foreach (string file in Directory.GetFiles(Ui("Resources", "BallverseUI", "Styles"), "*.uss"))
            {
                string src = Regex.Replace(File.ReadAllText(file), @"/\*.*?\*/", "", RegexOptions.Singleline);
                foreach (Match m in Regex.Matches(src, @"([^{}]+)\{([^{}]*)\}"))
                {
                    var props = new Dictionary<string, string>();
                    foreach (string decl in m.Groups[2].Value.Split(';'))
                    {
                        int colon = decl.IndexOf(':');
                        if (colon > 0) props[decl.Substring(0, colon).Trim()] = decl.Substring(colon + 1).Trim();
                    }
                    foreach (string sel in m.Groups[1].Value.Split(','))
                        rules.Add(new KeyValuePair<string, Dictionary<string, string>>(Regex.Replace(sel.Trim(), @"\s+", " "), props));
                }
            }
            return rules;
        }

        [Fact]
        public void EdgeSwipeFollowsCapturedTapTargetsAndCancelsOnDrags()
        {
            string shell = Strip(File.ReadAllText(Ui("Runtime", "Shell", "UiShell.cs")));
            Assert.Contains("RegisterCallback<PointerCaptureEvent>(OnPointerCaptured, TrickleDown.TrickleDown)", shell);
            string captured = Body(shell, "void OnPointerCaptured(PointerCaptureEvent e)");
            Assert.Contains("ClassListContains(Tap.TargetClass)", captured);
            Assert.Contains("FollowEdgeTarget(captor)", captured);
            Assert.Contains("_edgeSwipe.Cancel(e.pointerId)", captured);
            string follow = Body(shell, "void FollowEdgeTarget(VisualElement target)");
            foreach (string evt in new[] { "PointerMoveEvent", "PointerUpEvent", "PointerCancelEvent", "PointerCaptureOutEvent" })
                Assert.Contains("RegisterCallback<" + evt + ">", follow);
            Assert.Contains("_ctx.AppRoot.CapturePointer(e.pointerId)", Body(shell, "void OnFollowedMove(PointerMoveEvent e)"));
            Assert.Contains("EdgeRelease(e.pointerId, e.position)", Body(shell, "void OnFollowedUp(PointerUpEvent e)"));
            Assert.Contains("ReleaseEdgeCapture(pointerId)", Body(shell, "void EdgeRelease(int pointerId, Vector3 position)"));
            string tap = Strip(File.ReadAllText(Ui("Runtime", "Components", "Tap.cs")));
            Assert.Contains("element.AddToClassList(TargetClass)", tap);
        }

        [Fact]
        public void NoAncestorOfTheTimelineTracksClips()
        {
            // UI Toolkit never picks the children of a clipping element outside its rect (Panel.PerformPick), so the
            // track, the segments and every ancestor up to the screen layer must not clip, or edge handles lose hit area.
            var rules = UssRules();
            string studio = File.ReadAllText(Ui("Resources", "BallverseUI", "Screens", "Studio.uxml"));
            var hostClasses = Regex.Match(studio, "name=\"studio\" class=\"([^\"]+)\"").Groups[1].Value.Split(' ')
                .Concat(Regex.Match(studio, "name=\"timeline-host\" class=\"([^\"]+)\"").Groups[1].Value.Split(' '));
            var classes = hostClasses.Concat(new[]
            {
                "bv-timeline", "bv-timeline__area", "bv-timeline__row", "bv-timeline__track", "bv-timeline__track--sim",
                "bv-timeline__track--video", "bv-timeline__track--text", "bv-timeline__segment", "bv-timeline__segment--sim",
                "bv-timeline__segment--video", "bv-timeline__text", "bv-timeline__handle",
            }).Where(c => c.Length > 0).ToList();
            Assert.Contains("bv-studio__timeline-host", classes);
            foreach (var rule in rules)
            {
                if (!rule.Value.TryGetValue("overflow", out string overflow) || overflow == "visible") continue;
                foreach (string c in classes)
                    Assert.False(Regex.IsMatch(rule.Key, @"\." + Regex.Escape(c) + @"(?![\w-])"), rule.Key + " clips (" + c + ")");
            }
            string view = Strip(File.ReadAllText(Ui("Runtime", "Screens", "Studio", "TimelineView.cs")));
            Assert.Contains("TimelineGeometry.PlaceHandles(x, width, bandMin, bandMax)", view);
            Assert.Contains("PageRoot().worldBound", Body(view, "void PlaceHandles(VisualElement segment, float x, float width)"));
            // The page root the band is measured against fills the screen layer.
            Assert.Contains("bv-page", Regex.Match(studio, "name=\"studio\" class=\"([^\"]+)\"").Groups[1].Value);
            Assert.Contains("bv-fill", Regex.Match(studio, "name=\"studio\" class=\"([^\"]+)\"").Groups[1].Value);
            Assert.Contains("PlaceHandles(element, x, width)", Body(view, "void Place(VisualElement element, long startUs, long lengthUs)"));
            // A text handle pressed without moving selects the layer (short spans are mostly handle).
            Assert.Contains("new EdgeDrag(this, left, EditTarget.TextTrim, true, () => TextSelected?.Invoke(index), index)", view);
        }

        [Fact]
        public void FillColoursAreNeverTextColours()
        {
            // Accent / danger / success / warning as text or icon colour go through the *-text tokens, which .bv-glass
            // redefines to glass-safe values (spec §4.5).
            foreach (var rule in UssRules())
            {
                if (!rule.Value.TryGetValue("color", out string color)) continue;
                Assert.False(Regex.IsMatch(color, @"^var\(--color-(accent|accent-2|danger|success|warning)\)$"), rule.Key + " color: " + color);
            }
        }

        [Fact]
        public void EveryGlassTextTokenIsRedefinedAndKeepsAa()
        {
            var rules = UssRules().GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.Last().Value);
            foreach (bool dark in new[] { true, false })
            {
                string theme = dark ? ".bv-theme-dark" : ".bv-theme-light";
                var glass = rules[theme + " .bv-glass"];
                var values = GlassTextPalette.GlassTokens(dark);
                for (int i = 0; i < GlassTextPalette.TokenNames.Length; i++)
                {
                    string token = GlassTextPalette.TokenNames[i];
                    Assert.Equal(values[i], glass[token]);
                    // Every glass token also exists outside glass (the theme defines it).
                    Assert.True(rules[theme].ContainsKey(token), theme + " lacks " + token);
                }
                double target = GlassTextPalette.BackgroundTarget(dark);
                foreach (string hex in GlassTextPalette.Colors(dark))
                    Assert.True(GlassContrast.ContrastRatio(GlassTextPalette.Luminance(hex), target) >= GlassContrast.MinTextContrast - 1e-9, hex);
            }
            // Every text-colour token used anywhere in the styles is one the glass palette covers (or primary / on-accent).
            var covered = new HashSet<string>(GlassTextPalette.TokenNames) { "--color-text", "--color-on-accent" };
            foreach (var rule in UssRules())
            {
                if (!rule.Value.TryGetValue("color", out string color)) continue;
                var m = Regex.Match(color, @"^var\((--[\w-]+)\)$");
                if (m.Success) Assert.Contains(m.Groups[1].Value, covered);
            }
            // The weakest colour (the one the tint protects) is unchanged by the added tokens.
            Assert.Equal(GlassTextPalette.Luminance(GlassTextPalette.DarkTertiary), GlassTextPalette.WeakestLuminance(true));
            Assert.Equal(GlassTextPalette.Luminance(GlassTextPalette.LightTertiary), GlassTextPalette.WeakestLuminance(false));
        }

        [Fact]
        public void GlassTabSelectionHasNoTranslucentFillUnderItsLabel()
        {
            var rules = UssRules();
            foreach (string sel in new[] { ".bv-tabbar__item--selected", ".bv-tool-tab--selected" })
                Assert.DoesNotContain(rules, r => r.Key == sel && r.Value.ContainsKey("background-color"));
            Assert.Contains(rules, r => r.Key == ".bv-tabbar__item--selected .bv-tabbar__label" && r.Value.ContainsKey("border-bottom-color"));
            Assert.Contains(rules, r => r.Key == ".bv-tool-tab--selected .bv-tool-tab__label" && r.Value.ContainsKey("border-bottom-color"));
        }

        [Fact]
        public void ScreenReaderReadsAfterLayout()
        {
            string shell = Strip(File.ReadAllText(Ui("Runtime", "Shell", "UiShell.cs")));
            Assert.DoesNotContain("_screenReader.Update", Body(shell, "public void Update(float deltaTime)"));
            Assert.Contains("_screenReader.Update(Time.unscaledDeltaTime)", Body(shell, "public void LateUpdate()"));
            string sr = Strip(File.ReadAllText(Ui("Runtime", "Accessibility", "ScreenReader.cs")));
            Assert.Contains("_sync.Decide(ready, !SameAsPublished())", sr);
            Assert.DoesNotContain("opacity", sr);
            Assert.Contains("ready = false;", Body(sr, "void Collect(VisualElement element, ref bool ready)"));
        }
    }
}
