using System;
using System.IO;
using AGS.Native;
using AGS.Types;

namespace AGS.Editor
{
    /// <summary>Font operations of upstream FontsComponent (new, delete, import over a font).</summary>
    public static class FontOperations
    {
        // FontsComponent.BUILT_IN_FONTS / DEFAULT_IMPORTED_FONT_SIZE
        public const int BuiltInFonts = 3;
        public const int DefaultImportedFontSize = 10;

        /// <summary>COMMAND_NEW_ITEM: a copy of font 0 (settings and file) in the next slot.</summary>
        public static Font AddFont(GameProject project)
        {
            var items = project.Game.Fonts;
            Font copyFrom = items[0];
            var newItem = new Font
            {
                ID = items.Count,
                OutlineStyle = FontOutlineStyle.None,
                PointSize = copyFrom.PointSize,
            };
            newItem.Name = "Font " + newItem.ID;
            newItem.SourceFilename = ProjectPaths.ToStored(ProjectPaths.GetRelativeToBasePath(copyFrom.SourceFilename, project.Directory));
            newItem.ProjectFilename = $"agsfnt{newItem.ID}{Path.GetExtension(copyFrom.ProjectFilename)}";
            items.Add(newItem);
            CopyFont(project.Directory, copyFrom.ID, newItem.ID);
            TypeConverterLists.Refresh(project.Game);
            return newItem;
        }

        /// <summary>COMMAND_DELETE_ITEM: removes the font file and renumbers the fonts (and their files) after it.</summary>
        public static void DeleteFont(GameProject project, Font font)
        {
            if (font.ID < BuiltInFonts)
                throw new AGSEditorException("The built-in fonts 0-2 can't be deleted.");
            string dir = project.Directory;
            int removingID = font.ID;
            TryDelete(Path.Combine(dir, font.WFNFileName));
            TryDelete(Path.Combine(dir, font.TTFFileName));
            foreach (Font item in project.Game.Fonts)
            {
                if (item.ID > removingID)
                {
                    if (File.Exists(Path.Combine(dir, item.WFNFileName)))
                        File.Move(Path.Combine(dir, item.WFNFileName), Path.Combine(dir, "agsfnt" + (item.ID - 1) + ".wfn"));
                    if (File.Exists(Path.Combine(dir, item.TTFFileName)))
                        File.Move(Path.Combine(dir, item.TTFFileName), Path.Combine(dir, "agsfnt" + (item.ID - 1) + ".ttf"));
                    item.ID--;
                }
            }
            project.Game.Fonts.Remove(font);
            project.Game.FilesAddedOrRemoved = true;
            TypeConverterLists.Refresh(project.Game);
        }

        /// <summary>
        /// ImportFont: a TTF (at <paramref name="pointSize"/>), WFN or Sierra SCI font file (any other name, as
        /// "font.*" in the upstream file dialog; converted to WFN) replaces the font's file.
        /// </summary>
        public static void ImportFont(GameProject project, Font font, string fileName, int pointSize = DefaultImportedFontSize)
        {
            string dir = project.Directory;
            string newTTFName = Path.Combine(dir, "agsfnt" + font.ID + ".ttf");
            string newWFNName = Path.Combine(dir, "agsfnt" + font.ID + ".wfn");
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            string stored = ProjectPaths.ToStored(ProjectPaths.GetRelativeToBasePath(Path.GetFullPath(fileName), dir));
            if (ext == ".ttf")
            {
                // ImportTTFFont
                File.Copy(fileName, newTTFName, true);
                TryDelete(newWFNName);
                font.PointSize = pointSize;
                font.SizeMultiplier = 1;
                font.SourceFilename = stored;
                font.ProjectFilename = font.TTFFileName;
            }
            else
            {
                // ImportWFNFont: a WFN file is copied, anything else is converted from SCI
                TryDelete(newTTFName);
                if (ext == ".wfn")
                    File.Copy(fileName, newWFNName, true);
                else
                    NativeGuiRenderer.ImportSciFont(fileName, newWFNName);
                font.PointSize = 0;
                font.SizeMultiplier = 1;
                font.SourceFilename = stored;
                font.ProjectFilename = font.WFNFileName;
            }
            File.SetAttributes(ext == ".ttf" ? newTTFName : newWFNName, FileAttributes.Normal);
            project.Game.FilesAddedOrRemoved = true;
        }

        /// <summary>Utilities.CopyFont.</summary>
        private static void CopyFont(string dir, int fromSlot, int toSlot)
        {
            if (fromSlot == toSlot) return;
            foreach (string ext in new[] { ".wfn", ".ttf" })
            {
                string from = Path.Combine(dir, "agsfnt" + fromSlot + ext);
                if (File.Exists(from))
                {
                    string to = Path.Combine(dir, "agsfnt" + toSlot + ext);
                    File.Copy(from, to, true);
                    File.SetAttributes(to, FileAttributes.Normal); // if a read-only file was copied, make the new one normal
                    return;
                }
            }
            throw new AGSEditorException("Unable to create new font: Font " + fromSlot + " not found.");
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
