using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AGS.Imaging;

namespace AGS.Native.Tests
{
    /// <summary>Reads the reference game's acsprset.spr through upstream's sprite file reader.</summary>
    public class SpriteFileTests
    {
        internal static string ReferenceGameDir()
        {
            string dir = Path.GetDirectoryName(typeof(SpriteFileTests).Assembly.Location);
            while (dir != null && !File.Exists(Path.Combine(dir, "AGS4Mac.sln")))
                dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            string game = Path.Combine(dir, "testdata", "Bernard-SP");
            Assert.True(Directory.Exists(game), $"Reference game not found: {game}");
            return game;
        }

        private static (int Slot, int Width, int Height, int Depth)[] SpritesFromGameAgf(string gameDir)
        {
            // Game.agf declares Windows-1252; XDocument needs the code page provider for that.
            AGS.Types.AnsiEncoding.EnsureCodePagesRegistered();
            XDocument doc = XDocument.Load(Path.Combine(gameDir, "Game.agf"));
            return doc.Root.Element("Game").Element("Sprites").Descendants("Sprite")
                .Select(s => ((int)s.Attribute("Slot"), (int)s.Attribute("Width"), (int)s.Attribute("Height"), (int)s.Attribute("ColorDepth")))
                .ToArray();
        }

        [Fact]
        public void SpriteFileMatchesGameAgf()
        {
            string gameDir = ReferenceGameDir();
            var expected = SpritesFromGameAgf(gameDir);

            using SpriteFile file = SpriteFile.OpenInProject(gameDir);

            Assert.Equal(expected.Length, file.SpriteCount);
            Assert.Equal(expected.Max(s => s.Slot), file.TopmostSprite);
            foreach (var s in expected)
            {
                SpriteSlotInfo info = file.GetInfo(s.Slot);
                Assert.True(info.Exists, $"sprite {s.Slot} missing");
                Assert.Equal((s.Width, s.Height, s.Depth), (info.Width, info.Height, info.ColorDepth));
            }
            Assert.False(file.GetInfo(file.TopmostSprite + 1).Exists);
        }

        [Fact]
        public void AllSpritesDecode()
        {
            string gameDir = ReferenceGameDir();
            var expected = SpritesFromGameAgf(gameDir);

            using SpriteFile file = SpriteFile.OpenInProject(gameDir);

            foreach (var s in expected)
            {
                PixelImage image = file.LoadSprite(s.Slot);
                Assert.NotNull(image);
                Assert.Equal((s.Width, s.Height), (image.Width, image.Height));
                ImagePixelFormat format = s.Depth == 8 ? ImagePixelFormat.Indexed8
                    : s.Depth == 16 ? ImagePixelFormat.Rgb565 : ImagePixelFormat.Bgra32;
                Assert.Equal(format, image.PixelFormat);
            }
        }

        [Fact]
        public void EmptySlotLoadsAsNull()
        {
            string gameDir = ReferenceGameDir();
            var used = SpritesFromGameAgf(gameDir).Select(s => s.Slot).ToHashSet();
            int emptySlot = Enumerable.Range(0, 1224).First(i => !used.Contains(i));

            using SpriteFile file = SpriteFile.OpenInProject(gameDir);

            Assert.False(file.GetInfo(emptySlot).Exists);
            Assert.Null(file.LoadSprite(emptySlot));
        }

        [Fact]
        public void OpensWithoutIndexFile()
        {
            string gameDir = ReferenceGameDir();
            using SpriteFile withIndex = SpriteFile.OpenInProject(gameDir);
            using SpriteFile withoutIndex = SpriteFile.Open(Path.Combine(gameDir, SpriteFile.DefaultSpriteFileName));

            Assert.Equal(withIndex.SpriteCount, withoutIndex.SpriteCount);
            Assert.Equal(withIndex.TopmostSprite, withoutIndex.TopmostSprite);
        }

        [Fact]
        public void MissingFileThrows()
        {
            var ex = Assert.Throws<InvalidDataException>(() => SpriteFile.Open("/nonexistent/acsprset.spr"));
            Assert.Contains("unable to open", ex.Message);
        }
    }
}
