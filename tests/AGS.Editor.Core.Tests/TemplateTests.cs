using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AGS.Editor.Build;
using AGS.Editor.Hosting;
using AGS.Native;
using AGS.Types;

namespace AGS.Editor.Core.Tests
{
    [Collection(CurrentDirectoryCollection.Name)]
    public class TemplateTests : IDisposable
    {
        private readonly string _work = Path.Combine(Path.GetTempPath(), "ags4mac-tpl-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_work)) Directory.Delete(_work, true);
        }

        [Fact]
        public void MakeTemplateThenCreateANewGameFromIt()
        {
            string source = Path.Combine(_work, "source");
            CopyDirectory(ReferenceGame.Directory, source);
            var toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            Assert.NotNull(toolchain);
            GameProject original = GameProject.Load(Path.Combine(source, "Game.agf"), new ConsoleEditorHost(TextWriter.Null));
            int oldId = original.Game.Settings.UniqueID;

            string agt = Path.Combine(_work, "Starterpack.agt");
            var files = GameTemplates.MakeTemplate(original, agt, toolchain);
            Assert.Contains("Game.agf", files);
            Assert.Contains(files, f => f.StartsWith("AudioCache/"));
            Assert.Contains("room1.crm", files);
            Assert.True(new FileInfo(agt).Length > 1_000_000);

            string target = Path.Combine(_work, "Mein Spiel");
            GameProject created = GameTemplates.CreateNewGame(agt, target, "MeinSpiel", "Mein Spiel", new ConsoleEditorHost(TextWriter.Null));
            Assert.Equal("Mein Spiel", created.Game.Settings.GameName);
            Assert.Equal("MeinSpiel", created.Game.Settings.GameFileName);
            Assert.NotEqual(oldId, created.Game.Settings.UniqueID);
            Assert.True(Directory.Exists(Path.Combine(target, "Compiled", "Data")));
            Assert.Equal(File.ReadAllBytes(Path.Combine(source, "acsprset.spr")), File.ReadAllBytes(Path.Combine(target, "acsprset.spr")));

            // Rooms belong to the new game (the engine refuses rooms of another game).
            var room1 = (UnloadedRoom)created.Game.RootRoomFolder.AllItemsFlat.First();
            using (NativeRoom native = NativeRoom.Load(Path.Combine(target, room1.FileName)))
                Assert.Equal(created.Game.Settings.UniqueID, native.ToRoom(room1, AnsiEncoding.Default).GameID);

            BuildResult result = toolchain.Build(Path.Combine(target, "Game.agf"));
            Assert.True(result.Succeeded, ReferenceGame.BuildFailure(result));
            Assert.True(File.Exists(Path.Combine(target, "Compiled", "Data", "MeinSpiel.ags")));
        }

        [Fact]
        public void RoomTemplateBecomesANewRoom()
        {
            string game = Path.Combine(_work, "game");
            CopyDirectory(ReferenceGame.Directory, game);
            var toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            GameProject project = GameProject.Load(Path.Combine(game, "Game.agf"), new ConsoleEditorHost(TextWriter.Null));

            // A room template: room files + rtemplate.dat, packed like upstream's SaveRoomAsTemplate.
            string tplDir = Path.Combine(_work, "art");
            Directory.CreateDirectory(tplDir);
            File.Copy(Path.Combine(game, "room5.crm"), Path.Combine(tplDir, "room.crm"));
            File.Copy(Path.Combine(game, "room5.asc"), Path.Combine(tplDir, "room.asc"));
            File.WriteAllBytes(Path.Combine(tplDir, "rtemplate.dat"), BitConverter.GetBytes(0x74673812));
            string art = Path.Combine(_work, "Zimmer.art");
            var psi = new ProcessStartInfo(Path.Combine(toolchain.ToolsDirectory, "agspak")) { UseShellExecute = false, RedirectStandardOutput = true };
            foreach (string a in new[] { "-c", art, tplDir }) psi.ArgumentList.Add(a);
            using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); }

            UnloadedRoom room = GameTemplates.CreateRoomFromTemplate(project, art, 40);
            Assert.True(File.Exists(Path.Combine(game, "room40.crm")));
            Assert.Equal(File.ReadAllText(Path.Combine(game, "room5.asc")), File.ReadAllText(Path.Combine(game, "room40.asc")));
            Assert.False(File.Exists(Path.Combine(game, "room40.dat"))); // rtemplate.dat is not extracted
            Assert.Contains(room, project.Game.RootRoomFolder.AllItemsFlat);
            project.Save();
            var built = toolchain.Build(Path.Combine(game, "Game.agf"));
            Assert.True(built.Succeeded, ReferenceGame.BuildFailure(built));
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
