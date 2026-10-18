using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ballverse.Editor
{
    /// <summary>
    /// The Google Mobile Ads plugin fails Android and iOS builds while its app ids are empty. Until the owner's
    /// AdMob app exists, this fills empty ids with Google's public sample app ids (they only ever serve test ads,
    /// see developers.google.com/admob/android/test-ads and /ios/test-ads). Ids that are already set are never
    /// touched, so real ids entered in Assets > Google Mobile Ads > Settings win.
    /// <para>Sample ids are for development builds only: <see cref="BuildScript"/> refuses a release build (App Bundle,
    /// App Store/TestFlight Xcode project) that would ship them unless -ballverseAllowTestAds is passed (spec §8.9).</para>
    /// The plugin's settings class is internal, so it is located by name and edited as a serialized object.
    /// </summary>
    public static class AdMobDefaults
    {
        public const string SampleAndroidAppId = BuildPolicy.SampleAndroidAppId;
        public const string SampleIosAppId = BuildPolicy.SampleIosAppId;

        /// <summary>Asset path the plugin loads its settings from (Resources/GoogleMobileAdsSettings).</summary>
        public const string SettingsAssetPath = "Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset";

        const string SettingsTypeName = "GoogleMobileAds.Editor.GoogleMobileAdsSettings";
        const string AndroidField = "adMobAndroidAppId";
        const string IosField = "adMobIOSAppId";

        /// <summary>True when the Google Mobile Ads editor assembly is loaded.</summary>
        public static bool PluginPresent => FindSettingsType() != null;

        /// <summary>Fills empty app ids with the sample ids. Returns true when the settings asset was changed.</summary>
        public static bool EnsureAppIds()
        {
            Type settingsType = FindSettingsType();
            if (settingsType == null)
            {
                Debug.Log("[Ballverse] Google Mobile Ads plugin not present; AdMob app ids not needed.");
                return false;
            }

            var settings = AssetDatabase.LoadAssetAtPath(SettingsAssetPath, settingsType) as ScriptableObject;
            if (settings == null)
            {
                ProjectSetup.EnsureAssetFolder(Path.GetDirectoryName(SettingsAssetPath).Replace('\\', '/'));
                settings = ScriptableObject.CreateInstance(settingsType);
                AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            }

            if (!FillEmptyIds(settings)) return false;

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.LogWarning("[Ballverse] AdMob app ids were empty and now use Google's sample ids (test ads only). " +
                             "Release builds refuse them; enter the real ids in Assets > Google Mobile Ads > Settings before a store release.");
            return true;
        }

        /// <summary>
        /// Fills the empty app ids of a Google Mobile Ads settings object with the sample ids; ids that are set stay as
        /// they are. Returns true when a value changed. Does not save assets.
        /// </summary>
        public static bool FillEmptyIds(ScriptableObject settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var so = new SerializedObject(settings);
            bool changed = FillIfEmpty(so, AndroidField, SampleAndroidAppId) | FillIfEmpty(so, IosField, SampleIosAppId);
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }

        /// <summary>Reads the app ids of a settings object.</summary>
        public static (string android, string ios) ReadAppIds(ScriptableObject settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var so = new SerializedObject(settings);
            return (so.FindProperty(AndroidField)?.stringValue, so.FindProperty(IosField)?.stringValue);
        }

        /// <summary>Creates an in-memory (not saved) settings object of the installed plugin, or null without the plugin.</summary>
        public static ScriptableObject CreateTransientSettings()
        {
            Type settingsType = FindSettingsType();
            return settingsType == null ? null : ScriptableObject.CreateInstance(settingsType);
        }

        /// <summary>Reads the configured app ids (null when the plugin or its settings asset is absent).</summary>
        public static (string android, string ios)? ReadAppIds()
        {
            Type settingsType = FindSettingsType();
            if (settingsType == null) return null;
            var settings = AssetDatabase.LoadAssetAtPath(SettingsAssetPath, settingsType) as ScriptableObject;
            if (settings == null) return null;
            return ReadAppIds(settings);
        }

        static bool FillIfEmpty(SerializedObject so, string field, string value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
                throw new InvalidOperationException("Google Mobile Ads settings have no field '" + field + "'; update AdMobDefaults for this plugin version.");
            if (!string.IsNullOrWhiteSpace(prop.stringValue)) return false;
            prop.stringValue = value;
            return true;
        }

        static Type FindSettingsType() =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(SettingsTypeName, false))
                .FirstOrDefault(t => t != null && typeof(ScriptableObject).IsAssignableFrom(t));
    }
}
