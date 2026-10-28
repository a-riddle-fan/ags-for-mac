using System;
using System.Collections.Generic;
using System.IO;
using AGS.Types;

namespace AGS.Editor
{
    /// <summary>Audio clip import of upstream AudioComponent ("Add audio file(s)...").</summary>
    public static class AudioOperations
    {
        // AudioComponent.AUDIO_FILES_FILTER
        public static readonly string[] SupportedExtensions = { "ogg", "mp3", "wav", "voc", "mid", "mod", "xm", "s3m", "it" };

        // AudioComponent._fileTypeMappings
        private static readonly Dictionary<string, AudioClipFileType> FileTypeMappings = new Dictionary<string, AudioClipFileType>
        {
            { ".mp3", AudioClipFileType.MP3 },
            { ".ogg", AudioClipFileType.OGG },
            { ".voc", AudioClipFileType.VOC },
            { ".wav", AudioClipFileType.WAV },
            { ".mid", AudioClipFileType.MIDI },
            { ".mod", AudioClipFileType.MOD },
            { ".xm", AudioClipFileType.MOD },
            { ".s3m", AudioClipFileType.MOD },
            { ".it", AudioClipFileType.MOD },
        };

        /// <summary>
        /// ImportAudioFiles: one clip per file in <paramref name="folder"/> (its default bundling and type), the file
        /// copied into AudioCache. Files that fail are reported through <paramref name="warn"/> and skipped.
        /// </summary>
        public static IReadOnlyList<AudioClip> ImportAudioFiles(GameProject project, IEnumerable<string> files, AudioClipFolder folder = null,
            Action<string> warn = null)
        {
            Game game = project.Game;
            folder ??= game.RootAudioClipFolder;
            var added = new List<AudioClip>();
            foreach (string fileName in files)
            {
                AudioClip newClip = CreateAudioClipForFile(project, fileName, warn);
                if (newClip != null)
                {
                    newClip.BundlingType = folder.DefaultBundlingType;
                    newClip.Type = folder.DefaultType;
                    folder.Items.Add(newClip);
                    added.Add(newClip);
                }
            }
            if (added.Count > 0)
                TypeConverterLists.Refresh(game);
            return added;
        }

        private static AudioClip CreateAudioClipForFile(GameProject project, string sourceFileName, Action<string> warn)
        {
            Game game = project.Game;
            string newScriptName = EnsureScriptNameIsUnique(game, Path.GetFileNameWithoutExtension(sourceFileName));
            AudioClip newClip = new AudioClip(game.RootAudioClipFolder.GetAllItemsCount(), newScriptName, game.GetNextAudioIndex());
            try
            {
                SetAudioClipSourceFile(project, newClip, sourceFileName);
            }
            catch (Exception ex)
            {
                warn?.Invoke("Failed to create audio clip from " + sourceFileName + ": " + ex.Message);
                return null;
            }
            return newClip;
        }

        /// <summary>AudioComponent.SetAudioClipSourceFile.</summary>
        public static AudioClip SetAudioClipSourceFile(GameProject project, AudioClip clip, string fullSourceFileName)
        {
            if (string.IsNullOrEmpty(fullSourceFileName))
                return null;
            if (!File.Exists(fullSourceFileName))
                throw new FileNotFoundException("Source file doesn't exist.");
            if (new FileInfo(fullSourceFileName).Length == 0)
                throw new FileLoadException("New source file is empty.");

            string fileExtension = Path.GetExtension(fullSourceFileName).ToLowerInvariant();
            if (!FileTypeMappings.ContainsKey(fileExtension))
                throw new InvalidOperationException("Source file is of unsupported type.");

            string fullPath = Path.GetFullPath(fullSourceFileName);
            string sourceFileName = ProjectPaths.ToStored(ProjectPaths.GetRelativeToBasePath(fullPath, project.Directory));
            string newCacheFileName = Path.Combine(ProjectPaths.AUDIO_CACHE_DIRECTORY,
                string.Format("au{0:X6}{1}", clip.Index, Path.GetExtension(fullPath)));
            DateTime lastModifiedDate = File.GetLastWriteTimeUtc(fullPath);

            string previousCacheFileName = clip.CacheFileName;
            string cachePath = Path.Combine(project.Directory, newCacheFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
            // Utilities.CopyFileAndSetDestinationWritable
            File.Copy(fullPath, cachePath, overwrite: true);
            File.SetAttributes(cachePath, File.GetAttributes(cachePath) & ~FileAttributes.ReadOnly);
            if (!string.IsNullOrEmpty(previousCacheFileName) && previousCacheFileName != newCacheFileName)
                TryDelete(Path.Combine(project.Directory, ProjectPaths.ToLocal(previousCacheFileName))); // in case extension is different
            TryDelete(Path.Combine(project.Directory, "Compiled", "Data", "audio.vox"));

            clip.FileType = FileTypeMappings[fileExtension];
            clip.FileLastModifiedDate = lastModifiedDate;
            clip.SourceFileName = sourceFileName;
            clip.CacheFileName = newCacheFileName;
            clip.Length = TimeSpan.MinValue;
            project.Game.FilesAddedOrRemoved = true;
            return clip;
        }

        /// <summary>AudioComponent.EnsureScriptNameIsUnique.</summary>
        private static string EnsureScriptNameIsUnique(Game game, string nameToTry, int truncateToLength = int.MaxValue)
        {
            nameToTry = AGS.Types.Utilities.RemoveInvalidCharactersFromScriptName(nameToTry);
            nameToTry = "a" + char.ToUpperInvariant(nameToTry[0]) + nameToTry.Substring(1);
            string tryThisTime = nameToTry.Length > truncateToLength ? nameToTry.Substring(0, truncateToLength) : nameToTry;
            int suffix = 0;
            while (game.IsScriptNameAlreadyUsed(tryThisTime, null))
            {
                suffix++;
                string suffixStr = suffix.ToString();
                if (nameToTry.Length + suffixStr.Length > truncateToLength)
                    tryThisTime = nameToTry.Substring(0, truncateToLength - suffixStr.Length) + suffix;
                else
                    tryThisTime = nameToTry + suffix;
            }
            return tryThisTime;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
