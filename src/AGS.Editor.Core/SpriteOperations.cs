using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AGS.Types;

namespace AGS.Editor
{
    /// <summary>
    /// Sprite operations of the sprite manager: deleting sprites (upstream AGSEditor.DeleteSprite +
    /// SpriteSelector.DeleteSelectedSprites) and the usage report (upstream SpriteTools.GetSpriteUsageReport,
    /// copied unchanged).
    /// </summary>
    public static class SpriteOperations
    {
        /// <summary>
        /// Deletes sprites that are not in use: removes them from their folders and writes empty slots into
        /// acsprset.spr. <paramref name="isUsedElsewhere"/> lets the UI veto a sprite (upstream PreDeleteSprite,
        /// e.g. an object in the open room). Returns, per sprite that was kept, the reason.
        /// </summary>
        public static IReadOnlyList<string> DeleteSprites(Game game, string projectDirectory, IEnumerable<Sprite> sprites,
            Func<int, string> isUsedElsewhere = null)
        {
            var problems = new List<string>();
            var deleted = new HashSet<int>();
            foreach (Sprite sprite in sprites.ToList())
            {
                string usageReport = GetSpriteUsageReport(sprite.Number, game) ?? isUsedElsewhere?.Invoke(sprite.Number);
                if (usageReport != null)
                {
                    problems.Add("Cannot delete a sprite because it is in use:" + Environment.NewLine + usageReport);
                    continue;
                }
                SpriteFolder parent = game.RootSpriteFolder.FindFolderThatContainsSprite(sprite.Number);
                if (parent == null)
                {
                    problems.Add("The sprite " + sprite.Number + " could not be found in any sprite folders");
                    continue;
                }
                parent.Sprites.Remove(sprite);
                deleted.Add(sprite.Number);
            }
            if (deleted.Count > 0)
                SpriteImporter.RewriteSpriteFile(projectDirectory, 0, (slot, existing) => deleted.Contains(slot) ? null : existing);
            return problems;
        }

        /// <summary>NativeConstants.MAX_STATIC_SPRITES (ac/spritecache.h).</summary>
        public const int MaxStaticSprites = 90000;

        /// <summary>SpriteSelector "Change sprite number" / "Assign sequential sprite numbers" checks + NativeProxy.ChangeSpriteNumbers:
        /// moves the sprites' image data to the new slots (other slots unchanged) and renumbers them.</summary>
        public static void ChangeSpriteNumbers(Game game, string projectDirectory, IReadOnlyList<Sprite> sprites, IReadOnlyList<int> newNumbers)
        {
            if (sprites.Count != newNumbers.Count) throw new ArgumentException("One new number per sprite is needed.");
            var moving = sprites.Select(s => s.Number).ToHashSet();
            var used = game.RootSpriteFolder.GetAllSpritesFromAllSubFolders().Select(s => s.Number).Where(n => !moving.Contains(n)).ToHashSet();
            var fromSlot = new Dictionary<int, int>();
            for (int i = 0; i < sprites.Count; i++)
            {
                int n = newNumbers[i];
                if (n < 0 || n >= MaxStaticSprites)
                    throw new AGSEditorException($"The sprite number must be between 0 and {MaxStaticSprites - 1}.");
                if (used.Contains(n) || fromSlot.ContainsKey(n))
                    throw new AGSEditorException("The destination sprite number " + n + " already exists.");
                fromSlot[n] = sprites[i].Number;
            }
            SpriteImporter.RewriteSpriteFile(projectDirectory, newNumbers.Max(), (slot, existing) =>
            {
                if (fromSlot.TryGetValue(slot, out int from)) return (existing, from);
                if (moving.Contains(slot)) return null; // vacated
                return (existing, slot);
            });
            for (int i = 0; i < sprites.Count; i++)
                sprites[i].Number = newNumbers[i];
        }

        /// <summary>NativeProxy.FindNearestFreeSpriteNumber: the first free slot from <paramref name="from"/> up
        /// (slots of <paramref name="ignore"/> count as free), or -1.</summary>
        public static int FindNearestFreeSpriteNumber(Game game, int from, IEnumerable<int> ignore = null)
        {
            var ignored = ignore?.ToHashSet() ?? new HashSet<int>();
            var used = game.RootSpriteFolder.GetAllSpritesFromAllSubFolders().Select(s => s.Number).Where(n => !ignored.Contains(n)).ToHashSet();
            for (int n = Math.Max(0, from); n < MaxStaticSprites; n++)
                if (!used.Contains(n)) return n;
            return -1;
        }

