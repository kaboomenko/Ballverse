using System;
using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Config;
using Xunit;
using static Ballverse.Core.Tests.Config.ConfigTestUtil;

namespace Ballverse.Core.Tests.Config
{
    /// <summary>Strict builder and the immutable value types.</summary>
    public class RemoteConfigBuilderTests
    {
        [Fact]
        public void DefaultBuilderEqualsDefaults()
        {
            Assert.Equal(RawDictionary(RemoteConfigValues.Defaults), RawDictionary(new RemoteConfigBuilder().Build()));
        }

        [Fact]
        public void CopyBuilderRoundTrips()
        {
            var parsed = Parse(
                (RemoteConfigKeys.TierOverrides, "{\"X\":\"A\"}"),
                (RemoteConfigKeys.GenerationWeights, "{\"M02\":[1,1,1,1,1]}"),
                (RemoteConfigKeys.RemotePresets, "[{\"a\":1}]"),
                (RemoteConfigKeys.RemoteMelodies, "[{\"id\":\"m\",\"name\":\"M\",\"notes\":[{\"pitch\":1,\"durWeight\":1,\"velocity\":1}]}]")).Values;
            var copy = new RemoteConfigBuilder(parsed).Build();
            Assert.Equal(RawDictionary(parsed), RawDictionary(copy));
            Assert.Throws<ArgumentNullException>(() => new RemoteConfigBuilder(null));
        }

        [Fact]
        public void BuildNormalizesModeListsAndSortsMaps()
        {
            var b = new RemoteConfigBuilder
            {
                FreeModes = new List<ModeCode> { ModeCode.M03, ModeCode.M01, ModeCode.M03 },
                GalleryOrder = new List<ModeCode> { ModeCode.M15, ModeCode.M15 },
                TierOverrides = new List<TierOverride> { new TierOverride("b", DeviceTier.B), new TierOverride("A", DeviceTier.A) },
                RemotePresets = new List<string> { " { \"k\" : [1, 2] } " },
            };
            b.GenerationWeights[ModeCode.M09] = new GenerationWeights(1, 1, 1, 1, 1);
            b.GenerationWeights[ModeCode.M02] = new GenerationWeights(2, 2, 2, 2, 2);
            var v = b.Build();
            Assert.Equal(Codes(3, 1, 2, 4, 5), v.FreeModes);
            Assert.Equal(ModeCode.M15, v.GalleryOrder[0]);
            Assert.Equal(15, v.GalleryOrder.Count);
            Assert.Equal(new[] { "A", "b" }, v.TierOverrides.Select(o => o.DeviceModel));
            Assert.Equal(new[] { ModeCode.M02, ModeCode.M09 }, v.GenerationWeights.Select(kv => kv.Key));
            Assert.Equal("{\"k\":[1,2]}", v.RemotePresets[0]);
        }

        [Fact]
        public void BuiltSnapshotIsIsolatedFromBuilderAndReadOnly()
        {
            var b = new RemoteConfigBuilder();
            var v = b.Build();
            b.FreeModes.Add(ModeCode.M10);
            b.TextTemplates.Clear();
            Assert.Equal(5, v.FreeModes.Count);
            Assert.Equal(2, v.TextTemplates.Count);
            Assert.True(((ICollection<ModeCode>)v.FreeModes).IsReadOnly);
            Assert.True(((ICollection<ModeCode>)v.GalleryOrder).IsReadOnly);
            Assert.True(((ICollection<TextTemplateSet>)v.TextTemplates).IsReadOnly);
            Assert.True(((ICollection<NormalizedRect>)v.PlatformSafeZones[0].Rects).IsReadOnly);
            Assert.Throws<NotSupportedException>(() => ((IList<ModeCode>)v.EffectiveFreeModes).Add(ModeCode.M09));
        }

