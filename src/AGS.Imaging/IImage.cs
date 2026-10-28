using System;
using System.Collections.Generic;
using System.Drawing;

namespace AGS.Imaging
{
    /// <summary>
    /// An in-memory raster image. Replaces System.Drawing.Bitmap (Windows-only on .NET 6+)
    /// throughout the editor's model and plugin interfaces.
    /// </summary>
    public interface IImage : IDisposable
    {
        int Width { get; }
        int Height { get; }
        ImagePixelFormat PixelFormat { get; }

        /// <summary>Bytes per row, including any padding.</summary>
        int Stride { get; }

        /// <summary>Palette for <see cref="ImagePixelFormat.Indexed8"/> images; null for other formats.</summary>
        IReadOnlyList<Color> Palette { get; }

        /// <summary>Copies the raw pixel rows (<see cref="Stride"/> × <see cref="Height"/> bytes) into <paramref name="destination"/>.</summary>
        void CopyPixels(Span<byte> destination);
    }
}
