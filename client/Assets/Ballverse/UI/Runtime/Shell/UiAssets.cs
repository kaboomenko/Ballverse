using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI
{
    /// <summary>
    /// Loads the UI assets shipped under <c>Ballverse/UI/Resources/BallverseUI/</c>. Everything is loaded by path so the
    /// shell works without scene or inspector wiring (the project is built in CI without an editor session).
    /// </summary>
    public static class UiAssets
    {
        public const string Root = "BallverseUI/";
        public const string ScreensPath = Root + "Screens/";
        public const string StylesPath = Root + "Styles/";
        public const string LocalizationPath = Root + "Localization/";
        public const string ThemePath = Root + "Theme/BallverseRuntimeTheme";
        public const string GlassShaderPath = Root + "Shaders/BallverseGlass";

        /// <summary>Global style sheets applied to the app root, in order.</summary>
        public static readonly IReadOnlyList<string> GlobalStyleSheets = Array.AsReadOnly(new[]
        {
            "Tokens", "Theme", "Base", "Components", "Glass", "Screens", "Studio",
        });

        static readonly Dictionary<string, VisualTreeAsset> s_trees = new Dictionary<string, VisualTreeAsset>(StringComparer.Ordinal);

        public static VisualTreeAsset LoadTree(string name)
        {
            if (s_trees.TryGetValue(name, out var tree) && tree != null) return tree;
            tree = Resources.Load<VisualTreeAsset>(ScreensPath + name);
            if (tree == null) throw new InvalidOperationException("UI layout not found: Resources/" + ScreensPath + name);
            s_trees[name] = tree;
            return tree;
        }

        /// <summary>Instantiates a layout as a full-size element (the template container fills its parent).</summary>
        public static VisualElement Instantiate(string name)
        {
            var container = LoadTree(name).Instantiate();
            container.AddToClassList("bv-fill");
            container.pickingMode = PickingMode.Ignore;
            return container;
        }

        public static StyleSheet LoadStyle(string name)
        {
            var sheet = Resources.Load<StyleSheet>(StylesPath + name);
            if (sheet == null) Debug.LogError("[Ballverse.UI] Style sheet not found: Resources/" + StylesPath + name);
            return sheet;
        }

        public static void AddGlobalStyles(VisualElement root)
        {
            foreach (string name in GlobalStyleSheets)
            {
                var sheet = LoadStyle(name);
                if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            }
        }

        /// <summary>Required child by name; throws with a clear message when a layout and its controller disagree.</summary>
        public static T Q<T>(VisualElement root, string name) where T : VisualElement
        {
            var e = root.Q<T>(name);
            if (e == null) throw new InvalidOperationException("UI element '" + name + "' (" + typeof(T).Name + ") is missing in the layout");
            return e;
        }
    }
}
