using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Media.Internal;

namespace Ballverse.Media.Tests
{
    /// <summary>
    /// Makes the test thread the media "main thread" with a pumpable synchronization context (also installed as
    /// <see cref="SynchronizationContext.Current"/> so that awaits resume here). Works in the Unity editor (EditMode) and
    /// under plain .NET (Tests/DotNet~). Dispose restores the previous state.
    /// </summary>
    internal sealed class MainThreadPump : SynchronizationContext, IDisposable
    {
        readonly BlockingCollection<(SendOrPostCallback callback, object state)> _queue =
            new BlockingCollection<(SendOrPostCallback callback, object state)>();
        readonly SynchronizationContext _previous;
        readonly IDisposable _scope;

        public MainThreadPump()
        {
            _previous = Current;
            SetSynchronizationContext(this);
            _scope = MainThread.OverrideForTests(this);
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            _queue.Add((d, state));
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            throw new NotSupportedException();
        }

        public override SynchronizationContext CreateCopy()
        {
            return this;
        }

        /// <summary>Runs posted callbacks until <paramref name="done"/> is true; throws <see cref="TimeoutException"/> after <paramref name="timeoutMs"/>.</summary>
        public void RunUntil(Func<bool> done, int timeoutMs = 10000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!done())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not reached within " + timeoutMs + " ms");
                if (_queue.TryTake(out var item, 5)) item.callback(item.state);
            }
        }

        /// <summary>Runs posted callbacks for a while (lets late completions arrive).</summary>
        public void Drain(int milliseconds)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < end)
            {
                if (_queue.TryTake(out var item, 5)) item.callback(item.state);
            }
        }

        public T Wait<T>(Task<T> task, int timeoutMs = 10000)
        {
            RunUntil(() => task.IsCompleted, timeoutMs);
            return task.GetAwaiter().GetResult();
        }

        public void Wait(Task task, int timeoutMs = 10000)
        {
            RunUntil(() => task.IsCompleted, timeoutMs);
            task.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            _scope.Dispose();
            SetSynchronizationContext(_previous);
        }
    }

    /// <summary>
    /// Synthetic <see cref="IFrameSource"/>: a CFR video whose frame k has pts k·10⁶/fps (floor); every decoded buffer
    /// starts with the frame's pts (int64). Optionally blocks the decode of one pts until released.
    /// </summary>
    internal sealed class TestFrameSource : IFrameSource
    {
        public int Fps = 30;
        public long Duration = 30000000;
        public int ReadCount;
        public long BlockPts = long.MinValue;
        public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false);
        public readonly ManualResetEventSlim Release = new ManualResetEventSlim(false);

        public int Width => 4;
        public int Height => 2;
        public bool ToneMapped => false;
        public long DurationUs => Duration;

        public int FrameCount => (int)(Duration * Fps / 1000000);

        public long PtsOfFrame(long index)
        {
            return index * 1000000L / Fps;
        }

        /// <summary>pts of the frame shown at <paramref name="t"/> (last frame with pts ≤ t, clamped to the stream).</summary>
        public long ExpectedPts(long t)
        {
            long k = t <= 0 ? 0 : t * Fps / 1000000;
            while (k + 1 < FrameCount && PtsOfFrame(k + 1) <= t) k++;
            while (k > 0 && PtsOfFrame(k) > t) k--;
            if (k >= FrameCount) k = FrameCount - 1;
            return PtsOfFrame(k);
        }

        public void BeginThread()
        {
        }

        public void Open()
        {
        }

        public IFrameMemory CreateBuffer()
        {
            return new UnmanagedFrameMemory(Width * Height * 4);
        }

        public long ReadFrame(long ptsUs, IFrameMemory destination)
        {
            Interlocked.Increment(ref ReadCount);
            if (ptsUs == Volatile.Read(ref BlockPts))
            {
                Entered.Set();
                Release.Wait(10000);
            }
            long actual = ExpectedPts(ptsUs);
            Marshal.WriteInt64(destination.Pointer, actual);
            return actual;
        }

        public void Close()
        {
        }

        public void EndThread()
        {
        }
    }

    /// <summary>A unique empty temp directory, deleted on dispose.</summary>
    internal sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ballverse-media-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name)
        {
            return System.IO.Path.Combine(Path, name);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    /// <summary>IProgress that runs synchronously on the reporting thread.</summary>
    internal sealed class InlineProgress<T> : IProgress<T>
    {
        readonly Action<T> _action;

        public InlineProgress(Action<T> action)
        {
            _action = action;
        }

        public void Report(T value)
        {
            _action(value);
        }
    }
}
