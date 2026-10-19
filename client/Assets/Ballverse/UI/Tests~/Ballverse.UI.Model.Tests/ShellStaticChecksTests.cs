using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ballverse.UI.Format;
using Ballverse.UI.Glass;
using Ballverse.UI.Localization;
using Ballverse.UI.Theming;
using Xunit;

namespace Ballverse.UI.Tests
{
    /// <summary>Locale, fallbacks and string fixes from QA round 1.</summary>
    public class LocaleAndFallbackTests
    {
        [Fact]
        public void NumbersAndDatesFollowTheDeviceLocale()
        {
            var en = new Localizer(new FileTables(), "en");
            Assert.Equal("10/18/2026", DisplayFormat.ShortDate(new DateTime(2026, 10, 18), en));
            en.SetFormatCulture(LanguageResolver.DeviceFormatCulture("en-GB"));
            Assert.Equal("18/10/2026", DisplayFormat.ShortDate(new DateTime(2026, 10, 18), en));
            en.SetFormatCulture(LanguageResolver.DeviceFormatCulture("de_DE"));
            Assert.Equal("1,5 MB", DisplayFormat.Bytes(3L * 512 * 1024, en));
            Assert.Equal("~25 s left", DisplayFormat.Remaining(24_100, en));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not a locale !!")]
        public void UnknownDeviceLocaleFallsBackToTheLanguageCulture(string tag)
        {
            Assert.Null(LanguageResolver.DeviceFormatCulture(tag));
            var ru = new Localizer(new FileTables(), "ru");
            ru.SetFormatCulture(LanguageResolver.DeviceFormatCulture(tag));
            Assert.Equal("1,5 МБ", DisplayFormat.Bytes(3L * 512 * 1024, ru));
        }

        [Fact]
        public void WithoutNativeAppearanceTheDeviceLanguageIsUsed()
        {
            // Spec §4.7: device language by default (Unity Application.systemLanguage when the plugin is missing).
            var state = SystemAppearanceState.WithFallbacks(null, "ru", 1.2f);
            Assert.Equal("ru", ResolvedAppearance.Resolve(UiPreferences.Default, state).Language);
            Assert.Equal(TextScaleStep.Large, ResolvedAppearance.Resolve(UiPreferences.Default, state).TextScale);
            var silent = new SystemAppearanceState(true, false, false, 1f, null);
            Assert.Equal("ru", SystemAppearanceState.WithFallbacks(silent, "ru", 1f).LanguageTag);
            var reported = new SystemAppearanceState(true, false, false, 1.3f, "en-US");
            Assert.Same(reported, SystemAppearanceState.WithFallbacks(reported, "ru", 1f));
            Assert.Equal(1f, SystemAppearanceState.WithFallbacks(null, null, float.NaN).TextScale);
        }

        [Fact]
        public void ExportEstimateReadsAsOneSentence()
        {
            var en = new Localizer(new FileTables(), "en");
            var ru = new Localizer(new FileTables(), "ru");
            Assert.Equal("About 48 MB, ready in ~40 s",
                en.Format("export.estimate", DisplayFormat.Bytes(48L << 20, en), DisplayFormat.Duration(40_000, en)));
            Assert.Equal("Примерно 48 МБ, будет готово через ~40 с",
                ru.Format("export.estimate", DisplayFormat.Bytes(48L << 20, ru), DisplayFormat.Duration(40_000, ru)));
            Assert.Equal("~2 min", DisplayFormat.Duration(100_000, en));
        }

        [Fact]
        public void ReleaseOneModeNamesFollowTheSpec()
        {
            var ru = new Localizer(new FileTables(), "ru");
            Assert.Equal("Геометрия отскоков", ru.Get("mode.geometry_bounce.name"));
            Assert.Equal("Хеликс-дроп", ru.Get("mode.helix_drop.name"));
            Assert.Equal("Ring Escape", new Localizer(new FileTables(), "en").Get("mode.ring_escape.name"));
        }
    }

    /// <summary>
    /// Documented static checks for Unity-side fixes that need an editor to run (the EditMode tests under
    /// <c>UI/Tests/Editor</c> cover them in CI): they keep the fixed patterns from regressing.
    /// </summary>
    public class ShellStaticChecksTests
    {
        static string Ui(params string[] parts) => Path.Combine(new[] { Paths.UiRoot }.Concat(parts).ToArray());

        static IEnumerable<string> RuntimeSources() =>
            Directory.GetFiles(Ui("Runtime"), "*.cs", SearchOption.AllDirectories);

        static string Strip(string src) => Regex.Replace(src, @"//.*?$|/\*.*?\*/", "", RegexOptions.Multiline | RegexOptions.Singleline);

