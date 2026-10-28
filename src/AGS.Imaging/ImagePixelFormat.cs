namespace AGS.Imaging
{
    /// <summary>
    /// Pixel layouts the AGS editor works with. They mirror the System.Drawing formats that upstream
    /// uses for sprites, backgrounds and masks, and the native AGS formats in Common/gfx/bitmapdata.h
    /// (kPxFmt_Indexed8, R5G5B5, R5G6B5, R8G8B8, A8R8G8B8 in little-endian memory order).
    /// </summary>
    public enum ImagePixelFormat
    {
        /// <summary>8 bits per pixel, index into <see cref="IImage.Palette"/> (paletted games, area masks).</summary>
        Indexed8,
        /// <summary>16 bits per pixel, RGB 5-5-5 with the top bit unused (some 16-bit BMP files).</summary>
        Rgb555,
        /// <summary>16 bits per pixel, RGB 5-6-5 (16-bit games).</summary>
        Rgb565,
        /// <summary>24 bits per pixel, B-G-R byte order.</summary>
        Bgr24,
        /// <summary>32 bits per pixel, B-G-R-A byte order, straight (non-premultiplied) alpha.</summary>
        Bgra32,
    }
}
