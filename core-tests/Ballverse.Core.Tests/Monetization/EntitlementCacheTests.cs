using System;
using System.IO;
using System.Linq;
using System.Text;
using Ballverse.Core.Monetization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>Spec §5.4 / §7.9: HMAC-signed local entitlement cache.</summary>
    public class EntitlementCacheTests
    {
        static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        static readonly byte[] OtherKey = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        const long Ts = 1760000000000L;

        static string GoldenPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Monetization", "entitlement_cache_v1.json");

        static string Sample()
        {
            return EntitlementCache.Serialize(new[] { ProductIds.NoAds, ProductIds.AllModes, ProductIds.ModeOrbit }, Ts, Key);
        }

        static string Mutate(string json, Action<JObject> edit)
        {
            var o = JObject.Parse(json);
            edit(o);
            return o.ToString();
        }

        [Fact]
        public void RoundTrip()
        {
            var json = EntitlementCache.Serialize(new[] { ProductIds.Ultimate, ProductIds.AllModes, ProductIds.AllModes, null, "", "future.product" }, Ts, Key);
            Assert.Equal(EntitlementCacheStatus.Ok, EntitlementCache.TryLoad(json, Key, out var entry));
            Assert.Equal(new[] { ProductIds.AllModes, ProductIds.Ultimate, "future.product" }.OrderBy(s => s, StringComparer.Ordinal).ToArray(), entry.OwnedProductIds.ToArray());
            Assert.Equal(Ts, entry.SavedAtUnixMs);
            Assert.Equal(EntitlementSet.AllDefined, entry.GetEntitlements());
            Assert.NotNull(EntitlementCache.Load(json, Key));
        }

        [Fact]
        public void EmptyOwnedListAndNegativeTimestampRoundTrip()
        {
            var json = EntitlementCache.Serialize(new string[0], -5L, Key);
            var entry = EntitlementCache.Load(json, Key);
            Assert.Empty(entry.OwnedProductIds);
            Assert.Equal(-5L, entry.SavedAtUnixMs);
            Assert.Equal(EntitlementSet.Empty, entry.GetEntitlements());
        }

        [Fact]
        public void SerializationIsDeterministicAndIndependentOfInputOrder()
        {
            var a = EntitlementCache.Serialize(new[] { ProductIds.NoAds, ProductIds.AllModes }, Ts, Key);
            var b = EntitlementCache.Serialize(new[] { ProductIds.AllModes, ProductIds.NoAds, ProductIds.NoAds }, Ts, Key);
            Assert.Equal(a, b);
            Assert.Contains("\"ownedProducts\"", a);
            Assert.Contains("\"savedAtUnixMs\"", a);
        }

        [Fact]
        public void GoldenFileFromIndependentImplementationLoads()
        {
            var golden = File.ReadAllText(GoldenPath);
            var entry = EntitlementCache.Load(golden, Key);
            Assert.NotNull(entry);
            Assert.Equal(Ts, entry.SavedAtUnixMs);
            Assert.Equal(new[] { ProductIds.AllModes, ProductIds.NoAds, ProductIds.ModeOrbit }, entry.OwnedProductIds.ToArray());
            Assert.Equal(Normalize(golden), Normalize(Sample()));
        }

        static string Normalize(string s)
        {
            return s.Replace("\r\n", "\n").Trim();
        }

        [Fact]
        public void WrongKeyIsRejected()
        {
            Assert.Equal(EntitlementCacheStatus.BadSignature, EntitlementCache.TryLoad(Sample(), OtherKey, out var entry));
            Assert.Null(entry);
            Assert.Null(EntitlementCache.Load(Sample(), OtherKey));
        }

        [Fact]
        public void TamperedFieldsAreRejected()
        {
            var json = Sample();
            Assert.Equal(EntitlementCacheStatus.BadSignature, EntitlementCache.TryLoad(
                Mutate(json, o => ((JArray)o["ownedProducts"]).Add(ProductIds.Ultimate)), Key, out _));
            Assert.Equal(EntitlementCacheStatus.BadSignature, EntitlementCache.TryLoad(
                Mutate(json, o => ((JArray)o["ownedProducts"]).RemoveAt(0)), Key, out _));
            Assert.Equal(EntitlementCacheStatus.BadSignature, EntitlementCache.TryLoad(
                Mutate(json, o => ((JArray)o["ownedProducts"])[0] = ProductIds.Ultimate), Key, out _));
            Assert.Equal(EntitlementCacheStatus.BadSignature, EntitlementCache.TryLoad(
                Mutate(json, o => o["savedAtUnixMs"] = Ts + 1), Key, out _));
            Assert.Equal(EntitlementCacheStatus.BadSignature, EntitlementCache.TryLoad(
                Mutate(json, o => o.Remove("signature")), Key, out _));
            Assert.Equal(EntitlementCacheStatus.BadSignature, EntitlementCache.TryLoad(
                Mutate(json, o => o["signature"] = "00"), Key, out _));
            Assert.Equal(EntitlementCacheStatus.BadSignature, EntitlementCache.TryLoad(
                Mutate(json, o => o["signature"] = ((string)o["signature"]).ToUpperInvariant()), Key, out _));
            Assert.Equal(EntitlementCacheStatus.BadSignature, EntitlementCache.TryLoad(
                Mutate(json, o => o["signature"] = 123), Key, out _));
            Assert.Equal(EntitlementCacheStatus.BadSignature, EntitlementCache.TryLoad(
                Mutate(json, o => o["signature"] = new string('g', 64)), Key, out _));
        }

        [Fact]
        public void ReorderingSignedIdsIsRejected()
        {
            var json = Mutate(Sample(), o =>
            {
                var arr = (JArray)o["ownedProducts"];
                var first = arr[0];
                arr.RemoveAt(0);
                arr.Add(first);
            });
            Assert.Equal(EntitlementCacheStatus.BadSignature, EntitlementCache.TryLoad(json, Key, out _));
        }

        [Fact]
        public void WhitespaceAndKeyOrderDoNotMatter()
        {
            var json = Mutate(Sample(), o =>
            {
                var sig = o["signature"];
                o.Remove("signature");
                o.AddFirst(new JProperty("signature", sig));
            });
            Assert.Equal(EntitlementCacheStatus.Ok, EntitlementCache.TryLoad(json.Replace("\n", "\n   "), Key, out _));
        }

        [Fact]
        public void UnknownVersionIsRejected()
        {
            Assert.Equal(EntitlementCacheStatus.UnsupportedVersion, EntitlementCache.TryLoad(Mutate(Sample(), o => o["version"] = 2), Key, out _));
            Assert.Equal(EntitlementCacheStatus.UnsupportedVersion, EntitlementCache.TryLoad(Mutate(Sample(), o => o["version"] = 0), Key, out _));
            Assert.Equal(EntitlementCacheStatus.Malformed, EntitlementCache.TryLoad(Mutate(Sample(), o => o["version"] = "1"), Key, out _));
            Assert.Equal(EntitlementCacheStatus.Malformed, EntitlementCache.TryLoad(Mutate(Sample(), o => o.Remove("version")), Key, out _));
            Assert.Equal(EntitlementCacheStatus.Malformed, EntitlementCache.TryLoad(Mutate(Sample(), o => o["version"] = 1.0), Key, out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("42")]
        [InlineData("\"text\"")]
        [InlineData("{")]
        [InlineData("{}")]
        [InlineData("{\"version\":1}")]
        [InlineData("{\"version\":1,\"savedAtUnixMs\":1}")]
        [InlineData("{\"version\":1,\"savedAtUnixMs\":\"1\",\"ownedProducts\":[]}")]
        [InlineData("{\"version\":1,\"savedAtUnixMs\":99999999999999999999999,\"ownedProducts\":[]}")]
        [InlineData("{\"version\":1,\"savedAtUnixMs\":1,\"ownedProducts\":{}}")]
        [InlineData("{\"version\":1,\"savedAtUnixMs\":1,\"ownedProducts\":[1]}")]
        [InlineData("{\"version\":1,\"savedAtUnixMs\":1,\"ownedProducts\":[null]}")]
        [InlineData("{\"version\":99999999999999999999999}")]
        [InlineData("{\"version\":1} trailing")]
        public void MalformedInputNeverThrows(string json)
        {
            var status = EntitlementCache.TryLoad(json, Key, out var entry);
            Assert.NotEqual(EntitlementCacheStatus.Ok, status);
            Assert.Null(entry);
            Assert.Null(EntitlementCache.Load(json, Key));
        }

        [Fact]
        public void DeeplyNestedJsonNeverThrows()
        {
            var json = new string('[', 10000) + new string(']', 10000);
            Assert.Equal(EntitlementCacheStatus.Malformed, EntitlementCache.TryLoad(json, Key, out _));
        }

        [Fact]
        public void InvalidKey()
        {
            Assert.Equal(EntitlementCacheStatus.InvalidKey, EntitlementCache.TryLoad(Sample(), null, out _));
            Assert.Equal(EntitlementCacheStatus.InvalidKey, EntitlementCache.TryLoad(Sample(), new byte[0], out _));
            Assert.Throws<ArgumentNullException>(() => EntitlementCache.Serialize(new string[0], Ts, null));
            Assert.Throws<ArgumentException>(() => EntitlementCache.Serialize(new string[0], Ts, new byte[0]));
            Assert.Throws<ArgumentNullException>(() => EntitlementCache.Serialize(null, Ts, Key));
        }

        [Fact]
        public void RandomMutationsNeverThrowAndNeverVerify()
        {
            var original = Sample();
            var rng = Rng.FromSeed(0xCAC4EUL, 1UL);
            int accepted = 0;
            for (int iter = 0; iter < 3000; iter++)
            {
                var chars = original.ToCharArray();
                int edits = 1 + (int)rng.NextUInt(3);
                for (int k = 0; k < edits; k++)
                {
                    int pos = (int)rng.NextUInt((uint)chars.Length);
                    chars[pos] = (char)(32 + rng.NextUInt(95));
                }
                var mutated = new string(chars);
                var status = EntitlementCache.TryLoad(mutated, Key, out var entry);
                if (status == EntitlementCacheStatus.Ok)
                {
                    // Only whitespace-equivalent edits may still verify, and then the content is unchanged.
                    accepted++;
                    Assert.Equal(Ts, entry.SavedAtUnixMs);
                    Assert.Equal(new[] { ProductIds.AllModes, ProductIds.NoAds, ProductIds.ModeOrbit }, entry.OwnedProductIds.ToArray());
                }
            }
            Assert.True(accepted < 3000);
        }

        [Fact]
        public void RandomBytesNeverThrow()
        {
            var rng = Rng.FromSeed(77UL, 2UL);
            for (int iter = 0; iter < 2000; iter++)
            {
                int len = (int)rng.NextUInt(200);
                var bytes = new byte[len];
                for (int i = 0; i < len; i++) bytes[i] = (byte)rng.NextUInt(256);
                var text = Encoding.UTF8.GetString(bytes);
                Assert.NotEqual(EntitlementCacheStatus.Ok, EntitlementCache.TryLoad(text, Key, out _));
            }
        }

        [Fact]
        public void UnpairedSurrogateIdIsRejectedOnSave()
        {
            Assert.Throws<ArgumentException>(() => EntitlementCache.Serialize(new[] { "a\uD800" }, Ts, Key));
            Assert.Throws<ArgumentException>(() => EntitlementCache.Serialize(new[] { "\uDC00b" }, Ts, Key));
        }

        [Fact]
        public void UnpairedSurrogateIdInFileIsMalformedNotOk()
        {
            // With lossy UTF-8 every unpaired surrogate encoded like U+FFFD, so a file signed for "a\uFFFD" verified
            // after editing the id to "a" + a lone surrogate (raw char in the JSON text, as an editor would leave it).
            var signed = EntitlementCache.Serialize(new[] { "a\uFFFD" }, Ts, Key);
            Assert.Contains("\"a\uFFFD\"", signed);
            foreach (var lone in new[] { "\uD800", "\uDBFF", "\uDC00" })
            {
                var tampered = signed.Replace("\"a\uFFFD\"", "\"a" + lone + "\"");
                Assert.Equal(EntitlementCacheStatus.Malformed, EntitlementCache.TryLoad(tampered, Key, out var entry));
                Assert.Null(entry);
            }
        }

        [Fact]
        public void ValidSurrogatePairRoundTrips()
        {
            var json = EntitlementCache.Serialize(new[] { "future.\uD83D\uDE00" }, Ts, Key);
            Assert.Equal("future.\uD83D\uDE00", EntitlementCache.Load(json, Key).OwnedProductIds[0]);
        }

        [Fact]
        public void SerializedShapeIsExactlyTheV1Fields()
        {
            var o = JObject.Parse(Sample());
            Assert.Equal(new[] { "version", "savedAtUnixMs", "ownedProducts", "signature" }, o.Properties().Select(p => p.Name).ToArray());
            Assert.Equal(1, (int)o["version"]);
            Assert.Equal(Ts, (long)o["savedAtUnixMs"]);
            Assert.Equal(3, ((JArray)o["ownedProducts"]).Count);
            Assert.Equal(64, ((string)o["signature"]).Length);
        }

        [Fact]
        public void EntryIdsAreReadOnly()
        {
            Assert.False(EntitlementCache.Load(Sample(), Key).OwnedProductIds is string[]);
        }
    }
}
