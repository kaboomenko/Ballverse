// Stand-ins for the Unity 6 APIs used by Ballverse.App / Sim and the assemblies they wire (Studio, Media, Services, Render, UI
// view models), derived from Ballverse.Studio's compile-check stubs. Signatures follow the Unity 6000.x scripting reference;
// bodies are benign defaults so the engine-free parts can also run under plain .NET (Ballverse.App.Tests).
// Not part of the Unity project (the parent folder ends in "~").
#pragma warning disable CS0067, CS0649, CS0660, CS0661, CS1591
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public enum HideFlags { None = 0, HideInHierarchy = 1, DontSave = 52, HideAndDontSave = 61 }
    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }
    public enum TextureFormat { Alpha8 = 1, RGB24 = 3, RGBA32 = 4, ARGB32 = 5, BGRA32 = 14 }
    public enum MeshTopology { Triangles = 0, Quads = 2, Lines = 3, LineStrip = 4, Points = 5 }
    public enum ColorSpace { Uninitialized = -1, Gamma = 0, Linear = 1 }
    public enum FontStyle { Normal = 0, Bold = 1, Italic = 2, BoldAndItalic = 3 }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, AfterAssembliesLoaded, BeforeSplashScreen, SubsystemRegistration }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RequireComponent : Attribute
    {
        public RequireComponent(Type requiredComponent) { }
    }

    public static class Debug
    {
        public static bool isDebugBuild => true;
        public static void LogWarningFormat(string format, params object[] args) { }
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
        public static void LogException(Exception exception) { }
    }

    public static class Application
    {
        public static bool isEditor => true;
        public static string version => "1.0.0";
        public static string identifier => "com.ballverse.app";
        public static int targetFrameRate { get; set; }
        public static RuntimePlatform platform => RuntimePlatform.LinuxEditor;
        public static SystemLanguage systemLanguage => SystemLanguage.English;
        public static NetworkReachability internetReachability => NetworkReachability.ReachableViaLocalAreaNetwork;
        public static event Action lowMemory;
        public static void OpenURL(string url) { }
        public static string persistentDataPath => default;
        public static string temporaryCachePath => default;
        public static bool isPlaying => default;
    }

    public static class JsonUtility
    {
        public static string ToJson(object obj) => default;
        public static string ToJson(object obj, bool prettyPrint) => default;
        public static T FromJson<T>(string json) => default;
    }

    public static class Time
    {
        public static float unscaledDeltaTime => default;
    }

    public struct Mathf
    {
        public const float Rad2Deg = 57.29578f;
        public const float Deg2Rad = 0.0174532924f;
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static float Abs(float f) => Math.Abs(f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public const float PI = 3.14159274f;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
        public static int RoundToInt(float f) => (int)Math.Round(f, MidpointRounding.ToEven);
        public static int NextPowerOfTwo(int value) { int p = 1; while (p < value) p <<= 1; return p; }
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float GammaToLinearSpace(float value) => default;
    }

    public struct Vector2
    {
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => default;
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => default;
        public static Vector2 operator +(Vector2 a, Vector2 b) => default;
        public static Vector2 operator -(Vector2 a, Vector2 b) => default;
        public static Vector2 operator *(Vector2 a, float d) => default;
    }

    public struct Vector3
    {
        public static Vector3 one => new Vector3(1, 1, 1);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0);
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public struct Color
    {
        public static Color white => new Color(1, 1, 1, 1);
        public static Color clear => default;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; this.a = 1f; }
        public Color linear => this;
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color black => default;
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { }
        public float x { get => default; set { } }
        public float y { get => default; set { } }
        public float width { get => default; set { } }
        public float height { get => default; set { } }
        public float xMin => default;
        public float xMax => default;
        public float yMin => default;
        public float yMax => default;
        public static Rect MinMaxRect(float xmin, float ymin, float xmax, float ymax) => default;
    }

    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size) { }
    }

    public struct Matrix4x4
    {
        public static Matrix4x4 TRS(Vector3 pos, Quaternion q, Vector3 s) => default;
        public static Matrix4x4 Scale(Vector3 vector) => default;
        public static Matrix4x4 Translate(Vector3 vector) => default;
        public static Matrix4x4 operator *(Matrix4x4 lhs, Matrix4x4 rhs) => default;
        public static Matrix4x4 identity => default;
        public static Matrix4x4 Ortho(float left, float right, float bottom, float top, float zNear, float zFar) => default;
    }

    public class Object
    {
        public string name { get => default; set { } }
        public HideFlags hideFlags { get => default; set { } }
        public int GetInstanceID() => default;
        public static void Destroy(Object obj) { }
        public static void DestroyImmediate(Object obj) { }
        public static void DontDestroyOnLoad(Object target) { }
        public static T FindAnyObjectByType<T>() where T : Object => default;
        public static implicit operator bool(Object exists) => !ReferenceEquals(exists, null);
        public static bool operator ==(Object x, Object y) => ReferenceEquals(x, y);
        public static bool operator !=(Object x, Object y) => !ReferenceEquals(x, y);
    }

    public class Component : Object
    {
        public GameObject gameObject => default;
        public Transform transform => default;
        public T GetComponent<T>() => default;
    }

    public class Behaviour : Component
    {
        public bool enabled { get => default; set { } }
        public bool isActiveAndEnabled => default;
    }

    public class MonoBehaviour : Behaviour
    {
    }

    public sealed class Transform : Component
    {
        public void SetParent(Transform parent, bool worldPositionStays) { }
    }

    public sealed class GameObject : Object
    {
        public void SetActive(bool value) { }
        public bool activeSelf => true;
        public T GetComponent<T>() => default;
        public GameObject(string name) { }
        public Transform transform => default;
        public T AddComponent<T>() where T : Component => default;
    }

    public class Texture : Object
    {
        public virtual int width { get => default; set { } }
        public virtual int height { get => default; set { } }
        public FilterMode filterMode { get => default; set { } }
        public TextureWrapMode wrapMode { get => default; set { } }
    }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height) { }
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public static Texture2D whiteTexture => default;
        public bool LoadImage(byte[] data, bool markNonReadable) => false;
        public bool LoadImage(byte[] data) => false;
        public void SetPixels32(Color32[] colors) { }
        public void Apply() { }
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain, bool linear) { }
        public static Texture2D blackTexture => default;
        public void LoadRawTextureData<T>(Unity.Collections.NativeArray<T> data) where T : unmanaged { }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }
        public void ReadPixels(Rect source, int destX, int destY, bool recalculateMipMaps) { }
        public Unity.Collections.NativeArray<T> GetRawTextureData<T>() where T : unmanaged => default;
    }

    public static class ImageConversion
    {
        public static byte[] EncodeToPNG(this Texture2D tex) => default;
        public static bool LoadImage(this Texture2D tex, byte[] data, bool markNonReadable) => false;
    }

    public struct RenderTextureDescriptor
    {
        public RenderTextureDescriptor(int width, int height, UnityEngine.Experimental.Rendering.GraphicsFormat colorFormat, int depthBufferBits) { }
        public RenderTextureDescriptor(int width, int height, RenderTextureFormat colorFormat, int depthBufferBits) { }
        public int msaaSamples { get => default; set { } }
        public bool useMipMap { get => default; set { } }
        public bool autoGenerateMips { get => default; set { } }
        public bool sRGB { get => default; set { } }
    }

    public class RenderTexture : Texture
    {
        public RenderTexture(int width, int height, int depth, RenderTextureFormat format, RenderTextureReadWrite readWrite) { }
        public RenderTexture(int width, int height, int depth, RenderTextureFormat format) { }
        public RenderTexture(int width, int height, int depth) { }
        public int antiAliasing { get => 1; set { } }
        public bool IsCreated() => false;
        public RenderTextureDescriptor descriptor => default;
        public RenderTexture(RenderTextureDescriptor desc) { }
        public static RenderTexture active { get => default; set { } }
        public bool Create() => default;
        public void Release() { }
        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, UnityEngine.Experimental.Rendering.GraphicsFormat format) => default;
        public static void ReleaseTemporary(RenderTexture temp) { }
    }

    public sealed class Mesh : Object
    {
        public int subMeshCount { get => 1; set { } }
        public void SetVertexBufferParams(int vertexCount, params UnityEngine.Rendering.VertexAttributeDescriptor[] attributes) { }
        public void SetVertexBufferData<T>(T[] data, int dataStart, int meshBufferStart, int count, int stream = 0, UnityEngine.Rendering.MeshUpdateFlags flags = UnityEngine.Rendering.MeshUpdateFlags.Default) where T : struct { }
        public void SetIndexBufferParams(int indexCount, UnityEngine.Rendering.IndexFormat format) { }
        public void SetIndexBufferData<T>(T[] data, int dataStart, int meshBufferStart, int count, UnityEngine.Rendering.MeshUpdateFlags flags = UnityEngine.Rendering.MeshUpdateFlags.Default) where T : struct { }
        public void SetSubMesh(int index, UnityEngine.Rendering.SubMeshDescriptor desc, UnityEngine.Rendering.MeshUpdateFlags flags = UnityEngine.Rendering.MeshUpdateFlags.Default) { }
        public void SetVertices(Vector3[] inVertices) { }
        public void SetUVs(int channel, Vector2[] uvs) { }
        public void SetColors(Color32[] inColors, int start, int length) { }
        public void SetColors(Color[] inColors, int start, int length) { }
        public void SetTriangles(int[] triangles, int submesh, bool calculateBounds = true, int baseVertex = 0) { }
        public Mesh() { }
        public Bounds bounds { get => default; set { } }
        public void MarkDynamic() { }
        public void Clear(bool keepVertexLayout) { }
        public void SetVertices(List<Vector3> inVertices) { }
        public void SetVertices(Vector3[] inVertices, int start, int length) { }
        public void SetUVs(int channel, List<Vector2> uvs) { }
        public void SetUVs(int channel, Vector2[] uvs, int start, int length) { }
        public void SetIndices(int[] indices, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) { }
        public void SetIndices(int[] indices, int indicesStart, int indicesLength, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) { }
        public void UploadMeshData(bool markNoLongerReadable) { }
    }

    public sealed class Shader : Object
    {
        public static Shader Find(string name) => default;
        public bool isSupported => true;
        public static int PropertyToID(string name) => default;
    }

    public class Material : Object
    {
        public Material(Material source) { }
        public Shader shader { get => default; set { } }
        public bool enableInstancing { get => true; set { } }
        public void SetFloat(int nameID, float value) { }
        public void SetFloat(string name, float value) { }
        public void SetTexture(int nameID, Texture value) { }
        public void SetVector(int nameID, Vector4 value) { }
        public void SetColor(int nameID, Color value) { }
        public void EnableKeyword(string keyword) { }
        public void DisableKeyword(string keyword) { }
        public Material(Shader shader) { }
        public Texture mainTexture { get => default; set { } }
    }

    public sealed class MaterialPropertyBlock
    {
        public void SetVectorArray(int nameID, Vector4[] values) { }
        public void SetVectorArray(int nameID, List<Vector4> values) { }
        public void SetFloat(int nameID, float value) { }
        public void SetMatrix(int nameID, Matrix4x4 value) { }
        public void Clear() { }
        public void SetVector(int nameID, Vector4 value) { }
        public void SetColor(int nameID, Color value) { }
        public void SetTexture(int nameID, Texture value) { }
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => default;
        public static T GetBuiltinResource<T>(string path) where T : Object => default;
        public static void UnloadAsset(Object assetToUnload) { }
    }

    public struct CharacterInfo
    {
        public int advance { get => default; set { } }
        public int minX { get => default; set { } }
        public int maxX { get => default; set { } }
        public int minY { get => default; set { } }
        public int maxY { get => default; set { } }
        public Vector2 uvBottomLeft { get => default; set { } }
        public Vector2 uvBottomRight { get => default; set { } }
        public Vector2 uvTopLeft { get => default; set { } }
        public Vector2 uvTopRight { get => default; set { } }
    }

    public sealed class Font : Object
    {
        public static event Action<Font> textureRebuilt;
        public Material material { get => default; set { } }
        public int fontSize => default;
        public int ascent => default;
        public int lineHeight => default;
        public void RequestCharactersInTexture(string characters, int size, FontStyle style) { }
        public bool GetCharacterInfo(char ch, out CharacterInfo info, int size, FontStyle style) => default;
    }

    public static class GL
    {
        public static Matrix4x4 GetGPUProjectionMatrix(Matrix4x4 proj, bool renderIntoTexture) => default;
    }

    public static class Graphics
    {
        public static void Blit(Texture source, RenderTexture dest) { }
        public static void Blit(Texture source, RenderTexture dest, Material mat, int pass = -1) { }
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count) { }
        public static void ExecuteCommandBuffer(UnityEngine.Rendering.CommandBuffer buffer) { }
    }

    public static class QualitySettings
    {
        public static int vSyncCount { get; set; }
        public static ColorSpace activeColorSpace => default;
    }

    public static class SystemInfo
    {
        public static string deviceModel => "editor";
        public static string graphicsDeviceName => "stub";
        public static int systemMemorySize => 8192;
        public static bool supportsInstancing => true;
        public static bool SupportsRenderTextureFormat(RenderTextureFormat format) => true;
        public static int GetRenderTextureSupportedMSAASampleCount(RenderTextureDescriptor desc) => 4;
        public static bool supportsAsyncGPUReadback => default;
    }

    public sealed class AudioClip : Object
    {
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream) => default;
    }

    public sealed class AudioSource : Behaviour
    {
        public AudioClip clip { get => default; set { } }
        public bool loop { get => default; set { } }
        public bool playOnAwake { get => default; set { } }
        public float spatialBlend { get => default; set { } }
        public float volume { get => default; set { } }
        public int priority { get => default; set { } }
        public bool bypassEffects { get => default; set { } }
        public bool bypassListenerEffects { get => default; set { } }
        public bool bypassReverbZones { get => default; set { } }
        public bool isPlaying => default;
        public void Play() { }
        public void Stop() { }
    }

    public sealed class AudioSettings
    {
        public delegate void AudioConfigurationChangeHandler(bool deviceWasChanged);
        public static event AudioConfigurationChangeHandler OnAudioConfigurationChanged;
        public static int outputSampleRate => default;
        public static void GetDSPBufferSize(out int bufferLength, out int numBuffers) { bufferLength = 1024; numBuffers = 2; }
    }
}

