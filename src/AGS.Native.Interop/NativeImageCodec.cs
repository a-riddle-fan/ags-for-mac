using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using AGS.Imaging;

namespace AGS.Native
{
    /// <summary>
    /// BMP, PCX and PNG through upstream's own loaders (ags/Common/gfx/image_file), so palette indices,
    /// palette alpha (PNG tRNS) and 15/16/24/32-bit layouts are read exactly as AGS's tools read them.
    /// Register after general-purpose codecs so it takes precedence for these formats.
    /// </summary>
    public sealed class NativeImageCodec : IImageDecoder, IImageEncoder
    {
        private static readonly string[] _extensions = { ".bmp", ".pcx", ".png" };

        public IReadOnlyCollection<string> Extensions => _extensions;

        public static void RegisterAll()
        {
            NativeLibraryInfo.EnsureCompatible();
            var codec = new NativeImageCodec();
            ImageCodecs.Register((IImageDecoder)codec);
            ImageCodecs.Register((IImageEncoder)codec);
        }

        public DecodedImage Decode(string fileName)
        {
            IntPtr handle = NativeMethods.ImageLoad(fileName);
            if (handle == IntPtr.Zero)
                throw new InvalidDataException($"Unable to load image data from '{fileName}': {NativeMethods.GetLastError()}");
            try
            {
                PixelImage image = NativeImage.ToPixelImage(handle, usePalette: true, fallbackPalette: null);
                return new DecodedImage(new[] { image }, image.PixelFormat == ImagePixelFormat.Indexed8 ? image.Palette : null);
            }
            finally
            {
                NativeMethods.ImageFree(handle);
            }
        }

        public unsafe void Encode(IImage image, string fileName)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            byte[] pixels = new byte[image.Stride * image.Height];
            image.CopyPixels(pixels);

            NativeMethods.Rgba[] palette = null;
            if (image.PixelFormat == ImagePixelFormat.Indexed8)
            {
                palette = new NativeMethods.Rgba[256];
                for (int i = 0; i < 256 && i < image.Palette.Count; i++)
                {
                    Color c = image.Palette[i];
                    palette[i] = new NativeMethods.Rgba { R = c.R, G = c.G, B = c.B, A = c.A };
                }
            }

            fixed (byte* src = pixels)
            fixed (NativeMethods.Rgba* pal = palette)
            {
                int result = NativeMethods.ImageSave(fileName, image.Width, image.Height, ToNative(image.PixelFormat),
                    src, (nuint)image.Stride, pal, 0);
                if (result != NativeMethods.AGSN_OK)
                    throw new IOException($"Unable to save image '{fileName}': {NativeMethods.GetLastError()}");
            }
        }

        private static int ToNative(ImagePixelFormat format)
        {
            switch (format)
            {
                case ImagePixelFormat.Indexed8: return NativeMethods.AGSN_PXFMT_INDEXED8;
                case ImagePixelFormat.Rgb555: return NativeMethods.AGSN_PXFMT_R5G5B5;
                case ImagePixelFormat.Rgb565: return NativeMethods.AGSN_PXFMT_R5G6B5;
                case ImagePixelFormat.Bgr24: return NativeMethods.AGSN_PXFMT_R8G8B8;
                case ImagePixelFormat.Bgra32: return NativeMethods.AGSN_PXFMT_A8R8G8B8;
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }
    }
}
