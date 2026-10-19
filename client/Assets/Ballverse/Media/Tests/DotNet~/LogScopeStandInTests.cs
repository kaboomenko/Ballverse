using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ballverse.Media.Tests
{
    /// <summary>
    /// Harness-only checks that the LogAssert stand-in (LogAssertStandIn.cs) is no more lenient than the real Unity Test Framework,
    /// so a test that passes here cannot fail in Unity on log handling. Rules mirrored from UTF LogScope (AddLog,
    /// ProcessExpectedLogs, EvaluateLogScope, NoUnexpectedReceived). Each test resets the scope before it ends, so the
    /// end-of-test check of the harness sees a clean scope.
    /// </summary>
    public sealed class LogScopeStandInTests
    {
        [TearDown]
        public void TearDown() => LogScope.Reset();

        [Test]
        public void UnmatchedWarning_FailsNoUnexpectedReceived_ButNotTheEndOfTestCheck()
        {
            Debug.LogWarning("[Media] probe warning");
            Debug.Log("[Media] probe log");
            StringAssert.Contains("[Warning] [Media] probe warning", LogScope.Check(false));
            StringAssert.Contains("[Log] [Media] probe log", LogScope.Check(false));
            Assert.IsNull(LogScope.Check(true));
            LogScope.Reset();
        }

        [Test]
        public void ErrorUnderIgnoreFailingMessages_PassesTheEndOfTestCheck_ButStillFailsNoUnexpectedReceived()
        {
            LogAssert.ignoreFailingMessages = true;
            Debug.LogError("[Media] probe error");
            Assert.IsNull(LogScope.Check(true));
            StringAssert.Contains("[Error] [Media] probe error", LogScope.Check(false));
            LogScope.Reset();
        }

        [Test]
        public void UnmatchedErrorOrException_FailsTheEndOfTestCheck()
        {
            Debug.LogError("[Media] probe error");
            StringAssert.Contains("Unhandled log message: '[Error] [Media] probe error'", LogScope.Check(true));
            LogScope.Reset();
            Debug.LogException(new System.InvalidOperationException("probe"));
            StringAssert.Contains("[Exception] InvalidOperationException: probe", LogScope.Check(true));
            LogScope.Reset();
        }

        [Test]
        public void ExpectedLogs_AreHandled_InOrder_AndUnmetExpectationsFail()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Media\] one$"));
            LogAssert.Expect(LogType.Error, "[Media] two");
            Debug.LogWarning("[Media] one");
            Debug.LogError("[Media] two");
            Assert.IsNull(LogScope.Check(false));
            Assert.IsNull(LogScope.Check(true));
            LogScope.Reset();

            LogAssert.Expect(LogType.Error, "[Media] never");
            StringAssert.Contains("Expected log did not appear: Error '[Media] never'", LogScope.Check(true));
            LogScope.Reset();

            // Out of order: the second expectation waits for the first, so the earlier error stays unhandled.
            LogAssert.Expect(LogType.Warning, "[Media] a");
            LogAssert.Expect(LogType.Error, "[Media] b");
            Debug.LogError("[Media] b");
            Debug.LogWarning("[Media] a");
            string failure = LogScope.Check(true);
            StringAssert.Contains("Unhandled log message: '[Error] [Media] b'", failure);
            StringAssert.Contains("Expected log did not appear: Error '[Media] b'", failure);
            LogScope.Reset();
        }
    }
}
