using System;
using System.Linq;
using System.Text;
using Ballverse.Core;
using Xunit;

namespace Ballverse.Core.Tests.Foundation
{
    public class XxHash64Tests
    {
        // Expected values computed with the reference `xxhash` Python package (xxh64).
        [Theory]
        [InlineData("", 0UL, 0xef46db3751d8e999UL)]
        [InlineData("a", 0UL, 0xd24ec4f1a98c6e5bUL)]
        [InlineData("abc", 0UL, 0x44bc2cf5ad770999UL)]
        [InlineData("Ballverse", 0UL, 0x313a82ac407f7178UL)]
        [InlineData("", 12345UL, 0x95584af7701f808dUL)]
        [InlineData("a", 12345UL, 0x747b860523d69ab6UL)]
        [InlineData("abc", 12345UL, 0x01700e64f6f23509UL)]
        [InlineData("Ballverse", 12345UL, 0x2b55c01267545641UL)]
        public void MatchesReference(string input, ulong seed, ulong expected)
        {
            Assert.Equal(expected, XxHash64.Hash(Encoding.ASCII.GetBytes(input), seed));
        }

        [Fact]
        public void MatchesReferenceForLongInput()
        {
            byte[] data = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
            Assert.Equal(0x6ac1e58032166597UL, XxHash64.Hash(data, 0));
            Assert.Equal(0x028ba1ae2de4de27UL, XxHash64.Hash(data, 12345));
        }

        [Fact]
        public void StreamingEqualsOneShotForAllSplits()
        {
            byte[] data = Enumerable.Range(0, 257).Select(i => (byte)(i * 31)).ToArray();
            ulong expected = XxHash64.Hash(data, 7);
            for (int split = 0; split <= data.Length; split += 3)
            {
                var h = new XxHash64(7);
                h.Append(new ReadOnlySpan<byte>(data, 0, split));
                h.Append(new ReadOnlySpan<byte>(data, split, data.Length - split));
                Assert.Equal(expected, h.Digest());
            }
        }

        [Fact]
        public void ByteByByteEqualsOneShot()
        {
            byte[] data = Enumerable.Range(0, 99).Select(i => (byte)(255 - i)).ToArray();
            var h = new XxHash64();
            foreach (byte b in data) h.Append(new[] { b });
            Assert.Equal(XxHash64.Hash(data), h.Digest());
        }

        [Fact]
        public void AppendHelpersAreLittleEndian()
        {
            var h = new XxHash64();
            h.AppendULong(0x0807060504030201UL);
            h.AppendInt(0x0C0B0A09);
            Assert.Equal(XxHash64.Hash(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }), h.Digest());
        }

        [Fact]
        public void ResetRestartsHash()
        {
            var h = new XxHash64(3);
            h.Append(Encoding.ASCII.GetBytes("garbage"));
            h.Reset();
            h.Append(Encoding.ASCII.GetBytes("abc"));
            Assert.Equal(XxHash64.Hash(Encoding.ASCII.GetBytes("abc"), 3), h.Digest());
        }
    }
}
