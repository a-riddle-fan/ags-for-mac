using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AGS.Imaging;
using AGS.Native;
using AGS.Types;

namespace AGS.Editor
{
    /// <summary>
    /// "Export all sprites..." / "Create source files for all sprites with missing / external sources..."
    /// (SpriteTools.ExportSprite/ExportSprites/ExpandExportPath, ExportSpritesOptions, SkipIf — logic kept).
    /// Images are read from the game's sprite file; BMP for sprites below 32 bit without alpha, else PNG.
    /// </summary>
    public static class SpriteExport
    {
        [Flags]
        public enum SkipIf
        {
            SourceValid = 0x0001,
            SourceLocal = 0x0002
        }

        public struct ExportSpritesOptions
        {
            public string ExportPath;
            public bool Recursive;
            public SkipIf SkipIf;
            public bool UpdateSourcePath;
            public bool ResetTileSettings;

            public ExportSpritesOptions(string path, bool recurse,
                SkipIf skipIf, bool updateSourcePath, bool resetTileSettings)
            {
                ExportPath = path;
                Recursive = recurse;
                SkipIf = skipIf;
                UpdateSourcePath = updateSourcePath;
                ResetTileSettings = resetTileSettings;
            }
        }

        /// <summary>Exports the sprites of <paramref name="folder"/> (and sub-folders if recursive); returns the count.</summary>
        public static int ExportSprites(GameProject project, SpriteFile sprites, SpriteFolder folder, ExportSpritesOptions options)
        {
            int count = 0;
            Color[] palette = project.Game.Palette.Select(p => p.Colour).ToArray();
            var skipIf = options.SkipIf;

            foreach (Sprite sprite in folder.Sprites)
            {
                if (skipIf > 0 && !string.IsNullOrEmpty(sprite.SourceFile))
                {
                    string checkPath = Path.IsPathRooted(sprite.SourceFile) ?
                        sprite.SourceFile :
                        Path.Combine(project.Directory, ProjectPaths.ToLocal(sprite.SourceFile));

                    if (skipIf.HasFlag(SkipIf.SourceValid) && File.Exists(checkPath))
                    {
                        continue; // skip if source is valid
                    }
                    else if (skipIf.HasFlag(SkipIf.SourceLocal) &&
                        File.Exists(checkPath) &&
                        PathsAreSameOrNested(Path.GetFullPath(checkPath), project.Directory))
                    {
                        continue; // skip if source is valid and local (inside the project folder)
                    }
                }

                ExportSprite(project, sprites, palette, sprite, options.ExportPath, options.UpdateSourcePath, options.ResetTileSettings);
                count++;
            }

            if (options.Recursive)
            {
                foreach (SpriteFolder subFolder in folder.SubFolders)
                {
                    count += ExportSprites(project, sprites, subFolder, options);
                }
            }
            return count;
        }

        public static void ExportSprite(GameProject project, SpriteFile sprites, Color[] palette, Sprite sprite, string path, bool updateSourcePath, bool resetTileSettings)
        {
            path = ExpandExportPath(sprite, path);

            // stop if the export path is empty (file extension isn't appended yet)
            if (String.IsNullOrWhiteSpace(path.Trim(new char[] { Path.DirectorySeparatorChar })))
            {
                throw new InvalidOperationException("Export path cannot be empty");
            }

            // for a relative path, the parent directory is the game folder
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(project.Directory, path);
            }

            // create directory if it doesn't exist
            DirectoryInfo parent = Directory.GetParent(path);

            if (parent != null)
            {
                parent.Create();
            }

            string format = sprite.ColorDepth < 32 && !sprite.AlphaChannel ? "bmp" : "png";
            path = string.Format("{0}.{1}", path, format);
            using (PixelImage image = sprites.LoadSprite(sprite.Number, palette))
            {
                if (image == null)
                    throw new AGSEditorException($"Sprite {sprite.Number} could not be read.");
                ImageCodecs.Save(image, path);
            }

            if (updateSourcePath)
            {
                sprite.SourceFile = ProjectPaths.ToStored(ProjectPaths.GetRelativeToBasePath(path, project.Directory));
            }

            if (resetTileSettings)
            {
                sprite.ImportAsTile = false;
                sprite.OffsetX = 0;
                sprite.OffsetY = 0;
            }
        }

        // SpriteTools.ExpandExportPath (unchanged): %propertyname% tokens, e.g. %Number%
        private static string ExpandExportPath(object obj, string path)
        {
            Type type = obj.GetType();
            PropertyInfo[] properties = type.GetProperties();
            Dictionary<string, string> replace = new Dictionary<string, string>();

            foreach (PropertyInfo property in properties)
            {
                string token = String.Format("%{0}%", property.Name.ToLowerInvariant());
                object value = property.GetValue(obj);
                replace.Add(token, value != null ? value.ToString() : "");
            }

            Regex regex = new Regex("%[a-zA-Z0-9]+%");

            return regex.Replace(path, delegate (Match m)
            {
                string lookup = m.Value.ToLower();

                if (replace.ContainsKey(lookup))
                {
                    return replace[lookup];
                }

                return m.Value;
            });
        }

        // Utilities.PathsAreSameOrNested
        private static bool PathsAreSameOrNested(string path, string basepath)
        {
            Uri baseUri = new Uri(basepath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            Uri pathUri = new Uri(path + Path.DirectorySeparatorChar);
            return baseUri.IsBaseOf(pathUri);
        }
    }
}
