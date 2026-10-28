using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AGS.Imaging;

namespace AGS.Editor.Avalonia.Services
{
    public static class ImageConversion
    {
        /// <summary>
        /// Converts any AGS image to an Avalonia bitmap (straight-alpha BGRA). With
        /// <paramref name="transparentMagenta"/>, pure magenta (AGS's transparent colour for
        /// non-alpha sprites) becomes transparent.
        /// </summary>
        public static WriteableBitmap ToBitmap(IImage image, bool transparentMagenta = false)
        {
            PixelImage bgra = PixelConversion.ToBgra32(image);
            if (transparentMagenta)
            {
                for (int y = 0; y < bgra.Height; y++)
                {
                    Span<byte> r = bgra.GetRow(y);
                    for (int x = 0; x < bgra.Width; x++)
                        if (r[x * 4] == 255 && r[x * 4 + 1] == 0 && r[x * 4 + 2] == 255)
                            r[x * 4 + 3] = 0;
                }
            }
            var bitmap = new WriteableBitmap(new PixelSize(bgra.Width, bgra.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (ILockedFramebuffer fb = bitmap.Lock())
            {
                byte[] row = new byte[bgra.Width * 4];
                for (int y = 0; y < bgra.Height; y++)
                {
                    bgra.GetRow(y).Slice(0, row.Length).CopyTo(row);
                    Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
                }
            }
            return bitmap;
        }

        /// <summary>An Avalonia bitmap (e.g. from the clipboard) as a 32-bit BGRA image with straight alpha.</summary>
        public static PixelImage FromBitmap(Bitmap bitmap)
        {
            int width = bitmap.PixelSize.Width, height = bitmap.PixelSize.Height;
            int stride = width * 4;
            byte[] data = new byte[stride * height];
            PixelFormat format = bitmap.Format ?? PixelFormat.Bgra8888;
            AlphaFormat alpha = bitmap.AlphaFormat ?? AlphaFormat.Premul;
            GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, width, height), pin.AddrOfPinnedObject(), data.Length, stride);
            }
            finally
            {
                pin.Free();
            }
            bool rgba = format == PixelFormat.Rgba8888;
            var image = new PixelImage(width, height, ImagePixelFormat.Bgra32);
            for (int y = 0; y < height; y++)
            {
                Span<byte> dst = image.GetRow(y);
                for (int x = 0; x < width; x++)
                {
                    int i = y * stride + x * 4;
                    byte c0 = data[i], c1 = data[i + 1], c2 = data[i + 2], a = data[i + 3];
                    byte b = rgba ? c2 : c0, g = c1, r = rgba ? c0 : c2;
                    if (alpha == AlphaFormat.Premul && a != 0 && a != 255)
                    {
                        b = (byte)Math.Min(255, b * 255 / a);
                        g = (byte)Math.Min(255, g * 255 / a);
                        r = (byte)Math.Min(255, r * 255 / a);
                    }
                    if (alpha == AlphaFormat.Opaque) a = 255;
                    dst[x * 4] = b; dst[x * 4 + 1] = g; dst[x * 4 + 2] = r; dst[x * 4 + 3] = a;
                }
            }
            return image;
        }
    }
}
