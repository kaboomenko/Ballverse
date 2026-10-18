using System;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Media.Internal;

namespace Ballverse.Media
{
    /// <summary>Helpers on <see cref="IMediaBridge"/> that need no platform code (wrappers/decorators get them for free).</summary>
    public static class MediaBridgeExtensions
    {
        /// <summary>
        /// Like <see cref="IMediaBridge.OpenWriter"/>, but first waits until the native side of every writer that is being
        /// released (disposed, any path — frees the hardware encoder) and every earlier writer for the same output path
        /// has stopped (<see cref="IVideoWriter.Released"/>). Use it for the §3.8 segment redo/retry after disposing an
        /// interrupted writer. Main thread; the writer is opened through <paramref name="bridge"/>.<see cref="IMediaBridge.OpenWriter"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">A writer for the same path is still open (never disposed) or a
        /// <see cref="IMediaBridge.ConcatSegmentsAsync"/> is writing it: waiting would not end.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled while waiting (nothing opened).</exception>
        public static async Task<IVideoWriter> OpenWriterAsync(this IMediaBridge bridge, string outputPath, VideoWriterOptions options,
            CancellationToken ct)
        {
            if (bridge == null) throw new ArgumentNullException(nameof(bridge));
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentNullException(nameof(outputPath));
            if (options == null) throw new ArgumentNullException(nameof(options));
            await MediaBridgeBase.WaitUntilWriterCanOpenAsync(outputPath, ct);
            return bridge.OpenWriter(outputPath, options);
        }
    }
}
