// ags4mac: forked from ags/Editor/AGS.Editor/Components/TranslationsComponent.cs (WinForms component).
// UpdateSingleTranslationProcess is copied unchanged (made public); UpdateTranslationsProcess and
// GetFreeNameForTranslation are ported without the busy dialog / project tree; the room part of
// ProcessAllGameTexts (RoomsComponent.AGSEditor_ProcessAllGameTexts, read-only processors only) loads
// each room through libagsnative.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AGS.Native;
using AGS.Types;

namespace AGS.Editor
{
    public static class TranslationUpdater
    {
        /// <summary>TranslationsComponent.GetFreeNameForTranslation.</summary>
        public static string GetFreeNameForTranslation(Game game)
        {
            int attempt = 0;
            string newFileName;
            do
            {
                attempt++;
                newFileName = "New Translation" + ((attempt == 1) ? "" : attempt.ToString());
            }
            while (game.Translations.FirstOrDefault(t => t.Name == newFileName) != null);
            return newFileName;
        }

        /// <summary>COMMAND_NEW_ITEM: an empty .trs file and a new entry in the game.</summary>
        public static Translation CreateTranslation(GameProject project)
        {
            Game game = project.Game;
            Translation newItem = new Translation(GetFreeNameForTranslation(game));
            game.Translations.Add(newItem);
            File.WriteAllText(Path.Combine(project.Directory, newItem.FileName), string.Empty); // dummy placeholder file
            newItem.TryLoadData();
            game.FilesAddedOrRemoved = true;
            TypeConverterLists.Refresh(game);
            return newItem;
        }

        /// <summary>Tree label edit + _timer_Tick: renames the translation and its .trs file.</summary>
        public static void RenameTranslation(GameProject project, Translation translation, string newName)
        {
            string oldName = translation.Name;
            if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;
            translation.Name = newName;
            if (File.Exists(Path.Combine(project.Directory, translation.FileName)))
            {
                translation.Name = oldName;
                throw new AGSEditorException("A translation with the name " + newName + Translation.TRANSLATION_SOURCE_FILE_EXTENSION + " already exists.");
            }
            string sourcePath = Path.Combine(project.Directory, oldName + Translation.TRANSLATION_SOURCE_FILE_EXTENSION);
            if (File.Exists(sourcePath))
                File.Move(sourcePath, Path.Combine(project.Directory, translation.FileName));
            project.Game.FilesAddedOrRemoved = true;
            TypeConverterLists.Refresh(project.Game);
        }

        /// <summary>COMMAND_DELETE_ITEM: removes the translation and deletes its .trs file.</summary>
        public static void DeleteTranslation(GameProject project, Translation translation)
        {
            project.Game.Translations.Remove(translation);
            project.Game.FilesAddedOrRemoved = true;
            string path = Path.Combine(project.Directory, translation.FileName);
            if (File.Exists(path))
                File.Delete(path);
            TypeConverterLists.Refresh(project.Game);
        }

        /// <summary>
        /// UpdateTranslations/DoTranslationUpdate/UpdateTranslationsProcess: collects every translatable game text
        /// (TranslationGenerator, including all rooms) and merges it into the given translations, saving those that
        /// changed. The game must be saved beforehand, as upstream does. Returns the messages.
        /// </summary>
        public static CompileMessages UpdateTranslations(GameProject project, IList<Translation> translations)
        {
            Game game = project.Game;
            var messages = new CompileMessages();
            var toUpdate = new List<Translation>();
            foreach (Translation translation in translations)
            {
                translation.GameID = game.Settings.UniqueID;
                translation.GameName = game.Settings.GameName;
                CompileMessages errors = null;
                if (File.Exists(Path.Combine(project.Directory, translation.FileName)))
                {
                    errors = translation.TryLoadData(); // in case the trs files were modified externally
                    messages.AddRange(errors);
                }
                if (errors == null || !errors.HasErrors)
                    toUpdate.Add(translation);
            }
            if (toUpdate.Count == 0)
                return messages;

            var generator = new TranslationGenerator();
            CompileMessages generated;
            using (TextProcessingHooks.Install(project))
                generated = generator.CreateTranslationList(game);
            messages.AddRange(generated);
            if (generated.HasErrors)
                return messages; // abort for avoiding corrupting translations

            foreach (Translation translation in toUpdate)
                UpdateSingleTranslationProcess(translation, generator.LinesForTranslation, messages);
            foreach (Translation translation in toUpdate)
            {
                if (translation.Modified)
                    translation.SaveData();
            }
            return messages;
        }

