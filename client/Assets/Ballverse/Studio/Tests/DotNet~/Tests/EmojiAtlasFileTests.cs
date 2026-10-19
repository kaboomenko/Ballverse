using System;
using System.IO;
using System.Runtime.CompilerServices;
using Ballverse.Core.Studio;
using Xunit;

namespace Ballverse.Studio.Tests
{
    /// <summary>The bundled emoji atlas matches the Core catalog it was generated with (Studio/Tools~/make_emoji_atlas.py).</summary>
    public sealed class EmojiAtlasFileTests
    {
        static string StudioDir([CallerFilePath] string here = "")
        {
            // .../Studio/Tests/DotNet~/Tests/EmojiAtlasFileTests.cs
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", "..", ".."));
        }

        [Fact]
        public void AtlasPngHasTheCatalogGrid()
        {
            string path = Path.Combine(StudioDir(), "Resources", "BallverseStudio", "emoji_atlas.bytes");
            byte[] png = File.ReadAllBytes(path);
            Assert.True(png.Length > 33);
            Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }, png.AsSpan(0, 8).ToArray());
            Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
            int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            Assert.Equal(EmojiCatalog.Columns * EmojiCatalog.CellSize, width);
            Assert.Equal(EmojiCatalog.Rows * EmojiCatalog.CellSize, height);
            Assert.Equal(6, png[25]); // colour type RGBA
            Assert.True(File.Exists(Path.Combine(StudioDir(), "Resources", "BallverseStudio", "emoji_atlas_license.txt")));
        }
    }
}
