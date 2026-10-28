using System;
using System.Linq;
using AGS.Types;

namespace AGS.Editor
{
    /// <summary>
    /// Adding and deleting project items, ported from the upstream editor's components
    /// (CharactersComponent, InventoryComponent, BaseComponentWithFolders) without their UI.
    /// </summary>
    public static class ProjectOperations
    {
        /// <summary>Returns the folder (at any depth) that directly contains <paramref name="item"/>, or null.</summary>
        public static TFolder FindFolderContaining<TItem, TFolder>(TFolder folder, TItem item)
            where TItem : IToXml
            where TFolder : BaseFolderCollection<TItem, TFolder>
        {
            if (folder.Items.Contains(item))
                return folder;
            foreach (TFolder sub in folder.SubFolders)
            {
                TFolder found = FindFolderContaining(sub, item);
                if (found != null)
                    return found;
            }
            return null;
        }

        // CharactersComponent: COMMAND_NEW_ITEM + AddNewCharacter
        public static Character AddCharacter(Game game, CharacterFolder folder = null)
        {
            var character = new Character();
            character.RealName = "New character";
            character.StartingRoom = -1;
            character.ID = game.RootCharacterFolder.GetAllItemsCount();
            character.ScriptName = ScriptNames.GetFirstAvailableScriptName(game, "cChar");
            (folder ?? game.RootCharacterFolder).Items.Add(character);
            return character;
        }

        // BaseComponentWithFolders.DeleteSingleItem + CharactersComponent.DeleteCharacter
        public static void DeleteCharacter(Game game, Character character)
        {
            CharacterFolder parent = FindFolderContaining<Character, CharacterFolder>(game.RootCharacterFolder, character)
                ?? throw new ArgumentException("The character is not part of this game.", nameof(character));
            // Upstream pretends the character leaves its room first, so room views update.
            character.StartingRoom = -1;
            parent.Items.Remove(character);
            int removingID = character.ID;
            foreach (Character item in game.RootCharacterFolder.AllItemsFlat)
            {
                if (item.ID > removingID)
                    item.ID--;
            }
        }

        // InventoryComponent: COMMAND_NEW_ITEM + AddNewItem
        public static InventoryItem AddInventoryItem(Game game, InventoryItemFolder folder = null)
        {
            if (game.InventoryItems.Count >= Game.MAX_INV_ITEMS)
                throw new AGSEditorException("You already have the maximum number of inventory items in your game, and cannot add any more.");
            var item = new InventoryItem();
            item.ID = game.RootInventoryItemFolder.GetAllItemsCount() + 1;
            item.Name = ScriptNames.GetFirstAvailableScriptName(game, "iInvItem");
            (folder ?? game.RootInventoryItemFolder).Items.Add(item);
            item.Description = "New inventory item";
            return item;
        }

        // BaseComponentWithFolders.DeleteSingleItem + InventoryComponent.DeleteInventoryItem
        public static void DeleteInventoryItem(Game game, InventoryItem inventoryItem)
        {
            InventoryItemFolder parent = FindFolderContaining<InventoryItem, InventoryItemFolder>(game.RootInventoryItemFolder, inventoryItem)
                ?? throw new ArgumentException("The inventory item is not part of this game.", nameof(inventoryItem));
            parent.Items.Remove(inventoryItem);
            int removingID = inventoryItem.ID;
            foreach (InventoryItem item in game.RootInventoryItemFolder.AllItemsFlat)
            {
                if (item.ID > removingID)
                    item.ID--;
            }
        }

        // BaseComponentWithFolders.CreateSubFolder
        public static TFolder AddFolder<TItem, TFolder>(TFolder parent, Func<string, TFolder> create)
            where TItem : IToXml
            where TFolder : BaseFolderCollection<TItem, TFolder>
        {
            string name = "New folder";
            for (int i = 2; parent.SubFolders.Any(f => f.Name == name); i++)
                name = $"New folder {i}";
            TFolder folder = create(name);
            parent.SubFolders.Add(folder);
            return folder;
        }

