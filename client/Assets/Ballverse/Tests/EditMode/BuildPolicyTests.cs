using Ballverse.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Ballverse.Tests
{
    /// <summary>Development/release decision and the release guard against Google's sample AdMob ids (spec §8.9).</summary>
    public sealed class BuildPolicyTests
    {
        [Test]
        public void DevelopmentDefaultsPerBuildKind()
        {
            BuildArgs none = BuildArgs.Parse(new string[0]);
            Assert.IsTrue(BuildPolicy.IsDevelopment(BuildKind.AndroidApk, none));
            Assert.IsFalse(BuildPolicy.IsDevelopment(BuildKind.AndroidAab, none));
            Assert.IsTrue(BuildPolicy.IsDevelopment(BuildKind.IosXcode, none));
            Assert.IsTrue(BuildPolicy.IsDevelopment(BuildKind.IosXcode, BuildArgs.Parse(new[] { "-iosProvisioningProfileType", "Development" })));
            Assert.IsFalse(BuildPolicy.IsDevelopment(BuildKind.IosXcode, BuildArgs.Parse(new[] { "-iosProvisioningProfileType", "Distribution" })));
            Assert.IsTrue(BuildPolicy.IsDevelopment(BuildKind.AndroidAab, BuildArgs.Parse(new[] { "-ballverseDevelopment", "true" })));
            Assert.IsFalse(BuildPolicy.IsDevelopment(BuildKind.AndroidApk, BuildArgs.Parse(new[] { "-ballverseDevelopment", "false" })));
        }

        [Test]
        public void ReleaseBuildsRefuseGoogleSampleAdIds()
        {
            var sample = ((string, string)?)(BuildPolicy.SampleAndroidAppId, BuildPolicy.SampleIosAppId);
            var real = ((string, string)?)("ca-app-pub-1234567890123456~1111111111", "ca-app-pub-1234567890123456~2222222222");

            StringAssert.Contains("sample id", BuildPolicy.ReleaseAdIdsProblem(BuildTarget.Android, false, false, sample));
            StringAssert.Contains("sample id", BuildPolicy.ReleaseAdIdsProblem(BuildTarget.iOS, false, false, sample));
            StringAssert.Contains("empty", BuildPolicy.ReleaseAdIdsProblem(BuildTarget.Android, false, false, ("", "x")));
            Assert.IsNull(BuildPolicy.ReleaseAdIdsProblem(BuildTarget.Android, false, false, real));
            Assert.IsNull(BuildPolicy.ReleaseAdIdsProblem(BuildTarget.Android, true, false, sample), "development builds may use test ads");
            Assert.IsNull(BuildPolicy.ReleaseAdIdsProblem(BuildTarget.iOS, false, true, sample), "explicitly allowed");
            Assert.IsNull(BuildPolicy.ReleaseAdIdsProblem(BuildTarget.Android, false, false, null), "plugin absent");
            // Only the target's own id matters.
            Assert.IsNull(BuildPolicy.ReleaseAdIdsProblem(BuildTarget.iOS, false, false, (BuildPolicy.SampleAndroidAppId, "ca-app-pub-1234567890123456~2222222222")));

            Assert.IsTrue(BuildPolicy.IsGoogleSampleId("ca-app-pub-3940256099942544/5224354917"));
            Assert.IsFalse(BuildPolicy.IsGoogleSampleId(null));
            Assert.AreEqual(true, BuildArgs.Parse(new[] { "-" + BuildPolicy.AllowTestAdsOption, "true" }).GetBool(BuildPolicy.AllowTestAdsOption));
        }
    }
}
