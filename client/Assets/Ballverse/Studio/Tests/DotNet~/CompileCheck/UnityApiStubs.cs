// Compile-only stand-ins for the Unity 6 APIs used by Ballverse.Studio (and the Ballverse.Media sources it is compiled with).
// Signatures follow the Unity 6000.x scripting reference for the members the studio calls; bodies are never executed
// (Ballverse.Studio.CompileCheck only type-checks the Studio assembly outside the editor, because Unity is not available in
// the development container). Not part of the Unity project (the parent folder ends in "~").
#pragma warning disable CS0067, CS0649, CS0660, CS0661, CS1591
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public enum HideFlags { None = 0, HideAndDontSave = 61 }
    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }
    public enum TextureFormat { Alpha8 = 1, RGBA32 = 4, ARGB32 = 5, BGRA32 = 14 }
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
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
        public static void LogException(Exception exception) { }
    }

    public static class Application
    {
        public static string persistentDataPath => throw null;
        public static string temporaryCachePath => throw null;
        public static bool isPlaying => throw null;
    }

    public static class JsonUtility
    {
        public static string ToJson(object obj) => throw null;
        public static string ToJson(object obj, bool prettyPrint) => throw null;
        public static T FromJson<T>(string json) => throw null;
    }

    public static class Time
    {
        public static float unscaledDeltaTime => throw null;
    }

    public struct Mathf
    {
        public const float PI = 3.14159274f;
        public static float Max(float a, float b) => throw null;
        public static int Max(int a, int b) => throw null;
        public static float Min(float a, float b) => throw null;
        public static int Min(int a, int b) => throw null;
        public static float Clamp(float value, float min, float max) => throw null;
        public static int Clamp(int value, int min, int max) => throw null;
        public static float Clamp01(float value) => throw null;
        public static int RoundToInt(float f) => throw null;
        public static int NextPowerOfTwo(int value) => throw null;
        public static float Sin(float f) => throw null;
        public static float Cos(float f) => throw null;
        public static float GammaToLinearSpace(float value) => throw null;
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => throw null;
        public static Vector2 operator +(Vector2 a, Vector2 b) => throw null;
        public static Vector2 operator -(Vector2 a, Vector2 b) => throw null;
        public static Vector2 operator *(Vector2 a, float d) => throw null;
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => throw null;
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color black => throw null;
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) => throw null;
        public float x { get => throw null; set => throw null; }
        public float y { get => throw null; set => throw null; }
        public float width { get => throw null; set => throw null; }
        public float height { get => throw null; set => throw null; }
        public float xMin => throw null;
        public float xMax => throw null;
        public float yMin => throw null;
        public float yMax => throw null;
        public static Rect MinMaxRect(float xmin, float ymin, float xmax, float ymax) => throw null;
    }

    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size) => throw null;
    }

    public struct Matrix4x4
    {
        public static Matrix4x4 identity => throw null;
        public static Matrix4x4 Ortho(float left, float right, float bottom, float top, float zNear, float zFar) => throw null;
    }

    public class Object
    {
        public string name { get => throw null; set => throw null; }
        public HideFlags hideFlags { get => throw null; set => throw null; }
        public int GetInstanceID() => throw null;
        public static void Destroy(Object obj) => throw null;
        public static void DestroyImmediate(Object obj) => throw null;
        public static void DontDestroyOnLoad(Object target) => throw null;
        public static T FindAnyObjectByType<T>() where T : Object => throw null;
        public static implicit operator bool(Object exists) => throw null;
        public static bool operator ==(Object x, Object y) => throw null;
        public static bool operator !=(Object x, Object y) => throw null;
    }

    public class Component : Object
    {
        public GameObject gameObject => throw null;
        public Transform transform => throw null;
        public T GetComponent<T>() => throw null;
    }

    public class Behaviour : Component
    {
        public bool enabled { get => throw null; set => throw null; }
        public bool isActiveAndEnabled => throw null;
    }

    public class MonoBehaviour : Behaviour
    {
    }

    public sealed class Transform : Component
    {
        public void SetParent(Transform parent, bool worldPositionStays) => throw null;
    }

    public sealed class GameObject : Object
    {
        public GameObject(string name) => throw null;
        public Transform transform => throw null;
        public T AddComponent<T>() where T : Component => throw null;
    }

    public class Texture : Object
    {
        public virtual int width { get => throw null; set => throw null; }
        public virtual int height { get => throw null; set => throw null; }
        public FilterMode filterMode { get => throw null; set => throw null; }
        public TextureWrapMode wrapMode { get => throw null; set => throw null; }
    }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain, bool linear) => throw null;
        public static Texture2D blackTexture => throw null;
        public void LoadRawTextureData<T>(Unity.Collections.NativeArray<T> data) where T : unmanaged => throw null;
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) => throw null;
        public void ReadPixels(Rect source, int destX, int destY, bool recalculateMipMaps) => throw null;
        public Unity.Collections.NativeArray<T> GetRawTextureData<T>() where T : unmanaged => throw null;
    }

    public static class ImageConversion
    {
        public static byte[] EncodeToPNG(this Texture2D tex) => throw null;
        public static bool LoadImage(this Texture2D tex, byte[] data, bool markNonReadable) => throw null;
    }

    public struct RenderTextureDescriptor
    {
        public RenderTextureDescriptor(int width, int height, UnityEngine.Experimental.Rendering.GraphicsFormat colorFormat, int depthBufferBits) => throw null;
        public int msaaSamples { get => throw null; set => throw null; }
        public bool useMipMap { get => throw null; set => throw null; }
        public bool autoGenerateMips { get => throw null; set => throw null; }
        public bool sRGB { get => throw null; set => throw null; }
    }

    public class RenderTexture : Texture
    {
        public RenderTexture(RenderTextureDescriptor desc) => throw null;
        public static RenderTexture active { get => throw null; set => throw null; }
        public bool Create() => throw null;
        public void Release() => throw null;
        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, UnityEngine.Experimental.Rendering.GraphicsFormat format) => throw null;
        public static void ReleaseTemporary(RenderTexture temp) => throw null;
    }

    public sealed class Mesh : Object
    {
        public Mesh() => throw null;
        public Bounds bounds { get => throw null; set => throw null; }
        public void MarkDynamic() => throw null;
        public void Clear(bool keepVertexLayout) => throw null;
        public void SetVertices(List<Vector3> inVertices) => throw null;
        public void SetVertices(Vector3[] inVertices, int start, int length) => throw null;
        public void SetUVs(int channel, List<Vector2> uvs) => throw null;
        public void SetUVs(int channel, Vector2[] uvs, int start, int length) => throw null;
        public void SetIndices(int[] indices, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) => throw null;
        public void SetIndices(int[] indices, int indicesStart, int indicesLength, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) => throw null;
        public void UploadMeshData(bool markNoLongerReadable) => throw null;
    }

    public sealed class Shader : Object
    {
        public static int PropertyToID(string name) => throw null;
    }

    public class Material : Object
    {
        public Material(Shader shader) => throw null;
        public Texture mainTexture { get => throw null; set => throw null; }
    }

    public sealed class MaterialPropertyBlock
    {
        public void Clear() => throw null;
        public void SetVector(int nameID, Vector4 value) => throw null;
        public void SetColor(int nameID, Color value) => throw null;
        public void SetTexture(int nameID, Texture value) => throw null;
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => throw null;
        public static T GetBuiltinResource<T>(string path) where T : Object => throw null;
        public static void UnloadAsset(Object assetToUnload) => throw null;
    }

    public class TextAsset : Object
    {
        public byte[] bytes => throw null;
    }

    public struct CharacterInfo
    {
        public int advance { get => throw null; set => throw null; }
        public int minX { get => throw null; set => throw null; }
        public int maxX { get => throw null; set => throw null; }
        public int minY { get => throw null; set => throw null; }
        public int maxY { get => throw null; set => throw null; }
        public Vector2 uvBottomLeft { get => throw null; set => throw null; }
        public Vector2 uvBottomRight { get => throw null; set => throw null; }
        public Vector2 uvTopLeft { get => throw null; set => throw null; }
        public Vector2 uvTopRight { get => throw null; set => throw null; }
    }

    public sealed class Font : Object
    {
        public static event Action<Font> textureRebuilt;
        public Material material { get => throw null; set => throw null; }
        public int fontSize => throw null;
        public int ascent => throw null;
        public int lineHeight => throw null;
        public void RequestCharactersInTexture(string characters, int size, FontStyle style) => throw null;
        public bool GetCharacterInfo(char ch, out CharacterInfo info, int size, FontStyle style) => throw null;
    }

    public static class GL
    {
        public static Matrix4x4 GetGPUProjectionMatrix(Matrix4x4 proj, bool renderIntoTexture) => throw null;
    }

    public static class Graphics
    {
        public static void ExecuteCommandBuffer(UnityEngine.Rendering.CommandBuffer buffer) => throw null;
    }

    public static class QualitySettings
    {
        public static ColorSpace activeColorSpace => throw null;
    }

    public static class SystemInfo
    {
        public static bool supportsAsyncGPUReadback => throw null;
    }

    public sealed class AudioClip : Object
    {
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream) => throw null;
    }

    public sealed class AudioSource : Behaviour
    {
        public AudioClip clip { get => throw null; set => throw null; }
        public bool loop { get => throw null; set => throw null; }
        public bool playOnAwake { get => throw null; set => throw null; }
        public float spatialBlend { get => throw null; set => throw null; }
        public float volume { get => throw null; set => throw null; }
        public int priority { get => throw null; set => throw null; }
        public bool bypassEffects { get => throw null; set => throw null; }
        public bool bypassListenerEffects { get => throw null; set => throw null; }
        public bool bypassReverbZones { get => throw null; set => throw null; }
        public bool isPlaying => throw null;
        public void Play() => throw null;
        public void Stop() => throw null;
    }

    public sealed class AudioListener : Behaviour
    {
    }

    public sealed class AudioSettings
    {
        public delegate void AudioConfigurationChangeHandler(bool deviceWasChanged);
        public static event AudioConfigurationChangeHandler OnAudioConfigurationChanged;
        public static int outputSampleRate => throw null;
        public static void GetDSPBufferSize(out int bufferLength, out int numBuffers) => throw null;
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
        public static implicit operator RenderTargetIdentifier(Texture tex) => throw null;
        public static implicit operator RenderTargetIdentifier(RenderTexture rt) => throw null;
    }

    public class CommandBuffer : IDisposable
    {
        public string name { get => throw null; set => throw null; }
        public void Clear() => throw null;
        public void Release() => throw null;
        public void Dispose() => throw null;
        public void SetRenderTarget(RenderTargetIdentifier rt, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction) => throw null;
        public void ClearRenderTarget(bool clearDepth, bool clearColor, Color backgroundColor) => throw null;
        public void SetGlobalMatrix(int nameID, Matrix4x4 value) => throw null;
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex, int shaderPass, MaterialPropertyBlock properties) => throw null;
        public void DrawProcedural(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int vertexCount, int instanceCount, MaterialPropertyBlock properties) => throw null;
        public void Blit(Texture source, RenderTargetIdentifier dest) => throw null;
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest) => throw null;
    }

    public struct AsyncGPUReadbackRequest
    {
        public bool done => throw null;
        public bool hasError => throw null;
        public void WaitForCompletion() => throw null;
    }

    public static class AsyncGPUReadback
    {
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref Unity.Collections.NativeArray<T> output, Texture src, int mipIndex, TextureFormat dstFormat,
            Action<AsyncGPUReadbackRequest> callback = null) where T : unmanaged => throw null;
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

        public NativeArray(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory) => throw null;
        public bool IsCreated => m_Buffer != null;
        public int Length => m_Length;
        public void Dispose() => throw null;
        public NativeArray<T> GetSubArray(int start, int length) => throw null;
        public T this[int index] { get => throw null; set => throw null; }
        public static void Copy(NativeArray<T> src, NativeArray<T> dst, int length) => throw null;
        public static void Copy(NativeArray<T> src, T[] dst, int length) => throw null;
        public static void Copy(NativeArray<T> src, int srcIndex, T[] dst, int dstIndex, int length) => throw null;
        public static void Copy(T[] src, int srcIndex, NativeArray<T> dst, int dstIndex, int length) => throw null;
        public static void Copy(NativeArray<T> src, int srcIndex, NativeArray<T> dst, int dstIndex, int length) => throw null;
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    public static unsafe class NativeArrayUnsafeUtility
    {
        public static NativeArray<T> ConvertExistingDataToNativeArray<T>(void* dataPointer, int length, Allocator allocator) where T : unmanaged => throw null;
        public static void* GetUnsafeReadOnlyPtr<T>(NativeArray<T> array) where T : unmanaged => throw null;
    }

    public static unsafe class UnsafeUtility
    {
        public static void* Malloc(long size, int alignment, Allocator allocator) => throw null;
        public static void Free(void* memory, Allocator allocator) => throw null;
        public static void MemCpy(void* destination, void* source, long size) => throw null;
    }
}