        public static IEnumerable<object[]> BadBuilders()
        {
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.InterstitialEveryNGames = 0) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.InterstitialEveryNGames = 21) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.InterstitialMinIntervalSec = 601) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.InterstitialGraceGames = -1) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.RewardedWaitTimeoutSec = 0) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.RewardedRetryWindowMin = 61) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.PaidModeTrialSeconds = 9) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.RatePromptAfterExports = 21) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.ExportDefaultFpsByTier = new TierValues<int>(60, 50, 30)) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.ExportBitrates = null) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.GenerationBudgetByTier = new TierValues<GenerationBudgetConfig>(null, null, null)) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.FreeModes = null) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.FreeModes.Add((ModeCode)16)) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.ModesDisabled.Add((ModeCode)0)) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.GenerationWeights[(ModeCode)99] = new GenerationWeights(1, 1, 1, 1, 1)) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.GenerationWeights[ModeCode.M01] = null) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.TierOverrides.AddRange(new[] { new TierOverride("x", DeviceTier.A), new TierOverride("X", DeviceTier.B) })) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.PlatformSafeZones.Add(new PlatformSafeZone("TIKTOK", new NormalizedRect[0]))) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.TextTemplates.Add(null)) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.RemotePresets.Add("[1]")) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.RemotePresets.Add("{bad")) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.RemotePresets.Add(null)) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.RemoteMelodies.AddRange(new[] { Melody("m"), Melody("m") })) };
            yield return new object[] { (Action<RemoteConfigBuilder>)(b => b.RemoteMelodies.Add(null)) };
        }

        static RemoteMelody Melody(string id)
        {
            return new RemoteMelody(id, "Name", new[] { new RemoteMelodyNote(60, 1f, 1f) });
        }

        [Theory]
        [MemberData(nameof(BadBuilders))]
        public void BuildRejectsInvalidValues(Action<RemoteConfigBuilder> corrupt)
        {
            var b = new RemoteConfigBuilder();
            corrupt(b);
            Assert.ThrowsAny<ArgumentException>(() => b.Build());
        }

        [Fact]
        public void ValueTypeConstructorsValidate()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExportBitrates(999, 1000, 1000, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExportBitrates(1000, 1000, 1000, 50001));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExportBitrates.Defaults.GetKbps(ExportResolution.P720, 24));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExportBitrates.Defaults.GetKbps((ExportResolution)480, 30));
            Assert.Equal("1080p60", ExportBitrates.KeyOf(ExportResolution.P1080, 60));

            Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationBudgetConfig(0, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationBudgetConfig(65, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationBudgetConfig(8, 99));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationBudgetConfig(8, 10001));

            Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationWeights(-1, 0, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationWeights(0, float.NaN, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationWeights(0, 0, 0, 0, float.PositiveInfinity));

            Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedRect(0.5f, 0, 0.6f, 0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedRect(0, 0, 0, 0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedRect(-0.1f, 0, 0.5f, 0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedRect(0, float.NaN, 0.5f, 0.1f));
            Assert.NotNull(new NormalizedRect(0, 0, 1, 1));

            Assert.Throws<ArgumentOutOfRangeException>(() => new RemoteMelodyNote(128, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RemoteMelodyNote(-1, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RemoteMelodyNote(60, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RemoteMelodyNote(60, float.PositiveInfinity, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RemoteMelodyNote(60, 1, 1.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RemoteMelodyNote(60, 1, float.NaN));

            Assert.Throws<ArgumentException>(() => new RemoteMelody(" ", "n", new[] { new RemoteMelodyNote(1, 1, 1) }));
            Assert.Throws<ArgumentException>(() => new RemoteMelody("i", "", new[] { new RemoteMelodyNote(1, 1, 1) }));
            Assert.Throws<ArgumentException>(() => new RemoteMelody("i", "n", new RemoteMelodyNote[0]));
            Assert.Throws<ArgumentException>(() => new RemoteMelody("i", "n", new RemoteMelodyNote[] { null }));
            Assert.Throws<ArgumentNullException>(() => new RemoteMelody("i", "n", null));

            Assert.Throws<ArgumentException>(() => new TextTemplate("", "t"));
            Assert.Throws<ArgumentException>(() => new TextTemplate("i", " "));
            Assert.Throws<ArgumentException>(() => new TextTemplateSet("en", new[] { new TextTemplate("a", "1"), new TextTemplate("a", "2") }));
            Assert.Throws<ArgumentException>(() => new TextTemplateSet(" ", new TextTemplate[0]));
            Assert.Throws<ArgumentException>(() => new TextTemplateSet("en", new TextTemplate[] { null }));

            Assert.Throws<ArgumentException>(() => new TierOverride("", DeviceTier.A));
            Assert.Throws<ArgumentException>(() => new TierOverride("x", (DeviceTier)7));
            Assert.Throws<ArgumentException>(() => new PlatformSafeZone("", new NormalizedRect[0]));
            Assert.Throws<ArgumentException>(() => new PlatformSafeZone("p", new NormalizedRect[] { null }));
            Assert.Throws<ArgumentNullException>(() => new PlatformSafeZone("p", null));
        }

        [Fact]
        public void TierValuesGetAndWith()
        {
            var t = new TierValues<int>(1, 2, 3);
            Assert.Equal(1, t.Get(DeviceTier.A));
            Assert.Equal(2, t.Get(DeviceTier.B));
            Assert.Equal(3, t.Get(DeviceTier.C));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.Get((DeviceTier)5));
            var u = t.With(DeviceTier.B, 9);
            Assert.Equal(new[] { 1, 9, 3 }, new[] { u.A, u.B, u.C });
            Assert.Equal(2, t.B);
            Assert.Equal(7, t.With(DeviceTier.A, 7).A);
            Assert.Equal(7, t.With(DeviceTier.C, 7).C);
            Assert.Throws<ArgumentOutOfRangeException>(() => t.With((DeviceTier)5, 0));
        }

        [Fact]
        public void WriterRejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => RemoteConfigWriter.ToRaw(null));
        }
    }
}
