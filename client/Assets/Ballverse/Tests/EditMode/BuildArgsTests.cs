using Ballverse.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Ballverse.Tests
{
    public sealed class BuildArgsTests
    {
        [Test]
        public void ParsesGameCiStyleArguments()
        {
            var args = BuildArgs.Parse(new[]
            {
                "/opt/unity/Editor/Unity", "-batchmode", "-quit",
                "-customBuildPath", "/github/workspace/build/Android/Ballverse.apk",
                "-buildVersion", "0.1.42",
                "-androidVersionCode", "1042",
                "-androidKeystoreName", "",
                "-androidKeystorePass", "",
                "-ballverseVariant", "OwnerTest",
                "-stackTraceLogType", "false",
                "-iosTeamId", "ABCDE12345",
                "-androidTargetSdkVersion", "-1",
            });

            Assert.AreEqual("/github/workspace/build/Android/Ballverse.apk", args.Get("customBuildPath"));
            Assert.AreEqual("0.1.42", args.Get("buildVersion"));
            Assert.AreEqual(1042, args.GetPositiveInt("androidVersionCode"));
            Assert.IsNull(args.Get("androidKeystoreName"), "empty values count as absent");
            Assert.IsTrue(args.Has("androidKeystoreName"));
            Assert.AreEqual(true, args.GetBool("batchmode"), "bare flag");
            Assert.AreEqual(false, args.GetBool("stackTraceLogType"));
            Assert.AreEqual("OwnerTest", args.Get("ballverseVariant"));
            Assert.AreEqual("ABCDE12345", args.Get("iosTeamId"));
            Assert.AreEqual("-1", args.Get("androidTargetSdkVersion"), "negative numbers are values, not options");
            Assert.IsNull(args.GetPositiveInt("androidTargetSdkVersion"));
            Assert.IsNull(args.GetBool("missing"));
        }

        [Test]
        public void PasswordsStartingWithADashStayValues()
        {
            // GameCI passes each secret as its own argv entry, so a value may start with '-'.
            var args = BuildArgs.Parse(new[]
            {
                "-androidKeystoreName", "k.keystore",
                "-androidKeystorePass", "-Secr3t",
                "-androidKeyaliasName", "upload",
                "-androidKeyaliasPass", "--x",
                "-quit",
            });
            Assert.AreEqual("-Secr3t", args.Get("androidKeystorePass"));
            Assert.AreEqual("upload", args.Get("androidKeyaliasName"));
            Assert.AreEqual("--x", args.Get("androidKeyaliasPass"));
            Assert.IsFalse(args.Has("Secr3t"), "the password is not parsed as an option");
            Assert.AreEqual(true, args.GetBool("quit"));

            var trailing = BuildArgs.Parse(new[] { "-androidKeystorePass" });
            Assert.IsTrue(trailing.Has("androidKeystorePass"));
            Assert.IsNull(trailing.Get("androidKeystorePass"));
            Assert.IsTrue(BuildArgs.IsValueOption("iosTeamId"));
            Assert.IsTrue(BuildArgs.IsValueOption("ballverseVariant"));
            Assert.IsFalse(BuildArgs.IsValueOption("batchmode"), "flags may be bare");
        }

        [Test]
        public void TargetSdkParsing()
        {
            Assert.AreEqual(AndroidSdkVersions.AndroidApiLevelAuto, BuildScript.ParseTargetSdk(null));
            Assert.AreEqual((AndroidSdkVersions)36, BuildScript.ParseTargetSdk("36"));
            Assert.AreEqual((AndroidSdkVersions)35, BuildScript.ParseTargetSdk("AndroidApiLevel35"));
            Assert.AreEqual(AndroidSdkVersions.AndroidApiLevelAuto, BuildScript.ParseTargetSdk("21"));
            Assert.AreEqual(AndroidSdkVersions.AndroidApiLevelAuto, BuildScript.ParseTargetSdk("latest"));
        }
    }
}
