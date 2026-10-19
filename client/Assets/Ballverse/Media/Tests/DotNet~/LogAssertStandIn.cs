// NUnit-side stand-in for the Unity Test Framework's LogAssert, for the Media .NET harness only (see
// Ballverse.Media.DotNetTests.csproj). Kept out of UnityStubs.cs because other harnesses (Ballverse.Studio, xunit) compile
// that file and must not need NUnit. Logs reach LogScope through UnityEngine.Debug.Sink, which LogScopeCheckAttribute
// installs before each test; nothing is recorded in an assembly that does not include this file. Not part of the Unity
// project.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

[assembly: UnityEngine.TestTools.LogScopeCheck]

namespace UnityEngine.TestTools
{
    /// <summary>
    /// Stand-in for the Unity Test Framework's LogAssert (com.unity.test-framework), following UTF's LogScope
    /// (LogScope.AddLog / ProcessExpectedLogs / EvaluateLogScope / NoUnexpectedReceived):
    /// <list type="bullet">
    /// <item>Every log received while a test runs — from any thread — is recorded. Expectations
    /// (<see cref="Expect(LogType, Regex)"/>) are matched in order against the not yet matched logs in arrival order; a
    /// matched log is "handled".</item>
    /// <item>End of test: an <see cref="LogType.Error"/>, <see cref="LogType.Assert"/> or <see cref="LogType.Exception"/> log
    /// that no expectation matched fails the test, unless <see cref="ignoreFailingMessages"/> was set when it arrived; an
    /// expectation that no log matched fails the test too. Unmatched Log/Warning entries do not.</item>
    /// <item><see cref="NoUnexpectedReceived"/>: fails on any log no expectation matched, whatever its type (Log and Warning
    /// included) and regardless of <see cref="ignoreFailingMessages"/>.</item>
    /// </list>
    /// A log that arrives after its test ended is charged to whichever test runs then, exactly the CI failure mode of a
    /// leaked timer. Not modelled: UTF's NUnit result-state exception filtering and its "failing log matched an expectation
    /// out of order" message (such a case fails here too, as an unhandled log).
    /// </summary>
    public static class LogAssert
    {
        public static bool ignoreFailingMessages
        {
            get => LogScope.IgnoreFailingMessages;
            set => LogScope.IgnoreFailingMessages = value;
        }

        public static void Expect(LogType type, string message) => LogScope.Expect(type, message, null);

        public static void Expect(LogType type, Regex message) => LogScope.Expect(type, null, message);

        /// <summary>Fails now if any log (of any type) received so far in this test was not matched by an expectation.</summary>
        public static void NoUnexpectedReceived()
        {
            string failure = LogScope.Check(false);
            if (failure != null) NUnit.Framework.Assert.Fail(failure);
        }
    }

    internal static class LogScope
    {
        sealed class Entry
        {
            public LogType Type;
            public string Message;
            /// <summary>Error/Assert/Exception logged while <see cref="IgnoreFailingMessages"/> was off (UTF FailingLogs).</summary>
            public bool Failing;
            /// <summary>Matched by an expectation (UTF LogEvent.IsHandled).</summary>
            public bool Handled;
        }

        sealed class Match
        {
            public LogType Type;
            public string Message;
            public Regex Regex;

            public bool Matches(Entry e) => e.Type == Type && (Regex != null ? Regex.IsMatch(e.Message) : e.Message == Message);

            public override string ToString() => Type + " " + (Regex != null ? "/" + Regex + "/" : "'" + Message + "'");
        }

        static readonly object s_lock = new object();
        static readonly List<Entry> s_logs = new List<Entry>();
        static readonly Queue<Match> s_expected = new Queue<Match>();
        internal static bool IgnoreFailingMessages;

        static bool IsFailing(LogType t) => t == LogType.Error || t == LogType.Assert || t == LogType.Exception;

        /// <summary>Records every log from now on (installs the <see cref="Debug.Sink"/>) and starts a clean scope.</summary>
        internal static void Begin()
        {
            Debug.Sink = Add;
            Reset();
        }

        static void Add(LogType type, string message)
        {
            lock (s_lock) s_logs.Add(new Entry { Type = type, Message = message, Failing = IsFailing(type) && !IgnoreFailingMessages });
        }

        internal static void Expect(LogType type, string message, Regex regex)
        {
            lock (s_lock) s_expected.Enqueue(new Match { Type = type, Message = message, Regex = regex });
        }

        internal static void Reset()
        {
            lock (s_lock)
            {
                s_logs.Clear();
                s_expected.Clear();
                IgnoreFailingMessages = false;
            }
        }

        /// <summary>
        /// Matches expectations to logs (UTF ProcessExpectedLogs) and describes what is left, or null. <paramref name="end"/>:
        /// the end-of-test rule (unmatched failing logs, unmet expectations); otherwise the NoUnexpectedReceived rule (every
        /// unmatched log, whatever its type).
        /// </summary>
        internal static string Check(bool end)
        {
            lock (s_lock)
            {
                foreach (Entry e in s_logs)
                {
                    if (s_expected.Count == 0) break;
                    if (e.Handled || !s_expected.Peek().Matches(e)) continue;
                    s_expected.Dequeue();
                    e.Handled = true;
                }
                var problems = new List<string>();
                foreach (Entry e in s_logs)
                {
                    if (!e.Handled && (!end || e.Failing))
                    {
                        problems.Add("Unhandled log message: '[" + e.Type + "] " + e.Message + "'. Use UnityEngine.TestTools.LogAssert.Expect");
                    }
                }
                if (end)
                {
                    foreach (Match m in s_expected) problems.Add("Expected log did not appear: " + m);
                }
                return problems.Count == 0 ? null : string.Join(Environment.NewLine, problems);
            }
        }
    }

    /// <summary>Applies the <see cref="LogScope"/> rule around every test of the harness assembly.</summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class LogScopeCheckAttribute : Attribute, NUnit.Framework.ITestAction
    {
        public NUnit.Framework.ActionTargets Targets => NUnit.Framework.ActionTargets.Test;

        public void BeforeTest(NUnit.Framework.Interfaces.ITest test) => LogScope.Begin();

        public void AfterTest(NUnit.Framework.Interfaces.ITest test)
        {
            string failure = LogScope.Check(true);
            LogScope.Reset();
            if (failure != null) NUnit.Framework.Assert.Fail(failure);
        }
    }
}
