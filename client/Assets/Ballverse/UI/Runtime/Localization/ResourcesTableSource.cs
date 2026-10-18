using UnityEngine;

namespace Ballverse.UI.Localization
{
    /// <summary>
    /// Reads string tables shipped as <c>Resources/BallverseUI/Localization/{lang}.json</c> (TextAssets).
    /// Lightweight replacement for the Unity Localization package in release 1 (spec §4.7: key-based tables, EN + RU).
    /// </summary>
    public sealed class ResourcesTableSource : Localizer.ITableSource
    {
        public string LoadTableJson(string language)
        {
            var asset = Resources.Load<TextAsset>(UiAssets.LocalizationPath + language);
            if (asset == null) return null;
            string text = asset.text;
            Resources.UnloadAsset(asset);
            return text;
        }

        /// <summary>Device language as a two-letter tag (Unity reports a <see cref="SystemLanguage"/>).</summary>
        public static string SystemLanguageTag()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.English: return "en";
                default: return Application.systemLanguage.ToString();
            }
        }
    }
}