        /// <summary>Deletes an empty folder (deleting folders with items is not supported yet).</summary>
        public static void DeleteEmptyFolder<TItem, TFolder>(TFolder root, TFolder folder)
            where TItem : IToXml
            where TFolder : BaseFolderCollection<TItem, TFolder>
        {
            if (folder.Items.Count > 0 || folder.SubFolders.Count > 0)
                throw new AGSEditorException("Only empty folders can be deleted for now.");
            TFolder parent = FindParentFolder<TItem, TFolder>(root, folder)
                ?? throw new ArgumentException("The folder is not part of this tree.", nameof(folder));
            parent.SubFolders.Remove(folder);
        }

        /// <summary>
        /// BaseComponentWithFolders.DeleteFolder: deletes every item of the folder and its sub-folders through the item
        /// type's own delete (<paramref name="deleteItem"/>, which renumbers IDs and removes files), then the folder.
        /// Returns false (nothing more is deleted) as soon as an item's delete is cancelled.
        /// </summary>
        public static bool DeleteFolderWithContents<TItem, TFolder>(TFolder root, TFolder folder, Func<TItem, bool> deleteItem)
            where TItem : IToXml
            where TFolder : BaseFolderCollection<TItem, TFolder>
        {
            TFolder parent = FindParentFolder<TItem, TFolder>(root, folder)
                ?? throw new ArgumentException("The folder is not part of this tree.", nameof(folder));
            foreach (TItem item in folder.AllItemsFlat.ToList())
            {
                if (!deleteItem(item))
                    return false;
            }
            parent.SubFolders.Remove(folder);
            return true;
        }

        private static TFolder FindParentFolder<TItem, TFolder>(TFolder current, TFolder target)
            where TItem : IToXml
            where TFolder : BaseFolderCollection<TItem, TFolder>
        {
            if (current.SubFolders.Contains(target))
                return current;
            foreach (TFolder sub in current.SubFolders)
            {
                TFolder found = FindParentFolder<TItem, TFolder>(sub, target);
                if (found != null)
                    return found;
            }
            return null;
        }

        // ---- script modules (ScriptsComponent) ----

        /// <summary>
        /// New script module, as ScriptsComponent's "New script": NewScript[N].ash/.asc are written to disk
        /// at once and the module is put at the top of the list (script order is compile order).
        /// Relative file names, resolved against the game folder (the current directory, as upstream).
        /// </summary>
        public static ScriptAndHeader AddScriptModule(Game game, ScriptFolder folder = null)
        {
            string newFileName = FindFirstAvailableFileName("NewScript");
            var script = new Script(newFileName + ".asc", "// new module script\r\n", false);
            var header = new Script(newFileName + ".ash", "// new module header\r\n", true);
            script.Modified = true;
            script.SaveToDisk();
            header.Modified = true;
            header.SaveToDisk();
            game.FilesAddedOrRemoved = true;
            var module = new ScriptAndHeader(header, script);
            (folder ?? game.RootScriptFolder).Items.Insert(0, module);
            TypeConverterLists.Refresh(game);
            return module;
        }

        public static bool IsGlobalScript(ScriptAndHeader module) =>
            module.Header.FileName == Script.GLOBAL_HEADER_FILE_NAME;

        /// <summary>Deletes a script module and its files (not allowed for the global script).</summary>
        public static void DeleteScriptModule(Game game, ScriptAndHeader module)
        {
            if (IsGlobalScript(module))
                throw new AGSEditorException("The global script can't be deleted.");
            ScriptFolder parent = FindFolderContaining<ScriptAndHeader, ScriptFolder>(game.RootScriptFolder, module)
                ?? throw new ArgumentException("The script is not part of this game.", nameof(module));
            parent.Items.Remove(module);
            foreach (string file in new[] { module.Script.FileName, module.Header.FileName })
            {
                string path = System.IO.Path.GetFullPath(ProjectPaths.ToLocal(file));
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            game.FilesAddedOrRemoved = true;
            TypeConverterLists.Refresh(game);
        }

        /// <summary>Renames a module's .ash/.asc on disk and in the project (ScriptsComponent.ScriptRenamed).</summary>
        public static void RenameScriptModule(Game game, ScriptAndHeader module, string newName)
        {
            if (IsGlobalScript(module))
                throw new AGSEditorException("The global script can't be renamed.");
            newName = (newName ?? string.Empty).Trim();
            string oldName = System.IO.Path.GetFileNameWithoutExtension(module.Script.FileName);
            if (newName == oldName)
                return;
            if (newName.Length == 0 || !AGS.Editor.Utils.Validation.FilenameIsValid(newName + ".asc"))
                throw new AGSEditorException("The file name '" + newName + ".asc' contains some invalid characters. You cannot use some characters like : and / in script file names.");
            bool caseOnly = string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly && (System.IO.File.Exists(newName + ".asc") || System.IO.File.Exists(newName + ".ash")))
                throw new AGSEditorException("A file with the name '" + newName + ".asc' already exists.");

            RenameFile(module.Script.FileName, newName + ".asc");
            RenameFile(module.Header.FileName, newName + ".ash");
            module.Script.FileName = newName + ".asc";
            module.Header.FileName = newName + ".ash";
            game.FilesAddedOrRemoved = true;
            TypeConverterLists.Refresh(game);
        }

