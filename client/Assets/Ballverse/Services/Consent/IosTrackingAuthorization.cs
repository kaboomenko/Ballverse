#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

namespace Ballverse.Services
{
    /// <summary>
    /// App Tracking Transparency through the tiny native bridge <c>Plugins/iOS/BallverseTracking.mm</c>
    /// (<c>ATTrackingManager</c>). The completion handler arrives on an arbitrary thread and is moved to the main thread.
    /// The Info.plist text (NSUserTrackingUsageDescription) comes from the Google Mobile Ads settings or, when empty, from the
    /// build step <c>Editor/IosTrackingUsageDescription.cs</c>; without it the bridge skips the request (iOS would terminate
    /// the app) and reports the status unchanged.
    /// </summary>
    public sealed class IosTrackingAuthorization : ITrackingAuthorization
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void NativeCallback(int status);

        [DllImport("__Internal")]
        static extern int BallverseAtt_GetStatus();

        [DllImport("__Internal")]
        static extern void BallverseAtt_Request(NativeCallback callback);

        static readonly NativeCallback s_callback = OnNativeResult;
        static MainThreadQueue s_queue;
        static Action<TrackingAuthorizationStatus> s_pending;

        public IosTrackingAuthorization(MainThreadQueue queue)
        {
            s_queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        public TrackingAuthorizationStatus Status => Map(BallverseAtt_GetStatus());

        public void Request(Action<TrackingAuthorizationStatus> done)
        {
            var status = Status;
            if (status != TrackingAuthorizationStatus.NotDetermined)
            {
                done?.Invoke(status);
                return;
            }
            bool alreadyWaiting = s_pending != null;
            s_pending += done ?? (_ => { });
            if (!alreadyWaiting) BallverseAtt_Request(s_callback);
        }

        [AOT.MonoPInvokeCallback(typeof(NativeCallback))]
        static void OnNativeResult(int status)
        {
            var queue = s_queue;
            if (queue == null) return;
            queue.Post(() =>
            {
                var pending = s_pending;
                s_pending = null;
                pending?.Invoke(Map(status));
            });
        }

        static TrackingAuthorizationStatus Map(int status)
        {
            switch (status)
            {
                case 0: return TrackingAuthorizationStatus.NotDetermined;
                case 1: return TrackingAuthorizationStatus.Restricted;
                case 2: return TrackingAuthorizationStatus.Denied;
                case 3: return TrackingAuthorizationStatus.Authorized;
                default: return TrackingAuthorizationStatus.NotApplicable;
            }
        }
    }
}
#endif
