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
    public class DialogEditorTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-dlg-ed-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        [AvaloniaFact]
        public void EditOptionsAndScriptOfADialog()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            Game game = vm.Project.Game;

            // The reference game has no dialogs: create one with the tree action, then double-click it.
            Assert.Empty(game.RootDialogFolder.AllItemsFlat);
            vm.TreeRoots[0].Children.Single(n => n.Name == "Dialogs").Actions.Single(a => a.Header == "New dialog").Command.Execute(null);
            Dialog dialog = Assert.Single(game.RootDialogFolder.AllItemsFlat);
            Assert.Contains("\r\n", dialog.Script); // as the Windows editor creates it
            vm.OpenNode(vm.TreeRoots[0].Descendants().Single(n => n.Payload == dialog));
            var doc = Assert.IsType<DialogEditorDocument>(vm.SelectedDocument);
            Assert.Equal(dialog.Options.Count, doc.Options.Count);
            TestPaths.Settle(window);
            var view = window.GetVisualDescendants().OfType<DialogEditorView>().Single();
            Assert.Equal(dialog.Script, view.ScriptText);

            // Two options with entry points.
            doc.NewOption();
            doc.NewOption();
            Assert.Equal(2, dialog.Options.Count);
            Assert.True(dialog.Options[0].Show && dialog.Options[0].Say);

            // Option text and flags edit the dialog.
            doc.Options[0].Text = "Wie geht's?";
            doc.Options[0].Say = !doc.Options[0].Say;
            Assert.Equal("Wie geht's?", dialog.Options[0].Text);
            Assert.True(vm.IsModified);

            // A new option gets an entry point in the script (with the script's line breaks) and shows in the editor.
            int count = dialog.Options.Count;
            doc.NewOption();
            DialogOption added = dialog.Options.Last();
            Assert.Equal(count + 1, added.ID);
            string nl = DialogOperations.LineBreak(dialog.Script);
            Assert.Contains(nl + "@" + added.ID + nl + "return" + nl, dialog.Script);
            Assert.Equal(dialog.Script, view.ScriptText);
            added.Text = "Tschüss";
            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-dialog-editor.png"));

            vm.SaveCommand.Execute(null);
            var toolchain = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            var result = toolchain.Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.True(result.Succeeded, TestPaths.BuildFailure(result));

            doc.DeleteLastOption(); // Confirm unset = yes
            Assert.Equal(count, dialog.Options.Count);
            vm.SaveCommand.Execute(null);
            var reloaded = GameProject.Load(Path.Combine(_gameDir, "Game.agf"), new AGS.Editor.Hosting.ConsoleEditorHost(TextWriter.Null));
            Dialog saved = reloaded.Game.RootDialogFolder.AllItemsFlat.Single(d => d.ID == dialog.ID);
            Assert.Equal("Wie geht's?", saved.Options[0].Text);
            Assert.Contains("@" + (count + 1), saved.Script);
        }

        [AvaloniaFact]
        public void ErrorsInDialogScriptsLeadToTheDialog()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            vm.TreeRoots[0].Children.Single(n => n.Name == "Dialogs").Actions.Single(a => a.Header == "New dialog").Command.Execute(null);
            Dialog dialog = Assert.Single(vm.Project.Game.RootDialogFolder.AllItemsFlat);
            DialogOperations.AddOption(dialog);
            string nl = DialogOperations.LineBreak(dialog.Script);
            dialog.Script = dialog.Script.Replace("@1" + nl + "return", "@1" + nl + "  undefinedFunction();" + nl + "return");
            Assert.Contains("undefinedFunction", dialog.Script);
            vm.SaveCommand.Execute(null);

            var toolchain = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            var result = toolchain.Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.False(result.Succeeded);
            var error = Assert.Single(result.Messages.OfType<CompileError>());
            Assert.Equal(("Dialog 0", 5), (error.ScriptName, error.LineNumber)); // agsbuild keeps dialog script lines

            // Going to the error opens the dialog at that line.
            vm.GoToMessage(error);
            var doc = Assert.IsType<DialogEditorDocument>(vm.SelectedDocument);
            Assert.Equal(5, doc.ErrorLine);
            TestPaths.Settle(window);
            var view = window.GetVisualDescendants().OfType<DialogEditorView>().Single();
            Assert.Equal(5, view.SelectedLine);
        }

        [AvaloniaFact]
        public void GlobalVariablesAddEditDeleteAndAutocomplete()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            Game game = vm.Project.Game;
            vm.OpenNode(vm.TreeRoots[0].Children.Single(n => n.Name == "Global Variables"));
            var doc = Assert.IsType<GlobalVariablesDocument>(vm.SelectedDocument);
            int count = doc.Variables.Count;

            // Upstream's validation.
            var types = GlobalVariableEditing.VariableTypes(GlobalVariableEditing.ApiTypes());
            Assert.Contains(types, t => t.TypeName == "Character*");
            Assert.Contains(types, t => t.TypeName == "String");
            var intType = types.Single(t => t.TypeName == "int");
            var probe = new GlobalVariable();
            Assert.Contains("not valid for this type", GlobalVariableEditing.Apply(game, probe, "gCoins", intType, "abc", GlobalVariableArrayType.None, 0));
            Assert.Contains("not a valid variable name", GlobalVariableEditing.Apply(game, probe, "1coins", intType, "", GlobalVariableArrayType.None, 0));
            Assert.Contains("already being used", GlobalVariableEditing.Apply(game, probe, "cBernard", intType, "", GlobalVariableArrayType.None, 0));

            // Add through the document (dialog replaced by the hook).
            vm.EditGlobalVariable = v => GlobalVariableEditing.Apply(game, v, "gCoins", intType, "5", GlobalVariableArrayType.None, 0) == null;
            vm.GlobalVariableActionCommand.Execute("add");
            Assert.Equal(count + 1, doc.Variables.Count);
            Assert.Equal("gCoins", doc.Selected.Name);
            Assert.True(vm.IsModified);
            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-global-variables.png"));

            // Scripts see it in autocomplete.
            var completion = new ScriptCompletion(game);
            Script global = game.RootScriptFolder.GetScriptByFileName("GlobalScript.asc", true);
            completion.Update(global);
            Assert.Contains(completion.GetGlobal(global, "gCo"), c => c.Text == "gCoins");

            // Rename keeps one entry; then build and delete.
            vm.EditGlobalVariable = v => GlobalVariableEditing.Apply(game, v, "gMoney", intType, "7", GlobalVariableArrayType.None, 0) == null;
            vm.GlobalVariableActionCommand.Execute("edit");
            Assert.Single(game.GlobalVariables.ToList(), v => v.Name == "gMoney");
            Assert.DoesNotContain(game.GlobalVariables.ToList(), v => v.Name == "gCoins");
            vm.SaveCommand.Execute(null);
            var toolchain = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            Assert.True(toolchain.Build(Path.Combine(_gameDir, "Game.agf")).Succeeded);
            vm.GlobalVariableActionCommand.Execute("delete");
            Assert.Equal(count, doc.Variables.Count);
        }
    }
}
