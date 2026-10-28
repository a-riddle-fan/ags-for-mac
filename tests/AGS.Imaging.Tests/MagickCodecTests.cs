using System;
using System.Drawing;
using System.IO;
using AGS.Imaging.Magick;
using ImageMagick;

namespace AGS.Imaging.Tests
{
    public class MagickCodecTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "ags4mac-imaging-" + Guid.NewGuid().ToString("N"));

        public MagickCodecTests()
        {
            Directory.CreateDirectory(_dir);
            MagickImageCodec.RegisterAll();
        }

        public void Dispose()
        {
            Directory.Delete(_dir, true);
        }

        [Fact]
        public void IndexedGifKeepsPaletteAndIndices()
        {
            var palette = new[] { Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 255, 0, 0), Color.FromArgb(255, 0, 0, 255) };
            var image = new PixelImage(3, 2, ImagePixelFormat.Indexed8, new byte[] { 0, 1, 2, 0, 2, 1, 0, 0 }, palette);
            string file = Path.Combine(_dir, "indexed.gif");

            ImageCodecs.Save(image, file);
            DecodedImage decoded = ImageCodecs.Load(file);

            PixelImage frame = Assert.Single(decoded.Frames);
            Assert.Equal(ImagePixelFormat.Indexed8, frame.PixelFormat);
            Assert.NotNull(decoded.OriginalPalette);
            // Indices may be renumbered by the encoder; the colors they point to must match.
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 3; x++)
                    Assert.Equal(image.Palette[image.GetRow(y)[x]].ToArgb(), frame.Palette[frame.GetRow(y)[x]].ToArgb());
        }

        [Fact]
        public void AnimatedGifYieldsCoalescedFrames()
        {
            string file = Path.Combine(_dir, "anim.gif");
            using (var collection = new MagickImageCollection())
            {
                var first = new MagickImage(MagickColors.Red, 4, 4);
                first.AnimationDelay = 10;
                collection.Add(first);
                // Second frame only paints the top-left pixel; coalescing must fill in the rest.
                var second = new MagickImage(MagickColors.Blue, 1, 1);
                second.AnimationDelay = 10;
                second.Page = new MagickGeometry(0, 0, 4, 4);
                collection.Add(second);
                collection.Write(file);
            }

            DecodedImage decoded = ImageCodecs.Load(file);

            Assert.Equal(2, decoded.Frames.Count);
            PixelImage frame2 = PixelConversion.ToBgra32(decoded.Frames[1]);
            Assert.Equal(4, frame2.Width);
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, frame2.GetRow(0)[..4].ToArray()); // blue
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, frame2.GetRow(3)[12..16].ToArray()); // red
        }

        [Fact]
        public void TrueColorPngKeepsAlpha()
        {
            var image = new PixelImage(2, 1, ImagePixelFormat.Bgra32, new byte[] { 10, 20, 30, 128, 0, 0, 0, 0 });
            string file = Path.Combine(_dir, "alpha.png");

            ImageCodecs.Save(image, file);
            PixelImage frame = PixelConversion.ToBgra32(Assert.Single(ImageCodecs.Load(file).Frames));

            Assert.Equal(new byte[] { 10, 20, 30, 128 }, frame.GetRow(0)[..4].ToArray());
            Assert.Equal(0, frame.GetRow(0)[7]);
        }

        [Fact]
        public void UnknownExtensionAndBadDataThrowInvalidData()
        {
            string bogus = Path.Combine(_dir, "bogus.gif");
            File.WriteAllText(bogus, "not an image");
            Assert.Throws<InvalidDataException>(() => ImageCodecs.Load(bogus));

            string unknown = Path.Combine(_dir, "file.xyz");
            File.WriteAllText(unknown, "x");
            Assert.Throws<InvalidDataException>(() => ImageCodecs.Load(unknown));
        }
    }
}