        /// <summary>Numbers for "Assign sequential sprite numbers" (SpriteSelector.AssignSequentialNumbers), or null.</summary>
        public static int[] SequentialNumbers(Game game, IReadOnlyList<Sprite> sprites, int firstNumber)
        {
            int[] oldNumbers = sprites.Select(s => s.Number).ToArray();
            var newNumbers = new int[sprites.Count];
            var taken = new HashSet<int>();
            for (int handled = 0, n = firstNumber; handled < sprites.Count; ++handled, ++n)
            {
                n = FindNearestFreeSpriteNumber(game, n, oldNumbers);
                while (n >= 0 && taken.Contains(n)) n = FindNearestFreeSpriteNumber(game, n + 1, oldNumbers);
                if (n < 0) return null;
                newNumbers[handled] = n;
                taken.Add(n);
            }
            return newNumbers;
        }

        /// <summary>SpriteSelector.AssignSpritesToView (AssignToView dialog options); returns the message to show.</summary>
        public static string AssignSpritesToView(Game game, List<int> spriteNumbers, int viewNumber, int loop,
            bool reverseFrames, bool addFramesToExistingLoop, bool continueIntoNextLoop, bool flipFrames)
        {
            if (reverseFrames)
            {
                spriteNumbers.Reverse();
            }

            View view = game.FindViewByID(viewNumber);
            if (view == null)
            {
                return "The view number you selected (" + viewNumber + ") does not exist.";
            }

            while (loop >= view.Loops.Count)
            {
                view.AddNewLoop();
            }

            if (!addFramesToExistingLoop)
            {
                view.Loops[loop].Frames.Clear();
            }

            foreach (int spriteNum in spriteNumbers)
            {
                if (view.Loops[loop].Full)
                {
                    if (!continueIntoNextLoop)
                    {
                        view.NotifyClientsOfUpdate();
                        return "The selected loop is now full. Not all the selected sprites were assigned.";
                    }
                    loop++;
                    if (loop >= view.Loops.Count)
                    {
                        view.AddNewLoop();
                    }
                    view.Loops[loop - 1].RunNextLoop = true;
                }
                ViewFrame newFrame = new ViewFrame();
                newFrame.ID = view.Loops[loop].Frames.Count;
                newFrame.Image = spriteNum;
                if (flipFrames)
                {
                    newFrame.Flipped = true;
                }
                view.Loops[loop].Frames.Add(newFrame);
            }
            view.NotifyClientsOfUpdate();
            return "The selected sprites were assigned successfully.";
        }

        /// <summary>SpriteSelector.DeleteFolder (VerifySpriteFolderTreeCanBeDeleted + DeleteSpriteFolder): removes a
        /// folder with its subfolders and sprites, unless a sprite in it is in use (then throws, nothing changed).</summary>
        public static void DeleteSpriteFolder(Game game, string projectDirectory, SpriteFolder folderToDelete)
        {
            if (folderToDelete == game.RootSpriteFolder)
                throw new AGSEditorException("The main sprite folder cannot be deleted.");
            var sprites = folderToDelete.GetAllSpritesFromAllSubFolders().ToList();
            foreach (Sprite sprite in sprites)
            {
                string usageReport = GetSpriteUsageReport(sprite.Number, game);
                if (usageReport != null)
                    throw new AGSEditorException("Folder cannot be deleted because sprite " + sprite.Number + " is in use:" + Environment.NewLine + usageReport);
            }
            SpriteFolder parent = FindParent(game.RootSpriteFolder, folderToDelete)
                ?? throw new AGSEditorException("The folder could not be found.");
            var numbers = sprites.Select(s => s.Number).ToHashSet();
            parent.SubFolders.Remove(folderToDelete);
            if (numbers.Count > 0)
                SpriteImporter.RewriteSpriteFile(projectDirectory, 0, (slot, existing) => numbers.Contains(slot) ? null : existing);
        }

