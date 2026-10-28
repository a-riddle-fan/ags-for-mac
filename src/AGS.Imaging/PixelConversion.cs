using System;
using System.Collections.Generic;
using System.Drawing;

namespace AGS.Imaging
{
    /// <summary>Pixel format conversions between the AGS image formats.</summary>
    public static class PixelConversion
    {
        /// <summary>
        /// Converts any image to 32-bit BGRA. Indexed pixels take the palette color (including its alpha);
        /// 15/16-bit channels are widened by bit replication, so full intensity maps to 255.
        /// </summary>
        public static PixelImage ToBgra32(IImage source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var result = new PixelImage(source.Width, source.Height, ImagePixelFormat.Bgra32);
            byte[] src = new byte[source.Stride * source.Height];
            source.CopyPixels(src);
            Span<byte> dst = result.Pixels;
            IReadOnlyList<Color> palette = source.Palette;

            for (int y = 0; y < source.Height; y++)
            {
                int s = y * source.Stride;
                int d = y * result.Stride;
                for (int x = 0; x < source.Width; x++, d += 4)
                {
                    byte b, g, r, a = 255;
                    switch (source.PixelFormat)
                    {
                        case ImagePixelFormat.Indexed8:
                            Color c = palette[src[s++]];
                            b = c.B; g = c.G; r = c.R; a = c.A;
                            break;
                        case ImagePixelFormat.Rgb555:
                        {
                            int v = src[s] | (src[s + 1] << 8);
                            s += 2;
                            r = Expand5((v >> 10) & 0x1F);
                            g = Expand5((v >> 5) & 0x1F);
                            b = Expand5(v & 0x1F);
                            break;
                        }
                        case ImagePixelFormat.Rgb565:
                        {
                            int v = src[s] | (src[s + 1] << 8);
                            s += 2;
                            r = Expand5((v >> 11) & 0x1F);
                            g = Expand6((v >> 5) & 0x3F);
                            b = Expand5(v & 0x1F);
                            break;
                        }
                        case ImagePixelFormat.Bgr24:
                            b = src[s]; g = src[s + 1]; r = src[s + 2];
                            s += 3;
                            break;
                        case ImagePixelFormat.Bgra32:
                            b = src[s]; g = src[s + 1]; r = src[s + 2]; a = src[s + 3];
                            s += 4;
                            break;
                        default:
                            throw new NotSupportedException($"Unsupported pixel format {source.PixelFormat}.");
                    }
                    dst[d] = b;
                    dst[d + 1] = g;
                    dst[d + 2] = r;
                    dst[d + 3] = a;
                }
            }
            return result;
        }

        /// <summary>Packs 8-bit channels into an RGB 5-6-5 value (truncating, as AGS does).</summary>
        public static ushort ToRgb565(byte r, byte g, byte b)
        {
            return (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
        }

        private static byte Expand5(int v) => (byte)((v << 3) | (v >> 2));
        private static byte Expand6(int v) => (byte)((v << 2) | (v >> 4));
    }
}
