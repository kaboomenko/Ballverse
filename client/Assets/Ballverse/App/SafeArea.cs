using System;
using Ballverse.Platform;
using UnityEngine;

namespace Ballverse.App
{
    /// <summary><see cref="ISafeAreaProvider"/> polled once per frame by <see cref="Bootstrap"/> (Unity has no safe-area event).</summary>
    public sealed class SafeAreaProvider : ISafeAreaProvider
    {
        readonly Action<string, Exception> _reportError;

        public SafeAreaProvider(Action<string, Exception> reportError)
        {
            _reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        }

        public SafeAreaInsets Current { get; private set; }

        public event Action<SafeAreaInsets> Changed;

        /// <summary>Stores <paramref name="insets"/>; raises <see cref="Changed"/> and returns true when they differ from the current value.</summary>
        public bool Update(SafeAreaInsets insets)
        {
            if (insets == Current) return false;
            Current = insets;
            AppEvents.Raise(Changed, insets, _reportError, "SafeAreaProvider.Changed");
            return true;
        }

        public bool UpdateFromScreen() => Update(FromUnityRect(Screen.safeArea, Screen.width, Screen.height));

        /// <summary>Converts Unity's <see cref="Screen.safeArea"/> (bottom-left origin, pixels) to edge insets.</summary>
        public static SafeAreaInsets FromUnityRect(Rect safeArea, int screenWidth, int screenHeight) =>
            SafeAreaInsets.FromBottomLeftRect(safeArea.xMin, safeArea.yMin, safeArea.xMax, safeArea.yMax, screenWidth, screenHeight);
    }
}