        /// <summary>
        /// COMMAND_MAKE_DEFAULT (ReplaceGameTextWithTranslation + ReplaceGameTextWithTranslationProcess): replaces all
        /// game text (including rooms) with the translation and re-keys every translation on the new text. The game
        /// must be saved and no room open in an editor; the caller saves the game afterwards, as upstream does.
        /// </summary>
        public static CompileMessages ReplaceGameTextWithTranslation(GameProject project, Translation translation)
        {
            CompileMessages errors = translation.TryLoadData();
            if (errors.HasErrors)
                return errors;
            using (TextProcessingHooks.Install(project))
                errors.AddRange(TextImporter.ReplaceAllGameText(project.Game, translation));
            // Make a copy of the dictionary, otherwise it can get overwritten
            // while updating its translation
            Dictionary<string, string> textChanges = new Dictionary<string, string>();
            foreach (string key in translation.TranslatedLines.Keys)
            {
                textChanges.Add(key, translation.TranslatedLines[key]);
            }

            UpdateAllTranslationsWithNewDefaultText(project.Game, textChanges, errors);
            return errors;
        }

        // copied from TranslationsComponent (game passed in)
        private static void UpdateAllTranslationsWithNewDefaultText(Game game, Dictionary<string, string> textChanges, CompileMessages errors)
        {
            foreach (Translation otherTranslation in game.Translations)
            {
                var load_errors = otherTranslation.TryLoadData();
                errors.AddRange(load_errors);
                if (load_errors.HasErrors)
                    continue; // failure

                Dictionary<string, string> newTranslation = new Dictionary<string, string>();

                foreach (string sourceLine in otherTranslation.TranslatedLines.Keys)
                {
                    string otherTranslationOfThisLine = otherTranslation.TranslatedLines[sourceLine];
                    string newKeyName = null;
                    if ((textChanges.ContainsKey(sourceLine)) && (textChanges[sourceLine].Length > 0))
                    {
                        newKeyName = textChanges[sourceLine];

                        if (newTranslation.ContainsKey(newKeyName))
                        {
                            if (!string.IsNullOrEmpty(otherTranslationOfThisLine))
                            {
                                errors.Add(new CompileWarning("Text '" + newKeyName + "' already has a translation; '" + sourceLine + "' translation will be lost"));
                            }
                            newKeyName = null;
                        }
                    }
                    else if (!newTranslation.ContainsKey(sourceLine))
                    {
                        newKeyName = sourceLine;
                    }

                    if (newKeyName != null)
                    {
                        if (newKeyName == otherTranslationOfThisLine)
                        {
                            newTranslation.Add(newKeyName, string.Empty);
                        }
                        else
                        {
                            newTranslation.Add(newKeyName, otherTranslationOfThisLine);
                        }
                    }
                }

                otherTranslation.TranslatedLines = newTranslation;
                otherTranslation.Modified = true;
                otherTranslation.SaveData();
            }
        }

