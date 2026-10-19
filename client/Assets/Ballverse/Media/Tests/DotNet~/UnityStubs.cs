// Minimal runtime stand-ins for the Unity APIs used by the pure-C# parts of Ballverse.Media, so that the EditMode tests
// also run under plain .NET (see Ballverse.Media.DotNetTests.csproj). Behaviour mirrors Unity where the tests depend on
// it (JsonUtility: public fields only; NativeArray: view over unmanaged memory). Not part of the Unity project.
// This file is also compiled by other .NET harnesses (Ballverse.Studio's xunit harness), so it must not depend on any test
// framework: the NUnit-side LogAssert stand-in lives in LogAssertStandIn.cs (Media harness only) and plugs in through
// Debug.Sink. StubsCompileCheck/ (built by run.sh) compiles this file without a test framework to keep it that way.
using System;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

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
        /// <summary>
        /// Optional receiver of every log, from any thread (type, message; for exceptions "TypeName: Message"). Null unless a
        /// harness installs one (the Media harness's LogAssert stand-in does, per test); then nothing is retained here.
        /// </summary>
        internal static Action<LogType, string> Sink
        {
            get => s_sink;
            set => s_sink = value;
        }

        static volatile Action<LogType, string> s_sink;

        public static void Log(object message) => Write(LogType.Log, "" + message, null);
        public static void LogWarning(object message) => Write(LogType.Warning, "" + message, null);
        public static void LogError(object message) => Write(LogType.Error, "" + message, null);
        public static void LogException(Exception exception) => Write(LogType.Exception, exception.GetType().Name + ": " + exception.Message, exception);

        static void Write(LogType type, string message, Exception exception)
        {
            Console.WriteLine("[" + type + "] " + (exception != null ? exception.ToString() : message));
            s_sink?.Invoke(type, message);
        }
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