        public static SpriteFolder FindParent(SpriteFolder root, SpriteFolder child)
        {
            foreach (SpriteFolder folder in root.SubFolders)
            {
                if (folder == child) return root;
                SpriteFolder found = FindParent(folder, child);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// "Crop sprite edges (symmetric)" (SpriteSelector.CropSelectedSprites, agsnative crop_sprite_edges,
        /// NativeMethods.CropSpriteEdges): sprites of the same size are cut down to the area that is not transparent in
        /// any of them; the cut is remembered as their tile in the source image. Returns null when cropped, otherwise
        /// upstream's message. Transparent = the mask colour (index 0, magenta), or alpha 0 in 32-bit sprites.
        /// </summary>
        public static string CropSpriteEdges(string projectDirectory, IReadOnlyList<Sprite> sprites, bool symmetric)
        {
            if (sprites.Count == 0) return "No sprites are selected.";
            int width = sprites[0].Width, height = sprites[0].Height;
            if (sprites.Any(s => s.Width != width || s.Height != height))
                return "All sprites to be cropped must have the same dimensions. If you want to crop unrelated sprites, do them separately.";

            var images = new Dictionary<int, Imaging.PixelImage>();
            try
            {
                using (AGS.Native.SpriteFile file = AGS.Native.SpriteFile.OpenInProject(projectDirectory))
                    foreach (Sprite sprite in sprites)
                        images[sprite.Number] = file.LoadSprite(sprite.Number) ?? throw new AGSEditorException($"Sprite {sprite.Number} could not be read.");
                width = images[sprites[0].Number].Width;
                height = images[sprites[0].Number].Height;

                int left = width, right = 0, top = height, bottom = 0;
                foreach (Imaging.PixelImage image in images.Values)
                {
                    for (int y = 0; y < image.Height; y++)
                        for (int x = 0; x < image.Width; x++)
                        {
                            if (IsMaskPixel(image, x, y)) continue;
                            if (x < left) left = x;
                            if (x > right) right = x;
                            if (y < top) top = y;
                            if (y > bottom) bottom = y;
                        }
                }
                if (symmetric)
                {
                    // symmetric -- make sure that the left and right edge chopping are equal
                    int rightDist = (width - right) - 1;
                    if (rightDist < left)
                        left = rightDist;
                    if (left < rightDist)
                        right = (width - left) - 1;
                }
                int newWidth = (right - left) + 1;
                int newHeight = (bottom - top) + 1;
                if ((newWidth == width && newHeight == height) || newWidth < 1 || newHeight < 1)
                    return "The selected sprites could not be cropped any further.";

                var cropped = images.ToDictionary(p => p.Key, p => Crop(p.Value, left, top, newWidth, newHeight));
                try
                {
                    SpriteImporter.RewriteSpriteFile(projectDirectory, 0, (slot, existing) => (existing, slot),
                        slot => cropped.TryGetValue(slot, out Imaging.PixelImage img) ? img : null);
                }
                finally
                {
                    foreach (Imaging.PixelImage img in cropped.Values) img.Dispose();
                }
                foreach (Sprite sprite in sprites)
                {
                    sprite.ImportAsTile = true;
                    // Remember that this sprite may have been a tile already
                    sprite.OffsetX += left;
                    sprite.OffsetY += top;
                    sprite.ImportWidth = newWidth;
                    sprite.ImportHeight = newHeight;
                    sprite.Width = newWidth;
                    sprite.Height = newHeight;
                }
                return null;
            }
            finally
            {
                foreach (Imaging.PixelImage image in images.Values) image.Dispose();
            }
        }

        private static bool IsMaskPixel(Imaging.PixelImage image, int x, int y)
        {
            ReadOnlySpan<byte> row = image.GetRow(y);
            switch (image.PixelFormat)
            {
                case Imaging.ImagePixelFormat.Indexed8: return row[x] == 0;
                case Imaging.ImagePixelFormat.Rgb565: return BitConverter.ToUInt16(row.Slice(x * 2, 2)) == 0xF81F;
                case Imaging.ImagePixelFormat.Rgb555: return BitConverter.ToUInt16(row.Slice(x * 2, 2)) == 0x7C1F;
                case Imaging.ImagePixelFormat.Bgra32:
                    ReadOnlySpan<byte> px = row.Slice(x * 4, 4);
                    return px[3] == 0 || (px[0] == 0xFF && px[1] == 0 && px[2] == 0xFF);
                default: return false;
            }
        }

        private static Imaging.PixelImage Crop(Imaging.PixelImage image, int left, int top, int width, int height)
        {
            var result = new Imaging.PixelImage(width, height, image.PixelFormat, ReadOnlySpan<byte>.Empty,
                image.PixelFormat == Imaging.ImagePixelFormat.Indexed8 ? image.Palette : null);
            int bpp = Imaging.PixelImage.GetBytesPerPixel(image.PixelFormat);
            for (int y = 0; y < height; y++)
                image.GetRow(top + y).Slice(left * bpp, width * bpp).CopyTo(result.GetRow(y));
            return result;
        }

        /// <summary>
        /// "Replace source paths for sprites..." (SpriteSelector.ReplaceSourceFolderForSprites, Utilities.ReplacePathBase*):
        /// every sprite source under <paramref name="oldBase"/> is moved under <paramref name="newBase"/>. Compared as
        /// text (either slash, case-insensitive), so Windows paths like "C:\Art" stored in a project can be replaced on
        /// macOS too; relative results are stored with '\' as upstream. Returns the number of sprites changed.
        /// </summary>
        public static int ReplaceSourcePaths(Game game, string oldBase, string newBase)
        {
            static string Norm(string p) => (p ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            string from = Norm(oldBase), to = Norm(newBase);
            if (from.Length == 0 || string.Equals(from, to, StringComparison.Ordinal)) return 0;
            int count = 0;
            foreach (Sprite sprite in game.RootSpriteFolder.GetAllSpritesFromAllSubFolders())
            {
                if (string.IsNullOrEmpty(sprite.SourceFile)) continue;
                string path = Norm(sprite.SourceFile);
                if (!path.StartsWith(from + "/", StringComparison.OrdinalIgnoreCase)) continue;
                string result = (to.Length == 0 ? string.Empty : to + "/") + path.Substring(from.Length + 1);
                bool absolute = result.StartsWith("/") || (result.Length > 1 && result[1] == ':');
                sprite.SourceFile = absolute && !(result.Length > 1 && result[1] == ':') ? result : result.Replace('/', '\\');
                count++;
            }
            return count;
        }

        /// <summary>Drag &amp; drop of sprites onto another folder (SpriteSelector.MoveSpritesIntoNewFolder).</summary>
        public static void MoveSprites(Game game, IEnumerable<Sprite> sprites, SpriteFolder target)
        {
            foreach (Sprite sprite in sprites.ToList())
            {
                SpriteFolder from = game.RootSpriteFolder.FindFolderThatContainsSprite(sprite.Number);
                if (from == target) continue;
                from?.Sprites.Remove(sprite);
                target.Sprites.Add(sprite);
            }
        }

        public static string GetSpriteUsageReport(int spriteNumber, Game game)
        {
            StringBuilder usageReport = new StringBuilder(5000);
            FindSpriteUsageInViews(spriteNumber, usageReport, game.RootViewFolder);

            if (spriteNumber == 0)
            {
                usageReport.AppendLine("Sprite 0 is the default sprite and can never be deleted");
            }

            foreach (MouseCursor cursor in game.Cursors)
            {
                if (cursor.Image == spriteNumber)
                {
                    usageReport.AppendLine("Mouse cursor " + cursor.ID + " (" + cursor.Name + ")");
                }
            }

            foreach (InventoryItem item in game.RootInventoryItemFolder.AllItemsFlat)
            {
                if (item.Image == spriteNumber)
                {
                    usageReport.AppendLine("Inventory item " + item.ID + " (" + item.Name + ")");
                }
            }

            if (game.Settings.DialogOptionsBullet == spriteNumber)
            {
                usageReport.AppendLine("Dialog bullet point image");
            }

            if ((game.Settings.InventoryHotspotMarker.Style == InventoryHotspotMarkerStyle.Sprite) &&
                (game.Settings.InventoryHotspotMarker.Image == spriteNumber))
            {
                usageReport.AppendLine("Inventory hotspot dot");
            }

            foreach (GUI gui in game.RootGUIFolder.AllItemsFlat)
            {
                if (gui.BackgroundImage == spriteNumber)
                {
                    usageReport.AppendLine("GUI " + gui.Name + " background image");
                }

                foreach (GUIControl control in gui.Controls)
                {
                    GUIButton button = control as GUIButton;
                    if (button != null)
                    {                        
                        if ((button.Image == spriteNumber) ||
                            (button.MouseoverImage == spriteNumber) ||
                            (button.PushedImage == spriteNumber))
                        {
                            usageReport.AppendLine("GUI button " + control.Name + " on GUI " + gui.Name);
                        }
                    }
                    GUISlider slider = control as GUISlider;
                    if (slider != null)
                    {
                        if ((slider.HandleImage == spriteNumber) ||
                            (slider.BackgroundImage == spriteNumber))
                        {
                            usageReport.AppendLine("GUI slider " + control.Name + " on GUI " + gui.Name);
                        }
                    }
                    GUITextWindowEdge edge = control as GUITextWindowEdge;
                    if ((edge != null) && (edge.Image == spriteNumber))
                    {
                        usageReport.AppendLine("Text window edge " + control.Name + " on GUI " + gui.Name);
                    }
                }
            }

            if (usageReport.Length > 0)
            {
                string resultText = "Sprite " + spriteNumber + " is used in the following places. It may also be used in text script commands and in rooms (for example, room object graphics); we cannot detect those uses automatically.";
                resultText += Environment.NewLine + Environment.NewLine + usageReport.ToString();
                return resultText;
            }
            return null;
        }

        private static void FindSpriteUsageInViews(int spriteNumber, StringBuilder report, ViewFolder folder)
        {
            foreach (AGS.Types.View view in folder.Views)
            {
                foreach (ViewLoop loop in view.Loops)
                {
                    foreach (ViewFrame frame in loop.Frames)
                    {
                        if (frame.Image == spriteNumber)
                        {
                            report.AppendLine(string.Format("View {0}, loop {1}, frame {2}", view.ID, loop.ID, frame.ID));
                        }
                    }
                }
            }

            foreach (ViewFolder subFolder in folder.SubFolders)
            {
                FindSpriteUsageInViews(spriteNumber, report, subFolder);
            }
        }
    }
}
