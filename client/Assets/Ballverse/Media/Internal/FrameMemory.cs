using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Ballverse.Media.Internal
{
    /// <summary>
    /// One native frame buffer (RGBA32, Width × Height × 4 bytes) owned by a <see cref="FramePool"/>. Buffers are created
    /// on the reader worker thread, written there by the decoder through <see cref="Pointer"/>, and read on the main
    /// thread through <see cref="View"/>. <see cref="Release"/> may be called from any thread.
    /// </summary>
    internal interface IFrameMemory
    {
        int Length { get; }
        /// <summary>Stable address for native writers (valid until <see cref="Release"/>).</summary>
        IntPtr Pointer { get; }
        /// <summary>NativeArray view over the memory; main thread only (the editor safety handle is created lazily there).</summary>
        NativeArray<byte> View { get; }
        /// <summary>Frees the memory (deferred to the main thread). Any thread.</summary>
        void Release();
    }

    /// <summary>
    /// Shared part of frame memories: a lazily created NativeArray view (with its own safety handle when collections checks
    /// are enabled) and main-thread release.
    /// </summary>
    internal abstract class FrameMemoryBase : IFrameMemory
    {
        NativeArray<byte> _view;
        object _safety;
        bool _hasView;
        bool _released;

        protected FrameMemoryBase(IntPtr pointer, int length)
        {
            Pointer = pointer;
            Length = length;
        }

        public int Length { get; }
        public IntPtr Pointer { get; }

        public NativeArray<byte> View
        {
            get
            {
                MainThread.Assert("VideoFrame.Data");
                if (_released) throw new ObjectDisposedException(nameof(VideoFrame));
                if (!_hasView)
                {
                    _view = ForeignMemoryView.Create(Pointer, Length, out _safety);
                    _hasView = true;
                }
                return _view;
            }
        }

        public void Release()
        {
            MainThread.RunOrInline(() =>
            {
                if (_released) return;
                _released = true;
                if (_hasView) ForeignMemoryView.Release(_safety);
                _hasView = false;
                _view = default;
                FreeMemory();
            });
        }

        /// <summary>Frees the underlying memory (main thread, once).</summary>
        protected abstract void FreeMemory();
    }

    /// <summary>Frame memory from <see cref="UnsafeUtility.Malloc"/> (thread-safe allocation; iOS and fake backends).</summary>
    internal sealed unsafe class UnmanagedFrameMemory : FrameMemoryBase
    {
        public UnmanagedFrameMemory(int length)
            : base((IntPtr)UnsafeUtility.Malloc(length, 16, Allocator.Persistent), length)
        {
        }

        protected override void FreeMemory()
        {
            UnsafeUtility.Free((void*)Pointer, Allocator.Persistent);
        }
    }

    /// <summary>Wraps memory owned elsewhere in a NativeArray view, with an editor safety handle when checks are on.</summary>
    internal static unsafe class ForeignMemoryView
    {
        public static NativeArray<byte> Create(IntPtr pointer, int length, out object safety)
        {
            NativeArray<byte> view = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>((void*)pointer, length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle handle = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref view, handle);
            safety = handle;
#else
            safety = null;
#endif
            return view;
        }

        public static void Release(object safety)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (safety is AtomicSafetyHandle handle) AtomicSafetyHandle.Release(handle);
#endif
        }
    }
}
