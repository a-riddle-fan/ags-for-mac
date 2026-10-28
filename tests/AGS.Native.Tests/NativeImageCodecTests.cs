using System;
using System.Drawing;
using System.IO;
using AGS.Imaging;
using AGS.Imaging.Magick;

namespace AGS.Native.Tests
{
    public class NativeImageCodecTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "ags4mac-native-" + Guid.NewGuid().ToString("N"));
        private readonly NativeImageCodec _codec = new NativeImageCodec();

        public NativeImageCodecTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            Directory.Delete(_dir, true);
        }

        [Fact]
        public void LibraryMatchesUpstreamVersion()
        {
            NativeLibraryInfo.EnsureCompatible();
            Assert.Equal(AGS.Types.Version.AGS_EDITOR_VERSION, NativeLibraryInfo.AgsVersion);
        }

        private static PixelImage MakeIndexed(int width, int height, int alphaOfEntry1)
        {
            var palette = new Color[256];
            for (int i = 0; i < 256; i++)
                palette[i] = Color.FromArgb(255, i, 255 - i, (i * 7) & 0xFF);
            palette[1] = Color.FromArgb(alphaOfEntry1, palette[1]);
            var image = new PixelImage(width, height, ImagePixelFormat.Indexed8, palette);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    image.GetRow(y)[x] = (byte)((x + y * width) % 256);
            return image;
        }

        [Theory]
        [InlineData("bmp")]
        [InlineData("pcx")]
        [InlineData("png")]
        public void IndexedImagesKeepIndicesAndFullPrecisionPalette(string ext)
        {
            // 5x3: odd width exercises row padding.
            PixelImage image = MakeIndexed(5, 3, 255);
            string file = Path.Combine(_dir, "indexed." + ext);

            _codec.Encode(image, file);
            PixelImage loaded = Assert.Single(_codec.Decode(file).Frames);

            Assert.Equal(ImagePixelFormat.Indexed8, loaded.PixelFormat);
            for (int y = 0; y < 3; y++)
                Assert.Equal(image.GetRow(y)[..5].ToArray(), loaded.GetRow(y)[..5].ToArray());
            // 8-bit palette precision (not Allegro's 6-bit): odd values survive.
            for (int i = 0; i < 256; i++)
                Assert.Equal(Color.FromArgb(255, image.Palette[i]).ToArgb(), Color.FromArgb(255, loaded.Palette[i]).ToArgb());
        }

        [Fact]
        public void PngPaletteAlphaIsKept()
        {
            PixelImage image = MakeIndexed(4, 1, 0);
            string file = Path.Combine(_dir, "trns.png");

            _codec.Encode(image, file);
            PixelImage loaded = Assert.Single(_codec.Decode(file).Frames);

            Assert.Equal(0, loaded.Palette[1].A);
            Assert.Equal(255, loaded.Palette[2].A);
        }

        [Fact]
        public void BmpPaletteIsOpaque()
        {
            string file = Path.Combine(_dir, "opaque.bmp");
            _codec.Encode(MakeIndexed(2, 2, 0), file);

            PixelImage loaded = Assert.Single(_codec.Decode(file).Frames);

            Assert.All(loaded.Palette, c => Assert.Equal(255, c.A));
        }

        [Theory]
        [InlineData(ImagePixelFormat.Rgb565, "bmp")]
        [InlineData(ImagePixelFormat.Bgr24, "bmp")]
        [InlineData(ImagePixelFormat.Bgra32, "bmp")]
        [InlineData(ImagePixelFormat.Bgr24, "png")]
        [InlineData(ImagePixelFormat.Bgra32, "png")]
        public void TrueColorRoundTripsThroughBgra(ImagePixelFormat format, string ext)
        {
            var image = new PixelImage(3, 2, format);
            var rng = new Random(42);
            rng.NextBytes(image.Pixels);
            if (format == ImagePixelFormat.Bgra32)
                for (int y = 0; y < 2; y++)
                    for (int x = 0; x < 3; x++)
                        image.GetRow(y)[x * 4 + 3] = (byte)(x * 100 + 30);
            string file = Path.Combine(_dir, $"true.{format}.{ext}");

            _codec.Encode(image, file);
            PixelImage loaded = Assert.Single(_codec.Decode(file).Frames);

            PixelImage expected = PixelConversion.ToBgra32(image);
            PixelImage actual = PixelConversion.ToBgra32(loaded);
            for (int y = 0; y < 2; y++)
                Assert.Equal(expected.GetRow(y)[..12].ToArray(), actual.GetRow(y)[..12].ToArray());
        }

        [Fact]
        public void ReadsIndexedPngWrittenByImageMagick()
        {
            var palette = new[] { Color.Black, Color.Red, Color.Blue };
            var image = new PixelImage(3, 1, ImagePixelFormat.Indexed8, new byte[] { 2, 1, 0, 0 }, palette);
            string file = Path.Combine(_dir, "magick.png");
            new MagickImageCodec().Encode(image, file);

            PixelImage loaded = Assert.Single(_codec.Decode(file).Frames);

            Assert.Equal(ImagePixelFormat.Indexed8, loaded.PixelFormat);
            for (int x = 0; x < 3; x++)
                Assert.Equal(palette[image.GetRow(0)[x]].ToArgb(), loaded.Palette[loaded.GetRow(0)[x]].ToArgb());
        }

        [Fact]
        public void BadFilesThrowInvalidData()
        {
            string bogus = Path.Combine(_dir, "bogus.png");
            File.WriteAllText(bogus, "not a png");
            var ex = Assert.Throws<InvalidDataException>(() => _codec.Decode(bogus));
            Assert.Contains("bogus.png", ex.Message);
        }
    }
}
