using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using AGS.Editor.Build;
using AGS.Native;
using AGS.Types;

namespace AGS.Editor
{
    /// <summary>
    /// Imports images as sprites into the game's sprite file using upstream's own import code: the
    /// `spriteimport` tool converts the image (colour depth, transparency mode, alpha, palette remapping)
    /// exactly as the AGS tools do. Because that tool rebuilds a sprite file only from source images —
    /// and many projects have sprites whose sources are long gone — it is given a temporary project
    /// containing just the new sprite, and the result is merged into acsprset.spr; all other sprites
    /// are copied unchanged.
    /// </summary>
    public sealed class SpriteImporter
    {
        private readonly GameProject _project;
        private readonly AgsBuildToolchain _toolchain;

        public SpriteImporter(GameProject project, AgsBuildToolchain toolchain)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _toolchain = toolchain ?? throw new ArgumentNullException(nameof(toolchain));
        }

        /// <summary>First unused sprite slot from 1 up (slot 0 is the default sprite).</summary>
        public static int FindFreeSlot(Game game)
        {
            var used = game.RootSpriteFolder.GetAllSpritesFromAllSubFolders().Select(s => s.Number).ToHashSet();
            int slot = 1;
            while (used.Contains(slot)) slot++;
            return slot;
        }

        /// <summary>
        /// Imports <paramref name="imageFile"/> as a new sprite in <paramref name="folder"/>. The caller must
        /// not hold acsprset.spr open (it is replaced). Returns the new sprite.
        /// </summary>
        public Sprite Import(string imageFile, SpriteFolder folder = null,
            SpriteImportTransparency transparency = SpriteImportTransparency.LeaveAsIs, bool importAlphaChannel = true)
        {
            if (!File.Exists(imageFile))
                throw new FileNotFoundException("Image not found.", imageFile);
            Game game = _project.Game;
            folder ??= game.RootSpriteFolder;

            string fullPath = Path.GetFullPath(imageFile);
            int width, height;
            using (Imaging.PixelImage image = new NativeImageCodec().Decode(fullPath).Frames[0])
            {
                width = image.Width;
                height = image.Height;
            }

            int slot = FindFreeSlot(game);
            var sprite = new Sprite(slot, 0, 0, (int)game.Settings.ColorDepth * 8, SpriteImportResolution.Real, false)
            {
                SourceFile = ProjectPaths.ToStored(ProjectPaths.GetRelativeToBasePath(Path.GetFullPath(imageFile), _project.Directory)),
                TransparentColour = transparency,
                ImportAlphaChannel = importAlphaChannel,
                // Whole image, as upstream's editor stores it for a non-tiled import.
                ImportWidth = width,
                ImportHeight = height,
            };
            folder.Sprites.Add(sprite);
            try
            {
                ImportInto(new[] { sprite }, fullPath);
                return sprite;
            }
            catch
            {
                folder.Sprites.Remove(sprite);
                throw;
            }
        }

