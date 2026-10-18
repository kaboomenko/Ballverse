using System;
using Ballverse.Core.Params;
using Xunit;

namespace Ballverse.Core.Tests.Params
{
    public class ColorRgbaTests
    {
        [Theory]
        [InlineData("#FF8000", 255, 128, 0, 255)]
        [InlineData("#ff8000", 255, 128, 0, 255)]
        [InlineData("#Ff8000", 255, 128, 0, 255)]
        [InlineData("#FFFFFF33", 255, 255, 255, 0x33)]
        [InlineData("#00000000", 0, 0, 0, 0)]
        [InlineData("#abcdef80", 0xAB, 0xCD, 0xEF, 0x80)]
        public void ParsesBothFormsCaseInsensitively(string text, int r, int g, int b, int a)
        {
            var c = ColorRgba.Parse(text);
            Assert.Equal(new ColorRgba((byte)r, (byte)g, (byte)b, (byte)a), c);
        }

        [Theory]
        [InlineData(255, 128, 0, 255, "#FF8000")]
        [InlineData(255, 255, 255, 0x33, "#FFFFFF33")]
        [InlineData(0xAB, 0xCD, 0xEF, 0x80, "#ABCDEF80")]
        [InlineData(0, 0, 0, 0, "#00000000")]
        public void FormatsUppercaseShortWhenOpaque(int r, int g, int b, int a, string expected)
        {
            Assert.Equal(expected, new ColorRgba((byte)r, (byte)g, (byte)b, (byte)a).ToString());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("FF8000")]
        [InlineData("#FF800")]
        [InlineData("#FF80000")]
        [InlineData("#FF8000FFF")]
        [InlineData("#GG8000")]
        [InlineData("#FF 800")]
        [InlineData("rgb(1,2,3)")]
        public void RejectsMalformed(string text)
        {
            Assert.False(ColorRgba.TryParse(text, out _));
            Assert.Throws<FormatException>(() => ColorRgba.Parse(text));
        }

        [Fact]
        public void RoundTripsEveryAlphaAndPacks()
        {
            for (int a = 0; a < 256; a++)
            {
                var c = new ColorRgba(1, 2, 3, (byte)a);
                Assert.Equal(c, ColorRgba.Parse(c.ToString()));
                Assert.Equal(c, ColorRgba.FromRgba32(c.ToRgba32()));
            }
            Assert.Equal(0x01020380u, new ColorRgba(1, 2, 3, 0x80).ToRgba32());
        }
    }
}
