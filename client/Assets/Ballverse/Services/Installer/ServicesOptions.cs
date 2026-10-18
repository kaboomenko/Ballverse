using System.Collections.Generic;
using Ballverse.Core.Monetization;

namespace Ballverse.Services
{
    public enum ServicesMode
    {
        /// <summary>
        /// By build variant (<see cref="ServicesPolicy"/>). Editor: <see cref="ServicesConfig.editorMode"/> (fakes). Dev /
        /// OwnerTest device: real ads SDK with test units and UMP, fake store or sandbox. Store: the real SDKs compiled into
        /// the build; missing ones are "unavailable" (never fake purchases).
        /// </summary>
        Auto = 0,
        /// <summary>Fakes everywhere (editor, tests, Dev and OwnerTest builds; ignored in Store builds).</summary>
        Fake = 1,
        /// <summary>The real SDKs that are compiled in, also in the editor (their own editor placeholders) and the store sandbox.</summary>
        Real = 2,
    }

    /// <summary>Inputs of <see cref="ServicesInstaller.Create"/>. Null members get platform defaults.</summary>
    public sealed class ServicesOptions
    {
        public ServicesMode Mode { get; set; } = ServicesMode.Auto;

        /// <summary>Build variant (default: the one compiled in, <see cref="ServicesPolicy.CurrentVariant"/>). Tests only.</summary>
        public ServicesBuildVariant? Variant { get; set; }

        /// <summary>Purchases of Dev / OwnerTest device builds in Auto mode (default: <see cref="ServicesConfig.testBuildStore"/>).</summary>
        public TestBuildStore? TestBuildStore { get; set; }

        public IClock Clock { get; set; }

        /// <summary>Persistent store (default: PlayerPrefs).</summary>
        public IKeyValueStore Store { get; set; }

        public IServiceLog Log { get; set; }

        public IConnectivity Connectivity { get; set; }

        /// <summary>
        /// Real ad unit ids for Store builds (default: from <see cref="ServicesConfig"/>). Dev and OwnerTest always use
        /// Google's test units (<see cref="ServicesPolicy.SelectAdUnits"/>).
        /// </summary>
        public AdUnitIds AdUnits { get; set; }

        /// <summary>AdMob test device ids (development).</summary>
        public List<string> AdTestDeviceIds { get; } = new List<string>();

        /// <summary>UMP debug: force the EEA geography (Dev and OwnerTest builds only).</summary>
        public bool UmpForceEeaGeography { get; set; }

        public List<string> UmpTestDeviceHashedIds { get; } = new List<string>();

        /// <summary>Base64 RSA public key from Play Console for on-device purchase verification (public, not a secret).</summary>
        public string GooglePlayPublicKeyBase64 { get; set; }

        /// <summary>Application id / bundle id (default: <c>Application.identifier</c>).</summary>
        public string ApplicationId { get; set; }

        /// <summary>
        /// Development build behaviour (verbose logs, Remote Config without fetch cache). Default: <c>Debug.isDebugBuild</c>.
        /// Which services are fake is decided by the build variant, not by this flag.
        /// </summary>
        public bool? DeveloperBuild { get; set; }

        /// <summary>
        /// Release catalog and entitlement rules (Core). Default: <see cref="MonetizationConfig.Release1"/> — sells only Remove
        /// Export Ads, Remove Interstitial Ads and No Ads Bundle; No Ads or both removals also give NO_WATERMARK. The App may
        /// pass <c>MonetizationConfig.Release1.WithFreeModes(...)</c> after resolving Remote Config <c>free_modes</c>.
        /// </summary>
        public MonetizationConfig Monetization { get; set; }

#if !BALLVERSE_STORE
        /// <summary>Fake store setup when fakes are used.</summary>
        public FakeStoreConfig FakeStore { get; set; }
#endif

        /// <summary>Overrides the store backend (tests).</summary>
        public IStoreBackend StoreBackend { get; set; }

        /// <summary>Raw Remote Config delivered by the build-default service (no Firebase); tests.</summary>
        public IReadOnlyDictionary<string, string> FakeRemoteConfig { get; set; }

        /// <summary>Overrides the platform ATT access (tests).</summary>
        public ITrackingAuthorization Tracking { get; set; }

        /// <summary>Overrides the consent service (tests).</summary>
        public IConsentService Consent { get; set; }
    }
}
