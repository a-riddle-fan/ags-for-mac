using System;
using System.IO;
using System.Linq;
using AGS.Editor.Hosting;
using AGS.Types;

namespace AGS.Editor.Core.Tests
{
    [Collection(CurrentDirectoryCollection.Name)]
    public class ProjectOperationsTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-ops-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public ProjectOperationsTests()
        {
            AGS.Editor.Build.KnownBuildTargets.RegisterAll();
            Copy(ReferenceGame.Directory, _gameDir);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            Directory.Delete(_gameDir, true);
        }

        private GameProject Load() => GameProject.Load(Path.Combine(_gameDir, "Game.agf"), new ConsoleEditorHost(TextWriter.Null));

        [Fact]
        public void AddAndDeleteCharactersLikeUpstream()
        {
            GameProject project = Load();
            Game game = project.Game;
            Assert.Equal(21, game.Characters.Count);

            Character added = ProjectOperations.AddCharacter(game);
            Assert.Equal(21, added.ID);
            Assert.StartsWith("cChar", added.ScriptName);
            Assert.Equal(-1, added.StartingRoom);
            Assert.Equal(22, game.Characters.Count);

            Character fifth = game.Characters.Single(c => c.ID == 5);
            string sixthName = game.Characters.Single(c => c.ID == 6).ScriptName;
            ProjectOperations.DeleteCharacter(game, fifth);
            Assert.Equal(21, game.Characters.Count);
            Assert.Equal(sixthName, game.Characters.Single(c => c.ID == 5).ScriptName); // later IDs move down
            Assert.Equal(Enumerable.Range(0, 21), game.Characters.Select(c => c.ID).OrderBy(i => i));

            project.Save();
            GameProject reloaded = Load();
            Assert.Equal(21, reloaded.Game.Characters.Count);
            Assert.Contains(reloaded.Game.Characters, c => c.ScriptName == added.ScriptName && c.ID == 20);
        }

        [Fact]
        public void AddAndDeleteInventoryItems()
        {
            Game game = Load().Game;
            InventoryItem added = ProjectOperations.AddInventoryItem(game);
            Assert.Equal(7, added.ID); // inventory IDs start at 1
            Assert.Equal("New inventory item", added.Description);
            Assert.StartsWith("iInvItem", added.Name);

            ProjectOperations.DeleteInventoryItem(game, game.InventoryItems.Single(i => i.ID == 1));
            Assert.Equal(Enumerable.Range(1, 6), game.InventoryItems.Select(i => i.ID).OrderBy(i => i));
        }

        [Fact]
        public void FoldersCanBeAddedAndEmptyOnesDeleted()
        {
            Game game = Load().Game;
            CharacterFolder folder = ProjectOperations.AddFolder<Character, CharacterFolder>(game.RootCharacterFolder, n => new CharacterFolder(n));
            CharacterFolder second = ProjectOperations.AddFolder<Character, CharacterFolder>(game.RootCharacterFolder, n => new CharacterFolder(n));
            Assert.NotEqual(folder.Name, second.Name);

            ProjectOperations.AddCharacter(game, folder);
            Assert.Throws<AGSEditorException>(() => ProjectOperations.DeleteEmptyFolder<Character, CharacterFolder>(game.RootCharacterFolder, folder));
            ProjectOperations.DeleteEmptyFolder<Character, CharacterFolder>(game.RootCharacterFolder, second);
            Assert.DoesNotContain(second, game.RootCharacterFolder.SubFolders);
        }

        [Fact]
        public void ViewsKeepNumbersAndUsedViewsCannotBeDeleted()
        {
            Game game = Load().Game;
            var existing = game.RootViewFolder.AllItemsFlat.Select(v => v.ID).ToHashSet();
            View added = ProjectOperations.AddView(game);
            // Like upstream, numbers of deleted views are reused first (the reference game has gaps).
            Assert.DoesNotContain(added.ID, existing);
            Assert.True(added.ID < existing.Max(), "expected a freed view number to be reused");
            Assert.Equal("View" + added.ID, added.Name);

            View used = game.RootViewFolder.AllItemsFlat.Single(v => v.ID == game.Characters[0].NormalView);
            var ex = Assert.Throws<AGSEditorException>(() => ProjectOperations.DeleteView(game, used));
            Assert.Contains("Normal view for character 0", ex.Message);

            int addedId = added.ID;
            ProjectOperations.DeleteView(game, added);
            Assert.DoesNotContain(game.RootViewFolder.AllItemsFlat, v => v.ID == addedId);
            Assert.Equal(addedId, ProjectOperations.AddView(game).ID); // the freed number is reused
        }

        [Fact]
        public void DialogsAndGuisRenumberOnDelete()
        {
            Game game = Load().Game;
            Dialog d0 = ProjectOperations.AddDialog(game);
            Dialog d1 = ProjectOperations.AddDialog(game);
            Assert.Equal((0, 1), (d0.ID, d1.ID));
            ProjectOperations.DeleteDialog(game, d0);
            Assert.Equal(0, d1.ID);

            int guis = game.GUIs.Count;
            GUI gui = ProjectOperations.AddGui(game, textWindow: false);
            Assert.Equal(guis, gui.ID);
            Assert.IsType<NormalGUI>(gui);
            Assert.IsType<TextWindowGUI>(ProjectOperations.AddGui(game, textWindow: true));
            ProjectOperations.DeleteGui(game, game.GUIs.Single(g => g.ID == 0));
            Assert.Equal(Enumerable.Range(0, guis + 1), game.GUIs.Select(g => g.ID).OrderBy(i => i));
        }

        private static void Copy(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(source))
                Copy(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }
}