        public static void UpdateSingleTranslationProcess(Translation translation, ICollection<GameTextLine> gameTexts, CompileMessages messages)
        {
            // We take the existing translation source content as a base and merge the new changes in.
            // To be fair, this still does not guarantee that old content is kept in the same order,
            // because Translation class stores the lines in a hash dictionary. If we wanted to be
            // strict about this, we'd need to record them in a separate indexed list (or similar).

            bool translationModified = false;
            var newTextKeys = new HashSet<string>();
            var sectionByKey = new Dictionary<string, TranslationSection>();
            TranslationSection currentSection = null;

            // Process new texts, merging them with the existing translation
            foreach (var line in gameTexts)
            {
                // Save the new entry key, used to find obsolete entries afterwards
                bool modified = false;
                newTextKeys.Add(line.Text);

                TranslationEntryOptions entryOptions = null;
                TranslationSection oldSection = null;
                if (translation.TranslatedLines.ContainsKey(line.Text))
                {
                    // An entry already exists
                    translation.TranslationSections.TryGetValue(line.Text, out oldSection);
                    translation.TranslatedEntryOptions.TryGetValue(line.Text, out entryOptions);
                    // If entry options exist, remove all standard options from metadata list;
                    // these options must be set by merging new game line, below
                    if (entryOptions != null)
                    {
                        modified |= entryOptions.ParserWordID != line.ParserWordID
                            || entryOptions.IsObsolete;

                        entryOptions.ParserWordID = -1;
                        entryOptions.IsObsolete = false;
                        entryOptions.Metadata.RemoveAll((annotation) =>
                        {
                            var keyValue = AGS.Types.Utilities.ParseKeyValue(annotation, Translation.ANNOTATE_SEPARATOR);
                            return keyValue.Key == "OBSOLETE" || keyValue.Key == "PARSERWORD";
                        });
                    }
                }
                else
                {
                    // Add new entry
                    translation.TranslatedLines[line.Text] = string.Empty;
                    modified = true;
                }

                // Get or create a section for this entry, always use a section from the new game line
                modified |= (oldSection == null) || (oldSection.Name != line.Context);
                if (currentSection == null || currentSection.Name != line.Context)
                {
                    string context = line.Context ?? string.Empty;
                    if (!sectionByKey.TryGetValue(context, out currentSection))
                    {
                        currentSection = new TranslationSection(context, line.ContextComment);
                        sectionByKey.Add(context, currentSection);
                    }
                }
                translation.TranslationSections[line.Text] = currentSection;

                // Apply entry options from the new game line
                if (line.ParserWordID >= 0)
                {
                    if (entryOptions == null)
                    {
                        entryOptions = new TranslationEntryOptions();
                        translation.TranslatedEntryOptions[line.Text] = entryOptions;
                    }

                    entryOptions.Metadata.Add($"PARSERWORD:{line.ParserWordID}");
                    entryOptions.ParserWordID = line.ParserWordID;
                }

                translationModified |= modified;
            }

            // Handle obsolete translation entries
            var removeEntries = new List<string>();
            foreach (var entry in translation.TranslatedLines)
            {
                if (!newTextKeys.Contains(entry.Key))
                {
                    translationModified = true;
                    // If the entry was never translated, then simply remove one.
                    // If there's already a translated line, then keep the entry, but mark as OBSOLETE
                    if (string.IsNullOrEmpty(entry.Value))
                    {
                        removeEntries.Add(entry.Key);
                    }
                    else
                    {
                        TranslationEntryOptions entryOptions = null;
                        if (!translation.TranslatedEntryOptions.TryGetValue(entry.Key, out entryOptions))
                        {
                            entryOptions = new TranslationEntryOptions();
                            translation.TranslatedEntryOptions[entry.Key] = entryOptions;
                        }
                        if (!entryOptions.IsObsolete)
                        {
                            entryOptions.IsObsolete = true;
                            entryOptions.Metadata.Add("OBSOLETE");
                        }
                    }
                }
            }
            foreach (var removeEntry in removeEntries)
                translation.TranslatedLines.Remove(removeEntry);

            translation.Modified = translationModified;
        }
    }

    /// <summary>
    /// Replaces upstream's AGSEditor.ProcessAllGameTexts event (TextProcessingHelper calls it through a source
    /// rewrite). The only upstream subscriber is RoomsComponent, ported in <see cref="ProcessRoomTexts"/>.
    /// </summary>
    public static class TextProcessingHooks
    {
        [ThreadStatic] private static GameProject _project;

        internal static IDisposable Install(GameProject project)
        {
            _project = project;
            return new Reset();
        }

