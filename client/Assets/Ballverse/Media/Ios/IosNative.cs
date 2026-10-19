#if UNITY_IOS
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Ballverse.Media.Ios
{
    /// <summary>
    /// C ABI of Assets/Plugins/iOS/BallverseMedia/*.mm (statically linked: "__Internal"). Strings are UTF-8.
    /// Asynchronous functions take a request id and complete through the callbacks registered with
    /// <see cref="BVMedia_SetCallbacks"/> (status 0 ok + JSON, 1 cancelled, 2 error + {"code","message"}), invoked on
    /// arbitrary GCD threads. Synchronous functions report failures through a thread-local error string
    /// (<see cref="LastError"/>, "CODE|message").
    /// </summary>
    internal static class IosNative
    {
        const string Lib = "__Internal";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ResultCallback(long requestId, int status, IntPtr utf8Json);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ProgressCallback(long requestId, double progress);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Signals
        {
            public int thermal;
            public int lowPower;
            public int reduceMotion;
            public int reduceTransparency;
            public long freeSpaceBytes;
        }

        [DllImport(Lib)] internal static extern void BVMedia_SetCallbacks(ResultCallback onResult, ProgressCallback onProgress);
        [DllImport(Lib)] internal static extern void BVMedia_PickVideo(long requestId, string destinationDirectory, string baseName);
        [DllImport(Lib)] internal static extern void BVMedia_Cancel(long requestId);
        [DllImport(Lib)] internal static extern void BVMedia_Probe(long requestId, string path);
        [DllImport(Lib)] internal static extern void BVMedia_DecodeAudio(long requestId, string path, string outputPath, long startUs, long durationUs);
        [DllImport(Lib)] internal static extern void BVMedia_SaveToGallery(long requestId, string path, string album);
        [DllImport(Lib)] internal static extern void BVMedia_ConcatSegments(long requestId, string segmentsNewlineSeparated, string pcmPath,
            string outputPath, int fps, int audioBitrate, string title, string creationIso8601);
        [DllImport(Lib)] internal static extern void BVMedia_Share(string path, string caption);
        [DllImport(Lib)] internal static extern void BVMedia_GetSignals(out Signals signals);
        [DllImport(Lib)] internal static extern int BVMedia_CopyLastError(byte[] buffer, int capacity);

        [DllImport(Lib)] internal static extern IntPtr BVReader_Open(string path, int maxWidth, int maxHeight, int toneMapToSdr,
            out int width, out int height, out int toneMapped, out long durationUs);
        [DllImport(Lib)] internal static extern long BVReader_Read(IntPtr reader, long ptsUs, IntPtr destination, int capacity);
        [DllImport(Lib)] internal static extern void BVReader_Close(IntPtr reader);

        [DllImport(Lib)] internal static extern IntPtr BVWriter_Open(string path, int width, int height, int fps, int bitrate,
            int keyframeIntervalSeconds, string audioPcmPath, int audioBitrate, string title, string creationIso8601);
        [DllImport(Lib)] internal static extern int BVWriter_Append(IntPtr writer, IntPtr rgba, int rowBytes, long ptsUs, int timeoutMs);
        [DllImport(Lib)] internal static extern int BVWriter_FramesInFlight(IntPtr writer);
        [DllImport(Lib)] internal static extern void BVWriter_Finish(IntPtr writer, long requestId);
        [DllImport(Lib)] internal static extern void BVWriter_Cancel(IntPtr writer);
        /// <summary>Releases the handle (aborting an unfinished writer); completes <paramref name="requestId"/> once the native writer has stopped.</summary>
        [DllImport(Lib)] internal static extern void BVWriter_Release(IntPtr writer, long requestId);

        /// <summary>Thread-local "CODE|message" of the last failed synchronous call on this thread.</summary>
        internal static string LastError()
        {
            var buffer = new byte[1024];
            int length = BVMedia_CopyLastError(buffer, buffer.Length);
            return length <= 0 ? string.Empty : Encoding.UTF8.GetString(buffer, 0, Math.Min(length, buffer.Length));
        }

        internal static string Utf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return string.Empty;
            int length = 0;
            while (Marshal.ReadByte(p, length) != 0) length++;
            if (length == 0) return string.Empty;
            var bytes = new byte[length];
            Marshal.Copy(p, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
#endif
