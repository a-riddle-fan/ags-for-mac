using System;
using System.IO;
using AGS.Types;

namespace AGS.Editor
{
    /// <summary>
    /// Path helpers for project data. Paths stored in projects made on Windows use backslashes; they are
    /// kept as stored (so projects stay identical across platforms) and only normalized when a file is
    /// actually accessed (<see cref="ToLocal"/>).
    /// </summary>
    public static class ProjectPaths
    {
        // ags/Editor/AGS.Editor/Components/AudioComponent.cs
        public const string AUDIO_CACHE_DIRECTORY = "AudioCache";
        private const string COMPILED_AUDIO_FILENAME_PREFIX = "au";

        /// <summary>Converts a stored project path to one usable on this OS.</summary>
        public static string ToLocal(string storedPath)
        {
            if (string.IsNullOrEmpty(storedPath) || Path.DirectorySeparatorChar == '\\')
                return storedPath;
            return storedPath.Replace('\\', Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Converts a path for storing in the project: relative paths use '\\' as the Windows editor writes
        /// them, so projects stay identical across platforms. Absolute paths are kept.
        /// </summary>
        public static string ToStored(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
                return path;
            return path.Replace('/', '\\');
        }

        // Utilities.GetRelativeToBasePath (ags/Editor/AGS.Editor/Utils/Utilities.cs)
        public static string GetRelativeToBasePath(string absolutePath, string basePath)
        {
            if (string.IsNullOrEmpty(absolutePath) || string.IsNullOrEmpty(basePath) ||
                !absolutePath.Contains(basePath))
            {
                return absolutePath;
            }

            Uri basePathUri = new Uri(basePath + Path.DirectorySeparatorChar);
            Uri currentPathUri = new Uri(absolutePath);

            return Uri.UnescapeDataString(basePathUri.MakeRelativeUri(currentPathUri).OriginalString);
        }

        // AudioComponent.GetCacheFileName. Upstream builds it with Path.Combine; it is not saved in Game.agf.
        public static string GetAudioCacheFileName(AudioClip clip)
        {
            return Path.Combine(AUDIO_CACHE_DIRECTORY,
                string.Format("{0}{1:X6}{2}", COMPILED_AUDIO_FILENAME_PREFIX, clip.Index, Path.GetExtension(ToLocal(clip.SourceFileName))));
        }
    }
}