namespace UnityEngine.Experimental.Rendering
{
    public enum GraphicsFormat { None = 0, R8G8B8A8_SRGB = 8, R8G8B8A8_UNorm = 4 }
}

namespace UnityEngine.Rendering
{
    public enum RenderBufferLoadAction { Load = 0, Clear = 1, DontCare = 2 }
    public enum RenderBufferStoreAction { Store = 0, Resolve = 1, StoreAndResolve = 2, DontCare = 3 }

    public struct RenderTargetIdentifier
    {
        public static implicit operator RenderTargetIdentifier(Texture tex) => default;
        public static implicit operator RenderTargetIdentifier(RenderTexture rt) => default;
    }

    public class CommandBuffer : IDisposable
    {
        public void SetGlobalVector(int nameID, Vector4 value) { }
        public void SetGlobalTexture(int nameID, RenderTargetIdentifier value) { }
        public void SetGlobalFloat(int nameID, float value) { }
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex, int shaderPass) { }
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material) { }
        public void DrawProcedural(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int vertexCount) { }
        public void DrawProcedural(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int vertexCount, int instanceCount) { }
        public void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, int shaderPass, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties) { }
        public void SetViewport(Rect pixelRect) { }
        public string name { get => default; set { } }
        public void Clear() { }
        public void Release() { }
        public void Dispose() { }
        public void SetRenderTarget(RenderTargetIdentifier rt, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction) { }
        public void ClearRenderTarget(bool clearDepth, bool clearColor, Color backgroundColor) { }
        public void SetGlobalMatrix(int nameID, Matrix4x4 value) { }
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex, int shaderPass, MaterialPropertyBlock properties) { }
        public void DrawProcedural(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int vertexCount, int instanceCount, MaterialPropertyBlock properties) { }
        public void Blit(Texture source, RenderTargetIdentifier dest) { }
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest) { }
    }

    public struct AsyncGPUReadbackRequest
    {
        public bool done => default;
        public bool hasError => default;
        public void WaitForCompletion() { }
    }

    public static class AsyncGPUReadback
    {
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref Unity.Collections.NativeArray<T> output, Texture src, int mipIndex, TextureFormat dstFormat,
            Action<AsyncGPUReadbackRequest> callback = null) where T : unmanaged => default;
    }
}

