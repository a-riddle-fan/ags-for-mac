using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AGS.Editor.Build;
using AGS.Editor.Hosting;
using AGS.Native;
using AGS.Types;

namespace AGS.Editor
{
    /// <summary>
    /// Game and room templates (upstream Tasks.CreateNewGameFromTemplate / GUIController.CreateNewGame /
    /// CreateTemplateFromCurrentGame, RoomsComponent.CreateNewRoom with a template).
    /// </summary>
    public static class GameTemplates
    {
        public const string TEMPLATE_INCLUDE_FILE = "template.files";

        /// <summary>
        /// Creates a game from a .agt template in <paramref name="newGameDirectory"/> and gives it its own identity
        /// (file name, game name, save folder, new unique ID, setup title), like the upstream wizard. Rooms are saved
        /// with the new game ID (upstream rebuilds the game for that), as the engine rejects rooms of another game.
        /// </summary>
        public static GameProject CreateNewGame(string templatePath, string newGameDirectory, string gameFileName, string gameName, IEditorHost host)
        {
            if (Directory.Exists(newGameDirectory) && Directory.EnumerateFileSystemEntries(newGameDirectory).Any())
                throw new AGSEditorException($"The folder '{newGameDirectory}' already exists and is not empty.");
            Directory.CreateDirectory(newGameDirectory);
            NativeTemplates.ExtractGame(templatePath, newGameDirectory);
            EnsureStandardSubFoldersExist(newGameDirectory);

            string agf = Path.Combine(newGameDirectory, GameProject.GAME_FILE_NAME);
            if (!File.Exists(agf))
                throw new AGSEditorException("The template does not contain a Game.agf (games older than AGS 3.0 are not supported).");
            GameProject project = GameProject.Load(agf, host);
            Settings settings = project.Game.Settings;
            settings.GameFileName = gameFileName;
            settings.GameName = gameName;
            settings.SaveGameFolderName = gameName;
            settings.GenerateNewGameID();
            project.Game.DefaultSetup.TitleText = settings.GameName + " Setup";
            project.Game.WorkspaceState.LastBuildConfiguration = settings.DebugMode ? BuildConfiguration.Debug : BuildConfiguration.Release;
            project.Save();
            AssignRoomsToGame(project);
            return project;
        }

        /// <summary>Utilities.EnsureStandardSubFoldersExist.</summary>
        public static void EnsureStandardSubFoldersExist(string gameDirectory)
        {
            foreach (string folder in new[] { "Speech", ProjectPaths.AUDIO_CACHE_DIRECTORY, "Compiled", Path.Combine("Compiled", "Data") })
                Directory.CreateDirectory(Path.Combine(gameDirectory, folder));
        }

        /// <summary>Saves every room with the game's ID (what upstream's forced rebuild after a new game does).</summary>
        public static void AssignRoomsToGame(GameProject project)
        {
            foreach (IRoom room in project.Game.RootRoomFolder.AllItemsFlat)
            {
                if (room is UnloadedRoom unloaded)
                    AssignRoomToGame(project, unloaded);
            }
        }

        private static void AssignRoomToGame(GameProject project, UnloadedRoom unloaded)
        {
            string path = Path.Combine(project.Directory, ProjectPaths.ToLocal(unloaded.FileName));
            if (!File.Exists(path)) return;
            using NativeRoom native = NativeRoom.Load(path);
            Room room = native.ToRoom(unloaded, project.Game.TextEncoding);
            room.GameID = project.Game.Settings.UniqueID;
            native.ApplyRoom(room, project.Game.TextEncoding);
            native.Save(path);
        }

