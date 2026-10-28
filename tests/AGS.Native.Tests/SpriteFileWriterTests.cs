using System;
using System.IO;
using System.Linq;
using AGS.Imaging;

namespace AGS.Native.Tests
{
    public class SpriteFileWriterTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "ags4mac-sprwrite-" + Guid.NewGuid().ToString("N"));

        public SpriteFileWriterTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            Directory.Delete(_dir, true);
        }

        private delegate void SlotWriter(SpriteFileWriter writer, SpriteFile source, int slot);

        private (string Sprites, string Index) Rewrite(SlotWriter writeSlot, int storeFlags, int compression)
        {
            string sprites = Path.Combine(_dir, $"acsprset-{Guid.NewGuid():N}.spr");
            string index = Path.ChangeExtension(sprites, ".dat");
            using SpriteFile source = SpriteFile.OpenInProject(SpriteFileTests.ReferenceGameDir());
            using (SpriteFileWriter writer = SpriteFileWriter.Create(sprites, storeFlags, compression))
            {
                for (int slot = 0; slot <= source.TopmostSprite; slot++)
                {
                    if (source.GetInfo(slot).Exists)
                        writeSlot(writer, source, slot);
                    else
                        writer.WriteEmptySlot();
                }
                writer.Finish(index);
            }
            return (sprites, index);
        }

        private static void AssertSameSprites(string rewritten, string index)
        {
            using SpriteFile original = SpriteFile.OpenInProject(SpriteFileTests.ReferenceGameDir());
            using SpriteFile copy = SpriteFile.Open(rewritten, index);

            Assert.Equal(original.SpriteCount, copy.SpriteCount);
            Assert.Equal(original.TopmostSprite, copy.TopmostSprite);
            for (int slot = 0; slot <= original.TopmostSprite; slot++)
            {
                SpriteSlotInfo a = original.GetInfo(slot), b = copy.GetInfo(slot);
                Assert.Equal((a.Exists, a.Width, a.Height, a.BytesPerPixel), (b.Exists, b.Width, b.Height, b.BytesPerPixel));
                if (!a.Exists) continue;
                PixelImage x = original.LoadSprite(slot), y = copy.LoadSprite(slot);
                for (int row = 0; row < x.Height; row++)
                    Assert.True(x.GetRow(row).SequenceEqual(y.GetRow(row)), $"sprite {slot} row {row} differs");
            }
        }

        [Fact]
        public void RawCopyPreservesEverySprite()
        {
            using SpriteFile probe = SpriteFile.OpenInProject(SpriteFileTests.ReferenceGameDir());
            var (sprites, index) = Rewrite((w, src, slot) => w.CopySprite(src, slot), probe.StoreFlags, probe.Compression);
            AssertSameSprites(sprites, index);
        }

        [Theory]
        [InlineData(0, 0)] // uncompressed
        [InlineData(1, 1)] // optimized storage, RLE
        [InlineData(1, 2)] // LZW
        [InlineData(1, 3)] // Deflate
        public void ReEncodedSpritesArePixelIdentical(int storeFlags, int compression)
        {
            var (sprites, index) = Rewrite((w, src, slot) => w.WriteSprite(src.LoadSprite(slot)), storeFlags, compression);
            AssertSameSprites(sprites, index);
            using SpriteFile copy = SpriteFile.Open(sprites);
            Assert.Equal(compression, copy.Compression);
        }

        [Fact]
        public void RejectsUnsupportedSpriteFormats()
        {
            using SpriteFileWriter writer = SpriteFileWriter.Create(Path.Combine(_dir, "bad.spr"), 0, 0);
            Assert.Throws<ArgumentException>(() => writer.WriteSprite(new PixelImage(2, 2, ImagePixelFormat.Bgr24)));
        }
    }
}
