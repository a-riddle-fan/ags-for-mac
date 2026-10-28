using System;
using System.IO;
using System.Linq;
using AGS.Native;
using AGS.Types;

namespace AGS.Editor
{
    /// <summary>Room tree operations of upstream RoomsComponent that work on files.</summary>
    public static class RoomOperations
    {
        // NewRoomDialog.FIRST_SUGGESTED_ROOM_NUMBER
        private const int FirstSuggestedRoomNumber = 1;

        /// <summary>The number the new-room dialog suggests (state saving or not).</summary>
        public static int SuggestRoomNumber(Game game, bool nonStateSaving = false)
        {
            int number = game.FindFirstAvailableRoomNumber(nonStateSaving ? UnloadedRoom.NON_STATE_SAVING_INDEX : FirstSuggestedRoomNumber);
            int max = nonStateSaving ? UnloadedRoom.HIGHEST_ROOM_NUMBER_ALLOWED : UnloadedRoom.NON_STATE_SAVING_INDEX - 1;
            if (number > max)
                throw new AGSEditorException("There are no more room numbers available for this type of room. Consider changing some of your rooms to be non state saving.");
            return number;
        }

        /// <summary>
        /// Creates "Blank Room" <paramref name="roomNumber"/> (upstream CreateNewRoom with the blank template):
        /// writes roomN.crm with black backgrounds and empty masks and roomN.asc, and adds the room to
        /// <paramref name="folder"/>. Existing files are overwritten only if <paramref name="confirmOverwrite"/> agrees.
        /// Returns null when cancelled.
        /// </summary>
        public static UnloadedRoom CreateBlankRoom(GameProject project, int roomNumber, UnloadedRoomFolder folder = null,
            Func<string, bool> confirmOverwrite = null)
        {
            Game game = project.Game;
            if (game.Rooms.Any(r => r.Number == roomNumber))
                throw new AGSEditorException("You already have a room number " + roomNumber + ". Please choose another number.");
            var newRoom = new UnloadedRoom(roomNumber);
            string crm = Path.Combine(project.Directory, newRoom.FileName);
            string script = Path.Combine(project.Directory, newRoom.ScriptFileName);
            // PromptForAndDeleteAnyExistingRoomFile
            if (File.Exists(crm))
            {
                if (confirmOverwrite != null && !confirmOverwrite("A file called '" + newRoom.FileName + "' already exists. Do you want to overwrite it?"))
                    return null;
                File.Delete(crm);
            }

            Room room = CreateEmptyRoom(game, roomNumber);
            using (NativeRoom native = NativeRoom.CreateBlank(room, game.TextEncoding))
                native.Save(crm);
            // StreamWriter.WriteLine on Windows
            File.WriteAllText(script, "// room script file\r\n");

            (folder ?? game.RootRoomFolder).Items.Add(newRoom);
            game.FilesAddedOrRemoved = true;
            TypeConverterLists.Refresh(game);
            return newRoom;
        }

        /// <summary>
        /// Deletes a room (RoomsComponent COMMAND_DELETE_ITEM + DeleteRoom): characters starting in it get no
        /// starting room if <paramref name="ask"/> agrees (EnsureNoCharactersStartInRoom), the .crm, script and
        /// user file are deleted from disk and the room is removed from its folder. Returns false when cancelled;
        /// <paramref name="warn"/> receives non-fatal problems.
        /// </summary>
        public static bool DeleteRoom(GameProject project, UnloadedRoom room, Func<string, bool> ask, Action<string> warn = null)
        {
            Game game = project.Game;
            var starting = game.RootCharacterFolder.AllItemsFlat.Where(c => c.StartingRoom == room.Number).ToList();
            if (starting.Count > 0)
            {
                string characterList = string.Concat(starting.Select(c => string.Format("{0} ({1}){2}", c.ID, c.ScriptName, Environment.NewLine)));
                if (ask != null && !ask("The following characters are set to start in this room. If you remove the room, they will be set to have no starting room. Do you want to continue?" +
                    Environment.NewLine + Environment.NewLine + characterList))
                    return false;
                starting.ForEach(c => c.StartingRoom = -1);
            }

            UnloadedRoomFolder parent = FindFolder(game.RootRoomFolder, room);
            parent?.Items.Remove(room);

            string crm = Path.Combine(project.Directory, room.FileName);
            if (!File.Exists(crm))
                warn?.Invoke("The room file was not found and could not be deleted.");
            foreach (string file in new[] { room.FileName, room.ScriptFileName, room.UserFileName })
            {
                string path = Path.Combine(project.Directory, file);
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    warn?.Invoke("The room file could not be deleted: " + ex.Message);
                }
            }
            TypeConverterLists.Refresh(game);
            game.FilesAddedOrRemoved = true;
            return true;
        }

