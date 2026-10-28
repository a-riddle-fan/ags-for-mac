using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using AGS.Editor.Avalonia.ViewModels;
using AGS.Editor.Avalonia.Views;
using AGS.Types;

namespace AGS.Editor.Avalonia.Tests
{
    public class ProjectTreeActionsTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-tree-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        [AvaloniaFact]
        public void NewCharacterFromContextMenuAndRenameUpdatesTree()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));

            ProjectTreeNode Characters() => vm.TreeRoots[0].Children.Single(n => n.Name == "Characters");

            // The real context menu opens on the tree item and its entries are bound to the actions.
            TestPaths.Settle(window);
            var panel = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
                .OfType<global::Avalonia.Controls.StackPanel>().First(p => p.DataContext == Characters() && p.ContextMenu != null);
            panel.ContextMenu.Open(panel);
            TestPaths.Settle(window);
            var menuItems = panel.ContextMenu.GetLogicalChildren().OfType<global::Avalonia.Controls.MenuItem>().ToList();
            Assert.Equal(new[] { "New character", "New folder" }, menuItems.Select(m => ((TreeAction)m.DataContext).Header));
            Assert.All(menuItems, m => Assert.NotNull(m.Command));
            panel.ContextMenu.Close();
            Assert.Contains(Characters().Actions, a => a.Header == "New character");
            Characters().Actions.Single(a => a.Header == "New character").Command.Execute(null);

            var added = Assert.IsType<Character>(vm.SelectedNode.Payload);
            Assert.Equal(22, Characters().Children.Count);
            Assert.True(Characters().IsExpanded);
            Assert.True(vm.IsModified);

            var scriptName = vm.PropertyGrid.Entries.OfType<TextPropertyRow>().Single(r => r.Descriptor.Name == nameof(Character.ScriptName));
            scriptName.Text = "cMacUser";
            Assert.Contains(Characters().Children, n => n.Name == "21: cMacUser");
            Assert.Same(added, vm.SelectedNode.Payload);

            vm.SelectedNode.Actions.Single(a => a.Header == "Delete character").Command.Execute(null); // Confirm unset = yes
            Assert.Equal(21, Characters().Children.Count);

            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-tree-actions.png"));
        }

        [AvaloniaFact]
        public void ScriptModulesCanBeAddedRenamedAndDeleted()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            ProjectTreeNode Scripts() => vm.TreeRoots[0].Children.Single(n => n.Name == "Scripts");

            // The global script module has no rename/delete.
            Assert.Empty(Scripts().Children.Single(n => n.Name == "GlobalScript").Actions);

            Scripts().Actions.Single(a => a.Header == "New script").Command.Execute(null);
            Assert.Equal("NewScript", Scripts().Children[0].Name); // new modules go to the top
            Assert.True(File.Exists(Path.Combine(_gameDir, "NewScript.asc")));
            Assert.True(File.Exists(Path.Combine(_gameDir, "NewScript.ash")));

            vm.PromptText = (title, current) => "MacTools";
            Scripts().Children[0].Actions.Single(a => a.Header == "Rename…").Command.Execute(null);
            Assert.Equal("MacTools", Scripts().Children[0].Name);
            Assert.True(File.Exists(Path.Combine(_gameDir, "MacTools.asc")));
            Assert.False(File.Exists(Path.Combine(_gameDir, "NewScript.asc")));

            // A name that would be invalid on Windows is rejected even on macOS.
            vm.PromptText = (title, current) => "Bad:Name";
            Scripts().Children[0].Actions.Single(a => a.Header == "Rename…").Command.Execute(null);
            Assert.Equal("MacTools", Scripts().Children[0].Name);
            Assert.Contains("invalid characters", vm.OutputText);

            Scripts().Children[0].Actions.Single(a => a.Header == "Delete").Command.Execute(null);
            Assert.Equal(4, Scripts().Children.Count);
            Assert.False(File.Exists(Path.Combine(_gameDir, "MacTools.asc")));

            vm.SaveCommand.Execute(null);
            Assert.DoesNotContain("MacTools", File.ReadAllText(Path.Combine(_gameDir, "Game.agf")));
        }

        [AvaloniaFact]
        public void NewBlankRoomOpensSavesAndBuilds()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));

            ProjectTreeNode Rooms() => vm.TreeRoots[0].Children.Single(n => n.Name == "Rooms");
            int before = Rooms().Children.Count;
            int expected = RoomOperations.SuggestRoomNumber(vm.Project.Game);
            Rooms().Actions.Single(a => a.Header == "New room").Command.Execute(null);

            var created = Assert.IsType<UnloadedRoom>(vm.SelectedNode.Payload);
            Assert.Equal(expected, created.Number);
            Assert.Equal(before + 1, Rooms().Children.Count);
            Assert.True(File.Exists(Path.Combine(_gameDir, created.FileName)));
            Assert.Equal("// room script file\r\n", File.ReadAllText(Path.Combine(_gameDir, created.ScriptFileName)));

            vm.OpenNode(vm.SelectedNode);
            var room = Assert.IsType<RoomEditorDocument>(vm.SelectedDocument);
            Assert.Equal((vm.Project.Game.Settings.CustomResolution.Width, vm.Project.Game.Settings.CustomResolution.Height), (room.Room.Width, room.Room.Height));
            Assert.Equal(room.Room.Width - 1, room.Room.RightEdgeX);
            Assert.Equal(1, room.Room.BackgroundCount);
            Assert.Equal((int)vm.Project.Game.Settings.ColorDepth * 8, room.Room.ColorDepth);

            vm.SaveCommand.Execute(null);
            Directory.SetCurrentDirectory(_originalCwd);
            var reloaded = AGS.Editor.GameProject.Load(Path.Combine(_gameDir, "Game.agf"), new AGS.Editor.Hosting.ConsoleEditorHost(TextWriter.Null));
            Assert.Contains(reloaded.Game.Rooms, r => r.Number == expected);
            var toolchain = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            var result = toolchain.Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.True(result.Succeeded, TestPaths.BuildFailure(result));

            // Delete it again: closes its editor, removes the files and the tree node.
            ProjectTreeNode node = Rooms().Children.Single(n => n.Payload is UnloadedRoom r && r.Number == expected);
            node.Actions.Single(a => a.Header == "Delete room").Command.Execute(null);
            Assert.Equal(before, Rooms().Children.Count);
            Assert.DoesNotContain(vm.Documents, d => d is RoomEditorDocument);
            Assert.False(File.Exists(Path.Combine(_gameDir, created.FileName)));
            Assert.False(File.Exists(Path.Combine(_gameDir, created.ScriptFileName)));

            // A room where characters start: they lose their starting room.
            ProjectTreeNode first = Rooms().Children.First(n => n.Payload is UnloadedRoom r &&
                vm.Project.Game.RootCharacterFolder.AllItemsFlat.Any(c => c.StartingRoom == r.Number));
            int number = ((UnloadedRoom)first.Payload).Number;
            first.Actions.Single(a => a.Header == "Delete room").Command.Execute(null);
            Assert.DoesNotContain(vm.Project.Game.RootCharacterFolder.AllItemsFlat, c => c.StartingRoom == number);
        }

        [AvaloniaFact]
        public void AddAudioFilesCopiesToCacheAndBuilds()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            Game game = vm.Project.Game;

            // An existing clip's file, copied into a project subfolder under a new name.
            AudioClip existing = game.RootAudioClipFolder.GetAllAudioClipsFromAllSubFolders().First(c => c.FileType == AudioClipFileType.OGG || c.FileType == AudioClipFileType.WAV);
            string sourceDir = Path.Combine(_gameDir, "NewSounds");
            Directory.CreateDirectory(sourceDir);
            string source = Path.Combine(sourceDir, "door creak" + Path.GetExtension(existing.CacheFileName));
            File.Copy(Path.Combine(_gameDir, AGS.Editor.ProjectPaths.ToLocal(existing.CacheFileName)), source);
            int before = game.RootAudioClipFolder.GetAllItemsCount();

            vm.PickOpenFiles = (title, ext) => System.Threading.Tasks.Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(new[] { source });
            vm.TreeRoots[0].Children.Single(n => n.Name == "Audio").Actions.Single(a => a.Header == "Add audio file(s)...").Command.Execute(null);

            var clip = Assert.IsType<AudioClip>(vm.SelectedNode.Payload);
            Assert.Equal("aDoor_creak", clip.ScriptName); // upstream RemoveInvalidCharactersFromScriptName
            Assert.Equal(before + 1, game.RootAudioClipFolder.GetAllItemsCount());
            Assert.Equal("NewSounds\\door creak" + Path.GetExtension(source), clip.SourceFileName);
            Assert.True(File.Exists(Path.Combine(_gameDir, clip.CacheFileName)));
            Assert.Equal(existing.FileType, clip.FileType);

            vm.SaveCommand.Execute(null);
            var toolchain = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            var result = toolchain.Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.True(result.Succeeded, TestPaths.BuildFailure(result));
        }

        [AvaloniaFact]
        public void NewImportAndDeleteFonts()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            Game game = vm.Project.Game;
            ProjectTreeNode Fonts() => vm.TreeRoots[0].Children.Single(n => n.Name == "Fonts");
            int count = game.Fonts.Count;

            Fonts().Actions.Single(a => a.Header == "New Font").Command.Execute(null);
            var added = Assert.IsType<AGS.Types.Font>(vm.SelectedNode.Payload);
            Assert.Equal(count, added.ID);
            Assert.Equal("Font " + count, added.Name);
            Assert.True(File.Exists(Path.Combine(_gameDir, added.ProjectFilename)));

            // Import another font's file over the new one (WFN if the game has one, else TTF).
            AGS.Types.Font donor = game.Fonts.First(f => f.ID != added.ID && File.Exists(Path.Combine(_gameDir, f.ProjectFilename ?? "")));
            string source = Path.Combine(_gameDir, "import" + Path.GetExtension(donor.ProjectFilename));
            File.Copy(Path.Combine(_gameDir, donor.ProjectFilename), source);
            vm.PickOpenFiles = (title, ext) => System.Threading.Tasks.Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(new[] { source });
            vm.AskTtfSize = current => (false, 14);
            vm.SelectedNode.Actions.Single(a => a.Header == "Import over this font...").Command.Execute(null);
            Assert.Equal("import" + Path.GetExtension(source), added.SourceFilename);
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(Path.Combine(_gameDir, added.ProjectFilename)));
            if (source.EndsWith(".ttf")) Assert.Equal(14, added.PointSize);

            vm.SaveCommand.Execute(null);
            var toolchain = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            var result = toolchain.Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.True(result.Succeeded, TestPaths.BuildFailure(result));

            // Built-in fonts can't be deleted; the new one can.
            Assert.DoesNotContain(Fonts().Children[0].Actions, a => a.Header == "Delete this font");
            string file = Path.Combine(_gameDir, added.ProjectFilename);
            Fonts().Children.Single(n => n.Payload == added).Actions.Single(a => a.Header == "Delete this font").Command.Execute(null);
            Assert.Equal(count, game.Fonts.Count);
            Assert.False(File.Exists(file));
        }

        [AvaloniaFact]
        public void FontSizeAndFontFileEditorsInThePropertyGrid()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            vm.InstallFontHooks();
            Game game = vm.Project.Game;
            AGS.Types.Font font = game.Fonts.First(f => (f.ProjectFilename ?? "").EndsWith(".ttf") && f.PointSize > 0);
            vm.OpenNode(vm.TreeRoots[0].Children.Single(n => n.Name == "Fonts").Children.Single(n => n.Payload == font));
            Assert.Same(font, vm.PropertyGrid.Component);
            PropertyRow Row(string name) => vm.PropertyGrid.Entries.OfType<PropertyRow>().Single(r => r.Descriptor.Name == name);

            // Point size: the "..." button asks for a nominal size
            vm.AskTtfSize = current => (false, 17);
            Row("PointSize").OpenEditorCommand.Execute(null);
            Assert.Equal(17, font.PointSize);
            Assert.True(vm.IsModified);

            // ... or for a full pixel height, measured by upstream's font renderer
            vm.AskTtfSize = current => (true, 30);
            Row("PointSize").OpenEditorCommand.Execute(null);
            int sizeFor30 = font.PointSize;
            vm.AskTtfSize = current => (true, 15);
            Row("PointSize").OpenEditorCommand.Execute(null);
            Assert.True(font.PointSize > 0 && font.PointSize < sizeFor30, $"{font.PointSize} vs {sizeFor30}");

            // Font file: choose another TTF and import it over this font
            AGS.Types.Font donor = game.Fonts.First(f => f != font && (f.ProjectFilename ?? "").EndsWith(".ttf"));
            string source = Path.Combine(_gameDir, "other.ttf");
            File.Copy(Path.Combine(_gameDir, donor.ProjectFilename), source);
            vm.PickFileSync = (title, type, ext) => source;
            vm.AskTtfSize = current => (false, 12);
            Row("SourceFilename").OpenEditorCommand.Execute(null);
            Assert.Equal("other.ttf", font.SourceFilename);
            Assert.Equal(12, font.PointSize);
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(Path.Combine(_gameDir, font.ProjectFilename)));
            FontFileUIEditor.FontFileGUI = null;   // static hooks outlive this test
            FontSizeUIEditor.FontSizeGUI = null;
        }

        [AvaloniaFact]
        public void SpeechNumberingVoiceScriptAndMakeDefaultLanguage()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            Game game = vm.Project.Game;
            ProjectTreeNode roomNode = vm.TreeRoots[0].Children.Single(n => n.Name == "Rooms").Children.First(n => ((UnloadedRoom)n.Payload).Number == 2);
            vm.OpenNode(roomNode);
            Assert.IsType<RoomEditorDocument>(vm.SelectedDocument);
            Script global = game.RootScriptFolder.GetScriptByFileName("GlobalScript.asc", true);
            vm.OpenScriptAt(global, 0, 0);
            Assert.True(vm.AutoNumberSpeechCommand.CanExecute(null));

            // File → Auto-number speech lines (the game has translations: the warning is confirmed)
            vm.AskSpeechNumbering = g => (true, true, false, null);
            vm.AutoNumberSpeechCommand.Execute(null);
            Assert.DoesNotContain(vm.Documents, d => d is RoomEditorDocument);           // rooms were rewritten on disk
            var scriptDoc = Assert.Single(vm.Documents.OfType<ScriptDocument>());
            Assert.Same(global, scriptDoc.Script);
            Assert.Matches(@"Say\(""&\d+ ", File.ReadAllText(Path.Combine(_gameDir, "room2.asc"), AnsiEncoding.Default));
            Assert.Contains("Speech lines numbered successfully.", vm.OutputText);
            Assert.False(vm.IsModified);

            // File → Create voice acting script
            string voice = Path.Combine(_gameDir, "VoiceSpeech.txt");
            vm.PickSaveFileSync = (title, suggested, type, ext) => suggested;
            vm.CreateVoiceActingScriptCommand.Execute(null);
            Assert.Contains("Voice acting script created successfully", vm.OutputText);
            Assert.Contains(File.ReadAllLines(voice), l => l.StartsWith("*** Lines for character "));

            // Translations → english → Make default language
            ProjectTreeNode english = vm.TreeRoots[0].Children.Single(n => n.Name == "Translations").Children
                .Single(n => n.Payload is Translation t && t.FileName.Equals("english.trs", StringComparison.OrdinalIgnoreCase));
            english.Actions.Single(a => a.Header == "Make default language").Command.Execute(null);
            Assert.Contains("Game text replaced with the translation text.", vm.OutputText);
            var toolchain = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            var result = toolchain.Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.True(result.Succeeded, TestPaths.BuildFailure(result));
        }

        [AvaloniaFact]
        public async System.Threading.Tasks.Task DragAndDropMovesItemsFoldersScriptsAndSprites()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            Game game = vm.Project.Game;
            ProjectTreeNode Node(object payload) => vm.TreeRoots[0].Descendants().First(n => (n.Payload ?? n.Folder) == payload);
            ProjectTreeNode Section(string name) => vm.TreeRoots[0].Children.Single(n => n.Name == name);

            // a character folder; a character into it; another one before the first in the root
            vm.PromptText = (t, v) => "Heroes";
            Section("Characters").Actions.Single(a => a.Header == "New folder").Command.Execute(null);
            CharacterFolder heroes = game.RootCharacterFolder.SubFolders.Single(f => f.Name.StartsWith("New folder") || f.Name == "Heroes");
            Character first = game.RootCharacterFolder.Items[0], third = game.RootCharacterFolder.Items[2];
            Assert.True(vm.DropNode(Node(third), Node(heroes), TargetDropZone.MiddleTop));
            Assert.Contains(third, heroes.Items);
            Assert.Equal(third.ID, game.Characters.Single(c => c == third).ID); // IDs don't change
            Character second = game.RootCharacterFolder.Items[1];
            Assert.True(vm.DropNode(Node(second), Node(first), TargetDropZone.Top));
            Assert.Same(second, game.RootCharacterFolder.Items[0]);

            // refused: a folder into itself, a character onto a view, an item into its own folder
            Assert.False(vm.DropNode(Node(heroes), Node(heroes), TargetDropZone.MiddleTop, apply: false));
            Assert.False(vm.DropNode(Node(first), Node(game.RootViewFolder.AllItemsFlat.First()), TargetDropZone.MiddleTop, apply: false));
            Assert.False(vm.DropNode(Node(third), Node(heroes), TargetDropZone.MiddleTop, apply: false));

            // script order: the last module before the first (the compile order)
            var modules = game.RootScriptFolder.AllItemsFlat.ToList();
            ScriptAndHeader last = modules.Last(m => m.Script.FileName != "GlobalScript.asc");
            ScriptAndHeader firstModule = modules.First();
            Assert.True(vm.DropNode(Node(last), Node(firstModule), TargetDropZone.Top));
            Assert.Same(last, game.RootScriptFolder.AllItemsFlat.First());
            Assert.True(vm.DropNode(Node(last), Node(firstModule), TargetDropZone.Bottom)); // and back after it

            // sprites onto a sprite folder; image files dropped into a folder
            var spriteFolder = new SpriteFolder("Dropped");
            game.RootSpriteFolder.SubFolders.Add(spriteFolder);
            Sprite[] two = game.RootSpriteFolder.Sprites.Take(2).ToArray();
            vm.DropSpritesOnFolder(two, spriteFolder);
            Assert.Equal(two, spriteFolder.Sprites.ToArray());
            string png = Path.Combine(_gameDir, "dropped.png");
            using (var image = new AGS.Imaging.PixelImage(4, 4, AGS.Imaging.ImagePixelFormat.Bgra32))
            {
                image.Pixels.Fill(90);
                new AGS.Native.NativeImageCodec().Encode(image, png);
            }
            await vm.ImportImageFilesAsync(spriteFolder, new[] { png, Path.Combine(_gameDir, "Game.agf") }); // the .agf is ignored
            Assert.True(two.Length + 1 == spriteFolder.Sprites.Count, vm.OutputText);

            vm.SaveCommand.Execute(null);
            var result = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory).Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.True(result.Succeeded, TestPaths.BuildFailure(result));
            var reloaded = GameProject.Load(Path.Combine(_gameDir, "Game.agf"), new AGS.Editor.Hosting.ConsoleEditorHost(TextWriter.Null));
            Assert.Contains(reloaded.Game.RootCharacterFolder.SubFolders.SelectMany(f => f.Items), c => c.ID == third.ID);
        }

        [AvaloniaFact]
        public void DeleteFoldersWithTheirContents()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            Game game = vm.Project.Game;
            ProjectTreeNode Node(object payload) => vm.TreeRoots[0].Descendants().First(n => (n.Payload ?? n.Folder) == payload);
            int characters = game.Characters.Count;

            // a character folder with two new characters
            var folder = ProjectOperations.AddFolder<Character, CharacterFolder>(game.RootCharacterFolder, n => new CharacterFolder(n));
            ProjectOperations.AddCharacter(game, folder);
            ProjectOperations.AddCharacter(game, folder);
            vm.RebuildTree();
            Assert.Equal(characters + 2, game.Characters.Count);
            Node(folder).Actions.Single(a => a.Header == "Delete folder").Command.Execute(null); // Confirm unset = yes
            Assert.DoesNotContain(folder, game.RootCharacterFolder.SubFolders);
            Assert.Equal(characters, game.Characters.Count);
            Assert.Equal(Enumerable.Range(0, characters), game.Characters.Select(c => c.ID).OrderBy(i => i));

            // a view folder with a view in use is refused, nothing is deleted
            View used = game.RootViewFolder.AllItemsFlat.First(v => ProjectOperations.GetViewUsageReport(game, v.ID) != null);
            var viewFolder = ProjectOperations.AddFolder<View, ViewFolder>(game.RootViewFolder, n => new ViewFolder(n));
            FolderDragDrop<View, ViewFolder>.Drop(game.RootViewFolder, used, viewFolder, TargetDropZone.MiddleTop);
            vm.RebuildTree();
            int views = game.RootViewFolder.AllItemsFlat.Count();
            Node(viewFolder).Actions.Single(a => a.Header == "Delete folder").Command.Execute(null);
            Assert.Contains(viewFolder, game.RootViewFolder.SubFolders);
            Assert.Equal(views, game.RootViewFolder.AllItemsFlat.Count());

            vm.SaveCommand.Execute(null);
            var result = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory).Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.True(result.Succeeded, TestPaths.BuildFailure(result));
        }

        [AvaloniaFact]
        public void ImportExistingRoomFromAnotherFolder()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            ProjectTreeNode Rooms() => vm.TreeRoots[0].Children.Single(n => n.Name == "Rooms");
            var source = (UnloadedRoom)Rooms().Children.First(n => n.Payload is UnloadedRoom).Payload;

            // The same room number already exists: refused with upstream's message.
            string other = Path.Combine(_gameDir, "other");
            Directory.CreateDirectory(other);
            File.Copy(Path.Combine(_gameDir, source.FileName), Path.Combine(other, source.FileName));
            File.Copy(Path.Combine(_gameDir, source.ScriptFileName), Path.Combine(other, source.ScriptFileName));
            vm.PickOpenFiles = (title, ext) => System.Threading.Tasks.Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(new[] { Path.Combine(other, source.FileName) });
            Rooms().Actions.Single(a => a.Header == "Import existing room...").Command.Execute(null);
            Assert.Contains("already has a room " + source.Number, vm.OutputText);

            // Renamed: imported under the first free number, script copied along.
            string renamed = Path.Combine(other, "kitchen.crm");
            File.Move(Path.Combine(other, source.FileName), renamed);
            File.Move(Path.Combine(other, source.ScriptFileName), Path.ChangeExtension(renamed, ".asc"));
            int expected = vm.Project.Game.FindFirstAvailableRoomNumber(1);
            vm.PickOpenFiles = (title, ext) => System.Threading.Tasks.Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(new[] { renamed });
            Rooms().Actions.Single(a => a.Header == "Import existing room...").Command.Execute(null);
            var imported = Assert.IsType<UnloadedRoom>(vm.SelectedNode.Payload);
            Assert.Equal(expected, imported.Number);
            Assert.Equal(File.ReadAllBytes(renamed), File.ReadAllBytes(Path.Combine(_gameDir, imported.FileName)));
            Assert.Equal(File.ReadAllText(Path.ChangeExtension(renamed, ".asc")), File.ReadAllText(Path.Combine(_gameDir, imported.ScriptFileName)));

            vm.SaveCommand.Execute(null);
            var toolchain = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            var result = toolchain.Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.True(result.Succeeded, TestPaths.BuildFailure(result));
        }

        [AvaloniaFact]
        public void NewAndDeleteCursors()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            Game game = vm.Project.Game;
            ProjectTreeNode Cursors() => vm.TreeRoots[0].Children.Single(n => n.Name == "Mouse Cursors");
            int count = game.Cursors.Count;

            Cursors().Actions.Single(a => a.Header == "New Cursor").Command.Execute(null);
            var added = Assert.IsType<MouseCursor>(vm.SelectedNode.Payload);
            Assert.Equal((count, "Cursor" + count), (added.ID, added.Name));
            Assert.Equal(count + 1, Cursors().Children.Count);

            // Built-in cursors (0-7) have no delete action; deleting a custom one renumbers the later ones.
            Assert.DoesNotContain(Cursors().Children[7].Actions, a => a.Header == "Delete this cursor");
            Cursors().Actions.Single(a => a.Header == "New Cursor").Command.Execute(null);
            MouseCursor last = game.Cursors.Last();
            Cursors().Children.Single(n => n.Payload == added).Actions.Single(a => a.Header == "Delete this cursor").Command.Execute(null);
            Assert.Equal(count, last.ID);
            Assert.Equal(count + 1, game.Cursors.Count);

            vm.SaveCommand.Execute(null);
            var toolchain = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            var result = toolchain.Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.True(result.Succeeded, TestPaths.BuildFailure(result));
        }

        [AvaloniaFact]
        public void NewUpdateRenameAndDeleteTranslation()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            ProjectTreeNode Translations() => vm.TreeRoots[0].Children.Single(n => n.Name == "Translations");

            vm.PromptText = (title, value) => "Deutsch";
            Translations().Actions.Single(a => a.Header == "New translation").Command.Execute(null); // Confirm unset = update now
            var created = Assert.IsType<Translation>(vm.SelectedNode?.Payload ?? Translations().Children.Single(n => ((Translation)n.Payload).Name == "Deutsch").Payload);
            string trs = Path.Combine(_gameDir, "Deutsch.trs");
            Assert.True(File.Exists(trs));
            Assert.Contains("Ach nö, will heute keine Türen schleppen.", File.ReadAllText(trs) /* new translations are UTF-8 (Translation.EncodingHint) */);
            Assert.Contains("Translation(s) updated", vm.OutputText);

            var toolchain = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            var result = toolchain.Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.True(result.Succeeded, TestPaths.BuildFailure(result));
            Assert.True(File.Exists(Path.Combine(_gameDir, "Compiled", "Data", "Deutsch.tra")));

            vm.PromptText = (title, value) => "German";
            Translations().Children.Single(n => n.Payload == created).Actions.Single(a => a.Header == "Rename...").Command.Execute(null);
            Assert.False(File.Exists(trs));
            Assert.True(File.Exists(Path.Combine(_gameDir, "German.trs")));

            Translations().Children.Single(n => n.Payload == created).Actions.Single(a => a.Header == "Delete").Command.Execute(null);
            Assert.False(File.Exists(Path.Combine(_gameDir, "German.trs")));
            Assert.DoesNotContain(vm.Project.Game.Translations, t => t == created);
        }

        [AvaloniaFact]
        public async System.Threading.Tasks.Task MakeTemplateAndStartANewGameFromIt()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));

            string agt = Path.Combine(_gameDir, "..", Path.GetFileName(_gameDir) + "-template.agt");
            vm.PickSaveFile = (name, ext) => System.Threading.Tasks.Task.FromResult(agt);
            await vm.MakeTemplateCommand.ExecuteAsync(null);
            Assert.True(File.Exists(agt));

            string parent = _gameDir + "-new";
            Directory.CreateDirectory(parent);
            try
            {
                vm.PickOpenFiles = (title, ext) => System.Threading.Tasks.Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(new[] { agt });
                vm.PickFolder = title => System.Threading.Tasks.Task.FromResult(parent);
                vm.PromptText = (title, value) => "Bernards Abenteuer";
                await vm.NewGameFromTemplateCommand.ExecuteAsync(null);

                Assert.Equal("Bernards Abenteuer", vm.Project.Game.Settings.GameName);
                Assert.Equal("BernardsAbenteuer", vm.Project.Game.Settings.GameFileName);
                Assert.Equal(Path.GetFullPath(Path.Combine(parent, "Bernards Abenteuer")), Path.GetFullPath(vm.Project.Directory));
                Assert.Equal("Bernards Abenteuer", vm.TreeRoots[0].Name);
            }
            finally
            {
                Directory.SetCurrentDirectory(_originalCwd);
                Directory.Delete(parent, true);
                File.Delete(agt);
            }
        }

        [AvaloniaFact]
        public async System.Threading.Tasks.Task BuildMacAppFromTheMenu()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            await vm.BuildMacAppCommand.ExecuteAsync(null);
            Assert.NotNull(vm.LastMacApp);
            Assert.True(File.Exists(Path.Combine(vm.LastMacApp, "Contents", "Resources", "Bernard-SP.ags")));
            Assert.Contains("macOS app created", vm.OutputText);
        }
    }
}
