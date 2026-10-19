using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Sim;
using Ballverse.UI.Localization;
using Ballverse.UI.ViewModels;
using UnityEngine;

namespace Ballverse.App
{
    /// <summary>Static facts about the device (Unity <c>SystemInfo</c> / <c>Application</c> on device).</summary>
    public interface IDeviceInfo
    {
        /// <summary>"android", "ios" or "editor".</summary>
        string Platform { get; }
        /// <summary>Device model, e.g. "iPhone14,5" or "samsung SM-S911B".</summary>
        string Model { get; }
        /// <summary>GPU name, e.g. "Adreno (TM) 740", "Mali-G715", "Apple A15 GPU".</summary>
        string GpuName { get; }
        int SystemMemoryMb { get; }
        /// <summary>BCP 47 device language, e.g. "ru-RU".</summary>
        string LanguageTag { get; }
        float FontScale { get; }
        /// <summary>Screen width in points (iOS pt / Android dp: pixels · 160 / dpi), 0 when the density is unknown.</summary>
        float ScreenWidthPoints { get; }
        /// <summary>"1.0.0 (42)".</summary>
        string AppVersion { get; }
    }

    /// <summary>Platform actions outside the app (system share and gallery are in the media bridge).</summary>
    public interface IPlatformActions
    {
        /// <summary>Android back on the Modes root: send the app to the background (Activity.moveTaskToBack).</summary>
        void MoveTaskToBack();
        void OpenUrl(string url);
        /// <summary>Store page of the app ("Оценить приложение").</summary>
        void OpenStorePage();
        /// <summary>Mail composer with a subject and a body (diagnostics without personal data).</summary>
        void SendFeedback(string address, string subject, string body);
        void CopyToClipboard(string text);
    }

    /// <summary>Loads images from disk into textures (project covers).</summary>
    public interface ITextureLoader
    {
        /// <summary>The texture, or null when the file is missing or unreadable.</summary>
        Texture Load(string path);
        void Release(Texture texture);
    }

    /// <summary>Monotonic time for gestures, generation budgets and UI timers.</summary>
    public interface IAppClock
    {
        double NowSeconds { get; }
    }

    /// <summary>The live simulation's picture (Ballverse.Sim <c>LiveSimView</c> on device).</summary>
    public interface ISimSurface : IDisposable
    {
        Texture RenderTarget { get; }
        /// <summary><see cref="RenderTarget"/> was replaced (size change).</summary>
        event Action TargetChanged;
        void Bind(LiveSimController controller);
        void ApplyLook(ParamSet values);
        void SetTier(DeviceTier tier, QualityPreference preference);
        void SetReduceMotion(bool reduce);
        /// <summary>Draws the current frame (called after the simulation stepped in this display frame).</summary>
        void Present(float deltaSeconds);
        /// <summary>A small copy of the last frame for the recent-runs strip, or null.</summary>
        Texture CaptureThumbnail();
        void ReleaseThumbnail(Texture texture);
    }

    /// <summary>Looping previews of the gallery cards.</summary>
    public interface IGalleryPreviews : IDisposable
    {
        event Action Changed;
        bool Active { get; set; }
        void SetModes(IReadOnlyList<KeyValuePair<IModeDefinition, ParamSet>> modes, DeviceTier tier);
        Texture Get(string modeId);
        void Update(float deltaSeconds, int displayFrameRate, double nowSeconds);
        /// <summary>Low memory: drop the textures (rebuilt on demand).</summary>
        void ReleaseTextures();
    }

    /// <summary>What the composition root asks of the UI shell (Ballverse.UI <c>UiShell</c> on device, a recorder in tests).</summary>
    public interface IAppUi : IDisposable
    {
        /// <summary>Opens a mode's simulation screen (calls <c>IGalleryViewModel.OpenMode</c>).</summary>
        void OpenMode(string modeId);
        void ShowToast(LocalizedText text);
        /// <summary>The 0.5 s «Реклама» plaque; <paramref name="shown"/> runs after it.</summary>
        void ShowAdNotice(Action shown);
        void ShowTrackingPrePrompt();
        /// <summary>Closes the ATT explanation screen when it is still open (flow abandoned).</summary>
        void DismissTrackingPrePrompt();
        void ShowPhotosensitivityWarning(Action acknowledged);
        void SetDeviceTier(DeviceTier tier);
        /// <summary>Android back on the Modes root.</summary>
        event Action ExitRequested;
    }
}
