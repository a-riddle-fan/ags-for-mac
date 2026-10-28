using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AGS.Editor.Avalonia.ViewModels;
using AGS.Editor.Avalonia.Views;
using AGS.Types;

namespace AGS.Editor.Avalonia.Tests
{
    public class BuildErrorsTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-builderr-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        [AvaloniaFact]
        public async Task BuildErrorOpensScriptAtTheRightLine()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));

            // Break line 11 of Doors.asc in the editor (line 7 above it is blank: checks the LF line-number fix).
            vm.OpenNode(vm.TreeRoots[0].Children.Single(n => n.Name == "Scripts").Find("Doors.asc"));
            TestPaths.Settle(window);
            ScriptEditorView view = window.GetVisualDescendants().OfType<ScriptEditorView>().Single();
            var doc = view.TextEditor.Document;
            doc.Insert(doc.GetLineByNumber(11).Offset, "int broken = undefinedThing + ;\n");

            await vm.BuildCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            CompileMessage error = Assert.Single(vm.Errors);
            Assert.Equal(("Doors.asc", 11), (error.ScriptName, error.LineNumber));
            Assert.Contains("undefinedThing", error.Message);
            var shown = Assert.IsType<ScriptDocument>(vm.SelectedDocument);
            Assert.Equal("Doors.asc", shown.Script.FileName);
            Assert.Equal(11, shown.ErrorLine);

            TestPaths.Settle(window);
            Dispatcher.UIThread.RunJobs();
            view = window.GetVisualDescendants().OfType<ScriptEditorView>().Single();
            Assert.Equal(11, view.TextEditor.Document.GetLineByOffset(view.TextEditor.CaretOffset).LineNumber);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-build-error.png"));
        }
    }
}