        /// <summary>
        /// Tiled import (SpriteTools.ImportNewSprites with a SpriteSheet): one new sprite per selection of the sheet,
        /// in free slots, each remembering its tile (OffsetX/Y, ImportWidth/Height, ImportAsTile) like upstream, so it
        /// can be reimported from the source. All tiles are converted in one run of the import tool.
        /// </summary>
        public IReadOnlyList<Sprite> ImportTiles(string imageFile, Utils.SpriteSheet sheet, SpriteFolder folder = null,
            SpriteImportTransparency transparency = SpriteImportTransparency.LeaveAsIs, bool importAlphaChannel = true)
        {
            if (!File.Exists(imageFile))
                throw new FileNotFoundException("Image not found.", imageFile);
            Game game = _project.Game;
            folder ??= game.RootSpriteFolder;
            string fullPath = Path.GetFullPath(imageFile);
            System.Drawing.Size size;
            using (Imaging.PixelImage image = new NativeImageCodec().Decode(fullPath).Frames[0])
                size = new System.Drawing.Size(image.Width, image.Height);
            var selections = sheet.GetSpriteSelections(size).ToList();
            if (selections.Count == 0)
                throw new AGSEditorException("The tile selection does not fit into the image.");

            var created = new List<Sprite>();
            try
            {
                foreach (System.Drawing.Rectangle selection in selections)
                {
                    var sprite = new Sprite(FindFreeSlot(game), 0, 0, (int)game.Settings.ColorDepth * 8, SpriteImportResolution.Real, false)
                    {
                        SourceFile = ProjectPaths.ToStored(ProjectPaths.GetRelativeToBasePath(fullPath, _project.Directory)),
                        TransparentColour = transparency,
                        ImportAlphaChannel = importAlphaChannel,
                        OffsetX = selection.Left,
                        OffsetY = selection.Top,
                        ImportWidth = selection.Width,
                        ImportHeight = selection.Height,
                        ImportAsTile = true,
                    };
                    folder.Sprites.Add(sprite);
                    created.Add(sprite);
                }
                ImportInto(created, fullPath);
                return created;
            }
            catch
            {
                foreach (Sprite sprite in created)
                    folder.Sprites.Remove(sprite);
                throw;
            }
        }

        /// <summary>Re-imports an existing sprite from its source file with its current import settings.</summary>
        public void Reimport(Sprite sprite)
        {
            string source = string.IsNullOrEmpty(sprite.SourceFile) ? null
                : Path.GetFullPath(Path.Combine(_project.Directory, ProjectPaths.ToLocal(sprite.SourceFile)));
            if (source == null || !File.Exists(source))
                throw new AGSEditorException($"The source image of sprite {sprite.Number} ('{sprite.SourceFile}') was not found.");
            if (!sprite.ImportAsTile || sprite.ImportWidth <= 0 || sprite.ImportHeight <= 0)
            {
                // Whole image (older projects store no import size).
                using Imaging.PixelImage image = new NativeImageCodec().Decode(source).Frames[0];
                sprite.OffsetX = 0;
                sprite.OffsetY = 0;
                sprite.ImportWidth = image.Width;
                sprite.ImportHeight = image.Height;
            }
            ImportInto(new[] { sprite }, source);
        }

        /// <summary>
        /// "Replace sprite from file..." (SpriteTools.ReplaceSprite without tiling): the sprite keeps its number and
        /// gets the whole image as its new source.
        /// </summary>
        public void Replace(Sprite sprite, string imageFile, SpriteImportTransparency transparency, bool importAlphaChannel)
        {
            if (!File.Exists(imageFile))
                throw new FileNotFoundException("Image not found.", imageFile);
            string fullPath = Path.GetFullPath(imageFile);
            var old = (sprite.SourceFile, sprite.TransparentColour, sprite.ImportAlphaChannel, sprite.ImportAsTile, sprite.OffsetX, sprite.OffsetY, sprite.ImportWidth, sprite.ImportHeight);
            using (Imaging.PixelImage image = new NativeImageCodec().Decode(fullPath).Frames[0])
            {
                sprite.ImportWidth = image.Width;
                sprite.ImportHeight = image.Height;
            }
            sprite.SourceFile = ProjectPaths.ToStored(ProjectPaths.GetRelativeToBasePath(fullPath, _project.Directory));
            sprite.TransparentColour = transparency;
            sprite.ImportAlphaChannel = importAlphaChannel;
            sprite.ImportAsTile = false;
            sprite.OffsetX = 0;
            sprite.OffsetY = 0;
            try
            {
                ImportInto(new[] { sprite }, fullPath);
            }
            catch
            {
                (sprite.SourceFile, sprite.TransparentColour, sprite.ImportAlphaChannel, sprite.ImportAsTile, sprite.OffsetX, sprite.OffsetY, sprite.ImportWidth, sprite.ImportHeight) = old;
                throw;
            }
        }

