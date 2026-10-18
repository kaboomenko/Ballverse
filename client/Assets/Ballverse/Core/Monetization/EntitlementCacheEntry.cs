using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>Result of <see cref="EntitlementCache.TryLoad"/>.</summary>
    public enum EntitlementCacheStatus
    {
        Ok = 0,
        /// <summary>Null/empty text, invalid JSON, fields of the wrong type, or an id that is not valid UTF-16.</summary>
        Malformed = 1,
        /// <summary>Well-formed but written by an unknown format version.</summary>
        UnsupportedVersion = 2,
        /// <summary>Signature missing or not matching: edited file or a different device key.</summary>
        BadSignature = 3,
        /// <summary>The supplied key is null or empty.</summary>
        InvalidKey = 4,
    }

    /// <summary>A verified entitlement cache record (spec §5.4, §7.9). Immutable.</summary>
    public sealed class EntitlementCacheEntry
    {
        readonly string[] _ownedProductIds;
        readonly IReadOnlyList<string> _ownedView;

        /// <summary>Owned product ids as last read from the store; sorted ordinal, distinct. May include ids unknown to this build.</summary>
        public IReadOnlyList<string> OwnedProductIds => _ownedView;
        /// <summary>When the store was last read successfully, Unix milliseconds (supplied by the caller at save time).</summary>
        public long SavedAtUnixMs { get; }

        internal EntitlementCacheEntry(string[] ownedProductIds, long savedAtUnixMs)
        {
            _ownedProductIds = ownedProductIds;
            _ownedView = System.Array.AsReadOnly(ownedProductIds);
            SavedAtUnixMs = savedAtUnixMs;
        }

        /// <summary>Entitlements of the cached products (spec §5.1 <c>Entitlements.From</c>).</summary>
        /// <param name="config">Null means <see cref="MonetizationConfig.Default"/>.</param>
        /// <param name="watermarkGrandfathered">Persisted <see cref="WatermarkGrandfathering"/> flag.</param>
        public EntitlementSet GetEntitlements(MonetizationConfig config = null, bool watermarkGrandfathered = false)
        {
            return Entitlements.From(_ownedProductIds, config, watermarkGrandfathered);
        }
    }
}
