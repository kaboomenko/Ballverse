using System;
using System.Threading;
using Ballverse.Media.Internal;
using Unity.Collections;

namespace Ballverse.Media
{
    /// <summary>
    /// A decoded RGBA32 frame (top-down, tightly packed: <see cref="RowBytes"/> = <see cref="Width"/> × 4) lent from a
    /// reader's pool. Dispose it as soon as the pixels are uploaded (e.g. <c>Texture2D.LoadRawTextureData</c> +
    /// <c>Apply</c>) or copied; <see cref="Data"/> must not be used after disposal. A finalizer returns a leaked buffer to
    /// the pool, but until then the reader has one buffer less (3 per reader).
    /// </summary>
    public sealed class VideoFrame : IDisposable
    {
        FramePool _pool;
        IFrameMemory _memory;
        readonly int _length;

        internal VideoFrame(FramePool pool, IFrameMemory memory, int width, int height, long ptsUs, long requestedPtsUs)
        {
            _pool = pool;
            _memory = memory;
            Width = width;
            Height = height;
            PtsUs = ptsUs;
            RequestedPtsUs = requestedPtsUs;
            _length = width * height * 4;
        }

        public int Width { get; }
        public int Height { get; }
        public int RowBytes => Width * 4;
        /// <summary>Presentation time of the source frame, µs (≤ <see cref="RequestedPtsUs"/> except before the first frame).</summary>
        public long PtsUs { get; }
        /// <summary>The time that was asked for.</summary>
        public long RequestedPtsUs { get; }

        public bool IsDisposed => Volatile.Read(ref _memory) == null;

        /// <summary>
    /// Pixel bytes (RGBA32, length Width × Height × 4). Main thread only; valid until <see cref="Dispose"/>.
    /// Read-only: do not write into it (the Android reader copies an already converted buffer for repeated source frames).
    /// </summary>
        public NativeArray<byte> Data
        {
            get
            {
                IFrameMemory m = Volatile.Read(ref _memory);
                if (m == null) throw new ObjectDisposedException(nameof(VideoFrame));
                NativeArray<byte> view = m.View;
                return view.Length == _length ? view : view.GetSubArray(0, _length);
            }
        }

        /// <summary>Copies the pixels into a managed array of at least Width × Height × 4 bytes.</summary>
        public void CopyTo(byte[] destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (destination.Length < _length) throw new ArgumentException("destination too small", nameof(destination));
            NativeArray<byte>.Copy(Data, destination, _length);
        }

        public void Dispose()
        {
            ReturnToPool();
            GC.SuppressFinalize(this);
        }

        ~VideoFrame()
        {
            ReturnToPool();
        }

        void ReturnToPool()
        {
            IFrameMemory m = Interlocked.Exchange(ref _memory, null);
            if (m == null) return;
            FramePool pool = _pool;
            _pool = null;
            pool.Return(m);
        }
    }
}
