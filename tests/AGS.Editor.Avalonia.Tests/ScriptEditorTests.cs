using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AGS.Editor.Avalonia.Services;
using AGS.Editor.Avalonia.ViewModels;
using AGS.Editor.Avalonia.Views;
using AGS.Types;

namespace AGS.Editor.Avalonia.Tests
{
    public class ScriptEditorTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-script-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        [Fact]
        public void HighlightingKnowsBuiltInAndGameTypes()
        {
            var names = AgsScriptHighlighting.CollectTypeNames(null);
            Assert.Contains("Character", names);
            Assert.Contains("Room", names);
            Assert.Contains("CharacterDirection", names);
            Assert.NotNull(AgsScriptHighlighting.Create(names));
        }

        [AvaloniaFact]
        public void EditsAndSavesGlobalScript()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));

            ProjectTreeNode scripts = vm.TreeRoots[0].Children.Single(n => n.Name == "Scripts");
            vm.OpenNode(scripts.Find("GlobalScript.asc"));
            TestPaths.Settle(window);

            ScriptEditorView view = window.GetVisualDescendants().OfType<ScriptEditorView>().Single();
            Assert.NotNull(view.TextEditor.SyntaxHighlighting);
            Assert.Contains("function game_start ()", view.TextEditor.Text);

            view.TextEditor.Document.Insert(0, "// edited on macOS\n");
            Assert.True(vm.IsModified);

            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-script-editor.png"));

            vm.SaveCommand.Execute(null);
            Assert.False(vm.IsModified);
            string onDisk = File.ReadAllText(Path.Combine(_gameDir, "GlobalScript.asc"), AnsiEncoding.Default);
            Assert.StartsWith("// edited on macOS\n", onDisk);
        }

        [AvaloniaFact]
        public void CompletionPopupShowsCharacterMembers()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            vm.OpenNode(vm.TreeRoots[0].Children.Single(n => n.Name == "Scripts").Find("GlobalScript.asc"));
            TestPaths.Settle(window);

            ScriptEditorView view = window.GetVisualDescendants().OfType<ScriptEditorView>().Single();
            var doc = view.TextEditor.Document;
            doc.Insert(doc.TextLength, "\nfunction ags4mac_demo()\n{\n  player.");
            view.TextEditor.CaretOffset = doc.TextLength;
            view.TextEditor.ScrollToEnd();
            view.ShowCompletion(memberAccess: true, force: false);

            Assert.NotNull(view.CompletionWindow);
            var entries = view.CompletionWindow.CompletionList.CompletionData.Select(d => d.Text).ToList();
            Assert.Contains("Say", entries);
            Assert.Contains("Walk", entries);

            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-autocomplete.png"));
            view.CompletionWindow.Close();
        }

        [AvaloniaFact]
        public void CallTipShowsTheCurrentParameterAndEnumValues()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            vm.OpenNode(vm.TreeRoots[0].Children.Single(n => n.Name == "Scripts").Find("GlobalScript.asc"));
            TestPaths.Settle(window);

            ScriptEditorView view = window.GetVisualDescendants().OfType<ScriptEditorView>().Single();
            var doc = view.TextEditor.Document;
            doc.Insert(doc.TextLength, "\nfunction ags4mac_demo()\n{\n  player.Walk(100, ");
            view.TextEditor.CaretOffset = doc.TextLength;
            view.TextEditor.ScrollToEnd();
            view.ShowCallTip();
            Assert.NotNull(view.CurrentCallTip);
            Assert.NotNull(view.CallTipWindow);
            Assert.Equal(1, view.CurrentCallTip.CurrentParameter);
            Assert.StartsWith("void Character.Walk(", view.CurrentCallTip.Text);
            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-calltip.png"));

            // The blocking parameter is an enum: its values are offered.
            doc.Insert(doc.TextLength, "50, ");
            view.TextEditor.CaretOffset = doc.TextLength;
            view.ShowCallTip();
            Assert.NotNull(view.CompletionWindow);
            Assert.Contains("eBlock", view.CompletionWindow.CompletionList.CompletionData.Select(d => d.Text));
            view.CompletionWindow.Close();
        }

        [AvaloniaFact]
        public async System.Threading.Tasks.Task DebuggerStopsAtABreakpointInTheEditor()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            vm.OpenNode(vm.TreeRoots[0].Children.Single(n => n.Name == "Scripts").Find("GlobalScript.asc"));
            var doc = Assert.IsType<ScriptDocument>(vm.SelectedDocument);
            doc.ToggleBreakpoint(1624); // in game_start
            Assert.Contains(1624, doc.Script.BreakpointedLines);

            void WaitFor(Func<bool> condition, int seconds)
            {
                var until = DateTime.UtcNow.AddSeconds(seconds);
                while (!condition() && DateTime.UtcNow < until)
                {
                    global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    System.Threading.Thread.Sleep(20);
                }
            }

            Environment.SetEnvironmentVariable("SDL_VIDEODRIVER", "dummy");
            Environment.SetEnvironmentVariable("SDL_AUDIODRIVER", "dummy");
            try
            {
                await vm.DebugCommand.ExecuteAsync(null);
                WaitFor(() => vm.DebugState == AGS.Editor.Build.DebugSessionState.Paused, 60);
                Assert.Equal(AGS.Editor.Build.DebugSessionState.Paused, vm.DebugState);
                var stopped = Assert.IsType<ScriptDocument>(vm.SelectedDocument);
                Assert.Equal("GlobalScript.asc", stopped.Script.FileName);
                Assert.Equal(1624, stopped.ExecutionLine);
                Assert.Contains("Call stack:", vm.OutputText);
                TestPaths.Settle(window);
                window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase4-debugger.png"));

                vm.StepIntoCommand.Execute(null);
                WaitFor(() => vm.DebugState == AGS.Editor.Build.DebugSessionState.Paused && (vm.SelectedDocument as ScriptDocument)?.ExecutionLine != 1624, 30);
                Assert.NotEqual(1624, (vm.SelectedDocument as ScriptDocument)?.ExecutionLine);

                vm.StopDebuggingCommand.Execute(null);
                WaitFor(() => vm.DebugState == AGS.Editor.Build.DebugSessionState.NotRunning, 20);
                Assert.Equal(AGS.Editor.Build.DebugSessionState.NotRunning, vm.DebugState);
                Assert.Null(stopped.ExecutionLine);
            }
            finally
            {
                Environment.SetEnvironmentVariable("SDL_VIDEODRIVER", null);
                Environment.SetEnvironmentVariable("SDL_AUDIODRIVER", null);
            }
        }
    }
}