namespace Unity.Collections
{
    public enum Allocator { Invalid, None, Temp, TempJob, Persistent }
    public enum NativeArrayOptions { UninitializedMemory = 0, ClearMemory = 1 }

    public unsafe struct NativeArray<T> : IDisposable where T : unmanaged
    {
        internal void* m_Buffer;
        internal int m_Length;

        public NativeArray(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory) { m_Buffer = null; m_Length = length; }
        public bool IsCreated => m_Buffer != null;
        public int Length => m_Length;
        public void Dispose() { }
        public NativeArray<T> GetSubArray(int start, int length) => default;
        public T this[int index] { get => default; set { } }
        public static void Copy(NativeArray<T> src, NativeArray<T> dst, int length) { }
        public static void Copy(NativeArray<T> src, T[] dst, int length) { }
        public static void Copy(NativeArray<T> src, int srcIndex, T[] dst, int dstIndex, int length) { }
        public static void Copy(T[] src, int srcIndex, NativeArray<T> dst, int dstIndex, int length) { }
        public static void Copy(NativeArray<T> src, int srcIndex, NativeArray<T> dst, int dstIndex, int length) { }
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    public static unsafe class NativeArrayUnsafeUtility
    {
        public static NativeArray<T> ConvertExistingDataToNativeArray<T>(void* dataPointer, int length, Allocator allocator) where T : unmanaged => default;
        public static void* GetUnsafeReadOnlyPtr<T>(NativeArray<T> array) where T : unmanaged => default;
    }

    public static unsafe class UnsafeUtility
    {
        public static void* Malloc(long size, int alignment, Allocator allocator) => default;
        public static void Free(void* memory, Allocator allocator) { }
        public static void MemCpy(void* destination, void* source, long size) { }
    }
}
