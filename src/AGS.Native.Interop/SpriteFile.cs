using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using AGS.Imaging;

namespace AGS.Native
{
    /// <summary>Metrics of one sprite slot.</summary>
    public readonly struct SpriteSlotInfo
    {
        public SpriteSlotInfo(bool exists, int width, int height, int bytesPerPixel)
        {
            Exists = exists;
            Width = width;
            Height = height;
            BytesPerPixel = bytesPerPixel;
        }

        public bool Exists { get; }
        public int Width { get; }
        public int Height { get; }
        /// <summary>1 = palette index into the game palette, 2 = 16-bit (5-6-5), 4 = 32-bit ARGB.</summary>
        public int BytesPerPixel { get; }
        public int ColorDepth => BytesPerPixel * 8;
    }

    /// <summary>
    /// Read access to a game's sprite file (acsprset.spr, with optional sprindex.dat), using upstream's
    /// own reader. Not thread-safe: the native file keeps a read position.
    /// </summary>
    public sealed class SpriteFile : IDisposable
    {
        public const string DefaultSpriteFileName = "acsprset.spr";
        public const string DefaultSpriteIndexName = "sprindex.dat";

        private IntPtr _handle;

        private SpriteFile(IntPtr handle)
        {
            _handle = handle;
        }

        /// <summary>Opens a sprite file. Pass the index file path if available; it's optional.</summary>
        public static SpriteFile Open(string spriteFilePath, string indexFilePath = null)
        {
            NativeLibraryInfo.EnsureCompatible();
            IntPtr handle = NativeMethods.SpriteFileOpen(spriteFilePath, indexFilePath);
            if (handle == IntPtr.Zero)
                throw new InvalidDataException(NativeMethods.GetLastError());
            return new SpriteFile(handle);
        }

        /// <summary>Opens acsprset.spr (and sprindex.dat, if present) in a game project folder.</summary>
        public static SpriteFile OpenInProject(string projectDirectory)
        {
            return Open(Path.Combine(projectDirectory, DefaultSpriteFileName), Path.Combine(projectDirectory, DefaultSpriteIndexName));
        }

        /// <summary>Highest slot number, or -1 if the file has no sprites.</summary>
        public int TopmostSprite => NativeMethods.SpriteFileGetTopmost(Handle);

        /// <summary>Number of slots holding a sprite.</summary>
        public int SpriteCount => NativeMethods.SpriteFileGetCount(Handle);

        /// <summary>AGS::Common::SpriteStorage flags.</summary>
        public int StoreFlags => NativeMethods.SpriteFileGetStoreFlags(Handle);

        /// <summary>AGS::Common::SpriteCompression (0 none, 1 RLE, 2 LZW, 3 Deflate).</summary>
        public int Compression => NativeMethods.SpriteFileGetCompression(Handle);

        public SpriteSlotInfo GetInfo(int slot)
        {
            NativeImage.Check(NativeMethods.SpriteFileGetInfo(Handle, slot, out NativeMethods.SpriteInfo info));
            return new SpriteSlotInfo(info.Exists != 0, info.Width, info.Height, info.BytesPerPixel);
        }

        /// <summary>
        /// Loads a sprite, or returns null if the slot is empty. 8-bit sprites index the game palette, so
        /// pass it as <paramref name="gamePalette"/> to get correct colors.
        /// </summary>
        public PixelImage LoadSprite(int slot, IReadOnlyList<Color> gamePalette = null)
        {
            IntPtr image = NativeMethods.SpriteFileLoad(Handle, slot);
            if (image == IntPtr.Zero)
            {
                string error = NativeMethods.GetLastError();
                if (string.IsNullOrEmpty(error))
                    return null;
                throw new InvalidDataException(error);
            }
            try
            {
                return NativeImage.ToPixelImage(image, usePalette: false, fallbackPalette: gamePalette);
            }
            finally
            {
                NativeMethods.ImageFree(image);
            }
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.SpriteFileClose(_handle);
                _handle = IntPtr.Zero;
            }
        }

        internal IntPtr Handle
        {
            get
            {
                if (_handle == IntPtr.Zero) throw new ObjectDisposedException(nameof(SpriteFile));
                return _handle;
            }
        }
    }
}
