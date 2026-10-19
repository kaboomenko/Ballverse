using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Ballverse.Platform;
using Ballverse.UI.Accessibility;
using Ballverse.UI.Glass;
using Ballverse.UI.Layout;
using Xunit;

namespace Ballverse.UI.Tests
{
    /// <summary>Shared readers of the shipped sources for the QA round 3 checks.</summary>
    static class Sources
    {
        public static string Ui(params string[] parts) => Path.Combine(new[] { Paths.UiRoot }.Concat(parts).ToArray());

        public static string Read(params string[] parts) => File.ReadAllText(Ui(parts));

        public static string Strip(string src) => Regex.Replace(src, @"//.*?$|/\*.*?\*/", "", RegexOptions.Multiline | RegexOptions.Singleline);

        /// <summary>Body of a member at class-member indentation (8 spaces), found by its signature.</summary>
        public static string Body(string src, string signature)
        {
            var m = Regex.Match(src, Regex.Escape(signature) + @"\s*\{(.*?)\n        \}", RegexOptions.Singleline);
            Assert.True(m.Success, "missing " + signature);
            return m.Groups[1].Value;
        }

        /// <summary>(selector, declarations) of every USS rule, in file order.</summary>
        public static List<KeyValuePair<string, Dictionary<string, string>>> UssRules()
        {
            var rules = new List<KeyValuePair<string, Dictionary<string, string>>>();
            foreach (string file in Directory.GetFiles(Ui("Resources", "BallverseUI", "Styles"), "*.uss").OrderBy(f => f, StringComparer.Ordinal))
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

        /// <summary>Last declaration set of each selector (later rules win on equal specificity).</summary>
        public static Dictionary<string, Dictionary<string, string>> RulesBySelector() =>
            UssRules().GroupBy(r => r.Key).ToDictionary(g => g.Key, g =>
            {
                var merged = new Dictionary<string, string>();
                foreach (var r in g)
                foreach (var kv in r.Value)
                    merged[kv.Key] = kv.Value;
                return merged;
            });

        /// <summary>Parses "#RRGGBB" or "rgba(r, g, b, a)" into sRGB components 0–1 and alpha.</summary>
        public static bool TryColor(string value, out double r, out double g, out double b, out double a)
        {
            r = g = b = 0;
            a = 1;
            if (value == null) return false;
            var hex = Regex.Match(value, @"^#([0-9A-Fa-f]{6})$");
            if (hex.Success)
            {
                int v = int.Parse(hex.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                r = (v >> 16 & 0xFF) / 255.0;
                g = (v >> 8 & 0xFF) / 255.0;
                b = (v & 0xFF) / 255.0;
                return true;
            }
            var rgba = Regex.Match(value, @"^rgba\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d.]+)\s*\)$");
            if (!rgba.Success) return false;
            r = int.Parse(rgba.Groups[1].Value, CultureInfo.InvariantCulture) / 255.0;
            g = int.Parse(rgba.Groups[2].Value, CultureInfo.InvariantCulture) / 255.0;
            b = int.Parse(rgba.Groups[3].Value, CultureInfo.InvariantCulture) / 255.0;
            a = double.Parse(rgba.Groups[4].Value, CultureInfo.InvariantCulture);
            return true;
        }

        public static string Hex(double r, double g, double b) =>
            "#" + ((int)Math.Round(r * 255)).ToString("X2", CultureInfo.InvariantCulture)
                + ((int)Math.Round(g * 255)).ToString("X2", CultureInfo.InvariantCulture)
                + ((int)Math.Round(b * 255)).ToString("X2", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// QA round 3, major 1: text-span trim handles must be placed (the pill and its handles are in the track before
    /// <c>Place</c> runs), and minor 3: what a segment draws is clipped to its track while its handles are not.
    /// </summary>
    public class TimelineTextHandlesAndClippingTests
    {
        // QA probe layout: 390-unit reference width, host padding 8, badge column 24 → track x 32..382 (350 wide).
        const float TrackWidth = 350f;
        const float BandMin = -32f;
        const float BandMax = 358f;
        const float H = TimelineGeometry.HandleWidth;

        static TimelineGeometry Reel(double seconds)
        {
            var geo = new TimelineGeometry(20f, 0, 0);
            geo.SetViewWidth(TrackWidth);
            geo.SetTotal((long)(seconds * TimelineGeometry.MicrosPerSecond));
            geo.FitAll();
            return geo;
        }

        static float Pickable(float segX, float handleX)
        {
            float l = Math.Max(BandMin, segX + handleX);
            float r = Math.Min(BandMax, segX + handleX + H);
            return Math.Max(0f, r - l);
        }

        [Fact]
        public void TextPillsArePlacedAfterTheirHandlesAndTheTrack()
        {
            string view = Sources.Strip(Sources.Read("Runtime", "Screens", "Studio", "TimelineView.cs"));
            string text = Sources.Body(view, "void BuildTextTrack()");
            int addLeft = text.IndexOf("pill.Add(left)", StringComparison.Ordinal);
            int addRight = text.IndexOf("pill.Add(right)", StringComparison.Ordinal);
            int addPill = text.IndexOf("_textTrack.Add(pill)", StringComparison.Ordinal);
            int place = text.IndexOf("Place(pill, item.StartUs, item.EndUs - item.StartUs)", StringComparison.Ordinal);
            Assert.True(addLeft >= 0 && addRight >= 0 && addPill >= 0 && place >= 0, "BuildTextTrack calls missing");
            // QA round 3: Place ran first, so PlaceHandles found no handle and the USS offsets (centred, ±22) stayed.
            Assert.True(place > addLeft && place > addRight && place > addPill, "Place(pill, ...) must run after the handles and the pill are added");
            Assert.Single(Regex.Matches(text, @"\bPlace\(").Cast<Match>());

            string media = Sources.Body(view, "void BuildMediaTrack(VisualElement track, TimelineTrackData data, bool isSim)");
            int segAdd = media.IndexOf("track.Add(seg)", StringComparison.Ordinal);
            int segPlace = media.IndexOf("PlaceSegment(seg, isSim)", StringComparison.Ordinal);
            Assert.True(segAdd >= 0 && segPlace > segAdd && segPlace > media.IndexOf("seg.Add(right)", StringComparison.Ordinal));

            string music = Sources.Body(view, "void BuildMusicTrack()");
            Assert.True(music.IndexOf("Place(bar,", StringComparison.Ordinal) > music.IndexOf("_musicTrack.Add(bar)", StringComparison.Ordinal));
        }

        [Fact]
        public void SegmentsDrawThroughAClippedBodyAndAreDraggedByIt()
        {
            string view = Sources.Strip(Sources.Read("Runtime", "Screens", "Studio", "TimelineView.cs"));
            string text = Sources.Body(view, "void BuildTextTrack()");
            Assert.Contains("var body = Body(pill)", text);
            Assert.Contains("body.Add(label)", text);
            Assert.Contains("pill.pickingMode = PickingMode.Ignore", text);
            Assert.Contains("new BodyDrag(this, body, EditTarget.TextShift", text);
            string media = Sources.Body(view, "void BuildMediaTrack(VisualElement track, TimelineTrackData data, bool isSim)");
            Assert.Contains("var body = Body(seg)", media);
            Assert.Contains("body.Add(thumbs)", media);
            Assert.Contains("seg.pickingMode = PickingMode.Ignore", media);
            Assert.Contains("new BodyDrag(this, body, EditTarget.VideoShift", media);
            string place = Sources.Body(view, "void Place(VisualElement element, long startUs, long lengthUs)");
            Assert.Contains("TimelineGeometry.ClipToTrack(x, width, _geo.ViewWidth)", place);
            Assert.Contains("ClipBody(element, width, clip)", place);

            // Only the body clips; the segment, the pill and the handles do not (their handles reach outside).
            var rules = Sources.UssRules();
            Assert.Contains(rules, r => r.Key == ".bv-timeline__body" && r.Value.TryGetValue("overflow", out var o) && o == "hidden");
            foreach (var rule in rules)
            {
                if (!rule.Value.TryGetValue("overflow", out string overflow) || overflow == "visible") continue;
                foreach (string c in new[] { "bv-timeline__segment", "bv-timeline__text", "bv-timeline__handle", "bv-timeline__track" })
                    Assert.False(Regex.IsMatch(rule.Key, @"\." + Regex.Escape(c) + @"(?![\w-])"), rule.Key + " clips (" + c + ")");
            }
            // The visuals moved to the body: the segment and the pill paint nothing themselves.
            foreach (string sel in new[] { ".bv-timeline__segment", ".bv-timeline__segment--sim", ".bv-timeline__segment--video", ".bv-timeline__text" })
            foreach (var rule in rules.Where(r => r.Key == sel))
            {
                Assert.False(rule.Value.ContainsKey("background-color"), sel + " paints a fill");
                Assert.False(rule.Value.ContainsKey("border-width"), sel + " paints a border");
            }
        }

        [Fact]
        public void AVideoClipBeforeSIsDrawnOnlyInsideTheTrack()
        {
            // QA probe: 30 s Auto reel, 20 s video at offset −10 s → segment −116.7..116.7, drawn 32 px over the badges.
            var geo = Reel(30);
            float x = geo.ToX(-10 * TimelineGeometry.MicrosPerSecond);
            float w = geo.Width(20 * TimelineGeometry.MicrosPerSecond);
            Assert.Equal(-116.67f, x, 1);
            var clip = TimelineGeometry.ClipToTrack(x, w, TrackWidth);
            Assert.True(clip.Visible);
            Assert.True(clip.CutStart);
            Assert.False(clip.CutEnd);
            Assert.Equal(116.67f, clip.BodyX, 1);
            Assert.Equal(116.67f, clip.BodyWidth, 1);
            Assert.Equal(0f, x + clip.BodyX, 3); // drawn from the track start, not from the badge column
            // The handles keep their 44 pt hit areas; the start grip moves onto the drawn part (the track start).
            var p = TimelineGeometry.PlaceHandles(x, w, BandMin, BandMax);
            Assert.Equal(H, Pickable(x, p.LeftX), 3);
            Assert.Equal(H, Pickable(x, p.RightX), 3);
            float grip = TimelineGeometry.GripInHandle(0f, p.LeftX, clip);
            Assert.InRange(grip, 0f, H);
            Assert.Equal(0f, x + p.LeftX + grip, 3);
            // The end edge is inside the track: its grip stays on the edge.
            Assert.Equal(p.RightEdgeInHandle, TimelineGeometry.GripInHandle(w, p.RightX, clip), 3);
        }

        [Fact]
        public void SegmentsPastTheReelEndStopAtTheTrackEnd()
        {
            // QA probe: V offset 25 s → 291.7..525.0, spilling 8 px over the right padding; a text span ending after the reel.
            var geo = Reel(30);
            float x = geo.ToX(25 * TimelineGeometry.MicrosPerSecond);
            float w = geo.Width(20 * TimelineGeometry.MicrosPerSecond);
            var clip = TimelineGeometry.ClipToTrack(x, w, TrackWidth);
            Assert.False(clip.CutStart);
            Assert.True(clip.CutEnd);
            Assert.Equal(0f, clip.BodyX, 3);
            Assert.Equal(TrackWidth, x + clip.BodyX + clip.BodyWidth, 2);
            var p = TimelineGeometry.PlaceHandles(x, w, BandMin, BandMax);
            Assert.Equal(H, Pickable(x, p.LeftX), 3);
            Assert.Equal(H, Pickable(x, p.RightX), 3);
            Assert.Equal(TrackWidth, x + p.RightX + TimelineGeometry.GripInHandle(w, p.RightX, clip), 2);

            var text = TimelineGeometry.ClipToTrack(geo.ToX(28 * TimelineGeometry.MicrosPerSecond), geo.Width(4 * TimelineGeometry.MicrosPerSecond), TrackWidth);
            Assert.True(text.CutEnd);
            Assert.Equal(TrackWidth, geo.ToX(28 * TimelineGeometry.MicrosPerSecond) + text.BodyX + text.BodyWidth, 2);
        }

        [Fact]
        public void ASegmentInsideTheTrackIsDrawnWhole()
        {
            var clip = TimelineGeometry.ClipToTrack(100f, 28f, TrackWidth);
            Assert.Equal(0f, clip.BodyX);
            Assert.Equal(28f, clip.BodyWidth);
            Assert.False(clip.CutStart);
            Assert.False(clip.CutEnd);
            var p = TimelineGeometry.PlaceHandles(100f, 28f, BandMin, BandMax);
            Assert.Equal(p.LeftEdgeInHandle, TimelineGeometry.GripInHandle(0f, p.LeftX, clip));
            Assert.Equal(p.RightEdgeInHandle, TimelineGeometry.GripInHandle(28f, p.RightX, clip));
            // The whole S clip of an Auto reel spans the track exactly: nothing is cut.
            var whole = TimelineGeometry.ClipToTrack(0f, TrackWidth, TrackWidth);
            Assert.False(whole.CutStart);
            Assert.False(whole.CutEnd);
            Assert.Equal(TrackWidth, whole.BodyWidth);
        }

        [Fact]
        public void ASegmentEntirelyOutsideTheTrackDrawsNothing()
        {
            Assert.False(TimelineGeometry.ClipToTrack(400f, 30f, TrackWidth).Visible);
            Assert.False(TimelineGeometry.ClipToTrack(-80f, 30f, TrackWidth).Visible);
            Assert.False(TimelineGeometry.ClipToTrack(10f, 30f, 0f).Visible);
        }

        [Theory]
        [InlineData(30, 10, 2)] // QA: 28 px pill, body was fully covered as built
        [InlineData(30, 10, 3)]
        [InlineData(30, 27, 3)] // QA: end handle only 30 pt pickable as built
        [InlineData(60, 57, 3)] // QA: end handle only 19.5 pt pickable as built
        public void PlacedTextHandlesKeepTheBodyFreeAndTheirFullHitArea(double reel, double start, double length)
        {
            var geo = Reel(reel);
            float x = geo.ToX((long)(start * TimelineGeometry.MicrosPerSecond));
            float w = Math.Max(28f, geo.Width((long)(length * TimelineGeometry.MicrosPerSecond)));
            var p = TimelineGeometry.PlaceHandles(x, w, BandMin, BandMax);
            Assert.Equal(H, Pickable(x, p.LeftX), 3);
            Assert.Equal(H, Pickable(x, p.RightX), 3);
            if (x + w < TrackWidth - 1f) Assert.True(p.ExposedBody >= TimelineGeometry.MinExposedBody - 1e-3f, "exposed " + p.ExposedBody);
            // The USS resting offsets (what the handles kept before the fix) would not have done this.
            Assert.NotEqual(-H / 2f, p.LeftX);
        }
    }

    /// <summary>
    /// QA round 3, major 2 (spec §4.5 "Текст на стекле — WCAG AA (4,5:1) в худшем случае"): control fills under labels
    /// on glass and the frosted fallback.
    /// </summary>
    public class GlassFillAndFrostedContrastTests
    {
        static readonly (string Selector, bool Dark, bool Contrast)[] GlassScopes =
        {
            (".bv-theme-dark .bv-glass", true, false),
            (".bv-theme-light .bv-glass", false, false),
            (".bv-theme-dark.bv-contrast .bv-glass", true, true),
            (".bv-theme-light.bv-contrast .bv-glass", false, true),
        };

        [Fact]
        public void ATranslucentFillOnGlassCannotKeepAa()
        {
            // Why fills on glass are opaque: the compose pass clamps the glass exactly to the bound, and UI Toolkit blends
            // a fill on top in linear space (QA probe: 12% white → white text 4.18, text-3 2.55).
            double bound = GlassTextPalette.BackgroundTarget(true);
            double lifted = GlassContrast.LinearBlendLuminance(bound, 1.0, 0.12);
            Assert.True(GlassContrast.ContrastRatio(GlassTextPalette.WeakestLuminance(true), lifted) < GlassContrast.MinTextContrast);
            Assert.True(GlassContrast.ContrastRatio(1.0, lifted) < GlassContrast.MinTextContrast);
            // Lowering the bound cannot fix it: 12% white alone already adds more than the whole bound.
            Assert.True(GlassContrast.LinearBlendLuminance(0.0, 1.0, 0.12) > bound);
            double lightBound = GlassTextPalette.BackgroundTarget(false);
            double darkened = GlassContrast.LinearBlendLuminance(lightBound, 0.0, 0.07);
            Assert.True(GlassContrast.ContrastRatio(GlassTextPalette.WeakestLuminance(false), darkened) < GlassContrast.MinTextContrast);
        }

        [Fact]
        public void ControlFillsInsideGlassAreOpaqueAndKeepAaWithEveryGlassTextColour()
        {
            var rules = Sources.RulesBySelector();
            foreach (var scope in GlassScopes)
            {
                Assert.True(rules.ContainsKey(scope.Selector), "missing " + scope.Selector);
                var glass = rules[scope.Selector];
                var fills = GlassTextPalette.GlassFills(scope.Dark, scope.Contrast);
                for (int i = 0; i < GlassTextPalette.FillTokenNames.Length; i++)
                {
                    string token = GlassTextPalette.FillTokenNames[i];
                    Assert.True(glass.TryGetValue(token, out string value), scope.Selector + " lacks " + token);
                    Assert.Equal(fills[i], value);
                    Assert.Matches("^#[0-9A-F]{6}$", value); // opaque: no backdrop shows through under the label
                    double fill = GlassTextPalette.Luminance(value);
                    foreach (string text in GlassTextPalette.Colors(scope.Dark))
                    {
                        double ratio = GlassContrast.ContrastRatio(GlassTextPalette.Luminance(text), fill);
                        Assert.True(ratio >= GlassContrast.MinTextContrast, scope.Selector + " " + token + " " + value + " vs " + text + ": " + ratio.ToString("F2", CultureInfo.InvariantCulture));
                    }
                }
            }
            // Every fill token the glass redefines is a translucent one outside glass (otherwise it would not need it) and
            // exists in the theme.
            foreach (string theme in new[] { ".bv-theme-dark", ".bv-theme-light" })
            foreach (string token in GlassTextPalette.FillTokenNames)
                Assert.True(rules[theme].ContainsKey(token), theme + " lacks " + token);
        }

        [Fact]
        public void AccentFillsUnderOnAccentLabelsKeepAa()
        {
            // Selected chips, primary buttons and accent badges are also drawn on glass: opaque accent + on-accent text.
            var rules = Sources.RulesBySelector();
            foreach (string theme in new[] { ".bv-theme-dark", ".bv-theme-light" })
            {
                var t = rules[theme];
                double onAccent = GlassTextPalette.Luminance(t["--color-on-accent"]);
                foreach (string fill in new[] { "--color-accent", "--color-accent-pressed" })
                {
                    Assert.Matches("^#[0-9A-Fa-f]{6}$", t[fill]);
                    Assert.True(GlassContrast.ContrastRatio(onAccent, GlassTextPalette.Luminance(t[fill].ToUpperInvariant())) >= GlassContrast.MinTextContrast, theme + " " + fill);
                }
            }
        }

        static IEnumerable<(string Theme, bool Dark, string Token, string Value)> GlassSurfaceFills()
        {
            var rules = Sources.RulesBySelector();
            foreach (var (theme, dark, contrast) in new[] { (".bv-theme-dark", true, false), (".bv-theme-light", false, false), (".bv-theme-dark.bv-contrast", true, true), (".bv-theme-light.bv-contrast", false, true) })
            {
                string baseTheme = dark ? ".bv-theme-dark" : ".bv-theme-light";
                foreach (string token in new[] { "--glass-frosted", "--glass-solid" })
                {
                    string value = contrast && rules[theme].TryGetValue(token, out var v) ? v : rules[baseTheme][token];
                    yield return (theme, dark, token, value);
                }
            }
        }

        [Fact]
        public void FrostedAndSolidGlassKeepAaOverTheWorstContent()
        {
            // QA probe: the frosted tab bar over white gallery content dropped to 2.68 (selected label) / 3.11 / 4.07.
            int checkedFills = 0;
            foreach (var (theme, dark, token, value) in GlassSurfaceFills())
            {
                Assert.True(Sources.TryColor(value, out double r, out double g, out double b, out double a), theme + " " + token + " = " + value);
                string hex = Sources.Hex(r, g, b);
                double min = GlassTextPalette.MinFrostedAlpha(dark, hex);
                Assert.True(a + 1e-9 >= min, theme + " " + token + " alpha " + a + " < " + min.ToString("F3", CultureInfo.InvariantCulture));
                // Worst content: white behind light text (dark theme), black behind dark text (light theme).
                double content = dark ? 1.0 : 0.0;
                double linear = GlassContrast.LinearBlendLuminance(content, GlassTextPalette.Luminance(hex), a);
                double gamma = GlassContrast.GammaBlendLuminance(content, content, content, r, g, b, a);
                foreach (string text in GlassTextPalette.Colors(dark))
                {
                    double t = GlassTextPalette.Luminance(text);
                    Assert.True(GlassContrast.ContrastRatio(t, linear) >= GlassContrast.MinTextContrast, theme + " " + token + " linear vs " + text);
                    Assert.True(GlassContrast.ContrastRatio(t, gamma) >= GlassContrast.MinTextContrast, theme + " " + token + " gamma vs " + text);
                }
                checkedFills++;
            }
            Assert.Equal(8, checkedFills);
            // The shipped dark frosted value before the fix would fail.
            Assert.True(GlassTextPalette.MinFrostedAlpha(true, "#161924") > 0.8);
        }

        [Fact]
        public void FrostedAlphaMathMatchesTheBound()
        {
            double a = GlassTextPalette.MinFrostedAlpha(true, "#161924");
            double lum = GlassContrast.LinearBlendLuminance(1.0, GlassTextPalette.Luminance("#161924"), a);
            Assert.Equal(GlassTextPalette.BackgroundTarget(true), lum, 9);
            double la = GlassTextPalette.MinFrostedAlpha(false, "#FAFAFC");
            Assert.Equal(GlassTextPalette.BackgroundTarget(false), GlassContrast.LinearBlendLuminance(0.0, GlassTextPalette.Luminance("#FAFAFC"), la), 9);
            // A fill lighter than the bound can never protect light text.
            Assert.True(double.IsPositiveInfinity(GlassTextPalette.MinFrostedAlpha(true, "#FFFFFF")));
            // Gamma blending of a dark fill over white is never lighter than linear blending (convex sRGB curve).
            for (double alpha = 0; alpha <= 1.0001; alpha += 0.05)
            {
                double lin = GlassContrast.LinearBlendLuminance(1.0, GlassTextPalette.Luminance("#161924"), alpha);
                double gam = GlassContrast.GammaBlendLuminance(1, 1, 1, 22 / 255.0, 25 / 255.0, 36 / 255.0, alpha);
                Assert.True(gam <= lin + 1e-9);
            }
        }

        /// <summary>
        /// Translucent fills that may stay translucent: none of them carries text on a glass backdrop. Every other
        /// translucent background must be a token that <c>.bv-glass</c> redefines as opaque.
        /// </summary>
        static readonly Dictionary<string, string> OffGlassTranslucentFills = new Dictionary<string, string>
        {
            { ".bv-icon-btn--on-media", "white icon over media (studio fullscreen preview)" },
            { ".bv-chip--on-media", "white label over media (studio fullscreen preview)" },
            { ".bv-badge", "gallery card badge over the preview; badges inside glass sheets are .bv-badge--accent (opaque)" },
            { ".bv-divider", "separator line, no text" },
            { ".bv-timeline__tick", "ruler tick, no text" },
            { ".bv-sheet__grabber", "grabber shape, no text" },
            { ".bv-timeline__text .bv-timeline__grip", "grip shape, no text" },
            { ".bv-dialog-scrim", "scrim behind dialogs, no text on it" },
            { ".bv-mode-card__caption", "caption over the gallery preview (media, not glass)" },
            { ".bv-run-thumb__label", "duration over a run thumbnail (media, not glass)" },
            { ".bv-project-card__duration", "duration over a project thumbnail (media, not glass)" },
            { ".bv-studio__add-video", "placeholder in the studio preview (not glass)" },
            { ".bv-timeline__segment--sim .bv-timeline__body", "timeline (solid page, not glass)" },
            { ".bv-timeline__segment--video .bv-timeline__body", "timeline (solid page, not glass)" },
            { ".bv-timeline__music", "timeline (solid page, not glass)" },
        };

        /// <summary>Surfaces that are the glass (or a frosted pill with the same token): checked by the frosted test.</summary>
        static readonly HashSet<string> GlassSurfaceTokens = new HashSet<string> { "--glass-frosted", "--glass-solid" };

        [Fact]
        public void EveryTranslucentFillIsGlassSafeOrDocumentedOffGlass()
        {
            var rules = Sources.UssRules();
            var translucentTokens = new HashSet<string>();
            foreach (var rule in rules)
            foreach (var kv in rule.Value)
                if (kv.Key.StartsWith("--", StringComparison.Ordinal) && Sources.TryColor(kv.Value, out _, out _, out _, out double a) && a > 0 && a < 1)
                    translucentTokens.Add(kv.Key);
            Assert.Contains("--color-control", translucentTokens);

            var redefined = new HashSet<string>(GlassTextPalette.FillTokenNames);
            var offGlassSeen = new HashSet<string>();
            foreach (var rule in rules)
            {
                if (!rule.Value.TryGetValue("background-color", out string bg)) continue;
                var token = Regex.Match(bg, @"^var\((--[\w-]+)\)$");
                bool translucent = token.Success
                    ? translucentTokens.Contains(token.Groups[1].Value)
                    : Sources.TryColor(bg, out _, out _, out _, out double a) && a > 0 && a < 1;
                if (!translucent) continue;
                if (token.Success && (redefined.Contains(token.Groups[1].Value) || GlassSurfaceTokens.Contains(token.Groups[1].Value))) continue;
                Assert.True(OffGlassTranslucentFills.ContainsKey(rule.Key), rule.Key + " has a translucent fill (" + bg + "): make it opaque on glass or document why it never carries text on glass");
                offGlassSeen.Add(rule.Key);
            }
            // The list stays current.
            foreach (string sel in OffGlassTranslucentFills.Keys) Assert.Contains(sel, offGlassSeen);

            // No element of an off-glass class sits inside a glass surface in the layouts.
            var offGlassClasses = OffGlassTranslucentFills.Keys
                .Where(k => k != ".bv-badge")
                .Select(k => Regex.Match(k, @"\.([\w-]+)$").Groups[1].Value)
                .Where(c => c.Length > 0).ToList();
            foreach (string file in Directory.GetFiles(Sources.Ui("Resources", "BallverseUI", "Screens"), "*.uxml"))
            {
                var doc = new XmlDocument();
                doc.Load(file);
                foreach (XmlElement e in doc.GetElementsByTagName("*").OfType<XmlElement>())
                {
                    var classes = (e.GetAttribute("class") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (string c in classes.Intersect(offGlassClasses))
                    {
                        for (var p = e.ParentNode as XmlElement; p != null; p = p.ParentNode as XmlElement)
                            Assert.DoesNotContain("bv-glass", (p.GetAttribute("class") ?? "").Split(' '));
                    }
                }
            }
        }
    }

    /// <summary>QA round 3, minor 4: the UI takes the device safe area from Ballverse.Platform instead of polling it.</summary>
    public class SafeAreaFromPlatformTests
    {
        [Fact]
        public void DeviceInsetsAreScaledToPanelUnits()
        {
            // iPhone 15: 1179×2556 px, 59 px on top (3×), 34 pt home indicator.
            var device = new SafeAreaInsets(1179, 2556, 0, 177, 0, 102);
            var i = PanelInsets.FromDevice(device, 393f);
            Assert.Equal(59f, i.Top, 2);
            Assert.Equal(34f, i.Bottom, 2);
            Assert.Equal(0f, i.Left, 2);
            Assert.Equal(0f, i.Right, 2);
            // Android landscape-style side insets (a punch hole on the left) scale the same way.
            var side = PanelInsets.FromDevice(new SafeAreaInsets(1080, 2400, 90, 0, 0, 0), 390f);
            Assert.Equal(32.5f, side.Left, 2);
        }

        [Fact]
        public void UnknownInsetsOrAnUnlaidPanelGiveZero()
        {
            Assert.Equal(default(PanelInsets), PanelInsets.FromDevice(default(SafeAreaInsets), 390f));
            var device = new SafeAreaInsets(1170, 2532, 0, 141, 0, 102);
            Assert.Equal(default(PanelInsets), PanelInsets.FromDevice(device, float.NaN));
            Assert.Equal(default(PanelInsets), PanelInsets.FromDevice(device, 0f));
            Assert.False(PanelInsets.IsUsablePanelWidth(float.NaN));
            Assert.False(PanelInsets.IsUsablePanelWidth(float.PositiveInfinity));
            Assert.False(PanelInsets.IsUsablePanelWidth(-1f));
            Assert.True(PanelInsets.IsUsablePanelWidth(390f));
        }

        [Fact]
        public void TheUiNeverReadsTheScreenSafeAreaItself()
        {
            foreach (string file in Directory.GetFiles(Sources.Ui("Runtime"), "*.cs", SearchOption.AllDirectories)
                         .Concat(Directory.GetFiles(Sources.Ui("Model"), "*.cs", SearchOption.AllDirectories)))
                Assert.DoesNotContain("Screen.safeArea", Sources.Strip(File.ReadAllText(file)));
            string theme = Sources.Strip(Sources.Read("Runtime", "Theming", "ThemeController.cs"));
            Assert.Contains("public SafeAreaController(VisualElement root, ISafeAreaProvider provider)", theme);
            Assert.Contains("_provider.Changed += OnDeviceInsetsChanged", theme);
            Assert.Contains("_provider.Changed -= OnDeviceInsetsChanged", Sources.Body(theme, "public void Dispose()"));
            Assert.Contains("PanelInsets.FromDevice(_device, panelWidth)", theme);
            string shell = Sources.Strip(Sources.Read("Runtime", "Shell", "UiShell.cs"));
            Assert.DoesNotContain("SafeArea.Refresh", shell);
            Assert.Contains("new SafeAreaController(appRoot, safeArea)", shell);
            Assert.Contains("_ctx.SafeArea.Dispose()", Sources.Body(shell, "public void Dispose()"));
            string root = Sources.Strip(Sources.Read("Runtime", "Shell", "BallverseUIRoot.cs"));
            Assert.Contains("ISafeAreaProvider safeArea", root);
            Assert.Contains("new UiShell(root, viewModels, safeArea)", root);
            // The assembly references the platform interfaces (engine-free, no cycle: Platform references nothing).
            Assert.Contains("\"Ballverse.Platform\"", Sources.Read("Ballverse.UI.asmdef"));
            Assert.Contains("\"Ballverse.Platform\"", Sources.Read("Fakes", "Ballverse.UI.Fakes.asmdef"));
            Assert.Contains("\"Ballverse.Platform\"", Sources.Read("Tests", "Editor", "Ballverse.UI.Tests.asmdef"));
            string platform = File.ReadAllText(Path.Combine(Paths.UiRoot, "..", "App", "Platform", "Ballverse.Platform.asmdef"));
            Assert.Contains("\"references\": []", platform);
        }
    }

    /// <summary>Spec §4.8 minimum: buttons and navigation announced by VoiceOver / TalkBack (Unity 6 Accessibility module).</summary>
    public class ScreenReaderModuleTests
    {
        [Fact]
        public void TheAccessibilityModuleIsInTheProjectAndEnablesTheBridge()
        {
            string manifest = File.ReadAllText(Path.Combine(Paths.UiRoot, "..", "..", "..", "Packages", "manifest.json"));
            Assert.Contains("\"com.unity.modules.accessibility\": \"1.0.0\"", manifest);
            string asmdef = Sources.Read("Ballverse.UI.asmdef");
            Assert.Matches(new Regex("\"name\":\\s*\"com\\.unity\\.modules\\.accessibility\",\\s*\"expression\":\\s*\"1\\.0\\.0\",\\s*\"define\":\\s*\"BALLVERSE_ACCESSIBILITY\""), asmdef);
            string sr = Sources.Strip(Sources.Read("Runtime", "Accessibility", "ScreenReader.cs"));
            Assert.Contains("#if BALLVERSE_ACCESSIBILITY", sr);
            Assert.Contains("node.role = AccessibilityRole.Button", sr);
            Assert.Contains("node.state = spec.State", sr);
            Assert.Contains("UpdateStates();", sr);
            Assert.Contains("A11yStates.IsSelected(element.GetClasses())", sr);
            Assert.Contains("AccessibilityState.Disabled", sr);
        }

        [Fact]
        public void SelectedItemsAreRecognised()
        {
            Assert.True(A11yStates.IsSelected(new[] { "bv-tabbar__item", "bv-tabbar__item--selected" }));
            Assert.True(A11yStates.IsSelected(new[] { "bv-chip", "bv-chip--selected" }));
            Assert.False(A11yStates.IsSelected(new[] { "bv-tabbar__item" }));
            Assert.False(A11yStates.IsSelected(new string[] { null, "bv-chip--selectedx" }));
            Assert.False(A11yStates.IsSelected(null));
        }

        [Fact]
        public void EverySelectionMarkerIsStyledAndUsed()
        {
            var selectors = Sources.UssRules().Select(r => r.Key).ToList();
            string runtime = string.Join("\n", Directory.GetFiles(Sources.Ui("Runtime"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
            foreach (string c in A11yStates.SelectedClasses)
            {
                Assert.Contains(selectors, s => Regex.IsMatch(s, @"\." + Regex.Escape(c) + @"(?![\w-])"));
                Assert.Contains("\"" + c + "\"", runtime);
            }
            // The tab bar items, the navigation of release 1, are tap targets (registered as screen-reader buttons).
            string nav = Sources.Strip(Sources.Read("Runtime", "Navigation", "UiNavigator.cs"));
            Assert.Contains("Tap.On(item, () => TabSelected?.Invoke(tab))", nav);
            Assert.Contains("A11y.Button(", Sources.Strip(Sources.Read("Runtime", "Components", "Tap.cs")));
        }
    }
}