        /// <summary>RoomsComponent.CreateNewRoom with a room template (.art).</summary>
        public static UnloadedRoom CreateRoomFromTemplate(GameProject project, string templatePath, int roomNumber, UnloadedRoomFolder folder = null)
        {
            Game game = project.Game;
            if (game.Rooms.Any(r => r.Number == roomNumber))
                throw new AGSEditorException("You already have a room number " + roomNumber + ". Please choose another number.");
            NativeTemplates.ExtractRoom(templatePath, project.Directory, roomNumber);
            var newRoom = new UnloadedRoom(roomNumber);
            AssignRoomToGame(project, newRoom);
            (folder ?? game.RootRoomFolder).Items.Add(newRoom);
            game.FilesAddedOrRemoved = true;
            TypeConverterLists.Refresh(game);
            return newRoom;
        }

        /// <summary>
        /// CreateTemplateFromCurrentGame: packs the project's files (template.files patterns if present, otherwise
        /// upstream's basic list) into a .agt asset pack with agspak. The project should be saved first.
        /// </summary>
        public static IReadOnlyList<string> MakeTemplate(GameProject project, string templatePath, AgsBuildToolchain toolchain)
        {
            string dir = project.Directory;
            List<string> files = TemplateFileList(dir);
            if (!files.Contains(GameProject.GAME_FILE_NAME, StringComparer.OrdinalIgnoreCase))
                throw new AGSEditorException("The project has no Game.agf to put into the template.");

            string work = Path.Combine(Path.GetTempPath(), "ags4mac-template-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (string file in files)
                {
                    string target = Path.Combine(work, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(Path.Combine(dir, file), target);
                }
                if (File.Exists(templatePath)) File.Delete(templatePath);
                var psi = new ProcessStartInfo(Path.Combine(toolchain.ToolsDirectory, "agspak"))
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                };
                foreach (string arg in new[] { "-c", templatePath, work, "-r" })
                    psi.ArgumentList.Add(arg);
                using Process p = Process.Start(psi);
                string log = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0 || !File.Exists(templatePath))
                    throw new AGSEditorException("Creating the template failed:\n" + log.Trim());
            }
            finally
            {
                try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (IOException) { }
            }
            return files;
        }

        /// <summary>ConstructFileListUsingIncludeFile / ConstructBasicFileListForTemplate (paths relative, '/').</summary>
        public static List<string> TemplateFileList(string dir)
        {
            string includeFile = Path.Combine(dir, TEMPLATE_INCLUDE_FILE);
            List<string> all = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))
                .Where(f => !f.StartsWith("Compiled/", StringComparison.OrdinalIgnoreCase) && !f.StartsWith("_Debug/", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (File.Exists(includeFile))
            {
                using Stream s = File.OpenRead(includeFile);
                var patterns = IncludeUtils.CreatePatternList(s, IncludeUtils.MatchOption.CaseInsensitive);
                if (patterns.Length == 0) return new List<string>();
                // upstream matches Windows-style relative paths
                string[] candidates = all.Select(f => f.Replace('/', '\\')).ToArray();
                return IncludeUtils.FilterItemList(candidates, patterns, IncludeUtils.MatchOption.CaseInsensitive)
                    .Select(f => f.Replace('\\', '/')).ToList();
            }
            bool TopLevel(string f) => !f.Contains('/');
            bool Match(string f, string pattern) =>
                System.Text.RegularExpressions.Regex.IsMatch(f, "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            string[] topPatterns = { "*.ico", GameProject.GAME_FILE_NAME, SpriteFile.DefaultSpriteFileName, "preload.pcx", "flic*.fl?",
                "agsfnt*.ttf", "agsfnt*.wfn", "*.crm", "*.asc", "*.ash", "*.txt", "*.trs", "*.pdf", "*.ogv" };
            var result = all.Where(f => TopLevel(f) && topPatterns.Any(p => Match(f, p))).ToList();
            result.AddRange(all.Where(f => f.StartsWith(ProjectPaths.AUDIO_CACHE_DIRECTORY + "/", StringComparison.OrdinalIgnoreCase)
                                        || f.StartsWith("Speech/", StringComparison.OrdinalIgnoreCase)));
            return result;
        }
    }
}
