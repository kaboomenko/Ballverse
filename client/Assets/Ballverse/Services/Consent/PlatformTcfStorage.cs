#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;

namespace Ballverse.Services
{
    /// <summary>
    /// Android: the TCF values UMP writes into the default SharedPreferences (<c>&lt;package&gt;_preferences</c>, the file
    /// <c>PreferenceManager.getDefaultSharedPreferences</c> returns; IAB TCF v2 "where the values are stored"). Unity's own
    /// PlayerPrefs live in a different file, so they are read through JNI. Main thread; never throws.
    /// </summary>
    public sealed class AndroidTcfStorage : ITcfStorage
    {
        const string Tag = "Consent";
        const int ModePrivate = 0;

        readonly IServiceLog _log;

        public AndroidTcfStorage(IServiceLog log)
        {
            _log = log ?? NullServiceLog.Instance;
        }

        public TcfSignals Read()
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    if (activity == null) return default;
                    string file = activity.Call<string>("getPackageName") + "_preferences";
                    using (var prefs = activity.Call<AndroidJavaObject>("getSharedPreferences", file, ModePrivate))
                    {
                        if (prefs == null) return default;
                        bool? gdprApplies = null;
                        if (prefs.Call<bool>("contains", TcfSignals.GdprAppliesKey))
                        {
                            try
                            {
                                int value = prefs.Call<int>("getInt", TcfSignals.GdprAppliesKey, -1);
                                if (value == 1) gdprApplies = true;
                                else if (value == 0) gdprApplies = false;
                            }
                            catch (AndroidJavaException)
                            {
                                // stored with another type by some other CMP: treat as unknown
                            }
                        }
                        string purposes = null;
                        try
                        {
                            purposes = prefs.Call<string>("getString", TcfSignals.PurposeConsentsKey, string.Empty);
                        }
                        catch (AndroidJavaException)
                        {
                            // wrong type: unknown
                        }
                        return new TcfSignals(gdprApplies, purposes);
                    }
                }
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Reading the TCF consent values failed.", e);
                return default;
            }
        }
    }
}
#endif
#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

namespace Ballverse.Services
{
    /// <summary>
    /// iOS: the TCF values UMP writes into <c>NSUserDefaults.standardUserDefaults</c>, read by the native bridge
    /// <c>Plugins/iOS/BallverseTracking.mm</c>. Never throws.
    /// </summary>
    public sealed class IosTcfStorage : ITcfStorage
    {
        const string Tag = "Consent";

        [DllImport("__Internal")]
        static extern int BallverseTcf_GetGdprApplies();

        // The native side returns a malloc'ed UTF-8 copy (or NULL); the marshaller frees it.
        [DllImport("__Internal")]
        static extern string BallverseTcf_CopyPurposeConsents();

        readonly IServiceLog _log;

        public IosTcfStorage(IServiceLog log)
        {
            _log = log ?? NullServiceLog.Instance;
        }

        public TcfSignals Read()
        {
            try
            {
                int gdpr = BallverseTcf_GetGdprApplies();
                bool? gdprApplies = gdpr == 1 ? true : gdpr == 0 ? false : (bool?)null;
                return new TcfSignals(gdprApplies, BallverseTcf_CopyPurposeConsents());
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Reading the TCF consent values failed.", e);
                return default;
            }
        }
    }
}
#endif
