using System.Drawing;

namespace AGS.Imaging.Tests
{
    public class PixelImageTests
    {
        [Theory]
        [InlineData(ImagePixelFormat.Indexed8, 3, 4)]
        [InlineData(ImagePixelFormat.Rgb565, 3, 8)]
        [InlineData(ImagePixelFormat.Bgr24, 3, 12)]
        [InlineData(ImagePixelFormat.Bgr24, 4, 12)]
        [InlineData(ImagePixelFormat.Bgra32, 3, 12)]
        public void RowsArePaddedToFourBytes(ImagePixelFormat format, int width, int expectedStride)
        {
            var image = new PixelImage(width, 2, format);
            Assert.Equal(expectedStride, image.Stride);
            Assert.Equal(expectedStride * 2, image.Pixels.Length);
        }

        [Fact]
        public void IndexedImageAlwaysHasFullPalette()
        {
            var image = new PixelImage(1, 1, ImagePixelFormat.Indexed8, new[] { Color.Red, Color.Lime });
            Assert.Equal(256, image.Palette.Count);
            Assert.Equal(2, image.PaletteSize);
            Assert.Equal(Color.Lime.ToArgb(), image.Palette[1].ToArgb());
            Assert.Equal(Color.Black.ToArgb(), image.Palette[255].ToArgb());
        }

        [Fact]
        public void IndexedToBgraUsesPaletteIncludingAlpha()
        {
            var palette = new[] { Color.FromArgb(0, 255, 0, 255), Color.FromArgb(255, 10, 20, 30) };
            var image = new PixelImage(2, 1, ImagePixelFormat.Indexed8, new byte[] { 1, 0, 0, 0 }, palette);

            PixelImage bgra = PixelConversion.ToBgra32(image);

            Assert.Equal(new byte[] { 30, 20, 10, 255, 255, 0, 255, 0 }, bgra.GetRow(0).Slice(0, 8).ToArray());
        }

        [Fact]
        public void HighColorExpandsToFullRange()
        {
            // White, pure red, pure green in 5-6-5.
            ushort[] px = { 0xFFFF, 0xF800, 0x07E0 };
            var data = new byte[8];
            for (int i = 0; i < px.Length; i++) { data[i * 2] = (byte)px[i]; data[i * 2 + 1] = (byte)(px[i] >> 8); }
            var image = new PixelImage(3, 1, ImagePixelFormat.Rgb565, data);

            byte[] row = PixelConversion.ToBgra32(image).GetRow(0).ToArray();

            Assert.Equal(new byte[] { 255, 255, 255, 255 }, row[0..4]);
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, row[4..8]);
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, row[8..12]);
            Assert.Equal((ushort)0xF800, PixelConversion.ToRgb565(255, 0, 0));
        }

        [Fact]
        public void Rgb555AndBgr24Convert()
        {
            var image555 = new PixelImage(1, 1, ImagePixelFormat.Rgb555, new byte[] { 0x1F, 0x7C, 0, 0 }); // 0x7C1F = magenta
            Assert.Equal(new byte[] { 255, 0, 255, 255 }, PixelConversion.ToBgra32(image555).GetRow(0)[..4].ToArray());

            var image24 = new PixelImage(1, 1, ImagePixelFormat.Bgr24, new byte[] { 1, 2, 3, 0 });
            Assert.Equal(new byte[] { 1, 2, 3, 255 }, PixelConversion.ToBgra32(image24).GetRow(0)[..4].ToArray());
        }
    }
}
