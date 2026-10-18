using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ballverse.Core.Params;
using Ballverse.UI.Localization;
using Xunit;

namespace Ballverse.UI.Tests
{
    public class StringTableTests
    {
        [Fact]
        public void ParsesStringsPluralsAndSkipsComments()
        {
            var t = StringTable.Parse("ru", "{\"_c\":\"x\",\"a.b\":\"Привет\",\"n\":{\"one\":\"{0} шар\",\"few\":\"{0} шара\",\"many\":\"{0} шаров\",\"other\":\"{0} шара\"}}");
            Assert.Equal(2, t.Count);
            Assert.True(t.TryGet("a.b", out var s));
            Assert.Equal("Привет", s);
            Assert.True(t.TryGetPlural("n", PluralCategory.Many, out var p));
            Assert.Equal("{0} шаров", p);
            Assert.False(t.Contains("_c"));
        }

        [Fact]
        public void BadEntriesAreReportedNotFatal()
        {
            var t = StringTable.Parse("en", "{\"ok\":\"fine\",\"num\":5,\"pl\":{\"one\":\"x\"},\"pl2\":{\"weird\":\"y\",\"other\":\"z\"}}");
            Assert.True(t.Contains("ok"));
            Assert.False(t.Contains("num"));
            Assert.False(t.Contains("pl"));
            Assert.False(t.Contains("pl2"));
            Assert.Equal(3, t.Problems.Count);
        }