        private sealed class Reset : IDisposable
        {
            public void Dispose() => _project = null;
        }

        public static void RunProcessAllGameTextsEvent(IGameTextProcessor processor, CompileMessages errors)
        {
            if (_project != null)
                ProcessRoomTexts(_project, processor, errors);
        }

        /// <summary>
        /// RoomsComponent.AGSEditor_ProcessAllGameTexts: every room is loaded through libagsnative; processors that
        /// make changes (text import, speech numbering) get their results written back to the room file and the
        /// room script, as upstream's SaveRoomButDoNotShowAnyErrors does. Rooms must not be open in an editor.
        /// A room script already loaded in the project (e.g. by a search) is the one processed and saved.
        /// </summary>
        public static void ProcessRoomTexts(GameProject project, IGameTextProcessor processor, CompileMessages errors)
        {
            Game game = project.Game;
            foreach (UnloadedRoom unloadedRoom in game.RootRoomFolder.AllItemsFlat)
            {
                string roomFile = Path.Combine(project.Directory, ProjectPaths.ToLocal(unloadedRoom.FileName));
                NativeRoom native;
                Room room;
                try
                {
                    native = NativeRoom.Load(roomFile);
                    room = native.ToRoom(unloadedRoom, game.TextEncoding);
                }
                catch (Exception ex)
                {
                    errors.Add(new CompileError($"Unable to load room {unloadedRoom.Number}: {ex.Message}"));
                    continue;
                }
                using (native)
                {
                    Script script = unloadedRoom.Script;
                    if (script == null)
                    {
                        try
                        {
                            room.LoadScript();
                            script = room.Script;
                        }
                        catch (FileNotFoundException)
                        {
                            errors.Add(new CompileWarning($"The script file '{room.ScriptFileName}' is missing. An empty script will be created instead."));
                        }
                    }

                    string roomContext = $"Room{room.Number}";
                    string roomContextComment = room.Description;

                    if (script != null)
                    {
                        string text = processor.ProcessText(GameTextLine.MakeScript(script.Text, room.ScriptFileName, roomContext, roomContextComment), GameTextType.Script);
                        if (processor.MakesChanges && text != script.Text)
                        {
                            script.Text = text;
                            script.SaveToDisk();
                        }
                    }

                    foreach (RoomMessage message in room.Messages)
                    {
                        int charId = message.CharacterID;
                        if (!message.ShowAsSpeech)
                            charId = Character.NARRATOR_CHARACTER_ID;
                        string text = processor.ProcessText(GameTextLine.MakeSpeechLine(charId, message.Text, roomContext, roomContextComment), GameTextType.Message);
                        if (processor.MakesChanges) message.Text = text;
                    }

                    TextProcessingHelper.ProcessProperties(processor, game.PropertySchema, room.Properties, roomContext, roomContextComment, errors);

                    foreach (RoomHotspot hotspot in room.Hotspots)
                    {
                        string text = processor.ProcessText(new GameTextLine(hotspot.Description, roomContext, roomContextComment), GameTextType.ItemDescription);
                        if (processor.MakesChanges) hotspot.Description = text;
                        TextProcessingHelper.ProcessProperties(processor, game.PropertySchema, hotspot.Properties, roomContext, roomContextComment, errors);
                    }

                    foreach (RoomObject obj in room.Objects)
                    {
                        string text = processor.ProcessText(new GameTextLine(obj.Description, roomContext, roomContextComment), GameTextType.ItemDescription);
                        if (processor.MakesChanges) obj.Description = text;
                        TextProcessingHelper.ProcessProperties(processor, game.PropertySchema, obj.Properties, roomContext, roomContextComment, errors);
                    }

                    if (processor.MakesChanges)
                    {
                        try
                        {
                            native.ApplyRoom(room, game.TextEncoding);
                            native.Save(roomFile);
                        }
                        catch (Exception ex)
                        {
                            errors.Add(new CompileError("Failed to save room " + room.FileName + "; details below"));
                            errors.Add(new CompileError(ex.Message));
                        }
                    }
                }
            }
        }
    }
}
