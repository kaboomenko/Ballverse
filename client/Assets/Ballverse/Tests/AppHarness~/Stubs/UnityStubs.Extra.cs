// Additional Unity 6 API stand-ins needed by Ballverse.Render, Ballverse.Services, Ballverse.Sim and Ballverse.App (see
// UnityStubs.cs). Signatures follow the Unity 6000.x scripting reference; bodies are benign defaults. Not part of the Unity
// project (the parent folder ends in "~").
#pragma warning disable CS0067, CS0649, CS0660, CS0661, CS1591, CS0414
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string tooltip) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)] public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string header) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TextAreaAttribute : Attribute { public TextAreaAttribute() { } public TextAreaAttribute(int minLines, int maxLines) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute : Attribute { public MinAttribute(float min) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class CreateAssetMenuAttribute : Attribute { public string menuName { get; set; } public string fileName { get; set; } public int order { get; set; } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int order) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class ExecuteAlways : Attribute { }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();
    }

    public sealed class TextAsset : Object
    {
        public string text => "";
        public byte[] bytes => Array.Empty<byte>();
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
        public static implicit operator Color32(Color c) => new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(c.a * 255));
    }

    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
    }

    public struct Quaternion
    {
        public static Quaternion identity => default;
        public static Quaternion Euler(float x, float y, float z) => default;
    }

    public enum RenderTextureFormat { ARGB32 = 0, Depth = 1, ARGBHalf = 2, Default = 7, RGB111110Float = 22, ARGBFloat = 11, R8 = 16, RHalf = 15 }
    public enum RenderTextureReadWrite { Default = 0, Linear = 1, sRGB = 2 }
    public enum RuntimePlatform { OSXEditor = 0, OSXPlayer = 1, WindowsPlayer = 2, WindowsEditor = 7, IPhonePlayer = 8, Android = 11, LinuxPlayer = 13, LinuxEditor = 16 }
    public enum SystemLanguage { Afrikaans = 0, English = 10, Russian = 30, Unknown = 43 }
    public enum NetworkReachability { NotReachable = 0, ReachableViaCarrierDataNetwork = 1, ReachableViaLocalAreaNetwork = 2 }
    public static class SleepTimeout { public const int NeverSleep = -1; public const int SystemSetting = -2; }

    public static class Screen
    {
        public static int width => 1080;
        public static int height => 1920;
        public static float dpi => 420f;
        public static Rect safeArea => default;
        public static int sleepTimeout { get => 0; set { } }
    }

    public static class PlayerPrefs
    {
        static readonly Dictionary<string, string> s_values = new Dictionary<string, string>();
        public static string GetString(string key, string defaultValue = "") => s_values.TryGetValue(key, out string v) ? v : defaultValue;
        public static void SetString(string key, string value) => s_values[key] = value;
        public static int GetInt(string key, int defaultValue = 0) => s_values.TryGetValue(key, out string v) && int.TryParse(v, out int i) ? i : defaultValue;
        public static void SetInt(string key, int value) => s_values[key] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public static bool HasKey(string key) => s_values.ContainsKey(key);
        public static void DeleteKey(string key) => s_values.Remove(key);
        public static void DeleteAll() => s_values.Clear();
        public static void Save() { }
    }

    public sealed class AudioListener : Behaviour
    {
        public static bool pause { get; set; }
    }

    public static class Handheld
    {
        public static void Vibrate() { }
    }

    public static class GUIUtility
    {
        public static string systemCopyBuffer { get; set; }
    }

    public class AndroidJavaObject : IDisposable
    {
        public AndroidJavaObject(string className, params object[] args) { }
        public void Call(string methodName, params object[] args) { }
        public T Call<T>(string methodName, params object[] args) => default;
        public T Get<T>(string fieldName) => default;
        public T GetStatic<T>(string fieldName) => default;
        public void CallStatic(string methodName, params object[] args) { }
        public T CallStatic<T>(string methodName, params object[] args) => default;
        public void Dispose() { }
    }

    public class AndroidJavaClass : AndroidJavaObject
    {
        public AndroidJavaClass(string className) : base(className) { }
    }
}

namespace UnityEngine.Rendering
{
    [Flags]
    public enum MeshUpdateFlags { Default = 0, DontValidateIndices = 1, DontResetBoneBounds = 2, DontNotifyMeshUsers = 4, DontRecalculateBounds = 8 }
    public enum VertexAttribute { Position = 0, Normal = 1, Tangent = 2, Color = 3, TexCoord0 = 4, TexCoord1 = 5 }
    public enum VertexAttributeFormat { Float32 = 0, Float16 = 1, UNorm8 = 2 }
    public enum IndexFormat { UInt16 = 0, UInt32 = 1 }

    public struct VertexAttributeDescriptor
    {
        public VertexAttributeDescriptor(VertexAttribute attribute = VertexAttribute.Position, VertexAttributeFormat format = VertexAttributeFormat.Float32, int dimension = 3, int stream = 0) { }
    }

    public struct SubMeshDescriptor
    {
        public SubMeshDescriptor(int indexStart, int indexCount, MeshTopology topology = MeshTopology.Triangles) { firstVertex = 0; vertexCount = 0; bounds = default; }
        public int firstVertex { get; set; }
        public int vertexCount { get; set; }
        public Bounds bounds { get; set; }
    }
}

namespace UnityEngine.UIElements
{
    public class VisualElement
    {
        public Rect worldBound => default;
        public IPanel panel => null;
    }

    public interface IPanel
    {
        VisualElement visualTree { get; }
    }
}

namespace UnityEngine.UI
{
    public class RawImage : Behaviour
    {
        public Texture texture { get; set; }
        public RectTransform rectTransform => null;
        public Canvas canvas => null;
    }
}

namespace UnityEngine
{
    public sealed class RectTransform : Component
    {
        public Rect rect => default;
    }

    public sealed class Canvas : Behaviour
    {
        public float scaleFactor => 1f;
    }
}
