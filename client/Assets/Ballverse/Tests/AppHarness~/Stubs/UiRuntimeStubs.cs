// Signature stand-ins for the few Ballverse.UI runtime (UI Toolkit) members Ballverse.App calls, copied from
// UI/Runtime/Shell/BallverseUIRoot.cs, UI/Runtime/Shell/UiShell.cs, UI/Runtime/Navigation/UiNavigator.cs and
// UI/Runtime/Localization/ResourcesTableSource.cs. The UI runtime itself needs UnityEngine.UIElements and is compiled by Unity
// only; keep these in sync when those signatures change. Not part of the Unity project.
#pragma warning disable CS0067
using System;
using Ballverse.Core;
using Ballverse.UI.Localization;
using Ballverse.UI.Navigation;
using Ballverse.UI.ViewModels;
using UnityEngine;

namespace Ballverse.UI
{
    public sealed class BallverseUIRoot : MonoBehaviour
    {
        public UiShell Shell { get; private set; }

        public static BallverseUIRoot Create(IBallverseViewModels viewModels, Ballverse.Platform.ISafeAreaProvider safeArea, int sortingOrder = 0,
            Transform parent = null) => default;
    }

    public sealed class UiShell : IDisposable
    {
        public UiNavigator Navigator => default;
        public void OpenMode(string modeId) { }
        public void ShowAdNotice(Action shown) { }
        public void ShowTrackingPrePrompt() { }
        public void ShowPhotosensitivityWarning(Action acknowledged) { }
        public void ShowToast(LocalizedText text) { }
        public void SetDeviceTier(DeviceTier tier) { }
        public void Dispose() { }
    }
}

namespace Ballverse.UI.Navigation
{
    public sealed class UiNavigator
    {
        public event Action<ScreenId> ScreenChanged;
        public event Action ExitRequested;
        public ScreenId Current => default;
        public void Pop() { }
    }
}

namespace Ballverse.UI.Localization
{
    public sealed class ResourcesTableSource
    {
        public static string SystemLanguageTag() => "en";
    }
}
