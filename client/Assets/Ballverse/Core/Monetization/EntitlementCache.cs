using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Monetization
{
    /// <summary>
    /// Local cache of the last successful store read (spec §5.4 "Магазин недоступен", §7.9 "Кэш прав"):
    /// JSON via <see cref="CoreJson"/>, signed with HMAC-SHA256 under a device key (Keychain / Android Keystore) supplied
    /// by the caller. Guards against accidental corruption and casual editing, not against cracking; the store is the
    /// source of truth.
    /// <para>Format v1:</para>
    /// <code>{ "version": 1, "savedAtUnixMs": 1760000000000, "ownedProducts": ["ballverse.bundle.all_modes"], "signature": "&lt;64 hex&gt;" }</code>
    /// The MAC covers a canonical binary message (domain tag, version, timestamp, ids as stored), so JSON whitespace
    /// does not matter but every value does.
    /// </summary>
    public static class EntitlementCache
    {
        public const int FormatVersion = 1;

        const string Domain = "ballverse.entitlement_cache";

        // Strict UTF-8: an unpaired surrogate throws instead of collapsing to U+FFFD, so two different ids can never
        // produce the same MAC message.
        static readonly UTF8Encoding s_strictUtf8 = new UTF8Encoding(false, true);

        /// <summary>
        /// Serializes <paramref name="ownedProductIds"/> (null/empty ids dropped, then sorted ordinal and deduplicated)
        /// with the caller's timestamp and signs it.
        /// </summary>
        /// <exception cref="ArgumentNullException">ids or key is null.</exception>
        /// <exception cref="ArgumentException">key is empty, or an id is not valid UTF-16 (unpaired surrogate).</exception>
        public static string Serialize(IEnumerable<string> ownedProductIds, long savedAtUnixMs, byte[] key)
        {
            if (ownedProductIds == null) throw new ArgumentNullException(nameof(ownedProductIds));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length == 0) throw new ArgumentException("Key must not be empty", nameof(key));

            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var id in ownedProductIds)
            {
                if (!string.IsNullOrEmpty(id)) set.Add(id);
            }
            var ids = new string[set.Count];
            set.CopyTo(ids);

            byte[] mac;
            try
            {
                mac = Mac(key, FormatVersion, savedAtUnixMs, ids);
            }
            catch (EncoderFallbackException e)
            {
                throw new ArgumentException("Product ids must be valid UTF-16", nameof(ownedProductIds), e);
            }

            // Built explicitly (no reflection over a DTO), so IL2CPP code stripping cannot break the format (spec §8.9).
            var file = new JObject
            {
                ["version"] = FormatVersion,
                ["savedAtUnixMs"] = savedAtUnixMs,
                ["ownedProducts"] = new JArray(ids),
                ["signature"] = ToHex(mac),
            };
            return CoreJson.Serialize(file);
        }

        /// <summary>The verified entry, or null on any problem (tamper, wrong key, malformed JSON, unknown version). Never throws.</summary>
        public static EntitlementCacheEntry Load(string json, byte[] key)
        {
            return TryLoad(json, key, out var entry) == EntitlementCacheStatus.Ok ? entry : null;
        }

        /// <summary>Verifies and parses a cache file. Never throws for bad input; <paramref name="entry"/> is null unless Ok.</summary>
        public static EntitlementCacheStatus TryLoad(string json, byte[] key, out EntitlementCacheEntry entry)
        {
            entry = null;
            if (key == null || key.Length == 0) return EntitlementCacheStatus.InvalidKey;
            if (string.IsNullOrEmpty(json)) return EntitlementCacheStatus.Malformed;

            JObject root;
            try
            {
                root = CoreJson.Deserialize<JToken>(json) as JObject;
            }
            catch (JsonException)
            {
                return EntitlementCacheStatus.Malformed;
            }
            if (root == null) return EntitlementCacheStatus.Malformed;

            if (!TryGetLong(root, "version", out long version)) return EntitlementCacheStatus.Malformed;
            if (version != FormatVersion) return EntitlementCacheStatus.UnsupportedVersion;

            if (!TryGetLong(root, "savedAtUnixMs", out long savedAt)) return EntitlementCacheStatus.Malformed;

            if (!(root["ownedProducts"] is JArray array)) return EntitlementCacheStatus.Malformed;
            var ids = new string[array.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                if (array[i].Type != JTokenType.String) return EntitlementCacheStatus.Malformed;
                ids[i] = (string)((JValue)array[i]).Value;
            }

            var sigToken = root["signature"];
            if (sigToken == null || sigToken.Type != JTokenType.String) return EntitlementCacheStatus.BadSignature;
            if (!TryFromHex((string)((JValue)sigToken).Value, out byte[] signature)) return EntitlementCacheStatus.BadSignature;
            byte[] expected;
            try
            {
                expected = Mac(key, (int)version, savedAt, ids);
            }
            catch (EncoderFallbackException)
            {
                return EntitlementCacheStatus.Malformed; // unpaired surrogate: never written by Serialize
            }
            if (!FixedTimeEquals(signature, expected)) return EntitlementCacheStatus.BadSignature;

            // Signed ids are trusted; normalize anyway so the entry always honours its documented shape.
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                if (!string.IsNullOrEmpty(id)) set.Add(id);
            }
            var normalized = new string[set.Count];
            set.CopyTo(normalized);
            entry = new EntitlementCacheEntry(normalized, savedAt);
            return EntitlementCacheStatus.Ok;
        }

        static bool TryGetLong(JObject root, string name, out long value)
        {
            value = 0;
            var token = root[name];
            if (token == null || token.Type != JTokenType.Integer) return false;
            if (((JValue)token).Value is long l)
            {
                value = l;
                return true;
            }
            return false; // BigInteger: out of range
        }

        static byte[] Mac(byte[] key, int version, long savedAtUnixMs, string[] ids)
        {
            using (var ms = new MemoryStream())
            {
                WriteString(ms, Domain);
                VarInt.WriteLong(ms, version);
                VarInt.WriteLong(ms, savedAtUnixMs);
                VarInt.WriteULong(ms, (ulong)ids.Length);
                foreach (var id in ids) WriteString(ms, id);
                using (var hmac = new HMACSHA256(key))
                {
                    return hmac.ComputeHash(ms.ToArray());
                }
            }
        }

        static void WriteString(Stream s, string value)
        {
            byte[] bytes = s_strictUtf8.GetBytes(value ?? string.Empty);
            VarInt.WriteULong(s, (ulong)bytes.Length);
            s.Write(bytes, 0, bytes.Length);
        }

        static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        static string ToHex(byte[] bytes)
        {
            const string digits = "0123456789abcdef";
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(digits[b >> 4]);
                sb.Append(digits[b & 0xF]);
            }
            return sb.ToString();
        }

        static bool TryFromHex(string hex, out byte[] bytes)
        {
            bytes = null;
            if (hex == null || hex.Length != 64) return false;
            var result = new byte[32];
            for (int i = 0; i < 32; i++)
            {
                int hi = HexValue(hex[2 * i]);
                int lo = HexValue(hex[2 * i + 1]);
                if (hi < 0 || lo < 0) return false;
                result[i] = (byte)((hi << 4) | lo);
            }
            bytes = result;
            return true;
        }

        static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return -1; // lowercase only: one canonical spelling
        }
    }
}
