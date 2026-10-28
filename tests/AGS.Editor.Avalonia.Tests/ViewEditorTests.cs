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
    public class ViewEditorTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-view-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        [AvaloniaFact]
        public void EditFramesAndLoopsOfAView()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            Game game = vm.Project.Game;

            // Bernard's walking view.
            Character bernard = game.RootCharacterFolder.AllItemsFlat.Single(c => c.ScriptName == "cBernard");
            View view = game.FindViewByID(bernard.NormalView);
            ProjectTreeNode node = vm.TreeRoots[0].Descendants().Single(n => n.Payload == view);
            vm.OpenNode(node);
            var doc = Assert.IsType<ViewEditorDocument>(vm.SelectedDocument);
            Assert.Equal(view.Loops.Count, doc.Loops.Count);
            Assert.All(doc.Loops.SelectMany(l => l.Frames), t => Assert.NotNull(t.Image));

            // Select frame 1 of loop 0; insert after it: the new frame gets the next sprite of the folder.
            LoopRow loop0 = doc.Loops[0];
            int frames = loop0.Loop.Frames.Count;
            doc.SelectFrame(loop0.Frames[1]);
            Assert.Same(loop0.Loop.Frames[1], vm.PropertyGrid.Component);
            int previousSprite = loop0.Loop.Frames[1].Image;
            doc.InsertFrame(before: false);
            Assert.Equal(frames + 1, view.Loops[0].Frames.Count);
            Assert.Equal(Enumerable.Range(0, frames + 1), view.Loops[0].Frames.Select(f => f.ID));
            ViewFrame inserted = view.Loops[0].Frames[2];
            SpriteFolder folder = game.RootSpriteFolder.FindFolderThatContainsSprite(previousSprite);
            int nextInFolder = folder.Sprites[folder.Sprites.ToList().FindIndex(s => s.Number == previousSprite) + 1].Number;
            Assert.Equal(nextInFolder, inserted.Image);
            Assert.True(vm.IsModified);

            // Flip, choose a sprite by double-click, delete.
            doc.FlipSelectedFrame();
            Assert.True(view.Loops[0].Frames[2].Flipped);
            doc.PickSprite = current => 1;
            doc.ChooseSprite(doc.Loops[0].Frames[2]);
            Assert.Equal(1, view.Loops[0].Frames[2].Image);
            doc.DeleteSelectedFrame();
            Assert.Equal(frames, view.Loops[0].Frames.Count);

            // Loops: add one (property grid shows it), add a frame to it, delete it again.
            int loops = view.Loops.Count;
            doc.AddLoop();
            Assert.Equal(loops + 1, view.Loops.Count);
            Assert.Same(view.Loops.Last(), vm.PropertyGrid.Component);
            doc.NewFrame(doc.Loops.Last());
            Assert.Single(view.Loops.Last().Frames);

            // Preview animates the selected loop.
            doc.SelectLoop(doc.Loops[0]);
            string first = doc.PreviewText;
            for (int i = 0; i < 200 && doc.PreviewText == first; i++)
                doc.TickPreview();
            Assert.NotEqual(first, doc.PreviewText);

            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-view-editor.png"));

            doc.DeleteLastLoop();
            Assert.Equal(loops, view.Loops.Count);

            vm.SaveCommand.Execute(null);
            var toolchain = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            var result = toolchain.Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.True(result.Succeeded, TestPaths.BuildFailure(result));
        }
    }
}