        /// <summary>
        /// "Restore all sprites from sources" (Tasks.RecreateSpriteFileFromSources): every sprite whose source file exists
        /// is reimported (one run of the import tool); the others stay untouched. Returns the sprites that were skipped.
        /// </summary>
        public IReadOnlyList<Sprite> RestoreFromSources(IEnumerable<Sprite> sprites)
        {
            var sources = new Dictionary<Sprite, string>();
            var skipped = new List<Sprite>();
            foreach (Sprite sprite in sprites)
            {
                string source = string.IsNullOrEmpty(sprite.SourceFile) ? null
                    : Path.GetFullPath(Path.Combine(_project.Directory, ProjectPaths.ToLocal(sprite.SourceFile)));
                if (source == null || !File.Exists(source))
                {
                    skipped.Add(sprite);
                    continue;
                }
                if (!sprite.ImportAsTile || sprite.ImportWidth <= 0 || sprite.ImportHeight <= 0)
                {
                    using Imaging.PixelImage image = new NativeImageCodec().Decode(source).Frames[0];
                    sprite.OffsetX = 0;
                    sprite.OffsetY = 0;
                    sprite.ImportWidth = image.Width;
                    sprite.ImportHeight = image.Height;
                }
                sources[sprite] = source;
            }
            if (sources.Count > 0)
                ImportInto(sources.Keys.ToList(), sprite => sources[sprite]);
            return skipped;
        }

        private void ImportInto(IReadOnlyList<Sprite> sprites, string sourcePath) => ImportInto(sprites, _ => sourcePath);

        private void ImportInto(IReadOnlyList<Sprite> sprites, Func<Sprite, string> sourceOf)
        {
            var bySlot = sprites.ToDictionary(sp => sp.Number.ToString());

            string work = Path.Combine(Path.GetTempPath(), "ags4mac-spriteimport-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                // 1. A temporary project that contains only these sprites (with an absolute source path).
                string agf = Path.Combine(work, GameProject.GAME_FILE_NAME);
                var doc = new XmlDocument();
                doc.LoadXml(_project.BuildGameXml());
                foreach (XmlElement other in doc.SelectNodes("/AGSEditorDocument/Game/Sprites//Sprite").Cast<XmlElement>().ToList())
                {
                    if (!bySlot.TryGetValue(other.GetAttribute("Slot"), out Sprite own))
                        other.ParentNode.RemoveChild(other);
                    else if (other.SelectSingleNode("Source/FileName") is XmlElement fileName)
                        fileName.InnerText = sourceOf(own);
                }
                using (var writer = XmlWriter.Create(agf, new XmlWriterSettings { Encoding = _project.Game.TextEncoding, Indent = true }))
                    doc.Save(writer);

                // 2. Upstream's import into a one-sprite sprite file.
                int storeFlags, compression;
                using (SpriteFile existing = SpriteFile.OpenInProject(_project.Directory))
                {
                    storeFlags = existing.StoreFlags;
                    compression = existing.Compression;
                }
                string tempSprites = Path.Combine(work, "import.spr");
                string log = RunSpriteImport(agf, tempSprites, storeFlags, compression);

                // 3. Merge into the game's sprite file, copying everything else unchanged.
                using (SpriteFile imported = SpriteFile.Open(tempSprites))
                {
                    var numbers = sprites.Select(sp => sp.Number).ToHashSet();
                    foreach (Sprite sprite in sprites)
                        if (!imported.GetInfo(sprite.Number).Exists)
                            throw new AGSEditorException($"The image '{sourceOf(sprite)}' could not be imported.\n" + log.Trim());
                    RewriteSpriteFile(_project.Directory, numbers.Max(),
                        (slot, existing) => numbers.Contains(slot) ? imported : existing);
                    foreach (Sprite sprite in sprites)
                    {
                        SpriteSlotInfo info = imported.GetInfo(sprite.Number);
                        sprite.Width = info.Width;
                        sprite.Height = info.Height;
                        sprite.ColorDepth = info.ColorDepth;
                    }
                }
            }
            finally
            {
                try { Directory.Delete(work, true); } catch (IOException) { }
            }
        }

