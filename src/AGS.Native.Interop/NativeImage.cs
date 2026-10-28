using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using AGS.Imaging;

namespace AGS.Native
{
    /// <summary>Converts native agsn_image handles to managed images.</summary>
    internal static class NativeImage
    {
        /// <summary>
        /// Copies a native image into a <see cref="PixelImage"/>. Indexed images take the native palette
        /// if <paramref name="usePalette"/>, otherwise <paramref name="fallbackPalette"/> (e.g. the game palette for 8-bit sprites).
        /// </summary>
        public static unsafe PixelImage ToPixelImage(IntPtr handle, bool usePalette, IReadOnlyList<Color> fallbackPalette)
        {
            Check(NativeMethods.ImageGetInfo(handle, out NativeMethods.ImageInfo info));
            ImagePixelFormat format = FromNative(info.Format);

            IReadOnlyList<Color> palette = null;
            if (format == ImagePixelFormat.Indexed8)
            {
                if (usePalette)
                {
                    var native = new NativeMethods.Rgba[256];
                    fixed (NativeMethods.Rgba* p = native)
                        Check(NativeMethods.ImageCopyPalette(handle, p));
                    palette = ToPalette(native);
                }
                else
                {
                    palette = fallbackPalette;
                }
            }

            var image = new PixelImage(info.Width, info.Height, format, palette);
            fixed (byte* dst = image.Pixels)
                Check(NativeMethods.ImageCopyPixels(handle, dst, (nuint)image.Pixels.Length, (nuint)image.Stride));
            return image;
        }

        /// <summary>
        /// Palette alpha is only stored by PNG (tRNS). The BMP/PCX loaders and grayscale PNGs leave it 0,
        /// so a palette whose alpha is 0 everywhere is treated as fully opaque.
        /// </summary>
        public static Color[] ToPalette(NativeMethods.Rgba[] native)
        {
            bool hasAlpha = false;
            foreach (var e in native)
                if (e.A != 0) { hasAlpha = true; break; }

            var palette = new Color[native.Length];
            for (int i = 0; i < native.Length; i++)
                palette[i] = Color.FromArgb(hasAlpha ? native[i].A : 255, native[i].R, native[i].G, native[i].B);
            return palette;
        }

        public static ImagePixelFormat FromNative(int format)
        {
            switch (format)
            {
                case NativeMethods.AGSN_PXFMT_INDEXED8: return ImagePixelFormat.Indexed8;
                case NativeMethods.AGSN_PXFMT_R5G5B5: return ImagePixelFormat.Rgb555;
                case NativeMethods.AGSN_PXFMT_R5G6B5: return ImagePixelFormat.Rgb565;
                case NativeMethods.AGSN_PXFMT_R8G8B8: return ImagePixelFormat.Bgr24;
                case NativeMethods.AGSN_PXFMT_A8R8G8B8: return ImagePixelFormat.Bgra32;
                default: throw new InvalidDataException($"Unsupported native pixel format {format}.");
            }
        }

        public static void Check(int result)
        {
            if (result != NativeMethods.AGSN_OK)
                throw new InvalidDataException($"libagsnative error {result}: {NativeMethods.GetLastError()}");
        }
    }
}
