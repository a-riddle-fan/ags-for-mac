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
    public class SmallEditorsTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-small-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        private (MainWindow, MainWindowViewModel) Open()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            return (window, vm);
        }

        [AvaloniaFact]
        public void TextParserWordsAndSynonyms()
        {
            var (window, vm) = Open();
            TextParser parser = vm.Project.Game.TextParser;
            vm.OpenNode(vm.TreeRoots[0].Children.Single(n => n.Name == "Text Parser"));
            var doc = Assert.IsType<TextParserDocument>(vm.SelectedDocument);
            Assert.Equal(parser.Words.Count, doc.Words.Count);
            Assert.Equal(doc.Words.Select(w => w.Group).OrderBy(g => g), doc.Words.Select(w => w.Group)); // sorted by group

            vm.PromptText = (title, value) => "hammer";
            vm.TextParserAction("add");
            TextParserWord hammer = parser.Words.Single(w => w.Word == "hammer");
            Assert.Same(hammer, vm.PropertyGrid.Component);
            vm.PromptText = (title, value) => "mallet";
            vm.TextParserAction("synonym");
            Assert.Equal(hammer.WordGroup, parser.Words.Single(w => w.Word == "mallet").WordGroup);
            Assert.True(vm.IsModified);

            doc.FindText = "HAMMER";
            Assert.Same(hammer, doc.Selected.Word);

            // The last word of a special group can't be deleted.
            var ignore = doc.Words.Where(w => w.Group == TextParser.WORD_GROUP_ID_IGNORE).ToList();
            if (ignore.Count == 1)
                Assert.False(doc.CanDelete(ignore[0]));
            doc.Selected = doc.Words.Single(w => w.Text == "mallet");
            vm.TextParserAction("delete");
            Assert.DoesNotContain(parser.Words, w => w.Word == "mallet");

            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-text-parser.png"));
        }

        [AvaloniaFact]
        public void LipSyncFramesEditTheGame()
        {
            var (window, vm) = Open();
            vm.OpenNode(vm.TreeRoots[0].Children.Single(n => n.Name == "Lip Sync"));
            var doc = Assert.IsType<LipSyncDocument>(vm.SelectedDocument);
            Assert.Equal(LipSync.MAX_LIP_SYNC_FRAMES, doc.Frames.Count);
            Assert.Same(vm.Project.Game.LipSync, vm.PropertyGrid.Component);
            doc.Frames[9].Letters = "X/J";
            Assert.Equal("X/J", vm.Project.Game.LipSync.CharactersPerFrame[9]);
            Assert.True(vm.IsModified);
            vm.SaveCommand.Execute(null);
            var reloaded = GameProject.Load(Path.Combine(_gameDir, "Game.agf"), new AGS.Editor.Hosting.ConsoleEditorHost(TextWriter.Null));
            Assert.Equal("X/J", reloaded.Game.LipSync.CharactersPerFrame[9]);
        }

        [AvaloniaFact]
        public void ColourFinderConvertsBothWays()
        {
            var (window, vm) = Open();
            vm.OpenNode(vm.TreeRoots[0].Children.Single(n => n.Name == "Colours"));
            var doc = Assert.IsType<PaletteDocument>(vm.SelectedDocument);
            Assert.Equal(256, doc.Slots.Count);
            Assert.False(doc.IsPaletteGame); // a 16-bit game

            // AGS colour numbers in 16-bit games are RGB565: 63488 is pure red, 31 is pure blue.
            doc.ColourNumber = 63488;
            Assert.Equal((255, 0, 0), (doc.Red, doc.Green, doc.Blue)); // ColorMapper expands 5/6-bit channels to 0..255
            doc.Red = 0; doc.Green = 0; doc.Blue = 255;
            Assert.Equal(31, doc.ColourNumber);
            Assert.Equal("#0000FF", doc.WebColour);

            doc.SelectSlot(doc.Slots[15]);
            Assert.Same(vm.Project.Game.Palette[15], vm.PropertyGrid.Component);
            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-colours.png"));
        }
    }
}
