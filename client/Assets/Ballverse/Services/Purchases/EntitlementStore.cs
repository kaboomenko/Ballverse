using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Ballverse.Core.Monetization;

namespace Ballverse.Services
{
    /// <summary>Source of the device key that signs the entitlement cache (spec §7.9).</summary>
    public interface ISecureKeyStore
    {
        /// <summary>The device key (created on first use), at least 32 bytes.</summary>
        byte[] GetOrCreateKey();
    }

    /// <summary>
    /// Release-1 device key: 32 random bytes kept in the app's preferences. This is NOT a secure store (spec §7.9 allows it:
    /// the signed cache only guards against accidental corruption and casual editing; the store is the source of truth).
    /// Keychain / Android Keystore can replace it behind <see cref="ISecureKeyStore"/> without touching callers.
    /// </summary>
    public sealed class PrefsDeviceKeyStore : ISecureKeyStore
    {
        public const string Key = "ballverse.device_key.v1";
        public const int KeyLength = 32;

        readonly IKeyValueStore _store;
        byte[] _cached;

        public PrefsDeviceKeyStore(IKeyValueStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public byte[] GetOrCreateKey()
        {
            if (_cached != null) return (byte[])_cached.Clone();
            string stored = _store.GetString(Key);
            if (!string.IsNullOrEmpty(stored))
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(stored);
                    if (bytes.Length >= KeyLength)
                    {
                        _cached = bytes;
                        return (byte[])bytes.Clone();
                    }
                }
                catch (FormatException)
                {
                    // fall through: replace the unreadable key (the cache it signed becomes unreadable, the store restores it)
                }
            }
            var key = new byte[KeyLength];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(key);
            }
            _store.SetString(Key, Convert.ToBase64String(key));
            _store.Save();
            _cached = key;
            return (byte[])key.Clone();
        }
    }

    /// <summary>
    /// The signed local entitlement cache (spec §5.4 "Магазин недоступен", §7.9): Core <see cref="EntitlementCache"/> over
    /// <see cref="IKeyValueStore"/>. Read synchronously at start-up (frame 0) so rights are known before the store answers.
    /// </summary>
    public sealed class EntitlementStore
    {
        public const string CacheKey = "ballverse.entitlements.v1";

        const string Tag = "Entitlements";

        readonly IKeyValueStore _store;
        readonly ISecureKeyStore _keys;
        readonly IClock _clock;
        readonly IServiceLog _log;

        public EntitlementStore(IKeyValueStore store, ISecureKeyStore keys, IClock clock, IServiceLog log)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _keys = keys ?? throw new ArgumentNullException(nameof(keys));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log ?? NullServiceLog.Instance;
        }

        /// <summary>When the cached list was read from the store, unix ms; null without a valid cache.</summary>
        public long? SavedAtUnixMs { get; private set; }

        /// <summary>Owned product ids from the cache; empty when missing or invalid (logged). Never throws.</summary>
        public IReadOnlyList<string> Load()
        {
            SavedAtUnixMs = null;
            string json;
            byte[] key;
            try
            {
                json = _store.GetString(CacheKey);
                if (json == null) return Array.Empty<string>();
                key = _keys.GetOrCreateKey();
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Reading the entitlement cache failed.", e);
                return Array.Empty<string>();
            }
            var status = EntitlementCache.TryLoad(json, key, out var entry);
            if (status != EntitlementCacheStatus.Ok)
            {
                _log.Warning(Tag, "Entitlement cache ignored: " + status + ". Rights are restored from the store.");
                return Array.Empty<string>();
            }
            SavedAtUnixMs = entry.SavedAtUnixMs;
            return entry.OwnedProductIds;
        }

        /// <summary>Stores the result of a successful store read.</summary>
        public void Save(IEnumerable<string> ownedProductIds)
        {
            try
            {
                long now = _clock.UnixTimeMs;
                string json = EntitlementCache.Serialize(ownedProductIds, now, _keys.GetOrCreateKey());
                _store.SetString(CacheKey, json);
                _store.Save();
                SavedAtUnixMs = now;
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Saving the entitlement cache failed.", e);
            }
        }
    }
}
