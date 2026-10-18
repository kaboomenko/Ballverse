using Ballverse.Media.Internal;

namespace Ballverse.Media
{
    /// <summary>Creates the platform <see cref="IMediaBridge"/>. Call once from the main thread (composition root) and dispose on shutdown.</summary>
    public static class MediaBridgeFactory
    {
        /// <summary>
        /// Android player → Java MediaCodec bridge; iOS player → AVFoundation bridge; editor and desktop players →
        /// <see cref="MediaBackend.Fake"/> (synthetic test-pattern video, PNG sequence + WAV output).
        /// </summary>
        /// <param name="fakeOptions">Settings of the fake backend (ignored on devices); null = defaults.</param>
        public static IMediaBridge Create(FakeMediaOptions fakeOptions = null)
        {
            MainThread.Capture();
            MainThread.Assert(nameof(MediaBridgeFactory) + "." + nameof(Create));
#if UNITY_ANDROID && !UNITY_EDITOR
            return new Android.AndroidMediaBridge();
#elif UNITY_IOS && !UNITY_EDITOR
            return new Ios.IosMediaBridge();
#else
            return new Fake.FakeMediaBridge(fakeOptions ?? new FakeMediaOptions());
#endif
        }

        /// <summary>Always the fake backend (tests, editor tooling, device builds that want synthetic media).</summary>
        public static IMediaBridge CreateFake(FakeMediaOptions options = null)
        {
            MainThread.Capture();
            MainThread.Assert(nameof(MediaBridgeFactory) + "." + nameof(CreateFake));
            return new Fake.FakeMediaBridge(options ?? new FakeMediaOptions());
        }
    }
}