        [Fact]
        public void MalformedJsonThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => StringTable.Parse("en", "{ not json"));
            Assert.Throws<FormatException>(() => StringTable.Parse("en", "[1,2]"));
        }
    }

    public class PluralRulesTests
    {
        [Theory]
        [InlineData(1, PluralCategory.One)]
        [InlineData(21, PluralCategory.One)]
        [InlineData(101, PluralCategory.One)]
        [InlineData(2, PluralCategory.Few)]
        [InlineData(24, PluralCategory.Few)]
        [InlineData(5, PluralCategory.Many)]
        [InlineData(11, PluralCategory.Many)]
        [InlineData(12, PluralCategory.Many)]
        [InlineData(14, PluralCategory.Many)]
        [InlineData(111, PluralCategory.Many)]
        [InlineData(0, PluralCategory.Many)]
        public void Russian(long n, PluralCategory expected)
        {
            Assert.Equal(expected, PluralRules.Select("ru", n));
        }

        [Fact]
        public void English()
        {
            Assert.Equal(PluralCategory.One, PluralRules.Select("en", 1));
            Assert.Equal(PluralCategory.Other, PluralRules.Select("en", 0));
            Assert.Equal(PluralCategory.Other, PluralRules.Select("en", 2));
        }
    }

    public class LanguageResolverTests
    {
        [Theory]
        [InlineData(LanguagePreference.System, "ru-RU", "ru")]
        [InlineData(LanguagePreference.System, "ru", "ru")]
        [InlineData(LanguagePreference.System, "de-DE", "en")]
        [InlineData(LanguagePreference.System, null, "en")]
        [InlineData(LanguagePreference.System, "Russian", "en")]
        [InlineData(LanguagePreference.English, "ru-RU", "en")]
        [InlineData(LanguagePreference.Russian, "en-US", "ru")]
        public void Resolves(LanguagePreference pref, string system, string expected)
        {
            Assert.Equal(expected, LanguageResolver.Resolve(pref, system));
        }
    }

    public class LocalizerTests
    {
        static Localizer Make(string lang)
        {
            return new Localizer(new DictTables(new Dictionary<string, string>
            {
                { "en", "{\"hello\":\"Hello {0}\",\"only.en\":\"English only\",\"balls\":{\"one\":\"{0} ball\",\"other\":\"{0} balls\"}}" },
                { "ru", "{\"hello\":\"Привет, {0}\",\"balls\":{\"one\":\"{0} шар\",\"few\":\"{0} шара\",\"many\":\"{0} шаров\",\"other\":\"{0} шара\"}}" },
            }), lang);
        }

        [Fact]
        public void FormatsAndFallsBackToEnglish()
        {
            var l = Make("ru");
            Assert.Equal("Привет, Мир", l.Format("hello", "Мир"));
            Assert.Equal("English only", l.Get("only.en"));
            Assert.Empty(l.MissingKeys);
        }

        [Fact]
        public void MissingKeysAreHumanizedAndRecorded()
        {
            var l = Make("en");
            Assert.Equal("Ball count", l.Get("params.ballCount"));
            Assert.Equal("Tense finale", l.Get("generate.style.tense_finale"));
            Assert.Contains("params.ballCount", l.MissingKeys);
        }

        [Fact]
        public void PluralsPerLanguage()
        {
            var l = Make("ru");
            Assert.Equal("1 шар", l.Plural("balls", 1));
            Assert.Equal("3 шара", l.Plural("balls", 3));
            Assert.Equal("5 шаров", l.Plural("balls", 5));
            l.SetLanguage("en");
            Assert.Equal("5 balls", l.Plural("balls", 5));
        }

        [Fact]
        public void LanguageSwitchRaisesEventOnce()
        {
            var l = Make("en");
            int raised = 0;
            l.LanguageChanged += () => raised++;
            Assert.True(l.SetLanguage("ru-RU"));
            Assert.False(l.SetLanguage("ru"));
            Assert.Equal(1, raised);
            Assert.Equal("ru", l.Language);
        }

        [Fact]
        public void ResolvesLocalizedText()
        {
            var l = Make("en");
            Assert.Equal("Hello Bob", l.Resolve(LocalizedText.FromKey("hello", "Bob")));
            Assert.Equal("#tag", l.Resolve(LocalizedText.FromLiteral("#tag")));
            Assert.Equal("2 balls", l.Resolve(LocalizedText.Plural("balls", 2)));
            Assert.Equal("", l.Resolve(default));
        }

        [Fact]
        public void BrokenPatternDoesNotThrow()
        {
            var l = new Localizer(new DictTables(new Dictionary<string, string> { { "en", "{\"bad\":\"{0\"}" } }), "en");
            Assert.Equal("{0", l.Format("bad", 1));
        }
    }

    /// <summary>The shipped tables: same keys in EN and RU, every key used by layouts and the common parameters present.</summary>
    public class ShippedTablesTests
    {
        static readonly Newtonsoft.Json.Linq.JObject En = Load("en");
        static readonly Newtonsoft.Json.Linq.JObject Ru = Load("ru");

        static Newtonsoft.Json.Linq.JObject Load(string lang)
        {
            return Newtonsoft.Json.Linq.JObject.Parse(new FileTables().LoadTableJson(lang));
        }

        [Fact]
        public void TablesParseWithoutProblemsAndHaveSameKeys()
        {
            var en = StringTable.Parse("en", new FileTables().LoadTableJson("en"));
            var ru = StringTable.Parse("ru", new FileTables().LoadTableJson("ru"));
            Assert.Empty(en.Problems);
            Assert.Empty(ru.Problems);
            Assert.Equal(en.Keys(), ru.Keys());
        }

        [Fact]
        public void EveryUxmlKeyExists()
        {
            var missing = new List<string>();
            foreach (var file in Directory.GetFiles(Path.Combine(Paths.UiRoot, "Resources"), "*.uxml", SearchOption.AllDirectories))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "text=\"#([^\"]+)\""))
                    if (En[m.Groups[1].Value] == null || Ru[m.Groups[1].Value] == null) missing.Add(Path.GetFileName(file) + ": " + m.Groups[1].Value);
            Assert.Empty(missing);
        }

        [Fact]
        public void UxmlHasNoLiteralTexts()
        {
            var literals = new List<string>();
            foreach (var file in Directory.GetFiles(Path.Combine(Paths.UiRoot, "Resources"), "*.uxml", SearchOption.AllDirectories))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "text=\"([^\"]*)\""))
                    if (m.Groups[1].Value.Length > 0 && m.Groups[1].Value[0] != '#') literals.Add(Path.GetFileName(file) + ": " + m.Groups[1].Value);
            Assert.Empty(literals);
        }

        [Fact]
        public void EveryCommonParameterLabelAndOptionExists()
        {
            var missing = new List<string>();
            foreach (var def in CommonParams.Schema.Params)
            {
                if (En[def.LabelKey] == null) missing.Add(def.LabelKey);
                if (def is OptionParam o)
                    foreach (string option in o.Options)
                        if (En[o.OptionLabelKey(option)] == null) missing.Add(o.OptionLabelKey(option));
            }
            Assert.Empty(missing);
        }

        [Fact]
        public void RussianPluralsHaveAllCategories()
        {
            foreach (var p in Ru.Properties())
            {
                if (p.Value.Type != Newtonsoft.Json.Linq.JTokenType.Object) continue;
                foreach (string c in new[] { "one", "few", "many", "other" }) Assert.NotNull(p.Value[c]);
            }
        }
    }
}
