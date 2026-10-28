using System;
using System.IO;
using System.Linq;
using AGS.Editor.Hosting;
using AGS.Types;

namespace AGS.Editor.Core.Tests
{
    [Collection(CurrentDirectoryCollection.Name)]
    public class RoomScriptSaveTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-roomscript-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            Directory.Delete(_gameDir, true);
        }

        [Fact]
        public void EditedRoomScriptsAreSavedWithTheProject()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            GameProject project = GameProject.Load(Path.Combine(_gameDir, "Game.agf"), new ConsoleEditorHost(TextWriter.Null));
            var room = (UnloadedRoom)project.Game.RootRoomFolder.AllItemsFlat.First();
            room.LoadScript();
            room.Script.Text += "\r\n// edited on macOS\r\n";
            project.Save();
            Assert.EndsWith("// edited on macOS\r\n", File.ReadAllText(Path.Combine(_gameDir, room.ScriptFileName), AnsiEncoding.Default));
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }
}
