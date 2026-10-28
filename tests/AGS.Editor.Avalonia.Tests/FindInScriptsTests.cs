using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AGS.Editor.Avalonia.ViewModels;
using AGS.Editor.Avalonia.Views;
using AGS.Types;

namespace AGS.Editor.Avalonia.Tests
{
    public class FindInScriptsTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-find-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        [AvaloniaFact]
        public void FindAllOpensResultsAndReplaceAllSavesRoomScripts()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));

            vm.FindInScriptsCommand.Execute(null);
            var doc = Assert.IsType<FindInScriptsDocument>(vm.SelectedDocument);
            doc.SearchText = "ChangeRoom";
            doc.MatchCase = true;
            doc.FindAll();
            Assert.NotEmpty(doc.Results);
            Assert.Contains("occurrence", doc.Summary);
            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-find-in-scripts.png"));

            // Opening a result selects the match in its script.
            ScriptSearchResult inRoom = doc.Results.First(r => r.FileName.StartsWith("room", StringComparison.OrdinalIgnoreCase));
            doc.Open(inRoom);
            var scriptDoc = Assert.IsType<ScriptDocument>(vm.SelectedDocument);
            Assert.Same(inRoom.Script, scriptDoc.Script);
            TestPaths.Settle(window);
            var editor = window.GetVisualDescendants().OfType<ScriptEditorView>().Single().TextEditor;
            Assert.Equal("ChangeRoom", editor.SelectedText);

            // Replace All changes room scripts too, and Save writes them.
            vm.FindInScriptsCommand.Execute(null);
            doc.ReplaceText = "ChangeRoomAutoPosition";
            doc.ReplaceAll(); // Confirm unset = yes
            Assert.Contains("replaced", doc.Summary);
            vm.SaveCommand.Execute(null);
            string roomFile = Path.Combine(_gameDir, inRoom.FileName);
            Assert.Contains("ChangeRoomAutoPosition", File.ReadAllText(roomFile, AnsiEncoding.Default));
        }

        [AvaloniaFact]
        public void F12GoesToTheDefinitionInAnotherScript()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            Game game = vm.Project.Game;
            ScriptAndHeader doors = game.RootScriptFolder.AllItemsFlat.Single(m => m.Name == "Doors");
            AutoComplete.ConstructCache(doors.Header, null);
            string function = doors.Header.AutoCompleteData.Functions.First().FunctionName;
            // A room script that uses the Doors module's function.
            ScriptSearchResult use = ScriptSearch.FindAll(ScriptSearch.AllScripts(game), function + "(", matchCase: true)
                .First(r => r.Script != doors.Script && r.Script != doors.Header);
            vm.OpenScriptAt(use.Script, use.Offset, 0);
            var doc = Assert.IsType<ScriptDocument>(vm.SelectedDocument);

            doc.RequestDefinition(use.Script.Text, use.Offset + 1);
            var target = Assert.IsType<ScriptDocument>(vm.SelectedDocument);
            Assert.Same(doors.Script, target.Script);
            TestPaths.Settle(window);
            var editor = window.GetVisualDescendants().OfType<ScriptEditorView>().Single().TextEditor;
            Assert.Equal(function, editor.SelectedText);
        }
    
        [AvaloniaFact]
        public void FindAllUsagesFromTheTreeAndTheScriptEditor()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            // the character used most often (one usage would jump straight to it, as upstream)
            Game game = vm.Project.Game;
            Character player = game.RootCharacterFolder.AllItemsFlat
                .OrderByDescending(c => ScriptSearch.FindUsages(ScriptSearch.AllScripts(game), c.ScriptName).Count).First();

            // Tree: right-click a character → "Find All Usages of cX"
            ProjectTreeNode node = Flatten(vm.TreeRoots).Single(n => n.Payload == player);
            TreeAction action = node.Actions.Single(a => a.Header == "Find All Usages of " + player.ScriptName);
            action.Command.Execute(null);
            var find = Assert.IsType<FindInScriptsDocument>(vm.SelectedDocument);
            Assert.True(find.Results.Count > 1);
            Assert.Contains("usage(s) of '" + player.ScriptName + "'", find.Summary);
            Assert.All(find.Results, r => Assert.Equal(player.ScriptName, r.Script.Text.Substring(r.Offset, player.ScriptName.Length)));
            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-find-all-usages.png"));

            // Script editor: ⇧F12 on a word lists its usages too
            ScriptSearchResult first = find.Results[0];
            find.Open(first);
            var scriptDoc = Assert.IsType<ScriptDocument>(vm.SelectedDocument);
            TestPaths.Settle(window);
            var editor = window.GetVisualDescendants().OfType<ScriptEditorView>().Single().TextEditor;
            editor.TextArea.Caret.Offset = first.Offset + 1;
            editor.TextArea.Focus();
            window.KeyPressQwerty(global::Avalonia.Input.PhysicalKey.F12, global::Avalonia.Input.RawInputModifiers.Shift);
            var again = Assert.IsType<FindInScriptsDocument>(vm.SelectedDocument);
            Assert.Equal(find.Results.Count, again.Results.Count);
            Assert.Equal("cEgo", ScriptDocument.WordAt("x = cEgo.x;", 6));
            Assert.Null(ScriptDocument.WordAt("x = 12;", 5));
        }

        [AvaloniaFact]
        public void ProjectCommandsAreEnabledAfterOpeningAProject()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            var debug = window.GetVisualDescendants().OfType<global::Avalonia.Controls.Button>().Single(b => b.Content is global::Avalonia.Controls.StackPanel panel
                && panel.Children.OfType<global::Avalonia.Controls.TextBlock>().Any(t => t.Text == "Debug"));
            TestPaths.Settle(window);
            Assert.False(debug.IsEffectivelyEnabled);

            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            TestPaths.Settle(window);
            Assert.True(debug.IsEffectivelyEnabled, "Debug stays disabled after opening a project");
            Assert.True(vm.FindInScriptsCommand.CanExecute(null));
            Assert.True(vm.MakeTemplateCommand.CanExecute(null));
        }

        [AvaloniaFact]
        public void ScriptsChangedOutsideTheEditorAreReloaded()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            Game game = vm.Project.Game;
            int questions = 0;
            vm.Confirm = (title, question) => { questions++; Assert.Contains("has been modified externally", question); return true; };
            ScriptAndHeader module = game.RootScriptFolder.AllItemsFlat.First(m => m.Script.FileName != "GlobalScript.asc");
            Script global = game.RootScriptFolder.GetScriptByFileName("GlobalScript.asc", true);
            void WriteExternally(Script script, string text) =>
                File.WriteAllBytes(Path.Combine(_gameDir, script.FileName), Script.TextEncoding.GetBytes(text));

            // not open, unedited: reloaded silently (here through the real file watcher)
            WriteExternally(module.Script, module.Script.Text + "\n// changed in VS Code\n");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!module.Script.Text.Contains("changed in VS Code") && watch.ElapsedMilliseconds < 5000)
            {
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(50);
            }
            Assert.Contains("changed in VS Code", module.Script.Text);
            Assert.Equal(0, questions);

            // open: the question waits until the window is active again
            vm.OpenScriptAt(global, 0, 0);
            vm.IsWindowActive = false;
            WriteExternally(global, global.Text + "\n// external edit\n");
            vm.OnScriptFileChanged("GlobalScript.asc");
            Assert.Equal(0, questions);
            vm.OnMainWindowActivated();
            Assert.Equal(1, questions);
            var doc = Assert.IsType<ScriptDocument>(vm.SelectedDocument);
            Assert.Contains("// external edit", doc.Script.Text);
            TestPaths.Settle(window);
            Assert.Contains("// external edit", window.GetVisualDescendants().OfType<ScriptEditorView>().Single().TextEditor.Text);

            // the editor's own save is not an external change
            vm.SaveCommand.Execute(null);
            vm.OnScriptFileChanged("GlobalScript.asc");
            Assert.Equal(1, questions);
        }

        [AvaloniaFact]
        public void OpeningAGameFromTheFinderAsksAboutUnsavedChanges()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            string agf = Path.Combine(_gameDir, "Game.agf");
            vm.OpenProjectFromFinder(agf);
            Assert.NotNull(vm.Project);
            GameProject first = vm.Project;

            int asked = 0;
            vm.Confirm = (title, question) => { asked++; return false; };
            vm.OpenProjectFromFinder(agf);                 // the same game: nothing happens
            Assert.Same(first, vm.Project);
            Assert.Equal(0, asked);

            string other = _gameDir + "-copy";
            TestPaths.CopyDirectory(_gameDir, other);
            try
            {
                vm.IsModified = true;
                vm.OpenProjectFromFinder(Path.Combine(other, "Game.agf"));   // declined: stays
                Assert.Equal(1, asked);
                Assert.Same(first, vm.Project);
                vm.Confirm = (title, question) => true;
                vm.OpenProjectFromFinder(Path.Combine(other, "Game.agf"));
                Assert.NotSame(first, vm.Project);
                Assert.StartsWith(other, vm.Project.Directory);
            }
            finally
            {
                Directory.SetCurrentDirectory(_originalCwd);
                vm.OpenProject(agf);
                Directory.Delete(other, true);
            }
        }

        [AvaloniaFact]
        public void FindAllUsagesIncludesDialogScripts()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            Game game = vm.Project.Game;
            Dialog dialog = (Dialog)ProjectOperations.AddDialog(game, game.RootDialogFolder);
            dialog.Script = "// Dialog script\r\n@S\r\n  cMark.Walk(10, 20);\r\n  // cMark in a comment\r\nreturn\r\n";

            vm.FindAllUsages("cMark");
            var find = Assert.IsType<FindInScriptsDocument>(vm.SelectedDocument);
            ScriptSearchResult inDialog = Assert.Single(find.Results, r => r.Dialog == dialog);
            Assert.Equal((3, "Dialog " + dialog.ID), (inDialog.Line, inDialog.FileName));

            find.Open(inDialog);
            var doc = Assert.IsType<DialogEditorDocument>(vm.SelectedDocument);
            Assert.Same(dialog, doc.Dialog);
            Assert.Equal(3, doc.PendingLine);
            TestPaths.Settle(window);
            var editor = window.GetVisualDescendants().OfType<DialogEditorView>().Single().GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().First();
            Assert.Equal("  cMark.Walk(10, 20);", editor.SelectedText);
        }

        private static System.Collections.Generic.IEnumerable<ProjectTreeNode> Flatten(System.Collections.Generic.IEnumerable<ProjectTreeNode> nodes) =>
            nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));
    }
}
