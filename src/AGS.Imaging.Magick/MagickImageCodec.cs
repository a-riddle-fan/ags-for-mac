using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using ImageMagick;

namespace AGS.Imaging.Magick
{
    /// <summary>
    /// Decodes and encodes images with ImageMagick. Mirrors upstream's GIFLoader/GifDecoder: animated
    /// GIFs are coalesced so every frame is a full image. Palette images keep their palette, with each
    /// pixel mapped to the first palette entry of its color.
    /// </summary>
    public sealed class MagickImageCodec : IImageDecoder, IImageEncoder
    {
        private static readonly string[] _extensions = { ".gif", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".png" };

        IReadOnlyCollection<string> IImageDecoder.Extensions => _extensions;
        IReadOnlyCollection<string> IImageEncoder.Extensions => new[] { ".bmp", ".png", ".gif", ".tif", ".tiff" };

        /// <summary>Registers this codec for all its formats. Register native codecs afterwards to override.</summary>
        public static void RegisterAll()
        {
            var codec = new MagickImageCodec();
            ImageCodecs.Register((IImageDecoder)codec);
            ImageCodecs.Register((IImageEncoder)codec);
        }

        public DecodedImage Decode(string fileName)
        {
            MagickImageCollection collection;
            try
            {
                collection = new MagickImageCollection(fileName);
            }
            catch (MagickException ex)
            {
                throw new InvalidDataException($"Unable to load image data from '{fileName}': {ex.Message}", ex);
            }

            using (collection)
            {
                if (collection.Count == 0)
                    throw new InvalidDataException($"Unable to load image data from '{fileName}'");

                if (collection.Count > 1)
                    collection.Coalesce();

                IReadOnlyList<Color> originalPalette = GetPalette(collection[0]);
                var frames = new List<PixelImage>(collection.Count);
                foreach (IMagickImage<byte> frame in collection)
                    frames.Add(ToPixelImage(frame));
                return new DecodedImage(frames, originalPalette);
            }
        }

        public void Encode(IImage image, string fileName)
        {
            PixelImage bgra = PixelConversion.ToBgra32(image);
            var settings = new PixelReadSettings((uint)bgra.Width, (uint)bgra.Height, StorageType.Char, PixelMapping.BGRA);
            byte[] packed = new byte[bgra.Width * bgra.Height * 4];
            for (int y = 0; y < bgra.Height; y++)
                bgra.GetRow(y).Slice(0, bgra.Width * 4).CopyTo(packed.AsSpan(y * bgra.Width * 4));

            using (var magick = new MagickImage())
            {
                magick.ReadPixels(packed, settings);
                if (image.PixelFormat == ImagePixelFormat.Indexed8)
                {
                    // Keep it a palette image so 8-bit exports stay 8-bit.
                    magick.ClassType = ClassType.Pseudo;
                }
                magick.Write(fileName);
            }
        }

        private static IReadOnlyList<Color> GetPalette(IMagickImage<byte> image)
        {
            if (image.ClassType != ClassType.Pseudo || image.ColormapSize <= 0 || image.ColormapSize > 256)
                return null;
            var palette = new Color[image.ColormapSize];
            for (int i = 0; i < palette.Length; i++)
            {
                IMagickColor<byte> c = image.GetColormapColor(i);
                palette[i] = c == null ? Color.Black : Color.FromArgb(c.A, c.R, c.G, c.B);
            }
            return palette;
        }

        private static PixelImage ToPixelImage(IMagickImage<byte> image)
        {
            int width = (int)image.Width;
            int height = (int)image.Height;
            byte[] bgra;
            using (IPixelCollection<byte> pixels = image.GetPixels())
                bgra = pixels.ToByteArray(PixelMapping.BGRA);

            IReadOnlyList<Color> palette = GetPalette(image);
            if (palette != null)
            {
                var indexOf = new Dictionary<int, byte>();
                for (int i = palette.Count - 1; i >= 0; i--)
                    indexOf[palette[i].ToArgb()] = (byte)i;

                var indexed = new PixelImage(width, height, ImagePixelFormat.Indexed8, palette);
                bool allMapped = true;
                for (int y = 0; y < height && allMapped; y++)
                {
                    Span<byte> row = indexed.GetRow(y);
                    for (int x = 0; x < width; x++)
                    {
                        int p = (y * width + x) * 4;
                        int argb = (bgra[p + 3] << 24) | (bgra[p + 2] << 16) | (bgra[p + 1] << 8) | bgra[p];
                        if (!indexOf.TryGetValue(argb, out byte index))
                        {
                            allMapped = false;
                            break;
                        }
                        row[x] = index;
                    }
                }
                if (allMapped)
                    return indexed;
                // A pixel outside the colormap (shouldn't happen for GIF): fall back to true color.
            }

            var result = new PixelImage(width, height, ImagePixelFormat.Bgra32);
            for (int y = 0; y < height; y++)
                bgra.AsSpan(y * width * 4, width * 4).CopyTo(result.GetRow(y));
            return result;
        }
    }
}
