using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// Release 1 ships only M01, M02, M05 (<see cref="ModeCatalog.Released"/>), all free by default (owner decision Р18, spec
    /// §5.0.2); the free set is an extension of M01, M02, M05 (spec §5.1, §9.3).
    /// </summary>
    public class Release1ModeAvailabilityTests
    {
        static readonly MonetizationConfig R1 = MonetizationConfig.Release1;
        static readonly ModeCode[] Shipped = { ModeCode.M01, ModeCode.M02, ModeCode.M05 };

        [Fact]
        public void OnlyM01M02M05ArePlayable_ForAnyone()
        {
            var everything = Entitlements.From(new[] { ProductIds.Ultimate }, MonetizationConfig.FullCatalog);
            var allGrandfathered = Enumerable.Range(1, 15).Select(m => (ModeCode)m).ToArray();
            foreach (var e in new[] { EntitlementSet.Empty, everything })
            {
                foreach (var g in new[] { null, allGrandfathered })
                {
                    for (int m = 1; m <= 15; m++)
                    {
                        var mode = (ModeCode)m;
                        var access = ModeAvailability.GetAccess(mode, e, g, R1);
                        Assert.Equal(Shipped.Contains(mode) ? ModeAccess.Free : ModeAccess.NotReleased, access);
                        Assert.Equal(Shipped.Contains(mode), ModeAvailability.IsAvailable(mode, e, g, R1));
                    }
                    Assert.Equal(Shipped, ModeAvailability.AvailableModes(e, g, R1).ToArray());
                }
            }
        }

        [Fact]
        public void UnreleasedModesAreNotGrandfathered()
        {
            var g = ModeAvailability.RecordUse(null, ModeCode.M03, R1);
            Assert.Empty(g);
            g = ModeAvailability.RecordUse(g, ModeCode.M02, R1);
            Assert.Equal(new[] { ModeCode.M02 }, g.ToArray());
            // A widened free set with an unreleased mode does not grandfather it either.
            var widened = R1.WithFreeModes(new[] { ModeCode.M09 });
            Assert.Equal(new[] { ModeCode.M02 }, ModeAvailability.RecordUse(g, ModeCode.M09, widened).ToArray());
            Assert.Equal(ModeAccess.NotReleased, ModeAvailability.GetAccess(ModeCode.M09, EntitlementSet.Empty, null, widened));
        }

        [Fact]
        public void FreeSetCannotNarrowBelowDefaultFreeModes_EvenInRelease1()
        {
            var c = R1.WithFreeModes(new ModeCode[0]);
            foreach (var m in Shipped) Assert.Equal(ModeAccess.Free, ModeAvailability.GetAccess(m, EntitlementSet.Empty, null, c));
        }

        [Fact]
        public void LaterReleaseOpensModesWithoutBreakingGrandfathering()
        {
            // Release 1 user played M02; a later full release keeps M01, M02, M05 free and starts selling M06–M15.
            var g = ModeAvailability.RecordUse(null, ModeCode.M02, R1);
            var full = MonetizationConfig.FullCatalog;
            Assert.Equal(ModeAccess.Free, ModeAvailability.GetAccess(ModeCode.M02, EntitlementSet.Empty, g, full));
            Assert.Equal(ModeAccess.Locked, ModeAvailability.GetAccess(ModeCode.M06, EntitlementSet.Empty, g, full));
            // M03/M04 are free only when the owner lists them in free_modes at their release (spec §5.0.2, question В25).
            Assert.Equal(ModeAccess.Locked, ModeAvailability.GetAccess(ModeCode.M03, EntitlementSet.Empty, g, full));
            var withM03 = full.WithFreeModes(new[] { ModeCode.M03, ModeCode.M04 });
            Assert.Equal(ModeAccess.Free, ModeAvailability.GetAccess(ModeCode.M03, EntitlementSet.Empty, g, withM03));
            Assert.Equal(ModeAccess.Free, ModeAvailability.GetAccess(ModeCode.M04, EntitlementSet.Empty, g, withM03));
        }

        /// <summary>
        /// QA probe P1: releasing M03 in a later build does not make it free unless free_modes lists it (spec §5.0.2: "M03 и
        /// M04 станут бесплатными через free_modes при своём выпуске ..., а не по умолчанию в коде").
        /// </summary>
        [Fact]
        public void ReleasingM03DoesNotMakeItFreeByDefault()
        {
            var released = Shipped.Concat(new[] { ModeCode.M03 }).ToArray();
            var c = new MonetizationConfig(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M05 }, R1.SellableProductIds, released);
            Assert.True(c.IsReleased(ModeCode.M03));
            Assert.False(c.IsFree(ModeCode.M03));
            Assert.Equal(ModeAccess.Locked, ModeAvailability.GetAccess(ModeCode.M03, EntitlementSet.Empty, null, c));
            Assert.Equal(Shipped, R1.FreeModes.ToArray());
        }

        [Fact]
        public void OutOfRangeModesAreNotReleased()
        {
            Assert.Equal(ModeAccess.NotReleased, ModeAvailability.GetAccess((ModeCode)16, EntitlementSet.Empty, null, R1));
            Assert.Equal(ModeAccess.NotReleased, ModeAvailability.GetAccess((ModeCode)0, EntitlementSet.Empty, null, MonetizationConfig.FullCatalog));
            Assert.False(ModeAvailability.IsAvailable((ModeCode)16, EntitlementSet.Empty, null, MonetizationConfig.FullCatalog));
        }

        [Fact]
        public void FullCatalog_EveryModeReleased_PreviousBehaviourUnchanged()
        {
            for (int m = 1; m <= 15; m++)
            {
                Assert.NotEqual(ModeAccess.NotReleased, ModeAvailability.GetAccess((ModeCode)m, EntitlementSet.Empty, null, MonetizationConfig.FullCatalog));
            }
        }
    }
}