        [Fact]
        public void InstantiatedLayoutsFillTheirParentWithInlineStyles()
        {
            // Blocker 1: the app-shell container sits above the element that carries the global sheets, so a USS class
            // cannot size it. UiAssets.Instantiate must size it inline.
            string src = Strip(File.ReadAllText(Ui("Runtime", "Shell", "UiAssets.cs")));
            var body = Regex.Match(src, @"public static VisualElement Instantiate\(string name\)\s*\{(.*?)\n        \}", RegexOptions.Singleline);
            Assert.True(body.Success);
            Assert.Contains("FillParent(container)", body.Groups[1].Value);
            var fill = Regex.Match(src, @"public static void FillParent\(VisualElement element\)\s*\{(.*?)\n        \}", RegexOptions.Singleline).Groups[1].Value;
            foreach (string decl in new[] { "s.position = Position.Absolute", "s.left = 0", "s.top = 0", "s.right = 0", "s.bottom = 0" })
                Assert.Contains(decl, fill);
            string shell = Strip(File.ReadAllText(Ui("Runtime", "Shell", "UiShell.cs")));
            Assert.Contains("UiAssets.Instantiate(\"AppShell\")", shell);
        }

        [Fact]
        public void KeysAreBoundOnlyOnFreshLayouts()
        {
            // Blocker 2: Prepare runs on sheets and dialogs that already hold user content ("#satisfying").
            var allowed = new HashSet<string> { "UiContext.cs", "UiShell.cs", "LocalizedTreeBinder.cs" };
            foreach (string file in RuntimeSources())
            {
                string src = Strip(File.ReadAllText(file));
                if (Regex.IsMatch(src, @"Binder\.Bind\(|\.Bind\(root\)")) Assert.Contains(Path.GetFileName(file), allowed);
            }
            string ctx = Strip(File.ReadAllText(Ui("Runtime", "Shell", "UiContext.cs")));
            var prepare = Regex.Match(ctx, @"public void Prepare\(VisualElement root\)\s*\{(.*?)\n        \}", RegexOptions.Singleline);
            Assert.True(prepare.Success);
            Assert.DoesNotContain("Bind", prepare.Groups[1].Value);
            string binder = Strip(File.ReadAllText(Ui("Runtime", "Localization", "LocalizedTreeBinder.cs")));
            Assert.Contains("if (element is TextField) return;", binder);
        }

        [Fact]
        public void EveryKeyLiteralInRuntimeCodeExists()
        {
            var en = Newtonsoft.Json.Linq.JObject.Parse(new FileTables().LoadTableJson("en"));
            var ru = Newtonsoft.Json.Linq.JObject.Parse(new FileTables().LoadTableJson("ru"));
            var prefixes = new HashSet<string>(en.Properties().Select(p => p.Name.Split('.')[0]));
            var missing = new List<string>();
            foreach (string file in RuntimeSources())
            {
                foreach (Match m in Regex.Matches(Strip(File.ReadAllText(file)), "\"([a-z0-9_]+(?:\\.[a-zA-Z0-9_]+)+)\""))
                {
                    string key = m.Groups[1].Value;
                    if (!prefixes.Contains(key.Split('.')[0])) continue;
                    if (en[key] == null || ru[key] == null) missing.Add(Path.GetFileName(file) + ": " + key);
                }
            }
            Assert.Empty(missing);
        }

        [Fact]
        public void NoRoadmapTeasersOrSaleWording()
        {
            // Release-1 spec §4.3: no "coming soon" cards or roadmap teasers; spec §5.1: no sale wording or badges.
            foreach (string lang in new[] { "en", "ru" })
            {
                var table = Newtonsoft.Json.Linq.JObject.Parse(new FileTables().LoadTableJson(lang));
                Assert.Null(table["gallery.more_soon"]);
                foreach (var p in table.Properties())
                {
                    string v = p.Value.ToString().ToLowerInvariant();
                    foreach (string banned in new[] { @"скидк", @"\bакци", @"\bdiscount", @"\bsale\b", @"coming soon", @"\bскоро\b" })
                        Assert.False(Regex.IsMatch(v, banned), lang + " " + p.Name + " matches '" + banned + "'");
                }
                Assert.DoesNotContain("−", table["shop.savings"].ToString());
            }
            Assert.DoesNotContain("more_soon", File.ReadAllText(Ui("Resources", "BallverseUI", "Screens", "Gallery.uxml")));
            string shop = Strip(File.ReadAllText(Ui("Runtime", "Screens", "Shop", "ShopScreen.cs")));
            Assert.DoesNotContain("bv-badge", shop);
        }

