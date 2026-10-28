// ags4mac: forked from ags/Editor/AGS.Editor/Tasks.cs (lines 302-942): the game-load fixups and
// project upgrades (HandleObjectIDErrors, SetDefaultGameContentIfMissing, SetDefaultValuesForNewFeatures)
// that run right after a game is loaded. Changes, all marked "ags4mac:":
//  - instance-free: takes the game (and its directory) explicitly instead of using Factory/AGSEditor
//    and the process's current directory;
//  - Factory.GUIController.ShowMessage -> IEditorHost.ShowMessage;
//  - default fonts come from this assembly's resources.
using AGS.Editor.Hosting;
using AGS.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AGS.Editor
{
    public class GameUpgrader
    {
        private readonly IEditorHost _host;

        public GameUpgrader(IEditorHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary>
        /// Runs the fixups of upstream Tasks.LoadGameFromDisk after Game.FromXml, in the same order:
        /// default content, upgrades for new features, then object ID repairs.
        /// </summary>
        public void ApplyAfterLoad(Game game)
        {
            SetDefaultGameContentIfMissing(game);
            SetDefaultValuesForNewFeatures(game);
            // Object IDs must be tested after updating the project (SetDefaultValuesForNewFeatures),
            // because when filler objects are created, they are already complying to the current data version.
            HandleObjectIDErrors(game);
        }

        private delegate TItem CreateFillerItem<TItem>(int id, List<int> skipIDs);

        private class ProcIDErrorsState
        {
            // SkipFillerIndex saves filler indexes used during current processing.
            // This is necessary when generating script name, because Game is not updated
            // while we are creating fillers, and we need to know which script names we've already
            // taken during generation.
            public List<int> SkipFillerIndex = new List<int>();
            // FillerRecord list gathers counts of object fillers created, per type.
            public SortedList<string, int> FillerRecord = new SortedList<string, int>();
            // DuplicatesRecord list gathers counts of object with duplicate IDs resolved, per type.
            public SortedList<string, int> DuplicatesRecord = new SortedList<string, int>();

            // Contains temporary references to fillers created during *the last* object type processing.
            public List<IHasID> Fillers = new List<IHasID>();
            // Contains temporary references to duplicates found during *the last* object type processing.
            public List<IHasID> Duplicates = new List<IHasID>();
        }

        private void HandleObjectIDErrorsImpl<TItem>(IEnumerable<TItem> items,
            string friendlyTypeName, int startID, ProcIDErrorsState state, CreateFillerItem<TItem> createFiller)
            where TItem : IHasID
        {
            // We don't know if the input list is sorted, so copy item refs to temp list and sort
            List<TItem> sortedList = items.ToList();
            sortedList.Sort();

            state.Fillers.Clear();
            state.Duplicates.Clear();
            int presentItems = 0; // only currently present, not counting generated fillers
            int expectID = startID;
            // Since the items are already sorted in the order of item ID,
            // then we cannot introduce duplicates by inserting fillers.
            foreach (var item in sortedList)
            {
                presentItems++;
                // Since list is sorted, the duplicates are found when the next item has
                // the previous ID.
                if (item.ID < expectID)
                {
                    state.Duplicates.Add(item);
                    continue;
                }

                for (; expectID < item.ID; ++expectID)
                {
                    state.Fillers.Add(createFiller(expectID, state.SkipFillerIndex));
                }
                expectID++;
            }

            // Keep records of created fillers and found duplicates
            if (state.Fillers.Count > 0)
            {
                state.FillerRecord[friendlyTypeName] = state.FillerRecord.GetOrDefault(friendlyTypeName, 0) + state.Fillers.Count;
            }
            // Resolve ID duplicates by moving them to the back of the list;
            // we do this simply be assigning new IDs, their positions will be fixed when the list is sorted next time.
            if (state.Duplicates.Count > 0)
            {
                int newID = presentItems - state.Duplicates.Count + state.Fillers.Count;
                foreach (var dup in state.Duplicates)
                    dup.ID = newID++;
                state.DuplicatesRecord[friendlyTypeName] = state.DuplicatesRecord.GetOrDefault(friendlyTypeName, 0) + state.Duplicates.Count;
            }
        }

        /// <summary>
        /// Fills ID gaps in a BaseFolderCollection (any item list which is represented by folder tree).
        /// </summary>
        private void HandleObjectIDErrors<TItem, TFolder>(BaseFolderCollection<TItem, TFolder> folder,
            string friendlyTypeName, int startID, ProcIDErrorsState state, CreateFillerItem<TItem> createFiller)
            where TItem : class, IHasID, IToXml
            where TFolder : BaseFolderCollection<TItem, TFolder>
        {
            HandleObjectIDErrorsImpl(folder.AllItemsFlat, friendlyTypeName, startID, state, createFiller);
            // Add fillers to the root folder, and request a full resort
            if (state.Fillers.Count > 0)
            {
                foreach (var filler in state.Fillers)
                    folder.Items.Add(filler as TItem);
            }
            // Resolved duplicates (which already have new IDs) will be moved to their places during Sort().
            if (state.Fillers.Count > 0 || state.Duplicates.Count > 0)
                folder.Sort(true);
        }

        /// <summary>
        /// Fills ID gaps in a plain List (any item list which does not have folders).
        /// </summary>
        private void HandleObjectIDErrors<TItem>(IList<TItem> list, string friendlyTypeName,
            int startID, ProcIDErrorsState state, CreateFillerItem<TItem> createFiller)
            where TItem : class, IHasID
        {
            HandleObjectIDErrorsImpl(list, friendlyTypeName, startID, state, createFiller);
            // Insert fillers into their respective index slots in the list.
            // This should work, assuming the fillers are arranged in the order of IDs,
            // and the list will expand after each insertion, providing a place for the next one.
            if (state.Fillers.Count > 0)
            {
                foreach (var filler in state.Fillers)
                    list.Insert(filler.ID, filler as TItem);
            }
            // Resolve ID duplicates (which already have new IDs) by moving them to the back of the list;
            // this is done after already (possibly) adding gap fillers.
            if (state.Duplicates.Count > 0)
            {
                foreach (var dup in state.Duplicates)
                {
                    list.Remove(dup as TItem);
                    list.Add(dup as TItem);
                }
            }
        }

        /// <summary>
        /// Most of the object lists in AGS game are required to have no gaps in IDs,
        /// with small number of exceptions (Views and Sprites). If we've just loaded
        /// a Game, we should have the object lists sorted by ID. See if any IDs are
        /// missing, and fill these gaps with dummy filler objects, and report to user.
        /// </summary>
        public void HandleObjectIDErrors(Game game)
        {
            ProcIDErrorsState state = new ProcIDErrorsState();
            HandleObjectIDErrors(game.RootAudioClipFolder, "Audio Clip", 0, state,
                (int id, List<int> skipIDs) => {
                    return new AudioClip(id, ScriptNames.GetFirstAvailableScriptName(game, "filler", 1, null, skipIDs) /* ags4mac */, game.GetNextAudioIndex()); });
            HandleObjectIDErrors(game.AudioClipTypes, "Audio Clip Type", AudioClipType.FIRST_VALID_ID, state,
                (int id, List<int> skipIDs) => {
                    return new AudioClipType(id, ScriptNames.GetFirstAvailableScriptName(game, "filler", 1, null, skipIDs) /* ags4mac */); });
            HandleObjectIDErrors(game.RootCharacterFolder, "Character", 0, state,
                (int id, List<int> skipIDs) => {
                    return new Character(id, ScriptNames.GetFirstAvailableScriptName(game, "filler", 1, null, skipIDs) /* ags4mac */); });
            HandleObjectIDErrors(game.Cursors, "Cursor", 0, state,
                (int id, List<int> skipIDs) => {
                    return new MouseCursor(id, ScriptNames.GetFirstAvailableScriptName(game, "filler", 1, null, skipIDs) /* ags4mac */); });
            HandleObjectIDErrors(game.RootDialogFolder, "Dialog", 0, state,
                (int id, List<int> skipIDs) => {
                    return new Dialog(id, ScriptNames.GetFirstAvailableScriptName(game, "filler", 1, null, skipIDs) /* ags4mac */); });
            HandleObjectIDErrors(game.Fonts, "Font", 0, state,
                (int id, List<int> skipIDs) => {
                    return new Font(id, ScriptNames.GetFirstAvailableScriptName(game, "filler", 1, null, skipIDs) /* ags4mac */); });
            HandleObjectIDErrors(game.RootGUIFolder, "GUI", 0, state,
                (int id, List<int> skipIDs) => {
                    var gui = new NormalGUI(id, ScriptNames.GetFirstAvailableScriptName(game, "filler", 1, null, skipIDs) /* ags4mac */);
                    gui.Visible = false; // filler guis must be hidden by default
                    return gui; });
            foreach (GUI gui in game.GUIs)
            {
                HandleObjectIDErrors(gui.Controls, "GUI Controls", 0, state,
                (int id, List<int> skipIDs) => {
                    var label = new GUILabel(id, ScriptNames.GetFirstAvailableScriptName(game, "filler", 1, null, skipIDs) /* ags4mac */);
                    label.Visible = false; // filler guis must be hidden by default
                    return label;
                });
            }
            HandleObjectIDErrors(game.RootInventoryItemFolder, "Inventory Item", InventoryItem.FIRST_VALID_ID, state,
                (int id, List<int> skipIDs) => {
                    return new InventoryItem(id, ScriptNames.GetFirstAvailableScriptName(game, "filler", 1, null, skipIDs) /* ags4mac */); });

            // NOTE: Views are allowed to have gaps in IDs, so dont process them here

            if (state.FillerRecord.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("There are gaps found in some of the game object lists: their numeric IDs are not sequential. This could have happened in case of an earlier program error, or if the game files have been edited by hand.");
                sb.AppendLine();
                sb.AppendLine("The editor will fill these gaps with the dummy \"filler\" objects now. This is required in order to keep the object lists sequential, and existing object IDs unchanged.");
                sb.AppendLine("You may decide what to do with these filler objects on your own afterwards.");
                sb.AppendLine();
                sb.AppendLine("Following number of fillers have been added:");
                foreach (var record in state.FillerRecord)
                {
                    sb.AppendLine($"- {record.Key}(s): {record.Value}");
                }
                _host.ShowMessage(sb.ToString(), MessageSeverity.Warning); // ags4mac
            }
        }

        /// <summary>
        /// Ensures that any obligatory content is created,
        /// if it's missing after the game was loaded from project file.
        /// </summary>
        private void SetDefaultGameContentIfMissing(Game game)
        {
            // Current version of the Editor requires at least 3 fonts present,
            // copy them from the resources if these are not present in game data
            AddFontIfNotAlreadyThere(game, 0); // ags4mac: explicit game directory
            AddFontIfNotAlreadyThere(game, 1);
            AddFontIfNotAlreadyThere(game, 2);
            while (game.Fonts.Count < 3)
            {
                Font font = new Font();
                font.ID = game.Fonts.Count;
                font.Name = string.Format($"Font{game.Fonts.Count}");
                game.Fonts.Add(font);
            }
        }

        private void SetDefaultValuesForNewFeatures(Game game)
        {
#pragma warning disable 0618
            // TODO: this may be noticably if upgrading lots of items. Display some kind of
            // progress window to notify user.

            int xmlVersionIndex = 0;
            if (game.SavedXmlVersionIndex.HasValue)
            {
                xmlVersionIndex = game.SavedXmlVersionIndex.Value;
            }

            if (xmlVersionIndex < 2)
            {
                // Upgrade old games to use the Anti-Glide Mode setting
                foreach (Character character in game.RootCharacterFolder.AllItemsFlat)
                {
                    character.MovementLinkedToAnimation = game.Settings.AntiGlideMode;
                }
            }

            if (xmlVersionIndex < 3)
            {
                // Upgrade old games to flatten the dialog scripts
                foreach (Dialog dialog in game.RootDialogFolder.AllItemsFlat)
                {
                    dialog.Script = RemoveAllLeadingSpacesFromLines(dialog.Script);
                }
            }

            if (xmlVersionIndex < 8)
            {
                // GUIListBox Translated property should be false, as they never translated in older games
                foreach (GUI gui in game.GUIs)
                {
                    foreach (GUIControl guic in gui.Controls)
                    {
                        if (guic is GUIListBox)
                            (guic as GUIListBox).Translated = false;
                    }
                }
            }

            if (xmlVersionIndex < 12)
            {
                game.Settings.UseOldCustomDialogOptionsAPI = true;
            }

            if (xmlVersionIndex < 15)
            {
                game.DefaultSetup.SetDefaults();
            }

            if (xmlVersionIndex < 18)
            {
                // Promote sprites to "real" resolution when possible (ideally almost always)
                foreach (Sprite sprite in game.RootSpriteFolder.GetAllSpritesFromAllSubFolders())
                {
                    sprite.Resolution = FixupSpriteResolution(game, sprite.Resolution); // ags4mac
                }
            }

            if (xmlVersionIndex < 18)
            {
                foreach (Font font in game.Fonts)
                    font.SizeMultiplier = 1;
                // Apply font scaling to each individual font settings.
                // Bitmap fonts save multiplier explicitly, while vector fonts have their size doubled.
                if (game.IsHighResolution && !game.Settings.FontsForHiRes)
                {
                    foreach (Font font in game.Fonts)
                    {
                        if (font.PointSize == 0)
                        {
                            font.SizeMultiplier = 2;
                        }
                        else
                        {
                            font.PointSize *= 2;
                        }
                    }
                }
            }

            if (xmlVersionIndex < 18)
            {
                game.Settings.AllowRelativeAssetResolutions = true;
                game.Settings.DefaultRoomMaskResolution = game.IsHighResolution ? 2 : 1;
            }

            if (xmlVersionIndex < 19)
            {
                // ags4mac: AGSEditor.BaseGameFileName, evaluated for the game being loaded.
                game.Settings.GameFileName = string.IsNullOrWhiteSpace(game.Settings.GameFileName) ?
                    Path.GetFileName(game.DirectoryPath) : game.Settings.GameFileName;

                var buildNames = new Dictionary<string, string>();
                foreach (IBuildTarget target in BuildTargetsInfo.GetRegisteredBuildTargets())
                {
                    buildNames[target.Name] = game.Settings.GameFileName;
                }
                game.WorkspaceState.SetLastBuildGameFiles(buildNames);
            }

            if (xmlVersionIndex < 20)
            {
                // Set the alpha channel requests for re-import based on the presence of an alpha channel
                foreach (Sprite sprite in game.RootSpriteFolder.GetAllSpritesFromAllSubFolders())
                {
                    sprite.ImportAlphaChannel = sprite.AlphaChannel;
                }
            }

            if (xmlVersionIndex < 21)
            {
                // Assign audio clip ids to match and solidify their current position in AudioClips array.
                int clipId = 0;
                foreach (AudioClip clip in game.RootAudioClipFolder.GetAllAudioClipsFromAllSubFolders())
                {
                    clip.ID = clipId++;
                }
                game.RootAudioClipFolder.Sort(true);
            }

            if (xmlVersionIndex < 22)
            {
                game.Settings.ScaleMovementSpeedWithMaskResolution = true;
            }

            if (xmlVersionIndex < 23)
            {
                // Set the import dimensions based on existing sprite dimensions
                foreach (Sprite sprite in game.RootSpriteFolder.GetAllSpritesFromAllSubFolders())
                {
                    sprite.ImportWidth = sprite.Width;
                    sprite.ImportHeight = sprite.Height;
                }
            }

            if (xmlVersionIndex < 24)
            {
                // get all known source images and their largest known size
                // (avoiding System.Drawing / GDI as a dependency to load the project)
                Dictionary<string, Tuple<int, int>> sourceMaxSize = new Dictionary<string, Tuple<int, int>>(StringComparer.OrdinalIgnoreCase);

                foreach (Sprite sprite in game.RootSpriteFolder.GetAllSpritesFromAllSubFolders())
                {
                    if (!string.IsNullOrWhiteSpace(sprite.SourceFile))
                    {
                        int currentX = sprite.OffsetX + sprite.ImportWidth;
                        int currentY = sprite.OffsetY + sprite.ImportHeight;

                        if (sourceMaxSize.ContainsKey(sprite.SourceFile))
                        {
                            int maxX = sourceMaxSize[sprite.SourceFile].Item1;
                            int maxY = sourceMaxSize[sprite.SourceFile].Item2;
                            if (maxX < currentX) maxX = currentX;
                            if (maxY < currentY) maxY = currentY;
                            sourceMaxSize[sprite.SourceFile] = Tuple.Create(maxX, maxY);
                        }
                        else
                        {
                            sourceMaxSize.Add(sprite.SourceFile, Tuple.Create(currentX, currentY));
                        }
                    }
                }

                // Set the tiled image flag for existing imports - the only misdetection would be
                // a single import from a source image that starts at 0,0, but wasn't for the
                // entire image
                foreach (Sprite sprite in game.RootSpriteFolder.GetAllSpritesFromAllSubFolders())
                {
                    if (sprite.OffsetX > 0 || sprite.OffsetY > 0)
                    {
                        sprite.ImportAsTile = true;
                    }
                    else if (sourceMaxSize.ContainsKey(sprite.SourceFile))
                    {
                        int maxX = sourceMaxSize[sprite.SourceFile].Item1;
                        int maxY = sourceMaxSize[sprite.SourceFile].Item2;
                        sprite.ImportAsTile = sprite.ImportWidth < maxX || sprite.ImportHeight < maxY;
                    }
                    else
                    {
                        sprite.ImportAsTile = false;
                    }
                }
            }

            if (xmlVersionIndex >= 21 && xmlVersionIndex <= 25)
            {
                // Remap erroneous volatile clip ID references back to Fixed Index
                Dictionary<int, int> audioIDToIndex = new Dictionary<int, int>();
                foreach (AudioClip clip in game.RootAudioClipFolder.GetAllAudioClipsFromAllSubFolders())
                {
                    audioIDToIndex.Add(clip.ID, clip.Index);
                }

                game.Settings.PlaySoundOnScore = RemapAudioClipIDToFixedIndex(game.Settings.PlaySoundOnScore, audioIDToIndex);
                foreach (Types.View view in game.RootViewFolder.AllItemsFlat)
                {
                    foreach (Types.ViewLoop loop in view.Loops)
                    {
                        foreach (Types.ViewFrame frame in loop.Frames)
                        {
                            if (frame.Sound == AudioClip.IDNoValue)
                            {
                                frame.Sound = AudioClip.FixedIndexNoValue;
                            }
                            else
                            {
                                frame.Sound = RemapAudioClipIDToFixedIndex(frame.Sound, audioIDToIndex);
                            }
                        }
                    }
                }
            }

            if (xmlVersionIndex < 3060000)
            {
                foreach (Character c in game.Characters)
                {
                    c.IdleAnimationDelay = c.AnimationDelay + 5;
                }

                game.Settings.TTFHeightDefinedBy = FontHeightDefinition.NominalHeight;
                game.Settings.TTFMetricsFixup = FontMetricsFixup.SetAscenderToHeight;
                foreach (Font font in game.Fonts)
                {
                    font.AutoOutlineStyle = FontAutoOutlineStyle.Squared;
                    // For scaled-up bitmap fonts outline is xN
                    // NOTE: unfortunately as of now there's no direct way to determine if
                    // this is a bitmap font or TTF
                    if (!File.Exists(font.TTFFileName))
                    {
                        font.AutoOutlineThickness = font.SizeMultiplier;
                    }
                    font.TTFMetricsFixup = FontMetricsFixup.SetAscenderToHeight;
                    font.HeightDefinedBy = FontHeightDefinition.NominalHeight;
                }
                game.Settings.ClipGUIControls = false;
            }

            if (xmlVersionIndex < 3060020)
            {
                if (game.SavedXmlEncodingCodePage.HasValue &&
                    game.SavedXmlEncodingCodePage.Value == 65001)
                {
                    game.Settings.GameTextEncoding = "UTF-8";
                }
                else
                { // NOTE: use Encoding.GetEncoding(game.SavedXmlEncodingCodePage) if actual codepage is needed
                    game.Settings.GameTextEncoding = "ANSI";
                }

                game.Settings.UseOldKeyboardHandling = true;
            }

            if (xmlVersionIndex < 3060109)
            {
                game.Settings.ScaleCharacterSpriteOffsets = false;
            }

            if (xmlVersionIndex < 3060200)
            {
                game.Settings.UseOldVoiceClipNaming = true;
            }

            if (xmlVersionIndex < 3060206)
            {
                game.Settings.GameFPS = 40; // 40 was historical default FPS
            }

            if (xmlVersionIndex < 3060303)
            {
                // HeightDefinedBy moved from global setting to a per-Font individual setting
                foreach (Font font in game.Fonts)
                {
                    font.HeightDefinedBy = game.Settings.TTFHeightDefinedBy;
                }
            }

            if (xmlVersionIndex < 3060304)
            {
                foreach (GUI gui in game.GUIs)
                {
                    foreach (GUIControl gc in gui.Controls)
                    {
                        SetDefaultColors(game, gc, xmlVersionIndex);

                        // Special case: buttons that had NO graphic and NO text were not drawn previously.
                        // Now they are. In order to emulate same behavior, we disable their background and border
                        if (gc is GUIButton)
                        {
                            GUIButton button = gc as GUIButton;
                            if (button.Image == 0 && string.IsNullOrEmpty(button.Text))
                            {
                                button.SolidBackground = false;
                                button.ShowBorder = false;
                            }
                        }
                    }
                }
            }

            if (xmlVersionIndex < 3060310)
            {
                game.Settings.GameTextLanguage = string.Empty;

                // Removed implicit disabling of Labels in the engine, so they should be disabled
                // in the project when importing from older versions.
                foreach (GUI gui in game.GUIs)
                {
                    foreach (GUIControl gc in gui.Controls)
                    {
                        if (gc is GUILabel)
                        {
                            gc.Clickable = false;
                        }
                    }
                }

                foreach (InventoryItem item in game.InventoryItems)
                {
                    // Assign hotspot alignment depending on the old HotspotX/Y
                    item.HotspotAlignment = (item.HotspotX == 0 && item.HotspotY == 0) ?
                        FrameAlignment.MiddleCenter : FrameAlignment.TopLeft;
                }
            }

            // Fix default border width for Labels and InventoryWindows with hidden border
            // in early 3.6.3 Beta with revamped GUI controls
            if (xmlVersionIndex >= 3060304 && xmlVersionIndex < 3060316)
            {
                foreach (GUI gui in game.GUIs)
                {
                    foreach (GUIControl gc in gui.Controls)
                    {
                        if (!gc.ShowBorder && (gc is GUILabel || gc is GUIInventory))
                        {
                            gc.BorderWidth = 0;
                        }
                    }
                }
            }

            System.Version editorVersion = new System.Version(AGS.Types.Version.AGS_EDITOR_VERSION);
            System.Version projectVersion = game.SavedXmlEditorVersion != null ? Types.Utilities.TryParseVersion(game.SavedXmlEditorVersion) : null;
            if (projectVersion == null || projectVersion < editorVersion)
                game.SetScriptAPIForOldProject();
#pragma warning restore 0618
        }

        private static int RemapAudioClipIDToFixedIndex(int id, Dictionary<int, int> audioIDToIndex)
        {
            int fixedIndex;
            if (audioIDToIndex.TryGetValue(id, out fixedIndex))
                return fixedIndex;
            else
                return AudioClip.FixedIndexNoValue;
        }

        private string RemoveAllLeadingSpacesFromLines(string script)
        {
            StringReader sr = new StringReader(script);
            StringWriter sw = new StringWriter();
            string thisLine;
            while ((thisLine = sr.ReadLine()) != null)
            {
                sw.WriteLine(thisLine.Trim());
            }
            string returnValue = sw.ToString();
            sr.Close();
            sw.Close();
            return returnValue;
        }

        // ags4mac: upstream relies on the current directory being the game directory.
        private void AddFontIfNotAlreadyThere(Game game, int fontNumber)
        {
            string dir = game.DirectoryPath ?? Directory.GetCurrentDirectory();
            if ((!File.Exists(Path.Combine(dir, "agsfnt" + fontNumber + ".wfn"))) &&
                (!File.Exists(Path.Combine(dir, "agsfnt" + fontNumber + ".ttf"))))
            {
                EditorResources.CopyToFile("AGSFNT" + fontNumber + ".WFN", Path.Combine(dir, "AGSFNT" + fontNumber + ".WFN"));
            }
        }

        // ags4mac: Utilities.FixupSpriteResolution, for an explicit game instead of the current one.
        private static SpriteImportResolution FixupSpriteResolution(Game game, SpriteImportResolution res)
        {
            if (res == SpriteImportResolution.Real ||
                game.IsHighResolution && res == SpriteImportResolution.HighRes ||
                !game.IsHighResolution && res == SpriteImportResolution.LowRes)
            {
                return SpriteImportResolution.Real;
            }
            return res;
        }

        private static void SetDefaultColors(Game game, GUIControl control, int xmlVersionIndex)
        {
            if (control is GUIButton)
            {
                GUIButton but = control as GUIButton;
                but.SolidBackground = true;
                but.ShowBorder = true;
                but.BorderWidth = 1;
                but.BackgroundColor = 7;
                but.BorderColor = 15;
                but.BorderShadeColor = 8;
                if (xmlVersionIndex < 3060202)
                {
                    but.PaddingX = 1;
                    but.PaddingY = 1;
                }
                else
                {
                    // 3.6.2 TextPadding was an offset from exterior control edge
                    but.PaddingX = Math.Max(0, but.TextPaddingHorizontal - 1);
                    but.PaddingY = Math.Max(0, but.TextPaddingVertical - 1);
                }
            }
            else if (control is GUIInventory)
            {
            }
            else if (control is GUILabel)
            {
            }
            else if (control is GUIListBox)
            {
                GUIListBox lbox = control as GUIListBox;
                lbox.BorderColor = lbox.TextColor;
                lbox.BorderWidth = game.IsHighResolution ? 2 : 1;
            }
            else if (control is GUISlider)
            {
                GUISlider slider = control as GUISlider;
                slider.ShowBorder = true;
                slider.SolidBackground = true;
                slider.BorderWidth = 1;
                slider.BackgroundColor = 16;
                slider.BorderColor = 15;
                slider.HandleColor = 7;
                slider.BorderShadeColor = 8;
            }
            else if (control is GUITextBox)
            {
                GUITextBox tbox = control as GUITextBox;
                tbox.BorderColor = tbox.TextColor;
                tbox.BorderWidth = game.IsHighResolution ? 2 : 1;
                tbox.PaddingX = 1;
                tbox.PaddingY = 1;
            }
        }

    }
}
