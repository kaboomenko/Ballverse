using System;
using System.IO;
using Ballverse.Core;
using Xunit;

namespace Ballverse.Core.Tests.Foundation
{
    public class VarIntTests
    {
        [Theory]
        [InlineData(0UL, new byte[] { 0x00 })]
        [InlineData(1UL, new byte[] { 0x01 })]
        [InlineData(127UL, new byte[] { 0x7F })]
        [InlineData(128UL, new byte[] { 0x80, 0x01 })]
        [InlineData(300UL, new byte[] { 0xAC, 0x02 })]
        [InlineData(ulong.MaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 })]
        public void EncodesLeb128(ulong value, byte[] expected)
        {
            var ms = new MemoryStream();
            VarInt.WriteULong(ms, value);
            Assert.Equal(expected, ms.ToArray());

            Span<byte> buf = stackalloc byte[VarInt.MaxBytes64];
            int n = VarInt.WriteULong(buf, value);
            Assert.Equal(expected, buf.Slice(0, n).ToArray());
        }

        [Theory]
        [InlineData(0L)]
        [InlineData(1L)]
        [InlineData(-1L)]
        [InlineData(63L)]
        [InlineData(-64L)]
        [InlineData(long.MaxValue)]
        [InlineData(long.MinValue)]
        public void SignedRoundTrip(long value)
        {
            var ms = new MemoryStream();
            VarInt.WriteLong(ms, value);
            ms.Position = 0;
            Assert.Equal(value, VarInt.ReadLong(ms));
        }

        [Fact]
        public void ZigZagMapsSmallMagnitudesToSmallCodes()
        {
            Assert.Equal(0UL, VarInt.ZigZagEncode(0));
            Assert.Equal(1UL, VarInt.ZigZagEncode(-1));
            Assert.Equal(2UL, VarInt.ZigZagEncode(1));
            Assert.Equal(3UL, VarInt.ZigZagEncode(-2));
        }

        [Fact]
        public void SpanReaderAdvancesOffset()
        {
            byte[] data = { 0xAC, 0x02, 0x05 };
            int offset = 0;
            Assert.Equal(300UL, VarInt.ReadULong(data, ref offset));
            Assert.Equal(2, offset);
            Assert.Equal(5UL, VarInt.ReadULong(data, ref offset));
            Assert.Equal(3, offset);
        }

        [Fact]
        public void TruncatedInputThrows()
        {
            Assert.Throws<InvalidDataException>(() => VarInt.ReadULong(new MemoryStream(new byte[] { 0x80 })));
            int offset = 0;
            Assert.Throws<InvalidDataException>(() => VarInt.ReadULong(new byte[] { 0xFF, 0xFF }, ref offset));
        }

        [Fact]
        public void OverflowThrows()
        {
            byte[] tooBig = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02 };
            Assert.Throws<InvalidDataException>(() => VarInt.ReadULong(new MemoryStream(tooBig)));
            byte[] tooLong = { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x00 };
            Assert.Throws<InvalidDataException>(() => VarInt.ReadULong(new MemoryStream(tooLong)));
        }
    }
}
