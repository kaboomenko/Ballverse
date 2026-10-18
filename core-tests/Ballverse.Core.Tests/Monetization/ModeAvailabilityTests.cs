using System;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>Spec §5.1 "Права и бесплатные режимы": free by config, grandfathered, entitled.</summary>
    public class ModeAvailabilityTests
    {
        static readonly ModeCode[] None = new ModeCode[0];

        [Fact]
        public void DefaultFreeModesAreM01ToM05()
        {
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M03, ModeCode.M04, ModeCode.M05 }, MonetizationConfig.Default.FreeModes.ToArray());
            Assert.True(MonetizationConfig.Default.WatermarkByParts);
            for (int m = 1; m <= 15; m++)
            {
                var access = ModeAvailability.GetAccess((ModeCode)m, EntitlementSet.Empty, None);
                Assert.Equal(m <= 5 ? ModeAccess.Free : ModeAccess.Locked, access);
            }
        }

        [Fact]
        public void EntitlementUnlocksPaidMode()
        {
            var e = Entitlements.From(new[] { ProductIds.ModeOrbit });
            Assert.Equal(ModeAccess.Entitled, ModeAvailability.GetAccess(ModeCode.M14, e, None));
            Assert.False(ModeAvailability.IsAvailable(ModeCode.M13, e, None));
            Assert.True(ModeAvailability.IsAvailable(ModeCode.M14, e, null));
        }

        [Fact]
        public void WideningFreeSetOpensPaidMode()
        {
            var config = new MonetizationConfig(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M03, ModeCode.M04, ModeCode.M05, ModeCode.M06 });
            Assert.Equal(ModeAccess.Free, ModeAvailability.GetAccess(ModeCode.M06, EntitlementSet.Empty, None, config));
        }

        [Fact]
        public void RecordUseGrandfathersOnlyFreeModes()
        {
            var g = ModeAvailability.RecordUse(None, ModeCode.M03);
            Assert.Equal(new[] { ModeCode.M03 }, g.ToArray());
            g = ModeAvailability.RecordUse(g, ModeCode.M07); // paid: not grandfathered
            Assert.Equal(new[] { ModeCode.M03 }, g.ToArray());
            g = ModeAvailability.RecordUse(g, ModeCode.M01);
            g = ModeAvailability.RecordUse(g, ModeCode.M03);
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M03 }, g.ToArray());
            Assert.Equal(new[] { ModeCode.M02 }, ModeAvailability.RecordUse(null, ModeCode.M02).ToArray());
        }

        [Fact]
        public void RecordUseDoesNotMutateInputAndSortsDistinct()
        {
            var input = new[] { ModeCode.M05, ModeCode.M02, ModeCode.M05 };
            var g = ModeAvailability.RecordUse(input, ModeCode.M01);
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M05 }, g.ToArray());
            Assert.Equal(new[] { ModeCode.M05, ModeCode.M02, ModeCode.M05 }, input);
        }

        [Fact]
        public void NarrowingFreeSetKeepsGrandfatheredModes()
        {
            var wide = new MonetizationConfig(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M03, ModeCode.M04, ModeCode.M05, ModeCode.M06 });
            var grandfathered = ModeAvailability.RecordUse(None, ModeCode.M06, wide);
            grandfathered = ModeAvailability.RecordUse(grandfathered, ModeCode.M04, wide);

            var narrow = new MonetizationConfig(new[] { ModeCode.M01, ModeCode.M02 });
            Assert.Equal(ModeAccess.Grandfathered, ModeAvailability.GetAccess(ModeCode.M06, EntitlementSet.Empty, grandfathered, narrow));
            Assert.Equal(ModeAccess.Grandfathered, ModeAvailability.GetAccess(ModeCode.M04, EntitlementSet.Empty, grandfathered, narrow));
            Assert.Equal(ModeAccess.Locked, ModeAvailability.GetAccess(ModeCode.M05, EntitlementSet.Empty, grandfathered, narrow));
            Assert.Equal(ModeAccess.Locked, ModeAvailability.GetAccess(ModeCode.M07, EntitlementSet.Empty, grandfathered, narrow));

            // Using a mode under the narrow config never drops existing grandfathered entries.
            var after = ModeAvailability.RecordUse(grandfathered, ModeCode.M07, narrow);
            Assert.Equal(new[] { ModeCode.M04, ModeCode.M06 }, after.ToArray());

            Assert.Equal(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M04, ModeCode.M06 },
                ModeAvailability.AvailableModes(EntitlementSet.Empty, grandfathered, narrow).ToArray());
        }

        [Fact]
        public void GrandfatheringNeverRemovesModesUnderRandomConfigChanges()
        {
            var rng = Rng.FromSeed(42UL, 3UL);
            var grandfathered = (System.Collections.Generic.IReadOnlyList<ModeCode>)new ModeCode[0];
            var everFreeUsed = new System.Collections.Generic.HashSet<ModeCode>();
            for (int step = 0; step < 500; step++)
            {
                var free = Enumerable.Range(1, 15).Where(_ => rng.NextUInt(3) == 0).Select(m => (ModeCode)m).ToArray();
                var config = new MonetizationConfig(free);
                var used = (ModeCode)(1 + rng.NextUInt(15));
                if (config.IsFree(used)) everFreeUsed.Add(used);
                grandfathered = ModeAvailability.RecordUse(grandfathered, used, config);
                foreach (var m in everFreeUsed)
                {
                    Assert.True(ModeAvailability.IsAvailable(m, EntitlementSet.Empty, grandfathered, config));
                }
                Assert.Equal(everFreeUsed.OrderBy(m => m).ToArray(), grandfathered.ToArray());
            }
        }

        [Fact]
        public void ConfigFromRemoteParsesCodesAndIgnoresJunk()
        {
            var c = MonetizationConfig.FromRemote(new[] { "M02", "M01", "M16", "m03", null, "M01", "X" });
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M02 }, c.FreeModes.ToArray());
            Assert.Equal(MonetizationConfig.Default.FreeModes.ToArray(), MonetizationConfig.FromRemote(null).FreeModes.ToArray());
            Assert.Empty(MonetizationConfig.FromRemote(new string[0]).FreeModes);
            Assert.False(MonetizationConfig.FromRemote(null, false).WatermarkByParts);
        }

        [Fact]
        public void ConfigRejectsInvalidInput()
        {
            Assert.Throws<ArgumentNullException>(() => new MonetizationConfig(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MonetizationConfig(new[] { (ModeCode)0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MonetizationConfig(new[] { (ModeCode)16 }));
        }
    }
}
