using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Ballverse.Media.Internal
{
    /// <summary>
    /// Platform decoder behind <see cref="PrefetchVideoReader"/>. Every member is called on the reader's single worker
    /// thread, between <see cref="BeginThread"/> and <see cref="EndThread"/>, never concurrently.
    /// </summary>
    internal interface IFrameSource
    {
        /// <summary>First call on the worker thread (Android: attach the thread to the JVM).</summary>
        void BeginThread();

        /// <summary>Opens the decoder and decodes the first frame. Throws <see cref="MediaException"/>.</summary>
        void Open();

        int Width { get; }
        int Height { get; }
        bool ToneMapped { get; }
        long DurationUs { get; }

        /// <summary>Allocates one output buffer of Width × Height × 4 bytes.</summary>
        IFrameMemory CreateBuffer();

        /// <summary>Decodes the frame shown at <paramref name="ptsUs"/> into <paramref name="destination"/>; returns its pts. Throws <see cref="MediaException"/>.</summary>
        long ReadFrame(long ptsUs, IFrameMemory destination);

        /// <summary>Releases the decoder (also after a failed <see cref="Open"/>).</summary>
        void Close();

        /// <summary>Last call on the worker thread (Android: detach).</summary>
        void EndThread();
    }

    /// <summary>
    /// Shared <see cref="IVideoReader"/> implementation: request queue, 3-buffer pool and one worker thread that drives
    /// a synchronous <see cref="IFrameSource"/>. The public object only signals the worker; the worker owns the native
    /// decoder, so a leaked reader is finalized safely (the finalizer just asks the worker to stop and close the decoder
    /// on its own thread).
    /// </summary>
    internal sealed class PrefetchVideoReader : IVideoReader
    {
        public const int FramesInFlight = 3;

        readonly State _state;
        bool _disposed;

        public PrefetchVideoReader(IFrameSource source)
        {
            _state = new State(source);
            _state.Start();
        }

        ~PrefetchVideoReader()
        {
            _state.RequestStop();
        }

        public Task Ready => _state.ReadyTcs.Task;
        public bool IsOpen => _state.IsOpen;
        public int Width => _state.Width;
        public int Height => _state.Height;
        public bool ToneMappedToSdr => _state.ToneMapped;
        public long DurationUs => _state.DurationUs;
        public int MaxFramesInFlight => FramesInFlight;

        public bool TryReadFrameAt(long ptsUs, out VideoFrame frame)
        {
            MainThread.Assert(nameof(TryReadFrameAt));
            ThrowIfDisposed();
            return _state.TryRead(ptsUs, out frame);
        }

        public void Prefetch(long ptsUs)
        {
            MainThread.Assert(nameof(Prefetch));
            ThrowIfDisposed();
            _state.Prefetch(ptsUs);
        }

        public VideoFrame ReadFrameAt(long ptsUs, int timeoutMs)
        {
            MainThread.Assert(nameof(ReadFrameAt));
            ThrowIfDisposed();
            return _state.ReadBlocking(ptsUs, timeoutMs);
        }

        public Task<VideoFrame> ReadFrameAtAsync(long ptsUs, CancellationToken ct)
        {
            MainThread.Assert(nameof(ReadFrameAtAsync));
            ThrowIfDisposed();
            return _state.ReadAsync(ptsUs, ct);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _state.RequestStop();
            GC.SuppressFinalize(this);
        }

        void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(IVideoReader));
        }

        sealed class State
        {
            readonly object _lock = new object();
            readonly IFrameSource _source;
            readonly FramePool _pool = new FramePool(FramesInFlight);
            readonly List<long> _pending = new List<long>();
            readonly Dictionary<long, VideoFrame> _ready = new Dictionary<long, VideoFrame>();
            readonly Dictionary<long, TaskCompletionSource<VideoFrame>> _waiters = new Dictionary<long, TaskCompletionSource<VideoFrame>>();
            public readonly TaskCompletionSource<bool> ReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            volatile bool _stop;
            volatile bool _open;
            bool _hasInProgress;
            bool _inProgressWanted;
            long _inProgress;
            long _lastRead = long.MinValue;
            MediaException _error;
            int _width, _height;
            bool _toneMapped;
            long _durationUs;

            public State(IFrameSource source)
            {
                _source = source;
            }

            public bool IsOpen => _open;
            public int Width { get { lock (_lock) return _width; } }
            public int Height { get { lock (_lock) return _height; } }
            public bool ToneMapped { get { lock (_lock) return _toneMapped; } }
            public long DurationUs { get { lock (_lock) return _durationUs; } }

            public void Start()
            {
                var thread = new Thread(Run) { IsBackground = true, Name = "Ballverse.VideoReader" };
                thread.Start();
            }

            public void RequestStop()
            {
                lock (_lock)
                {
                    _stop = true;
                    Monitor.PulseAll(_lock);
                }
            }

            public bool TryRead(long pts, out VideoFrame frame)
            {
                lock (_lock)
                {
                    ThrowIfError();
                    frame = TakeReadyLocked(pts);
                    if (frame == null) RequestReadLocked(pts);
                }
                return frame != null;
            }

            public void Prefetch(long pts)
            {
                lock (_lock)
                {
                    ThrowIfError();
                    if (pts < _lastRead || _ready.ContainsKey(pts) || IsQueuedLocked(pts)) return;
                    InsertPendingLocked(pts);
                }
            }

            public VideoFrame ReadBlocking(long pts, int timeoutMs)
            {
                if (timeoutMs < 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                lock (_lock)
                {
                    ThrowIfError();
                    VideoFrame frame = TakeReadyLocked(pts);
                    if (frame != null) return frame;
                    RequestReadLocked(pts);
                    while (true)
                    {
                        int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                        if (remaining <= 0)
                        {
                            throw new MediaException(MediaErrorCode.Timeout,
                                "frame at " + pts + " us not decoded within " + timeoutMs + " ms (are earlier VideoFrames disposed?)");
                        }
                        Monitor.Wait(_lock, remaining);
                        ThrowIfError();
                        if (_stop) throw new ObjectDisposedException(nameof(IVideoReader));
                        if (_ready.TryGetValue(pts, out frame))
                        {
                            _ready.Remove(pts);
                            return frame;
                        }
                        if (!IsQueuedLocked(pts)) RequestReadLocked(pts); // dropped meanwhile (should not happen): ask again
                    }
                }
            }

            public Task<VideoFrame> ReadAsync(long pts, CancellationToken ct)
            {
                if (ct.IsCancellationRequested) return Task.FromCanceled<VideoFrame>(ct);
                TaskCompletionSource<VideoFrame> tcs;
                lock (_lock)
                {
                    if (_error != null) return Task.FromException<VideoFrame>(_error);
                    VideoFrame ready = TakeReadyLocked(pts);
                    if (ready != null) return Task.FromResult(ready);
                    if (_waiters.ContainsKey(pts)) throw new InvalidOperationException("a read of " + pts + " us is already pending");
                    tcs = new TaskCompletionSource<VideoFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _waiters[pts] = tcs;
                    RequestReadLocked(pts);
                }
                if (ct.CanBeCanceled)
                {
                    CancellationTokenRegistration reg = ct.Register(() =>
                    {
                        lock (_lock)
                        {
                            if (_waiters.TryGetValue(pts, out var current) && current == tcs) _waiters.Remove(pts);
                        }
                        tcs.TrySetCanceled(ct);
                    });
                    tcs.Task.ContinueWith(_ => reg.Dispose(), TaskScheduler.Default);
                }
                return tcs.Task;
            }

            // A read of pts: hand out the ready frame (if any) and drop older ready/pending frames (forward reading).
            VideoFrame TakeReadyLocked(long pts)
            {
                if (!_ready.TryGetValue(pts, out VideoFrame frame)) return null;
                _ready.Remove(pts);
                DropStaleBeforeLocked(pts);
                _lastRead = pts;
                return frame;
            }

            // A read of pts that is not ready. Backward jump (pts < last read): drop everything prefetched. Forward:
            // drop older entries, and return speculative frames prefetched beyond pts to the queue so that their
            // buffers are free for pts (they are decoded again later if still wanted).
            void RequestReadLocked(long pts)
            {
                if (pts < _lastRead)
                {
                    var keys = new List<long>(_ready.Keys);
                    foreach (long k in keys)
                    {
                        if (_waiters.ContainsKey(k)) continue;
                        _ready[k].Dispose();
                        _ready.Remove(k);
                    }
                    _pending.RemoveAll(p => !_waiters.ContainsKey(p));
                    if (_hasInProgress && _inProgress != pts && !_waiters.ContainsKey(_inProgress)) _inProgressWanted = false;
                }
                else
                {
                    DropStaleBeforeLocked(pts);
                    var later = new List<long>();
                    foreach (long k in _ready.Keys) if (k > pts && !_waiters.ContainsKey(k)) later.Add(k);
                    foreach (long k in later)
                    {
                        _ready[k].Dispose();
                        _ready.Remove(k);
                        InsertPendingLocked(k);
                    }
                }
                _lastRead = pts;
                if (_hasInProgress && _inProgress == pts) _inProgressWanted = true;
                else if (!_pending.Contains(pts)) InsertPendingLocked(pts);
                Monitor.PulseAll(_lock);
            }

            bool IsQueuedLocked(long pts)
            {
                return _pending.Contains(pts) || (_hasInProgress && _inProgressWanted && _inProgress == pts);
            }

            void InsertPendingLocked(long pts)
            {
                int index = _pending.BinarySearch(pts);
                if (index >= 0) return;
                _pending.Insert(~index, pts);
                Monitor.PulseAll(_lock);
            }

            // Lock order: State._lock may be held while frames return to the FramePool (FramePool never calls back into
            // State), so disposing dropped frames inside the lock is deadlock-free and keeps buffers flowing.
            void DropStaleBeforeLocked(long pts)
            {
                List<long> keys = null;
                foreach (var pair in _ready)
                {
                    if (pair.Key >= pts || _waiters.ContainsKey(pair.Key)) continue;
                    (keys ??= new List<long>()).Add(pair.Key);
                }
                if (keys != null)
                {
                    foreach (long k in keys)
                    {
                        _ready[k].Dispose();
                        _ready.Remove(k);
                    }
                }
                _pending.RemoveAll(p => p < pts && !_waiters.ContainsKey(p));
                if (_hasInProgress && _inProgress < pts && !_waiters.ContainsKey(_inProgress)) _inProgressWanted = false;
            }

            void ThrowIfError()
            {
                if (_error != null) throw _error;
            }

            void Run()
            {
                try
                {
                    _source.BeginThread();
                }
                catch (Exception e)
                {
                    Fail(e);
                    return;
                }
                try
                {
                    try
                    {
                        _source.Open();
                        lock (_lock)
                        {
                            _width = _source.Width;
                            _height = _source.Height;
                            _toneMapped = _source.ToneMapped;
                            _durationUs = _source.DurationUs;
                            _open = true;
                            Monitor.PulseAll(_lock);
                        }
                        ReadyTcs.TrySetResult(true);
                    }
                    catch (Exception e)
                    {
                        Fail(e);
                        return;
                    }
                    Loop();
                }
                finally
                {
                    Shutdown();
                }
            }

            void Loop()
            {
                while (true)
                {
                    long pts;
                    lock (_lock)
                    {
                        while (!_stop && _pending.Count == 0) Monitor.Wait(_lock);
                        if (_stop) return;
                        pts = _pending[0];
                        _pending.RemoveAt(0);
                        _inProgress = pts;
                        _hasInProgress = true;
                        _inProgressWanted = true;
                    }

                    IFrameMemory memory;
                    try
                    {
                        memory = _pool.Acquire(_source.CreateBuffer, () => _stop);
                    }
                    catch (Exception e)
                    {
                        Fail(e);
                        return;
                    }
                    if (memory == null) return;

                    bool wanted;
                    lock (_lock) wanted = _inProgressWanted && !_stop;
                    if (!wanted)
                    {
                        _pool.Return(memory);
                        lock (_lock) _hasInProgress = false;
                        continue;
                    }

                    long actual;
                    try
                    {
                        actual = _source.ReadFrame(pts, memory);
                    }
                    catch (Exception e)
                    {
                        _pool.Return(memory);
                        Fail(e);
                        return;
                    }

                    int w, h;
                    lock (_lock) { w = _width; h = _height; }
                    var frame = new VideoFrame(_pool, memory, w, h, actual, pts);
                    TaskCompletionSource<VideoFrame> waiter = null;
                    bool keep;
                    lock (_lock)
                    {
                        keep = _inProgressWanted && !_stop;
                        _hasInProgress = false;
                        if (keep)
                        {
                            if (_waiters.TryGetValue(pts, out waiter)) _waiters.Remove(pts);
                            else _ready[pts] = frame;
                        }
                        Monitor.PulseAll(_lock);
                    }
                    if (!keep)
                    {
                        frame.Dispose();
                    }
                    else if (waiter != null && !waiter.TrySetResult(frame))
                    {
                        lock (_lock)
                        {
                            if (_stop) keep = false;
                            else _ready[pts] = frame;
                        }
                        if (!keep) frame.Dispose();
                    }
                }
            }

            void Fail(Exception e)
            {
                MediaException error = e as MediaException ?? new MediaException(MediaErrorCode.DecoderFailed, e.Message, e);
                List<TaskCompletionSource<VideoFrame>> waiters;
                lock (_lock)
                {
                    if (_error == null) _error = error;
                    waiters = new List<TaskCompletionSource<VideoFrame>>(_waiters.Values);
                    _waiters.Clear();
                    Monitor.PulseAll(_lock);
                }
                ReadyTcs.TrySetException(_error);
                foreach (var w in waiters) w.TrySetException(_error);
            }

            void Shutdown()
            {
                List<VideoFrame> ready;
                List<TaskCompletionSource<VideoFrame>> waiters;
                lock (_lock)
                {
                    _stop = true;
                    _open = false;
                    ready = new List<VideoFrame>(_ready.Values);
                    _ready.Clear();
                    _pending.Clear();
                    waiters = new List<TaskCompletionSource<VideoFrame>>(_waiters.Values);
                    _waiters.Clear();
                    Monitor.PulseAll(_lock);
                }
                foreach (var w in waiters) w.TrySetCanceled();
                ReadyTcs.TrySetCanceled();
                foreach (VideoFrame f in ready) f.Dispose();
                _pool.Dispose();
                try
                {
                    _source.Close();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                try
                {
                    _source.EndThread();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}
