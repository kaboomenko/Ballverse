using System;
using Ballverse.Platform;
using UnityEngine;

namespace Ballverse.UI.Fakes
{
    /// <summary>
    /// Stand-in for the app's safe-area service when the UI runs alone (<see cref="UiPreviewBootstrap"/>, no app bootstrap
    /// in the scene). Reads <c>Screen.safeArea</c> once per <see cref="Poll"/> through the platform conversion
    /// (<see cref="SafeAreaInsets.FromBottomLeftRect"/>), exactly like the app's provider. Editor and development builds only.
    /// </summary>
    public sealed class PreviewSafeAreaProvider : ISafeAreaProvider
    {
        public SafeAreaInsets Current { get; private set; }

        public event Action<SafeAreaInsets> Changed;

        public PreviewSafeAreaProvider()
        {
            Current = Read();
        }

        /// <summary>Call once per frame: raises <see cref="Changed"/> when the safe area or the screen size changed.</summary>
        public void Poll()
        {
            var insets = Read();
            if (insets == Current) return;
            Current = insets;
            Changed?.Invoke(insets);
        }

        static SafeAreaInsets Read()
        {
            var r = Screen.safeArea;
            return SafeAreaInsets.FromBottomLeftRect(r.xMin, r.yMin, r.xMax, r.yMax, Screen.width, Screen.height);
        }
    }
}