        private static UnloadedRoomFolder FindFolder(UnloadedRoomFolder folder, UnloadedRoom room)
        {
            if (folder.Items.Contains(room)) return folder;
            foreach (UnloadedRoomFolder sub in folder.SubFolders)
                if (FindFolder(sub, room) is UnloadedRoomFolder found) return found;
            return null;
        }

        /// <summary>
        /// RoomsComponent.ImportExistingRoomFile: copies a .crm (and its .asc, or the script embedded in old room
        /// files) into the project as room N — the number in its name "roomN.crm" if that is free, else the first
        /// free one. Returns the new room, or throws with upstream's message when it can't be imported.
        /// </summary>
        public static UnloadedRoom ImportRoomFile(GameProject project, string fileName, UnloadedRoomFolder folder = null)
        {
            Game game = project.Game;
            string fullPath = Path.GetFullPath(fileName);
            bool selectedFileInGameDirectory = string.Equals(Path.GetDirectoryName(fullPath), Path.GetFullPath(project.Directory).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

            int newRoomNumber, fileRoomNumber = -1;
            var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(fullPath), @"room(\d+).crm", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count == 2)
            {
                fileRoomNumber = Convert.ToInt32(match.Groups[1].Captures[0].Value);
                if (game.DoesRoomNumberAlreadyExist(fileRoomNumber))
                {
                    if (selectedFileInGameDirectory)
                        throw new AGSEditorException("This file is already part of this game.");
                    throw new AGSEditorException("This game already has a room " + fileRoomNumber + ". If you are sure that you want to import this file, rename it first.");
                }
                newRoomNumber = fileRoomNumber;
            }
            else
            {
                newRoomNumber = game.FindFirstAvailableRoomNumber(FirstSuggestedRoomNumber);
            }

            string newFileName = Path.Combine(project.Directory, string.Format("room{0}.crm", newRoomNumber));
            string newScriptName = Path.ChangeExtension(newFileName, ".asc");
            if (selectedFileInGameDirectory)
            {
                if (!File.Exists(newScriptName))
                    CopyScriptOutOfOldRoomFile(fullPath, newScriptName);
                if (newRoomNumber != fileRoomNumber)
                    File.Copy(fullPath, newFileName, true);
            }
            else
            {
                File.Copy(fullPath, newFileName, true);
                if (File.Exists(Path.ChangeExtension(fullPath, ".asc")))
                    File.Copy(Path.ChangeExtension(fullPath, ".asc"), newScriptName, true);
                else
                    CopyScriptOutOfOldRoomFile(fullPath, newScriptName);
            }

            var newRoom = new UnloadedRoom(newRoomNumber);
            (folder ?? game.RootRoomFolder).Items.Add(newRoom);
            game.FilesAddedOrRemoved = true;
            TypeConverterLists.Refresh(game);
            return newRoom;
        }

        private static void CopyScriptOutOfOldRoomFile(string roomFile, string scriptFile)
        {
            string roomScript = NativeRoom.LoadRoomScript(roomFile) ?? "// room script file\r\n";
            // NOTE: old game format: texts are always ANSI/ASCII
            File.WriteAllText(scriptFile, roomScript, AnsiEncoding.Default);
        }

        /// <summary>RoomsComponent.CreateEmptyRoom.</summary>
        private static Room CreateEmptyRoom(Game game, int roomNumber)
        {
            Room room = new Room(roomNumber);
            var gameSettings = game.Settings;
            room.GameID = gameSettings.UniqueID;
            room.Width = gameSettings.CustomResolution.Width;
            room.Height = gameSettings.CustomResolution.Height;
            room.MaskResolution = gameSettings.DefaultRoomMaskResolution;
            room.ColorDepth = (int)gameSettings.ColorDepth * 8; // from bytes to bits per pixel
            room.BackgroundCount = 1;
            room.RightEdgeX = room.Width - 1;
            room.BottomEdgeY = room.Height - 1;
            room.Interactions.ScriptModule = room.ScriptFileName;
            foreach (var hotspot in room.Hotspots)
                hotspot.Interactions.ScriptModule = room.Interactions.ScriptModule;
            foreach (var region in room.Regions)
                region.Interactions.ScriptModule = room.Interactions.ScriptModule;
            return room;
        }
    }
}
