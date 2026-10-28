using System;
using System.Collections.Generic;
using System.Drawing;

namespace AGS.Imaging
{
    /// <summary>
    /// A raster image stored as raw pixel rows in one of the AGS pixel formats, plus a palette for
    /// indexed images. Toolkit-neutral: decoders produce it, the UI converts it to its own bitmaps.
    /// </summary>
    public sealed class PixelImage : IImage
    {
        private readonly byte[] _pixels;
        private readonly Color[] _palette;

        /// <summary>Creates a zero-filled image (transparent black / palette index 0).</summary>
        public PixelImage(int width, int height, ImagePixelFormat format, IReadOnlyList<Color> palette = null)
            : this(width, height, format, null, palette)
        {
        }

        /// <summary>
        /// Creates an image from existing pixel rows. <paramref name="pixels"/> must hold at least
        /// <c>GetStride(format, width) * height</c> bytes; it is copied.
        /// </summary>
        public PixelImage(int width, int height, ImagePixelFormat format, ReadOnlySpan<byte> pixels, IReadOnlyList<Color> palette = null)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (palette != null && format != ImagePixelFormat.Indexed8)
                throw new ArgumentException("Only indexed images have a palette.", nameof(palette));
            if (palette != null && palette.Count > 256)
                throw new ArgumentException("A palette has at most 256 entries.", nameof(palette));

            Width = width;
            Height = height;
            PixelFormat = format;
            Stride = GetStride(format, width);
            _pixels = new byte[Stride * height];
            if (!pixels.IsEmpty)
            {
                if (pixels.Length < _pixels.Length)
                    throw new ArgumentException($"Expected at least {_pixels.Length} bytes of pixel data, got {pixels.Length}.", nameof(pixels));
                pixels.Slice(0, _pixels.Length).CopyTo(_pixels);
            }

            if (format == ImagePixelFormat.Indexed8)
            {
                // Always keep a full 256-entry palette so any index is valid; unused entries are opaque black.
                _palette = new Color[256];
                for (int i = 0; i < _palette.Length; i++)
                    _palette[i] = (palette != null && i < palette.Count) ? palette[i] : Color.Black;
                PaletteSize = palette?.Count ?? 256;
            }
        }

        public int Width { get; }
        public int Height { get; }
        public ImagePixelFormat PixelFormat { get; }
        public int Stride { get; }

        /// <summary>The 256-entry palette of an indexed image; null otherwise.</summary>
        public IReadOnlyList<Color> Palette => _palette;

        /// <summary>Number of palette entries that came from the source image (≤ 256).</summary>
        public int PaletteSize { get; }

        /// <summary>Direct, mutable access to the pixel rows (Stride × Height bytes).</summary>
        public Span<byte> Pixels => _pixels;

        public Span<byte> GetRow(int y)
        {
            if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
            return new Span<byte>(_pixels, y * Stride, Stride);
        }

        public void SetPaletteEntry(int index, Color color)
        {
            if (_palette == null) throw new InvalidOperationException("Image has no palette.");
            _palette[index] = color;
        }

        public void CopyPixels(Span<byte> destination)
        {
            _pixels.CopyTo(destination);
        }

        public PixelImage Clone()
        {
            return new PixelImage(Width, Height, PixelFormat, _pixels, _palette);
        }

        public void Dispose()
        {
            // Managed memory only; nothing to release. Present to satisfy IImage.
        }

        /// <summary>Bytes per pixel for a format.</summary>
        public static int GetBytesPerPixel(ImagePixelFormat format)
        {
            switch (format)
            {
                case ImagePixelFormat.Indexed8: return 1;
                case ImagePixelFormat.Rgb555:
                case ImagePixelFormat.Rgb565: return 2;
                case ImagePixelFormat.Bgr24: return 3;
                case ImagePixelFormat.Bgra32: return 4;
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        /// <summary>Row size in bytes, padded to a multiple of 4 like the native AGS bitmaps.</summary>
        public static int GetStride(ImagePixelFormat format, int width)
        {
            return (width * GetBytesPerPixel(format) + 3) & ~3;
        }
    }
}
