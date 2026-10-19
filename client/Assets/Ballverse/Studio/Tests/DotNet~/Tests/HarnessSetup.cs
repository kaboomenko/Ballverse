using Xunit;

// The Media layer keeps its main-thread binding in static state (MainThread captures the pump's synchronization context),
// so harness test classes that drive the fake backend must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Ballverse.Studio.Tests
{
    using System;
    using System.IO;
    using System.Threading;
    using Ballverse.Media.Internal;
    using Ballverse.Media.Tests;

    static class HarnessPump
    {
        /// <summary>
        /// Pumps until every writer of the process that is being released has settled. The Media layer tracks writers process-wide
        /// and OpenWriterAsync waits for every release in progress; a release whose completion was posted to a test's pump must
        /// finish before that pump goes away, or the next test's segment open would wait for it forever (on a device there is
        /// one main thread, which always keeps pumping).
        /// </summary>
        public static void SettleWriters(this MainThreadPump pump, string anyDirectory)
        {
            try
            {
                pump.Wait(MediaBridgeBase.WaitUntilWriterCanOpenAsync(Path.Combine(anyDirectory, "settle-probe.mp4"), CancellationToken.None), 30000);
            }
            catch (Exception e) when (e is TimeoutException || e is InvalidOperationException)
            {
                // reported by the test that left it; never mask that test's own failure here
            }
        }
    }
}
