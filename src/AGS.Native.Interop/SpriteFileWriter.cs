using System;
using System.IO;
using AGS.Imaging;

namespace AGS.Native
{
    /// <summary>
    /// Writes a sprite file slot by slot with upstream's writer (AGS::Common::SpriteFileWriter), like the
    /// upstream editor's SpriteFileWriter_NET. Write slots in order from 0, then call <see cref="Finish"/>.
    /// </summary>
    public sealed class SpriteFileWriter : IDisposable
    {
        private IntPtr _handle;

        private SpriteFileWriter(IntPtr handle)
        {
            _handle = handle;
        }

        /// <param name="storeFlags">AGS::Common::SpriteStorage flags (1 = optimize for size).</param>
        /// <param name="compression">AGS::Common::SpriteCompression (0 none, 1 RLE, 2 LZW, 3 Deflate).</param>
        public static SpriteFileWriter Create(string fileName, int storeFlags, int compression)
        {
            NativeLibraryInfo.EnsureCompatible();
            IntPtr handle = NativeMethods.SpriteWriterCreate(fileName, storeFlags, compression);
            if (handle == IntPtr.Zero)
                throw new IOException(NativeMethods.GetLastError());
            return new SpriteFileWriter(handle);
        }

        /// <summary>Writes the next slot. Sprites are 8-bit indexed, 16-bit (Rgb565) or 32-bit (Bgra32).</summary>
        public unsafe void WriteSprite(PixelImage sprite)
        {
            if (sprite == null) throw new ArgumentNullException(nameof(sprite));
            int format;
            switch (sprite.PixelFormat)
            {
                case ImagePixelFormat.Indexed8: format = NativeMethods.AGSN_PXFMT_INDEXED8; break;
                case ImagePixelFormat.Rgb565: format = NativeMethods.AGSN_PXFMT_R5G6B5; break;
                case ImagePixelFormat.Bgra32: format = NativeMethods.AGSN_PXFMT_A8R8G8B8; break;
                default: throw new ArgumentException($"Sprites can't be stored as {sprite.PixelFormat}; convert to Indexed8, Rgb565 or Bgra32.", nameof(sprite));
            }
            fixed (byte* p = sprite.Pixels)
                Check(NativeMethods.SpriteWriterWriteImage(Handle, sprite.Width, sprite.Height, format, p, (nuint)sprite.Stride));
        }

        public void WriteEmptySlot()
        {
            Check(NativeMethods.SpriteWriterWriteEmpty(Handle));
        }

        /// <summary>Writes the next slot by copying sprite <paramref name="slot"/> of <paramref name="source"/> as-is.</summary>
        public void CopySprite(SpriteFile source, int slot)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Check(NativeMethods.SpriteWriterCopyFrom(Handle, source.Handle, slot));
        }

        /// <summary>Completes the file; also writes the sprite index (sprindex.dat) if a path is given.</summary>
        public void Finish(string indexFileName = null)
        {
            Check(NativeMethods.SpriteWriterFinish(Handle, indexFileName));
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.SpriteWriterFree(_handle);
                _handle = IntPtr.Zero;
            }
        }

        private static void Check(int result)
        {
            if (result != NativeMethods.AGSN_OK)
                throw new IOException(NativeMethods.GetLastError());
        }

        private IntPtr Handle
        {
            get
            {
                if (_handle == IntPtr.Zero) throw new ObjectDisposedException(nameof(SpriteFileWriter));
                return _handle;
            }
        }
    }
}