        /// <summary>
        /// Rewrites the project's acsprset.spr/sprindex.dat (same storage flags and compression), copying each
        /// slot raw from the file <paramref name="sourceFor"/> returns (given the slot and the existing file),
        /// or writing an empty slot when it returns null or the source has no sprite there.
        /// </summary>
        internal static void RewriteSpriteFile(string projectDirectory, int minTopmost, Func<int, SpriteFile, SpriteFile> sourceFor) =>
            RewriteSpriteFile(projectDirectory, minTopmost, (slot, existing) =>
            {
                SpriteFile source = sourceFor(slot, existing);
                return source == null ? ((SpriteFile, int)?)null : (source, slot);
            });

        /// <summary>As above, but the source may also name the slot to copy from (sprite renumbering).</summary>
        internal static void RewriteSpriteFile(string projectDirectory, int minTopmost, Func<int, SpriteFile, (SpriteFile File, int Slot)?> sourceFor) =>
            RewriteSpriteFile(projectDirectory, minTopmost, sourceFor, null);

        /// <summary>As above; <paramref name="newImage"/> may give new image data for a slot instead (cropping).</summary>
        internal static void RewriteSpriteFile(string projectDirectory, int minTopmost, Func<int, SpriteFile, (SpriteFile File, int Slot)?> sourceFor,
            Func<int, Imaging.PixelImage> newImage)
        {
            string spriteFile = Path.Combine(projectDirectory, SpriteFile.DefaultSpriteFileName);
            string indexFile = Path.Combine(projectDirectory, SpriteFile.DefaultSpriteIndexName);
            string newSprites = spriteFile + ".new";
            string newIndex = indexFile + ".new";
            try
            {
                using (SpriteFile existing = SpriteFile.OpenInProject(projectDirectory))
                using (SpriteFileWriter writer = SpriteFileWriter.Create(newSprites, existing.StoreFlags, existing.Compression))
                {
                    int last = Math.Max(existing.TopmostSprite, minTopmost);
                    for (int slot = 0; slot <= last; slot++)
                    {
                        Imaging.PixelImage replacement = newImage?.Invoke(slot);
                        if (replacement != null)
                        {
                            writer.WriteSprite(replacement);
                            continue;
                        }
                        var source = sourceFor(slot, existing);
                        if (source != null && source.Value.File.GetInfo(source.Value.Slot).Exists)
                            writer.CopySprite(source.Value.File, source.Value.Slot);
                        else
                            writer.WriteEmptySlot();
                    }
                    writer.Finish(newIndex);
                }
                File.Move(newSprites, spriteFile, overwrite: true);
                File.Move(newIndex, indexFile, overwrite: true);
            }
            finally
            {
                File.Delete(newSprites);
                File.Delete(newIndex);
            }
        }

        // Values of AGS::Common::SpriteCompression, as named on spriteimport's command line.
        private static readonly string[] CompressionNames = { "none", "rle", "lzw", "deflate" };

        private string RunSpriteImport(string agf, string output, int storeFlags, int compression)
        {
            string tool = Path.Combine(_toolchain.ToolsDirectory, "spriteimport");
            var psi = new ProcessStartInfo(tool)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = _project.Directory,
            };
            foreach (string arg in new[] { "-c", agf, output, "-r", _project.Directory,
                "-s", storeFlags.ToString("x"), "-z", CompressionNames[Math.Clamp(compression, 0, CompressionNames.Length - 1)], "-v" })
                psi.ArgumentList.Add(arg);
            using Process process = Process.Start(psi);
            string log = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 || !File.Exists(output))
                throw new AGSEditorException("Sprite import failed:\n" + log.Trim());
            return log;
        }
    }
}
