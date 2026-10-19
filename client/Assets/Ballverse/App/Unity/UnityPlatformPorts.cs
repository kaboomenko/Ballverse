using System;
using System.IO;
using Ballverse.UI;
using Ballverse.UI.Components;
using Ballverse.UI.Localization;
using UnityEngine;

namespace Ballverse.App
{
    /// <summary><see cref="IDeviceInfo"/> from Unity's <see cref="SystemInfo"/> and <see cref="Application"/>.</summary>
    public sealed class UnityDeviceInfo : IDeviceInfo
    {
        public string Platform
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.Android: return "android";
                    case RuntimePlatform.IPhonePlayer: return "ios";
                    default: return "editor";
                }
            }
        }

        public string Model => SystemInfo.deviceModel ?? "";
        public string GpuName => SystemInfo.graphicsDeviceName ?? "";
        public int SystemMemoryMb => SystemInfo.systemMemorySize;
        public string LanguageTag => ResourcesTableSource.SystemLanguageTag();

        public float FontScale
        {
            get
            {
#if BALLVERSE_ACCESSIBILITY && (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
                return UnityEngine.Accessibility.AccessibilitySettings.fontScale;
#else
                return 1f;
#endif
            }
        }

        public string AppVersion => Application.version;

        public float ScreenWidthPoints
        {
            get
            {
                float dpi = Screen.dpi;
                return dpi > 1f ? Mathf.Min(Screen.width, Screen.height) * 160f / dpi : 0f;
            }
        }
    }

    /// <summary><see cref="IPlatformActions"/> on Android (JNI) / iOS (URL schemes) / the editor.</summary>
    public sealed class UnityPlatformActions : IPlatformActions
    {
        /// <summary>Store id placeholder until the store listings exist (bundle id com.ballverse.app; the owner sets the App Store id).</summary>
        public const string AppStoreId = "";

        public void MoveTaskToBack()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                activity.Call<bool>("moveTaskToBack", true);
            }
#endif
        }

        public void OpenUrl(string url)
        {
            if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
        }

        public void OpenStorePage()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!string.IsNullOrEmpty(AppStoreId)) Application.OpenURL("itms-apps://itunes.apple.com/app/id" + AppStoreId + "?action=write-review");
#else
            Application.OpenURL("market://details?id=" + Application.identifier);
#endif
        }

        public void SendFeedback(string address, string subject, string body)
        {
            Application.OpenURL("mailto:" + address + "?subject=" + Uri.EscapeDataString(subject ?? "") + "&body=" + Uri.EscapeDataString(body ?? ""));
        }

        public void CopyToClipboard(string text)
        {
            GUIUtility.systemCopyBuffer = text ?? "";
        }
    }

    /// <summary><see cref="ITextureLoader"/>: PNG/JPEG from disk into a <see cref="Texture2D"/>.</summary>
    public sealed class UnityTextureLoader : ITextureLoader
    {
        public Texture Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            byte[] bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = "BallverseCover" };
            if (tex.LoadImage(bytes, true)) return tex;
            Release(tex);
            return null;
        }

        public void Release(Texture texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    /// <summary><see cref="IAppUi"/> over the UI Toolkit shell (<see cref="BallverseUIRoot"/>).</summary>
    public sealed class UnityAppUi : IAppUi
    {
        readonly BallverseUIRoot _root;
        bool _disposed;

        public UnityAppUi(BallverseUIRoot root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(root.gameObject);
            _root.Shell.Navigator.ExitRequested += OnExit;
        }

        public UiShell Shell => _root.Shell;

        public event Action ExitRequested;

        public void OpenMode(string modeId) => Shell.OpenMode(modeId);
        public void ShowToast(LocalizedText text) => Shell.ShowToast(text);
        public void ShowAdNotice(Action shown) => Shell.ShowAdNotice(shown);
        public void ShowTrackingPrePrompt() => Shell.ShowTrackingPrePrompt();

        public void DismissTrackingPrePrompt()
        {
            if (Shell.Navigator.Current == UI.Navigation.ScreenId.TrackingPrePrompt) Shell.Navigator.Pop();
        }

        public void ShowPhotosensitivityWarning(Action acknowledged) => Shell.ShowPhotosensitivityWarning(acknowledged);

        public IDisposable ShowChoice(AppChoice choice)
        {
            if (choice == null) throw new ArgumentNullException(nameof(choice));
            var spec = new DialogSpec
            {
                Title = choice.Title,
                Body = choice.Body,
                Cancellable = true,
                Cancelled = choice.Cancelled,
            };
            for (int i = 0; i < choice.Options.Count; i++)
            {
                AppChoiceOption option = choice.Options[i];
                spec.Buttons.Add(new DialogButton(option.Label, i == 0 ? DialogButtonStyle.Primary : DialogButtonStyle.Secondary,
                    _ => option.Chosen?.Invoke()));
            }
            DialogHandle handle = Shell.Dialogs.Show(spec);
            return new ActionDisposable(() =>
            {
                if (handle.IsOpen) handle.Close();
            });
        }

        public void SetDeviceTier(Core.DeviceTier tier) => Shell.SetDeviceTier(tier);

        void OnExit() => ExitRequested?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_root != null)
            {
                if (_root.Shell != null) _root.Shell.Navigator.ExitRequested -= OnExit;
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root.gameObject);
                else UnityEngine.Object.DestroyImmediate(_root.gameObject);
            }
        }
    }

    /// <summary>Drives the composition from the Unity player loop (Update → Tick, LateUpdate → LateTick).</summary>
    [DefaultExecutionOrder(-5000)]
    public sealed class AppRunner : MonoBehaviour
    {
        Action<float> _tick;
        Action<float> _late;

        public static IDisposable Create(Action<float> tick, Action<float> late)
        {
            var go = new GameObject("BallverseAppRunner") { hideFlags = HideFlags.HideInHierarchy };
            if (Application.isPlaying) DontDestroyOnLoad(go);
            var runner = go.AddComponent<AppRunner>();
            runner._tick = tick;
            runner._late = late;
            return new ActionDisposable(() =>
            {
                runner._tick = null;
                runner._late = null;
                if (go == null) return;
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            });
        }

        void Update()
        {
            try
            {
                _tick?.Invoke(Time.unscaledDeltaTime);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void LateUpdate()
        {
            try
            {
                _late?.Invoke(Time.unscaledDeltaTime);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