        [Fact]
        public void TappableControlsHave44PointHitAreas()
        {
            // Spec §4.4: minimum touch zone 44 × 44 pt (panel units ≈ pt).
            var rules = UssRules();
            var checks = new Dictionary<string, string[]>
            {
                { ".bv-btn", new[] { "min-height" } },
                { ".bv-btn--small", new[] { "min-height" } },
                { ".bv-icon-btn", new[] { "width", "height" } },
                { ".bv-icon-btn--small", new[] { "width", "height" } },
                { ".bv-chip", new[] { "min-height", "min-width" } },
                { ".bv-segmented__item", new[] { "min-height" } },
                { ".bv-swatch", new[] { "width", "height" } },
                { ".bv-color-dot--removable", new[] { "width", "height" } },
                { ".bv-link", new[] { "min-height", "min-width" } },
                { ".bv-row", new[] { "min-height" } },
                { ".bv-slider", new[] { "height" } },
                { ".bv-timeline__row", new[] { "height" } },
                { ".bv-timeline__handle", new[] { "width" } },
                { ".bv-quest__header", new[] { "min-height" } },
            };
            foreach (var c in checks)
            {
                Assert.True(rules.ContainsKey(c.Key), "missing rule " + c.Key);
                foreach (string prop in c.Value)
                {
                    Assert.True(rules[c.Key].TryGetValue(prop, out string v), c.Key + " has no " + prop);
                    Assert.True(Px(v) >= 44f, c.Key + " " + prop + " = " + v);
                }
            }
            // Switch rows: the whole row toggles (the 52 × 32 switch alone is too small).
            string form = Strip(File.ReadAllText(Ui("Runtime", "Components", "FormBuilder.cs")));
            Assert.Contains("Tap.On(row, sw.Toggle, false)", form);
            string editors = Strip(File.ReadAllText(Ui("Runtime", "Screens", "Simulation", "ParamEditors.cs")));
            Assert.Contains("Tap.On(Root, _switch.Toggle, false)", editors);
        }

        static float Px(string value)
        {
            if (value.Trim() == "var(--touch)") return 44f;
            var m = Regex.Match(value, @"^(\d+(?:\.\d+)?)px$");
            return m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0f;
        }

        static Dictionary<string, Dictionary<string, string>> UssRules()
        {
            var rules = new Dictionary<string, Dictionary<string, string>>();
            foreach (string file in Directory.GetFiles(Ui("Resources", "BallverseUI", "Styles"), "*.uss"))
            {
                string src = Regex.Replace(File.ReadAllText(file), @"/\*.*?\*/", "", RegexOptions.Singleline);
                foreach (Match m in Regex.Matches(src, @"([^{}]+)\{([^{}]*)\}"))
                {
                    foreach (string sel in m.Groups[1].Value.Split(','))
                    {
                        string s = sel.Trim();
                        if (!rules.TryGetValue(s, out var props)) rules[s] = props = new Dictionary<string, string>();
                        foreach (string decl in m.Groups[2].Value.Split(';'))
                        {
                            int colon = decl.IndexOf(':');
                            if (colon > 0) props[decl.Substring(0, colon).Trim()] = decl.Substring(colon + 1).Trim();
                        }
                    }
                }
            }
            return rules;
        }

        [Fact]
        public void GlassTextColoursMatchThePaletteAndKeepAa()
        {
            // Spec §4.5: text on glass keeps 4.5:1 in the worst case. The tint is computed for the weakest glass colour,
            // so every colour used on glass must reach 4.5:1 against the tint target.
            var rules = UssRules();
            Assert.Equal(GlassTextPalette.DarkSecondary, rules[".bv-theme-dark .bv-glass"]["--color-text-2"]);
            Assert.Equal(GlassTextPalette.DarkTertiary, rules[".bv-theme-dark .bv-glass"]["--color-text-3"]);
            Assert.Equal(GlassTextPalette.LightSecondary, rules[".bv-theme-light .bv-glass"]["--color-text-2"]);
            Assert.Equal(GlassTextPalette.LightTertiary, rules[".bv-theme-light .bv-glass"]["--color-text-3"]);
            Assert.Equal(GlassTextPalette.DarkPrimary, rules[".bv-theme-dark"]["--color-text"]);
            Assert.Equal(GlassTextPalette.LightPrimary, rules[".bv-theme-light"]["--color-text"]);
            foreach (bool dark in new[] { true, false })
            {
                double target = GlassTextPalette.BackgroundTarget(dark);
                Assert.InRange(target, 0.0, 1.0);
                foreach (string hex in GlassTextPalette.Colors(dark))
                    Assert.True(GlassContrast.ContrastRatio(GlassTextPalette.Luminance(hex), target) >= GlassContrast.MinTextContrast - 1e-9, hex);
            }
            // The weakest dark-theme colour is stricter than white: the tint gets denser than before (target < 0.183).
            Assert.True(GlassTextPalette.BackgroundTarget(true) < (1.0 + 0.05) / 4.5 - 0.05);
        }

        [Fact]
        public void ScreenReaderLabelKeysExist()
        {
            string src = Strip(File.ReadAllText(Ui("Runtime", "Accessibility", "ScreenReader.cs")));
            var en = Newtonsoft.Json.Linq.JObject.Parse(new FileTables().LoadTableJson("en"));
            var keys = Regex.Matches(src, "\\{ \"[a-z-]+\", \"([a-z0-9_.]+)\" \\}").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.True(keys.Count >= 10);
            foreach (string k in keys) Assert.NotNull(en[k]);
        }
    }
}