        // AGSEditor.RenameFileOnDisk; a case-only rename goes through a temporary name (case-insensitive file systems).
        private static void RenameFile(string currentName, string newName)
        {
            string source = System.IO.Path.GetFullPath(ProjectPaths.ToLocal(currentName));
            if (!System.IO.File.Exists(source))
                return;
            string dest = System.IO.Path.GetFullPath(newName);
            if (string.Equals(source, dest, StringComparison.OrdinalIgnoreCase))
            {
                string temp = dest + ".renaming";
                System.IO.File.Move(source, temp);
                source = temp;
            }
            System.IO.File.Move(source, dest);
        }

        // ScriptsComponent.FindFirstAvailableFileName
        private static string FindFirstAvailableFileName(string prefix)
        {
            int attempt = 0;
            string newFileName;
            do
            {
                newFileName = prefix + ((attempt > 0) ? attempt.ToString() : string.Empty);
                attempt++;
            }
            while (System.IO.File.Exists(newFileName + ".asc") || System.IO.File.Exists(newFileName + ".ash"));
            return newFileName;
        }

        // ---- views (ViewsComponent) ----

        public static View AddView(Game game, ViewFolder folder = null)
        {
            var view = new View();
            view.ID = game.FindAndAllocateAvailableViewID();
            view.Name = "View" + view.ID;
            (folder ?? game.RootViewFolder).Items.Add(view);
            return view;
        }

        /// <summary>Where a view is used (characters, cursors), or null; port of ViewsComponent.GetViewUsageReport.</summary>
        public static string GetViewUsageReport(Game game, int viewNumber)
        {
            var usage = new System.Text.StringBuilder();
            foreach (Character character in game.RootCharacterFolder.AllItemsFlat)
            {
                string charText = "character " + character.ID + " (" + character.RealName + ")";
                if (character.BlinkingView == viewNumber) usage.AppendLine("Blinking view for " + charText);
                if (character.NormalView == viewNumber) usage.AppendLine("Normal view for " + charText);
                if (character.IdleView == viewNumber) usage.AppendLine("Idle view for " + charText);
                if (character.ThinkingView == viewNumber) usage.AppendLine("Thinking view for " + charText);
                if (character.SpeechView == viewNumber) usage.AppendLine("Speech view for " + charText);
            }
            foreach (MouseCursor item in game.Cursors)
            {
                if (item.View == viewNumber) usage.AppendLine("Mouse cursor " + item.ID + " (" + item.Name + ")");
            }
            if (usage.Length == 0)
                return null;
            return "View " + viewNumber + " is used in the following places. It may also be used in animations controlled by script commands; we cannot detect those uses automatically."
                + Environment.NewLine + Environment.NewLine + usage;
        }

        /// <summary>
        /// Deletes a view. Like upstream, views keep their numbers: the ID is remembered as free
        /// (Game.ViewDeleted) instead of renumbering. Refuses views that are still in use.
        /// </summary>
        public static void DeleteView(Game game, View view)
        {
            string usage = GetViewUsageReport(game, view.ID);
            if (usage != null)
                throw new AGSEditorException(usage);
            ViewFolder parent = FindFolderContaining<View, ViewFolder>(game.RootViewFolder, view)
                ?? throw new ArgumentException("The view is not part of this game.", nameof(view));
            parent.Items.Remove(view);
            game.ViewDeleted(view.ID);
        }

