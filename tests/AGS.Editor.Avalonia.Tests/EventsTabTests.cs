using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AGS.Editor.Avalonia.ViewModels;
using AGS.Editor.Avalonia.Views;
using AGS.Types;

namespace AGS.Editor.Avalonia.Tests
{
    public class EventsTabTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-events-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        [AvaloniaFact]
        public void CreatesAndOpensEventHandlerFromEventsTab()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            vm.InstallScriptHooks();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));

            ProjectTreeNode inventory = vm.TreeRoots[0].Children.Single(n => n.Name == "Inventory Items");
            vm.SelectedNode = inventory.Children.First();
            var item = Assert.IsType<InventoryItem>(vm.SelectedNode.Payload);

            Assert.True(vm.PropertyGrid.HasTabs);
            Assert.Equal(new[] { "Properties", "Events" }, vm.PropertyGrid.Tabs);
            vm.PropertyGrid.SelectedTabIndex = 1;

            // Event names use the game's cursor names (e.g. "Look at", "Interact") instead of $$01..
            var rows = vm.PropertyGrid.Entries.OfType<TextPropertyRow>().ToList();
            Assert.NotEmpty(rows);
            Assert.DoesNotContain(rows, r => r.Name.Contains("$$0"));
            TextPropertyRow empty = rows.First(r => string.IsNullOrEmpty(r.Text) && r.HasEditorButton);

            empty.OpenEditorCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            string function = empty.Text;
            Assert.StartsWith(item.Name + "_", function);
            Script global = vm.FindScript("GlobalScript.asc");
            Assert.Contains($"function {function}(", global.Text);
            var doc = Assert.IsType<ScriptDocument>(vm.SelectedDocument);
            Assert.Same(global, doc.Script);
            Assert.True(vm.IsModified);

            // The editor scrolled to the new function, and ScriptModule shows the module (not "[unknown]").
            TestPaths.Settle(window);
            Dispatcher.UIThread.RunJobs();
            var editorView = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<ScriptEditorView>().Single();
            int caretLine = editorView.TextEditor.Document.GetLineByOffset(editorView.TextEditor.CaretOffset).LineNumber;
            int functionLine = editorView.TextEditor.Document.GetLineByOffset(global.Text.IndexOf($"function {function}(")).LineNumber;
            Assert.InRange(caretLine, functionLine, functionLine + 2);
            Assert.DoesNotContain("[unknown]", vm.PropertyGrid.Entries.OfType<PropertyRow>().Select(r => (r as TextPropertyRow)?.Text ?? (r as ChoicePropertyRow)?.Selected?.Text ?? ""));

            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-events-tab.png"));
        }
    }
}
