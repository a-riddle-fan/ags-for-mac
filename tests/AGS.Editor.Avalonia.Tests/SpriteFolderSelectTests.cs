using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using AGS.Editor.Avalonia.ViewModels;
using AGS.Editor.Avalonia.Views;
using AGS.Types;

namespace AGS.Editor.Avalonia.Tests
{
    public class SpriteFolderSelectTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-sprsel-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        [AvaloniaFact]
        public void SelectingSpriteNodesDoesNotHang()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            ProjectTreeNode sprites = vm.TreeRoots[0].Children.Single(n => n.Name == "Sprites");
            vm.SelectedNode = sprites.Children.First();
            // List properties show "(Collection)" read-only, as in the WinForms grid.
            var spritesRow = vm.PropertyGrid.Entries.OfType<TextPropertyRow>().Single(r => r.Name == "Sprites");
            Assert.Equal("(Collection)", spritesRow.Text);
            Assert.False(spritesRow.CanEditText);
            Assert.DoesNotContain(vm.PropertyGrid.Entries, e => e is ChoicePropertyRow);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            vm.SelectedNode = sprites;
            TestPaths.Settle(window);
            Assert.True(sw.ElapsedMilliseconds < 2000);
            var all = new System.Collections.Generic.List<ProjectTreeNode>();
            void Walk(ProjectTreeNode n) { all.Add(n); foreach (var c in n.Children) Walk(c); }
            Walk(sprites);
            foreach (ProjectTreeNode folder in all)
            {
                sw.Restart();
                vm.SelectedNode = folder;
                TestPaths.Settle(window);
                long sel = sw.ElapsedMilliseconds;
                sw.Restart();
                vm.OpenNode(folder);
                TestPaths.Settle(window);
                long open = sw.ElapsedMilliseconds;
                sw.Restart();
                foreach (var row in vm.PropertyGrid.Entries.OfType<ChoicePropertyRow>())
                    _ = row.Choices.Count;
                Assert.True(sel + open < 2000, $"{folder.Path}: select {sel} ms, open {open} ms");
            }
        }
    }
}