        // ---- dialogs (DialogsComponent) ----

        public static Dialog AddDialog(Game game, DialogFolder folder = null)
        {
            var dialog = new Dialog();
            // The default script is built with Environment.NewLine: use CR LF as the Windows editor writes it.
            dialog.Script = dialog.Script.Replace("\r\n", "\n").Replace("\n", "\r\n");
            dialog.ID = game.RootDialogFolder.GetAllItemsCount();
            dialog.Name = ScriptNames.GetFirstAvailableScriptName(game, "dDialog");
            (folder ?? game.RootDialogFolder).Items.Add(dialog);
            return dialog;
        }

        public static void DeleteDialog(Game game, Dialog dialog)
        {
            DialogFolder parent = FindFolderContaining<Dialog, DialogFolder>(game.RootDialogFolder, dialog)
                ?? throw new ArgumentException("The dialog is not part of this game.", nameof(dialog));
            parent.Items.Remove(dialog);
            int removingID = dialog.ID;
            foreach (Dialog item in game.RootDialogFolder.AllItemsFlat)
            {
                if (item.ID > removingID)
                    item.ID--;
                // Force a refresh since IDs have changed
                item.CachedConvertedScript = null;
            }
        }

        // ---- GUIs (GuiComponent) ----

        private const int GUI_DEFAULT_WIDTH_MAX = 640;
        private const int GUI_DEFAULT_HEIGHT_MAX = 480;

        public static GUI AddGui(Game game, bool textWindow, GUIFolder folder = null)
        {
            System.Drawing.Size res = game.Settings.CustomResolution;
            GUI gui = textWindow
                ? new TextWindowGUI()
                : new NormalGUI(Math.Min(res.Width, GUI_DEFAULT_WIDTH_MAX), Math.Min(res.Height, GUI_DEFAULT_HEIGHT_MAX));
            gui.ID = game.RootGUIFolder.GetAllItemsCount();
            gui.Name = ScriptNames.GetFirstAvailableScriptName(game, "gGui");
            (folder ?? game.RootGUIFolder).Items.Add(gui);
            game.NotifyClientsGUIAddedOrRemoved(gui);
            TypeConverterLists.Refresh(game);
            return gui;
        }

        public static void DeleteGui(Game game, GUI gui)
        {
            GUIFolder parent = FindFolderContaining<GUI, GUIFolder>(game.RootGUIFolder, gui)
                ?? throw new ArgumentException("The GUI is not part of this game.", nameof(gui));
            parent.Items.Remove(gui);
            int removingID = gui.ID;
            foreach (GUI item in game.RootGUIFolder.AllItemsFlat)
            {
                if (item.ID > removingID)
                    item.ID--;
            }
            game.NotifyClientsGUIAddedOrRemoved(gui);
            TypeConverterLists.Refresh(game);
        }

        // ---- mouse cursors (CursorsComponent) ----

        /// <summary>CursorsComponent.BUILT_IN_CURSORS: the standard cursor modes can't be deleted.</summary>
        public const int BuiltInCursors = 8;

        /// <summary>COMMAND_NEW_ITEM / AddNewCursor.</summary>
        public static MouseCursor AddCursor(Game game)
        {
            var newItem = new MouseCursor();
            newItem.ID = game.Cursors.Count;
            newItem.Name = "Cursor" + newItem.ID;
            game.Cursors.Add(newItem);
            TypeConverterLists.Refresh(game);
            return newItem;
        }

        /// <summary>COMMAND_DELETE_ITEM: later cursors move down one ID.</summary>
        public static void DeleteCursor(Game game, MouseCursor cursor)
        {
            if (cursor.ID < BuiltInCursors)
                throw new AGSEditorException("The built-in cursors can't be deleted.");
            int removingID = cursor.ID;
            foreach (MouseCursor item in game.Cursors)
            {
                if (item.ID > removingID)
                    item.ID--;
            }
            game.Cursors.Remove(cursor);
            TypeConverterLists.Refresh(game);
        }
    }
}
