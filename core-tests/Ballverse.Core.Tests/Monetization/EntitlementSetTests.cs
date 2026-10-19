using System;
using Ballverse.Core;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    public class EntitlementSetTests
    {
        [Fact]
        public void SetAlgebra()
        {
            var a = EntitlementSet.Of(Entitlement.ModeM06, Entitlement.NoExportAds);
            var b = EntitlementSet.Of(Entitlement.NoExportAds, Entitlement.NoWatermark);
            Assert.Equal(EntitlementSet.Of(Entitlement.ModeM06, Entitlement.NoExportAds, Entitlement.NoWatermark), a.Union(b));
            Assert.Equal(EntitlementSet.Of(Entitlement.NoExportAds), a.Intersect(b));
            Assert.Equal(EntitlementSet.Of(Entitlement.ModeM06), a.Except(b));
            Assert.True(a.Union(b).IsSupersetOf(a));
            Assert.True(a.IsSubsetOf(a.Union(b)));
            Assert.False(a.IsSupersetOf(b));
            Assert.Equal(2, a.Count);
            Assert.Equal(1, a.ModeCount);
            Assert.True(EntitlementSet.Empty.IsEmpty);
            Assert.Equal(a, a.With(Entitlement.ModeM06));
            Assert.Equal(EntitlementSet.Of(Entitlement.NoExportAds), a.Without(Entitlement.ModeM06));
        }

        [Fact]
        public void IsImmutableValue()
        {
            var a = EntitlementSet.Of(Entitlement.ModeM06);
            var b = a.With(Entitlement.ModeM07);
            Assert.Equal(1, a.Count);
            Assert.Equal(2, b.Count);
            Assert.True(a != b);
            Assert.True(a == EntitlementSet.Of(Entitlement.ModeM06));
            Assert.Equal(a.GetHashCode(), EntitlementSet.Of(Entitlement.ModeM06).GetHashCode());
            Assert.True(a.Equals((object)EntitlementSet.Of(Entitlement.ModeM06)));
        }

        [Fact]
        public void ToArrayIsInEnumOrderRegardlessOfInsertionOrder()
        {
            var s = EntitlementSet.Of(Entitlement.NoWatermark, Entitlement.ModeM15, Entitlement.ModeM06);
            Assert.Equal(new[] { Entitlement.ModeM06, Entitlement.ModeM15, Entitlement.NoWatermark }, s.ToArray());
            Assert.Equal("MODE_M06, MODE_M15, NO_WATERMARK", s.ToString());
            Assert.Equal("{}", EntitlementSet.Empty.ToString());
        }

        [Fact]
        public void ContainsModeMapsModeCodes()
        {
            var s = EntitlementSet.Of(Entitlement.ModeM09);
            Assert.True(s.ContainsMode(ModeCode.M09));
            Assert.False(s.ContainsMode(ModeCode.M10));
            Assert.False(EntitlementSet.AllDefined.ContainsMode(ModeCode.M01));
        }

        [Fact]
        public void UndefinedEntitlementThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EntitlementSet.Of((Entitlement)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => EntitlementSet.Empty.Contains((Entitlement)5));
            Assert.Throws<ArgumentNullException>(() => EntitlementSet.Of((System.Collections.Generic.IEnumerable<Entitlement>)null));
        }

        [Fact]
        public void SpecNamesRoundTrip()
        {
            Assert.Equal(13, EntitlementInfo.All.Count);
            foreach (var e in EntitlementInfo.All)
            {
                Assert.True(EntitlementInfo.TryParseSpecName(EntitlementInfo.SpecName(e), out var back));
                Assert.Equal(e, back);
            }
            Assert.Equal("MODE_M06", EntitlementInfo.SpecName(Entitlement.ModeM06));
            Assert.Equal("NO_EXPORT_ADS", EntitlementInfo.SpecName(Entitlement.NoExportAds));
            Assert.Equal("NO_INTERSTITIALS", EntitlementInfo.SpecName(Entitlement.NoInterstitials));
            Assert.Equal("NO_WATERMARK", EntitlementInfo.SpecName(Entitlement.NoWatermark));
            Assert.False(EntitlementInfo.TryParseSpecName("MODE_M01", out _));
            Assert.False(EntitlementInfo.TryParseSpecName(null, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => EntitlementInfo.SpecName((Entitlement)3));
        }

        [Fact]
        public void ModeEntitlementMapping()
        {
            for (int m = 1; m <= 15; m++)
            {
                bool has = EntitlementInfo.TryGetModeEntitlement((ModeCode)m, out var e);
                Assert.Equal(m >= 6, has);
                if (has)
                {
                    Assert.True(EntitlementInfo.TryGetMode(e, out var back));
                    Assert.Equal((ModeCode)m, back);
                }
            }
            Assert.False(EntitlementInfo.TryGetMode(Entitlement.NoWatermark, out _));
            Assert.Equal(10, EntitlementSet.PaidModes10.ModeCount);
        }
    }
}
