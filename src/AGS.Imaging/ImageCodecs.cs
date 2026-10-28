using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace AGS.Imaging
{
    /// <summary>The decoded contents of an image file: one frame for most formats, several for animated GIFs.</summary>
    public sealed class DecodedImage
    {
        public DecodedImage(IReadOnlyList<PixelImage> frames, IReadOnlyList<Color> originalPalette = null)
        {
            if (frames == null || frames.Count == 0)
                throw new ArgumentException("An image file has at least one frame.", nameof(frames));
            Frames = frames;
            OriginalPalette = originalPalette;
        }

        public IReadOnlyList<PixelImage> Frames { get; }

        /// <summary>The palette stored in the file, if any (before padding to 256 entries).</summary>
        public IReadOnlyList<Color> OriginalPalette { get; }
    }

    public interface IImageDecoder
    {
        /// <summary>Lower-case file extensions including the dot, e.g. ".png".</summary>
        IReadOnlyCollection<string> Extensions { get; }

        /// <summary>Decodes the file. Throws <see cref="InvalidDataException"/> if it isn't a valid image.</summary>
        DecodedImage Decode(string fileName);
    }

    public interface IImageEncoder
    {
        /// <summary>Lower-case file extensions including the dot, e.g. ".bmp".</summary>
        IReadOnlyCollection<string> Extensions { get; }

        void Encode(IImage image, string fileName);
    }

    /// <summary>
    /// Registry of image decoders/encoders, chosen by file extension. Later registrations win, so a
    /// more faithful codec (e.g. the native AGS one) can override a general-purpose one.
    /// </summary>
    public static class ImageCodecs
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, IImageDecoder> _decoders = new Dictionary<string, IImageDecoder>();
        private static readonly Dictionary<string, IImageEncoder> _encoders = new Dictionary<string, IImageEncoder>();

        public static void Register(IImageDecoder decoder)
        {
            lock (_lock)
                foreach (string ext in decoder.Extensions)
                    _decoders[NormalizeExtension(ext)] = decoder;
        }

        public static void Register(IImageEncoder encoder)
        {
            lock (_lock)
                foreach (string ext in encoder.Extensions)
                    _encoders[NormalizeExtension(ext)] = encoder;
        }

        public static IReadOnlyCollection<string> DecodableExtensions
        {
            get { lock (_lock) return _decoders.Keys.OrderBy(e => e, StringComparer.Ordinal).ToArray(); }
        }

        public static IReadOnlyCollection<string> EncodableExtensions
        {
            get { lock (_lock) return _encoders.Keys.OrderBy(e => e, StringComparer.Ordinal).ToArray(); }
        }

        public static DecodedImage Load(string fileName)
        {
            if (!File.Exists(fileName))
                throw new FileNotFoundException($"Unable to open file '{fileName}'", fileName);
            IImageDecoder decoder;
            lock (_lock)
                _decoders.TryGetValue(NormalizeExtension(Path.GetExtension(fileName)), out decoder);
            if (decoder == null)
                throw new InvalidDataException($"No image decoder for '{Path.GetExtension(fileName)}' files ('{fileName}').");
            return decoder.Decode(fileName);
        }

        public static void Save(IImage image, string fileName)
        {
            IImageEncoder encoder;
            lock (_lock)
                _encoders.TryGetValue(NormalizeExtension(Path.GetExtension(fileName)), out encoder);
            if (encoder == null)
                throw new InvalidDataException($"No image encoder for '{Path.GetExtension(fileName)}' files ('{fileName}').");
            encoder.Encode(image, fileName);
        }

        private static string NormalizeExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return string.Empty;
            ext = ext.ToLowerInvariant();
            return ext[0] == '.' ? ext : "." + ext;
        }
    }
}
