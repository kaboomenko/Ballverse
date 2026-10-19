// Minimal runtime stand-ins for the Unity APIs used by the pure-C# parts of Ballverse.Media, so that the EditMode tests
// also run under plain .NET (see Ballverse.Media.DotNetTests.csproj). Behaviour mirrors Unity where the tests depend on
// it (JsonUtility: public fields only; NativeArray: view over unmanaged memory; LogAssert: an unexpected error or
// exception log fails the running test, as in the Unity Test Framework). Not part of the Unity project.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

[assembly: UnityEngine.TestTools.LogScopeCheck]

namespace UnityEngine
{
    public enum LogType
    {
        Error,
        Assert,
        Warning,
        Log,
        Exception,
    }

    public static class Debug
    {
        public static void Log(object message) => TestTools.LogScope.Add(LogType.Log, "" + message);
        public static void LogWarning(object message) => TestTools.LogScope.Add(LogType.Warning, "" + message);
        public static void LogError(object message) => TestTools.LogScope.Add(LogType.Error, "" + message);
        public static void LogException(Exception exception) => TestTools.LogScope.Add(LogType.Exception, exception.GetType().Name + ": " + exception.Message);
    }

    public static class Application
    {
        static readonly string Root = Path.Combine(Path.GetTempPath(), "ballverse-media-dotnet-tests");
        public static string temporaryCachePath => Path.Combine(Root, "cache");
        public static string persistentDataPath => Path.Combine(Root, "data");
    }

    public enum RuntimeInitializeLoadType
    {
        AfterSceneLoad,
        BeforeSceneLoad,
        AfterAssembliesLoaded,
        BeforeSplashScreen,
        SubsystemRegistration,
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType)
        {
        }
    }

    public static class JsonUtility
    {
        sealed class PublicFieldsOnly : DefaultContractResolver
        {
            protected override System.Collections.Generic.IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
            {
                var list = new System.Collections.Generic.List<JsonProperty>();
                foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    JsonProperty p = CreateProperty(field, memberSerialization);
                    p.Readable = true;
                    p.Writable = true;
                    list.Add(p);
                }
                return list;
            }
        }

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings { ContractResolver = new PublicFieldsOnly() };

        public static string ToJson(object obj, bool prettyPrint) =>
            JsonConvert.SerializeObject(obj, prettyPrint ? Formatting.Indented : Formatting.None, Settings);

        public static string ToJson(object obj) => ToJson(obj, false);

        public static T FromJson<T>(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(json, Settings);
            }
            catch (JsonException e)
            {
                throw new ArgumentException("JSON parse error: " + e.Message);
            }
        }
    }
}

namespace Unity.Collections
{
    public enum Allocator
    {
        Invalid,
        None,
        Temp,
        TempJob,
        Persistent,
    }

    public unsafe struct NativeArray<T> where T : unmanaged
    {
        internal void* m_Buffer;
        internal int m_Length;

        public bool IsCreated => m_Buffer != null;
        public int Length => m_Length;

        public NativeArray<T> GetSubArray(int start, int length)
        {
            if (start < 0 || length < 0 || start + length > m_Length) throw new ArgumentOutOfRangeException(nameof(length));
            return new NativeArray<T> { m_Buffer = (T*)m_Buffer + start, m_Length = length };
        }

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)m_Length) throw new IndexOutOfRangeException();
                return ((T*)m_Buffer)[index];
            }
            set
            {
                if ((uint)index >= (uint)m_Length) throw new IndexOutOfRangeException();
                ((T*)m_Buffer)[index] = value;
            }
        }

        public static void Copy(NativeArray<T> src, T[] dst, int length)
        {
            if (length > src.m_Length || length > dst.Length) throw new ArgumentOutOfRangeException(nameof(length));
            fixed (T* d = dst) Buffer.MemoryCopy(src.m_Buffer, d, (long)length * sizeof(T), (long)length * sizeof(T));
        }
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    public static unsafe class NativeArrayUnsafeUtility
    {
        public static NativeArray<T> ConvertExistingDataToNativeArray<T>(void* dataPointer, int length, Allocator allocator) where T : unmanaged =>
            new NativeArray<T> { m_Buffer = dataPointer, m_Length = length };

        public static void* GetUnsafeReadOnlyPtr<T>(NativeArray<T> array) where T : unmanaged => array.m_Buffer;
    }

    public static unsafe class UnsafeUtility
    {
        public static void* Malloc(long size, int alignment, Allocator allocator) => (void*)Marshal.AllocHGlobal((IntPtr)size);
        public static void Free(void* memory, Allocator allocator) => Marshal.FreeHGlobal((IntPtr)memory);
        public static void MemCpy(void* destination, void* source, long size) => Buffer.MemoryCopy(source, destination, size, size);
    }
}

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

        internal static void Add(LogType type, string message)
        {
            Console.WriteLine("[" + type + "] " + message);
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

        public void BeforeTest(NUnit.Framework.Interfaces.ITest test) => LogScope.Reset();

        public void AfterTest(NUnit.Framework.Interfaces.ITest test)
        {
            string failure = LogScope.Check(true);
            LogScope.Reset();
            if (failure != null) NUnit.Framework.Assert.Fail(failure);
        }
    }
}
