using System;
using Ballverse.Editor;
using Ballverse.Platform;
using NUnit.Framework;
using UnityEditor;

namespace Ballverse.Tests
{
    /// <summary>Build variants (spec §7.9) and the Store guard against Google's sample AdMob ids (spec §8.9).</summary>
    public sealed class BuildPolicyTests
    {
        static BuildArgs Variant(string value) => BuildArgs.Parse(new[] { "-" + BuildPolicy.VariantOption, value });

        [Test]
        public void VariantDefaultsPerBuildKind()
        {
            BuildArgs none = BuildArgs.Parse(new string[0]);
            Assert.AreEqual(BuildVariant.OwnerTest, BuildPolicy.ResolveVariant(BuildKind.AndroidApk, none), "the owner's phones get OwnerTest");
            Assert.AreEqual(BuildVariant.OwnerTest, BuildPolicy.ResolveVariant(BuildKind.IosXcode, none));
            Assert.AreEqual(BuildVariant.Store, BuildPolicy.ResolveVariant(BuildKind.AndroidAab, none));
            // The provisioning profile type does not change the variant (an App Store profile is used for OwnerTest TestFlight builds).
            Assert.AreEqual(BuildVariant.OwnerTest, BuildPolicy.ResolveVariant(BuildKind.IosXcode, BuildArgs.Parse(new[] { "-iosProvisioningProfileType", "Distribution" })));
            // GameCI passes "" for an unset customParameters value: absent.
            Assert.AreEqual(BuildVariant.OwnerTest, BuildPolicy.ResolveVariant(BuildKind.AndroidApk, Variant("")));
        }

        [Test]
        public void ExplicitVariantWinsAndUnknownValuesAreRejected()
        {
            Assert.AreEqual(BuildVariant.Dev, BuildPolicy.ResolveVariant(BuildKind.AndroidAab, Variant("Dev")));
            Assert.AreEqual(BuildVariant.Store, BuildPolicy.ResolveVariant(BuildKind.AndroidApk, Variant("store")));
            Assert.AreEqual(BuildVariant.OwnerTest, BuildPolicy.ResolveVariant(BuildKind.IosXcode, Variant(" OWNERTEST ")));
            Assert.Throws<ArgumentException>(() => BuildPolicy.ResolveVariant(BuildKind.AndroidApk, Variant("Release")));
            Assert.Throws<ArgumentException>(() => BuildPolicy.ResolveVariant(BuildKind.AndroidApk, Variant("Owner")));
        }

        [Test]
        public void VariantMapsToBuildOptionsAndExactlyOneSymbol()
        {
            Assert.AreEqual(BuildOptions.Development, BuildPolicy.OptionsFor(BuildVariant.Dev));
            Assert.AreEqual(BuildOptions.None, BuildPolicy.OptionsFor(BuildVariant.OwnerTest), "OwnerTest is release-optimised");
            Assert.AreEqual(BuildOptions.None, BuildPolicy.OptionsFor(BuildVariant.Store));

            CollectionAssert.AreEqual(new[] { "BALLVERSE_DEV" }, BuildPolicy.ScriptingDefinesFor(BuildVariant.Dev));
            CollectionAssert.AreEqual(new[] { "BALLVERSE_OWNERTEST" }, BuildPolicy.ScriptingDefinesFor(BuildVariant.OwnerTest));
            CollectionAssert.AreEqual(new[] { "BALLVERSE_STORE" }, BuildPolicy.ScriptingDefinesFor(BuildVariant.Store));

            Assert.IsTrue(BuildVariants.AllowsTestServices(BuildVariant.Dev));
            Assert.IsTrue(BuildVariants.AllowsTestServices(BuildVariant.OwnerTest));
            Assert.IsFalse(BuildVariants.AllowsTestServices(BuildVariant.Store));
            // The Editor (and the test runner) compiles without a variant symbol, which counts as Dev.
            Assert.AreEqual(BuildVariant.Dev, BuildVariants.Current);
            Assert.IsFalse(BuildVariants.TryParse(null, out _));
        }

        [Test]
        public void TestFlightArchiveWithAnAppStoreProfileIsOwnerTestAndKeepsTestAds()
        {
            // unity-ios.yml on a manual archive run with an App Store profile: GameCI's own arguments, then customParameters.
            BuildArgs args = BuildArgs.Parse(new[]
            {
                "-batchmode", "-quit", "-customBuildTarget", "iOS", "-customBuildPath", "/github/workspace/build/iOS/Ballverse",
                "-buildVersion", "0.1.7", "-androidVersionCode", "1007",
                "-ballverseVariant", "OwnerTest", "-iosTeamId", "ABCDE12345",
                "-iosProvisioningProfileId", "1111-AAAA", "-iosProvisioningProfileType", "Distribution",
            });
            BuildVariant variant = BuildPolicy.ResolveVariant(BuildKind.IosXcode, args);
            Assert.AreEqual(BuildVariant.OwnerTest, variant);
            Assert.AreEqual(BuildOptions.None, BuildPolicy.OptionsFor(variant));
            Assert.IsNull(BuildPolicy.AdIdsProblem(BuildTarget.iOS, variant, (BuildPolicy.SampleAndroidAppId, BuildPolicy.SampleIosAppId)),
                "the sample ids ProjectSetup fills in must not fail an OwnerTest build");
        }

        [Test]
        public void StoreBuildsRefuseGoogleSampleAdIds()
        {
            var sample = ((string, string)?)(BuildPolicy.SampleAndroidAppId, BuildPolicy.SampleIosAppId);
            var real = ((string, string)?)("ca-app-pub-1234567890123456~1111111111", "ca-app-pub-1234567890123456~2222222222");

            StringAssert.Contains("sample id", BuildPolicy.AdIdsProblem(BuildTarget.Android, BuildVariant.Store, sample));
            StringAssert.Contains("sample id", BuildPolicy.AdIdsProblem(BuildTarget.iOS, BuildVariant.Store, sample));
            StringAssert.Contains("empty", BuildPolicy.AdIdsProblem(BuildTarget.Android, BuildVariant.Store, ("", "x")));
            Assert.IsNull(BuildPolicy.AdIdsProblem(BuildTarget.Android, BuildVariant.Store, real));
            Assert.IsNull(BuildPolicy.AdIdsProblem(BuildTarget.Android, BuildVariant.OwnerTest, sample), "OwnerTest uses test ads by design");
            Assert.IsNull(BuildPolicy.AdIdsProblem(BuildTarget.iOS, BuildVariant.OwnerTest, sample), "OwnerTest TestFlight builds use test ads");
            Assert.IsNull(BuildPolicy.AdIdsProblem(BuildTarget.iOS, BuildVariant.Dev, sample));
            Assert.IsNull(BuildPolicy.AdIdsProblem(BuildTarget.Android, BuildVariant.Store, null), "plugin absent");
            // Only the target's own id matters.
            Assert.IsNull(BuildPolicy.AdIdsProblem(BuildTarget.iOS, BuildVariant.Store, (BuildPolicy.SampleAndroidAppId, "ca-app-pub-1234567890123456~2222222222")));

            Assert.IsTrue(BuildPolicy.IsGoogleSampleId("ca-app-pub-3940256099942544/5224354917"));
            Assert.IsFalse(BuildPolicy.IsGoogleSampleId(null));
        }
    }
}
