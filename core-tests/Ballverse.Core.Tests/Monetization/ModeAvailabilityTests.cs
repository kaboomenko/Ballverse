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
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M03, ModeCode.M04, ModeCode.M05 }, MonetizationConfig.FullCatalog.FreeModes.ToArray());
            Assert.True(MonetizationConfig.FullCatalog.WatermarkByParts);
            for (int m = 1; m <= 15; m++)
            {
                var access = ModeAvailability.GetAccess((ModeCode)m, EntitlementSet.Empty, None, MonetizationConfig.FullCatalog);
                Assert.Equal(m <= 5 ? ModeAccess.Free : ModeAccess.Locked, access);
            }
        }

        [Fact]
        public void EntitlementUnlocksPaidMode()
        {
            var e = Entitlements.From(new[] { ProductIds.ModeOrbit }, MonetizationConfig.FullCatalog, false);
            Assert.Equal(ModeAccess.Entitled, ModeAvailability.GetAccess(ModeCode.M14, e, None, MonetizationConfig.FullCatalog));
            Assert.False(ModeAvailability.IsAvailable(ModeCode.M13, e, None, MonetizationConfig.FullCatalog));
            Assert.True(ModeAvailability.IsAvailable(ModeCode.M14, e, null, MonetizationConfig.FullCatalog));
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
            var g = ModeAvailability.RecordUse(None, ModeCode.M03, MonetizationConfig.FullCatalog);
            Assert.Equal(new[] { ModeCode.M03 }, g.ToArray());
            g = ModeAvailability.RecordUse(g, ModeCode.M07, MonetizationConfig.FullCatalog); // paid: not grandfathered
            Assert.Equal(new[] { ModeCode.M03 }, g.ToArray());
            g = ModeAvailability.RecordUse(g, ModeCode.M01, MonetizationConfig.FullCatalog);
            g = ModeAvailability.RecordUse(g, ModeCode.M03, MonetizationConfig.FullCatalog);
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M03 }, g.ToArray());
            Assert.Equal(new[] { ModeCode.M02 }, ModeAvailability.RecordUse(null, ModeCode.M02, MonetizationConfig.FullCatalog).ToArray());
        }

        [Fact]
        public void RecordUseDoesNotMutateInputAndSortsDistinct()
        {
            var input = new[] { ModeCode.M05, ModeCode.M02, ModeCode.M05 };
            var g = ModeAvailability.RecordUse(input, ModeCode.M01, MonetizationConfig.FullCatalog);
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M05 }, g.ToArray());
            Assert.Equal(new[] { ModeCode.M05, ModeCode.M02, ModeCode.M05 }, input);
        }

        [Fact]
        public void NarrowingFreeSetKeepsGrandfatheredModes()
        {
            var wide = new MonetizationConfig(new[] { ModeCode.M06, ModeCode.M07 });
            var grandfathered = ModeAvailability.RecordUse(None, ModeCode.M06, wide);
            grandfathered = ModeAvailability.RecordUse(grandfathered, ModeCode.M04, wide);

            var narrow = new MonetizationConfig(None);
            Assert.Equal(ModeAccess.Grandfathered, ModeAvailability.GetAccess(ModeCode.M06, EntitlementSet.Empty, grandfathered, narrow));
            Assert.Equal(ModeAccess.Free, ModeAvailability.GetAccess(ModeCode.M04, EntitlementSet.Empty, grandfathered, narrow));
            Assert.Equal(ModeAccess.Locked, ModeAvailability.GetAccess(ModeCode.M07, EntitlementSet.Empty, grandfathered, narrow));

            // Using a mode under the narrow config never drops existing grandfathered entries.
            var after = ModeAvailability.RecordUse(grandfathered, ModeCode.M08, narrow);
            Assert.Equal(new[] { ModeCode.M04, ModeCode.M06 }, after.ToArray());

            Assert.Equal(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M03, ModeCode.M04, ModeCode.M05, ModeCode.M06 },
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

        /// <summary>Spec §5.1 "M01–M05 доступны всегда", §9.3 free_modes "только расширение".</summary>
        [Fact]
        public void ConfigCanOnlyWidenTheFreeSet()
        {
            var defaults = new[] { ModeCode.M01, ModeCode.M02, ModeCode.M03, ModeCode.M04, ModeCode.M05 };
            Assert.Equal(defaults, new MonetizationConfig(None).FreeModes.ToArray());
            Assert.Equal(defaults, new MonetizationConfig(new[] { ModeCode.M02, ModeCode.M01 }).FreeModes.ToArray());
            Assert.Equal(defaults.Concat(new[] { ModeCode.M09 }).ToArray(),
                new MonetizationConfig(new[] { ModeCode.M09, ModeCode.M03, ModeCode.M09 }).FreeModes.ToArray());
            Assert.False(new MonetizationConfig(None, false).WatermarkByParts);
        }

        /// <summary>
        /// Owner-level rule (spec §5.1, §9.3, §9.6): effective free set = FreeByDefault ∪ configured, for every one of the
        /// 2^10 configured subsets of M06…M15, each paired with a different subset of M01…M05 (all 32 occur).
        /// </summary>
        [Fact]
        public void FreeSetIsExactlyDefaultUnionConfigured_Exhaustive()
        {
            var defaults = ModeCatalog.All.Where(m => m.FreeByDefault).Select(m => m.Code).ToArray();
            for (int paid = 0; paid < 1 << 10; paid++)
            {
                int mask = (paid << 5) | (paid % 32);
                var configured = Enumerable.Range(1, 15).Where(m => (mask & (1 << (m - 1))) != 0).Select(m => (ModeCode)m).ToArray();
                var config = new MonetizationConfig(configured);
                var expected = defaults.Union(configured).OrderBy(m => m).ToArray();
                Assert.Equal(expected, config.FreeModes.ToArray());
                for (int m = 1; m <= 15; m++)
                    Assert.Equal(expected.Contains((ModeCode)m), config.IsFree((ModeCode)m));
            }
        }

        [Fact]
        public void EmptyFreeSetPayloadStillLeavesFreeModesForNewUser()
        {
            var config = new MonetizationConfig(None);
            Assert.Equal(5, ModeAvailability.AvailableModes(EntitlementSet.Empty, None, config).Count);
            for (int m = 1; m <= 5; m++)
                Assert.Equal(ModeAccess.Free, ModeAvailability.GetAccess((ModeCode)m, EntitlementSet.Empty, None, config));
        }

        [Fact]
        public void FreeModesIsReadOnly()
        {
            var config = new MonetizationConfig(new[] { ModeCode.M06 });
            Assert.False(config.FreeModes is ModeCode[]);
            Assert.True(((System.Collections.Generic.ICollection<ModeCode>)config.FreeModes).IsReadOnly);
            Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<ModeCode>)config.FreeModes)[0] = ModeCode.M15);
            Assert.True(config.IsFree(ModeCode.M01));
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
