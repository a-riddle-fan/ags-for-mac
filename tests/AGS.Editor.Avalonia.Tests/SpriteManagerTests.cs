using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AGS.Editor.Avalonia.ViewModels;
using AGS.Editor.Avalonia.Views;
using AGS.Imaging;
using AGS.Imaging.Magick;
using AGS.Types;

namespace AGS.Editor.Avalonia.Tests
{
    public class SpriteManagerTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-sprites-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        [AvaloniaFact]
        public async Task ImportsASpriteSheetIntoTheOpenFolder()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            ProjectTreeNode spritesSection = vm.TreeRoots[0].Children.Single(n => n.Name == "Sprites");
            vm.OpenNode(spritesSection.Payload is ISpriteFolder ? spritesSection : spritesSection.Descendants().First(n => n.Payload is ISpriteFolder));
            var doc = Assert.IsType<SpriteFolderDocument>(vm.SelectedDocument);
            int before = doc.Folder.Sprites.Count;

            string sheetFile = Path.Combine(_gameDir, "sheet.png");
            using (var sheet = new PixelImage(40, 20, ImagePixelFormat.Bgra32))
            {
                sheet.Pixels.Fill(128);
                new AGS.Native.NativeImageCodec().Encode(sheet, sheetFile);
            }
            vm.PickOpenFiles = (title, ext) => Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(new[] { sheetFile });
            vm.AskSpriteSheet = file => new AGS.Editor.Utils.SpriteSheet(new System.Drawing.Point(0, 0), new System.Drawing.Size(10, 10),
                new System.Drawing.Size(0, 0), SpriteImportTilingDirection.Right, 100);
            await vm.ImportSpriteSheetCommand.ExecuteAsync(doc);

            Assert.Equal(before + 8, doc.Folder.Sprites.Count);
            var refreshed = Assert.IsType<SpriteFolderDocument>(vm.SelectedDocument);
            Assert.Equal(before + 8, refreshed.Items.Count);
            Assert.Contains("Imported 8 sprites", vm.OutputText);
        }

        [AvaloniaFact]
        public async Task MultiSelectionRenumbersAssignsExportsAndDeletes()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            Game game = vm.Project.Game;
            ProjectTreeNode spritesSection = vm.TreeRoots[0].Children.Single(n => n.Name == "Sprites");
            vm.OpenNode(spritesSection.Payload is ISpriteFolder ? spritesSection : spritesSection.Descendants().First(n => n.Payload is ISpriteFolder));
            var doc = Assert.IsType<SpriteFolderDocument>(vm.SelectedDocument);

            // three new (unused) sprites
            var files = new System.Collections.Generic.List<string>();
            for (int k = 0; k < 3; k++)
            {
                string file = Path.Combine(_gameDir, $"new{k}.png");
                using var image = new PixelImage(3 + k, 3, ImagePixelFormat.Bgra32);
                image.Pixels.Fill((byte)(60 * (k + 1)));
                new AGS.Native.NativeImageCodec().Encode(image, file);
                files.Add(file);
            }
            vm.PickOpenFiles = (title, ext) => Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(files);
            await vm.ImportSpritesCommand.ExecuteAsync(doc);
            doc = Assert.IsType<SpriteFolderDocument>(vm.SelectedDocument);
            var fresh = doc.Items.Where(t => t.Sprite.SourceFile?.StartsWith("new") == true).ToList();
            Assert.Equal(3, fresh.Count);

            // select them in the list (multiple selection)
            TestPaths.Settle(window);
            var list = window.GetVisualDescendants().OfType<global::Avalonia.Controls.ListBox>().First(l => l.ItemsSource == doc.Items);
            list.SelectedItems.Clear(); // the last imported sprite is selected after the import
            foreach (SpriteThumbnail t in fresh) list.SelectedItems.Add(t);
            Assert.Equal(3, doc.SelectedItems.Count);

            // Assign sequential sprite numbers 8000…
            vm.PromptText = (title, value) => "8000";
            vm.AssignSequentialSpriteNumbersCommand.Execute(fresh[0]);
            Assert.Equal(new[] { 8000, 8001, 8002 }, fresh.Select(t => t.Sprite.Number).OrderBy(n => n));
            doc = Assert.IsType<SpriteFolderDocument>(vm.SelectedDocument);
            var renumbered = doc.Items.Where(t => t.Number >= 8000).ToList();
            doc.SelectedItems.Clear();
            foreach (SpriteThumbnail t in renumbered) doc.SelectedItems.Add(t);

            // Assign to view: three frames in a new loop
            View view = game.RootViewFolder.AllItemsFlat.First();
            int loops = view.Loops.Count;
            vm.AskAssignToView = g => (view.ID, loops, false, false, false, false);
            vm.AssignSpriteToViewCommand.Execute(renumbered[0]);
            Assert.Equal(new[] { 8000, 8001, 8002 }, view.Loops[loops].Frames.Select(f => f.Image));
            view.Loops.RemoveAt(loops);

            // Export all (this folder) to a sub-folder
            string exportDir = Path.Combine(_gameDir, "export");
            vm.AskSpriteExport = dir => (Path.Combine(exportDir, "s%Number%"), false, false, 0, false, false);
            vm.ExportAllSpritesCommand.Execute(doc);
            Assert.True(File.Exists(Path.Combine(exportDir, "s8001.bmp")));

            // Delete all three
            vm.DeleteSpriteCommand.Execute(renumbered[1]);
            Assert.DoesNotContain(game.RootSpriteFolder.GetAllSpritesFromAllSubFolders(), sp => sp.Number >= 8000);
            vm.SaveCommand.Execute(null);
            var result = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory).Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.True(result.Succeeded, TestPaths.BuildFailure(result));
        }

        [AvaloniaFact]
        public async Task ClipboardAndExternalImageEditor()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            ProjectTreeNode spritesSection = vm.TreeRoots[0].Children.Single(n => n.Name == "Sprites");
            vm.OpenNode(spritesSection.Payload is ISpriteFolder ? spritesSection : spritesSection.Descendants().First(n => n.Payload is ISpriteFolder));
            var doc = Assert.IsType<SpriteFolderDocument>(vm.SelectedDocument);

            // paste a new sprite from the clipboard
            PixelImage Solid(int w, int h, byte value)
            {
                var image = new PixelImage(w, h, ImagePixelFormat.Bgra32);
                image.Pixels.Fill(value);
                return image;
            }
            vm.GetClipboardImage = () => Task.FromResult(Solid(7, 5, 200));
            await vm.PasteSpriteFromClipboardCommand.ExecuteAsync(doc);
            Sprite pasted = doc.Folder.Sprites.OrderBy(sp => sp.Number).Last(sp => sp.SourceFile?.StartsWith("ClipboardSprites") == true);
            Assert.Equal((7, 5, @"ClipboardSprites\clipboard1.png"), (pasted.Width, pasted.Height, pasted.SourceFile));

            // copy it back to the clipboard
            PixelImage copied = null;
            vm.CopyImageToClipboard = img => { copied = img.Clone(); return Task.CompletedTask; };
            await vm.CopySpriteToClipboardCommand.ExecuteAsync(new SpriteThumbnail(pasted, null));
            Assert.Equal((7, 5), (copied.Width, copied.Height));

            // edit in the image editor: saving the temporary file replaces the sprite, the source stays
            string opened = null;
            vm.OpenWithDefaultApplication = file => opened = file;
            vm.EditSpriteExternallyCommand.Execute(new SpriteThumbnail(pasted, null));
            Assert.True(File.Exists(opened));
            System.Threading.Thread.Sleep(1100); // a new modification time
            using (PixelImage edited = Solid(9, 4, 90))
                new AGS.Native.NativeImageCodec().Encode(edited, opened);
            vm.OnExternalSpriteFileChanged(opened);
            Assert.Equal((9, 4, @"ClipboardSprites\clipboard1.png"), (pasted.Width, pasted.Height, pasted.SourceFile));
            Assert.Contains("updated from your image editor", vm.OutputText);
        }

        [AvaloniaFact]
        public void ClipboardBitmapConversionRoundTrips()
        {
            using var image = new PixelImage(3, 2, ImagePixelFormat.Bgra32);
            byte[] pixels = { 10, 20, 30, 255,  0, 0, 255, 255,  1, 2, 3, 128,
                              255, 0, 255, 255, 50, 60, 70, 0,   200, 100, 50, 255 };
            pixels.CopyTo(image.Pixels);
            using var bitmap = AGS.Editor.Avalonia.Services.ImageConversion.ToBitmap(image);
            using PixelImage back = AGS.Editor.Avalonia.Services.ImageConversion.FromBitmap(bitmap);
            Assert.Equal((3, 2), (back.Width, back.Height));
            // opaque pixels are exact; translucent ones within rounding; alpha is kept
            Assert.Equal(new byte[] { 10, 20, 30, 255 }, back.GetRow(0).Slice(0, 4).ToArray());
            Assert.Equal(new byte[] { 200, 100, 50, 255 }, back.GetRow(1).Slice(8, 4).ToArray());
            Assert.Equal(128, back.GetRow(0)[11]);
            Assert.Equal(0, back.GetRow(1)[7]);
        }

        [AvaloniaFact]
        public void SpriteFoldersFindAssignAndRenumber()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            Game game = vm.Project.Game;
            ProjectTreeNode Sprites() => vm.TreeRoots[0].Children.Single(n => n.Name == "Sprites");
            ProjectTreeNode Root() => Sprites().Payload == game.RootSpriteFolder ? Sprites() : Sprites().Descendants().First(n => n.Payload == game.RootSpriteFolder);

            // Create sub-folder, rename it
            vm.PromptText = (title, value) => "Props";
            Root().Actions.Single(a => a.Header == "Create sub-folder").Command.Execute(null);
            SpriteFolder props = game.RootSpriteFolder.SubFolders.OfType<SpriteFolder>().Single(f => f.Name == "Props");
            ProjectTreeNode propsNode = vm.TreeRoots[0].Children.Single(n => n.Name == "Sprites").Descendants().Single(n => n.Payload == props);
            vm.PromptText = (title, value) => "Requisiten";
            propsNode.Actions.Single(a => a.Header == "Rename").Command.Execute(null);
            Assert.Equal("Requisiten", props.Name);
            Assert.DoesNotContain(Root().Actions, a => a.Header == "Delete"); // the main folder stays

            // Find sprite by number opens its folder and selects it
            Sprite some = game.RootSpriteFolder.GetAllSpritesFromAllSubFolders().OrderBy(x => x.Number).Skip(5).First();
            vm.PromptText = (title, value) => some.Number.ToString();
            Root().Actions.Single(a => a.Header == "Find sprite by number...").Command.Execute(null);
            var doc = Assert.IsType<SpriteFolderDocument>(vm.SelectedDocument);
            Assert.Same(some, doc.Selected.Sprite);

            // Move to folder, then change its number (not in use) and assign it to a view
            vm.PromptText = (title, value) => System.Text.RegularExpressions.Regex.Match(title, @"(\d+): [^,]*/Requisiten").Groups[1].Value;
            vm.MoveSpriteToFolderCommand.Execute(doc.Selected);
            Assert.Contains(some, props.Sprites);
            View view = game.RootViewFolder.AllItemsFlat.First();
            int loops = view.Loops.Count;
            vm.AskAssignToView = g => (view.ID, loops, false, false, false, false);
            vm.AssignSpriteToViewCommand.Execute(new SpriteThumbnail(some, null));
            Assert.Equal(some.Number, view.Loops[loops].Frames.Single().Image);
            view.Loops.RemoveAt(loops);
            if (SpriteOperations.GetSpriteUsageReport(some.Number, game) == null)
            {
                vm.PromptText = (title, value) => "7777";
                vm.ChangeSpriteNumberCommand.Execute(new SpriteThumbnail(some, null));
                Assert.Equal(7777, some.Number);
            }
            vm.SaveCommand.Execute(null);
            var result = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory).Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.True(result.Succeeded, TestPaths.BuildFailure(result));
        }

        [AvaloniaFact]
        public async Task ShowsThumbnailsSelectsAndExports()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));

            // The sprite folder with the most sprites.
            ProjectTreeNode spritesSection = vm.TreeRoots[0].Children.Single(n => n.Name == "Sprites");
            ProjectTreeNode folderNode = spritesSection.Descendants().Where(n => n.Payload is ISpriteFolder)
                .OrderByDescending(n => ((ISpriteFolder)n.Payload).Sprites.Count).First();
            vm.OpenNode(folderNode);

            var doc = Assert.IsType<SpriteFolderDocument>(vm.SelectedDocument);
            Assert.True(doc.Items.Count > 10);
            Assert.All(doc.Items, t => Assert.NotNull(t.Image));

            SpriteThumbnail pick = doc.Items[3];
            doc.Selected = pick;
            Assert.Same(pick.Sprite, vm.PropertyGrid.Component);

            string exported = Path.Combine(_gameDir, "exported.png");
            vm.PickSaveFile = (name, ext) => Task.FromResult(exported);
            await vm.ExportSpriteCommand.ExecuteAsync(pick);
            MagickImageCodec.RegisterAll();
            AGS.Native.NativeImageCodec.RegisterAll();
            PixelImage image = Assert.Single(ImageCodecs.Load(exported).Frames);
            Assert.Equal((pick.Sprite.Width, pick.Sprite.Height), (image.Width, image.Height));

            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-sprite-manager.png"));
        }
    
        [AvaloniaFact]
        public async Task ImportsExportedSpriteAsNewSprite()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));

            ProjectTreeNode spritesSection = vm.TreeRoots[0].Children.Single(n => n.Name == "Sprites");
            ProjectTreeNode folderNode = spritesSection.Descendants().Where(n => n.Payload is ISpriteFolder)
                .OrderByDescending(n => ((ISpriteFolder)n.Payload).Sprites.Count).First();
            vm.OpenNode(folderNode);
            var doc = Assert.IsType<SpriteFolderDocument>(vm.SelectedDocument);
            int countBefore = doc.Items.Count;
            SpriteThumbnail original = doc.Items[3];

            string exported = Path.Combine(_gameDir, "exported.png");
            vm.PickSaveFile = (name, ext) => Task.FromResult(exported);
            await vm.ExportSpriteCommand.ExecuteAsync(original);

            vm.PickOpenFiles = (title, ext) => Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(new[] { exported });
            int asked = 0;
            vm.PickSpriteImportOptions = (transparency, alpha, count) =>
            {
                asked++;
                Assert.Equal(SpriteImportTransparency.LeaveAsIs, transparency); // upstream default
                Assert.Equal(1, count);
                return (SpriteImportTransparency.LeaveAsIs, false);
            };
            await vm.ImportSpritesCommand.ExecuteAsync(doc);
            Assert.Equal(1, asked);

            var refreshed = Assert.IsType<SpriteFolderDocument>(vm.SelectedDocument);
            Assert.Equal(countBefore + 1, refreshed.Items.Count);
            SpriteThumbnail imported = refreshed.Selected;
            Assert.NotNull(imported);
            Assert.NotNull(imported.Image);
            Assert.Equal((original.Sprite.Width, original.Sprite.Height), (imported.Sprite.Width, imported.Sprite.Height));
            Assert.Equal("exported.png", imported.Sprite.SourceFile);
            Assert.False(imported.Sprite.ImportAlphaChannel);
            Assert.True(vm.IsModified);

            // Reimport from source keeps the slot.
            await vm.ReimportSpriteCommand.ExecuteAsync(imported);
            var again = Assert.IsType<SpriteFolderDocument>(vm.SelectedDocument);
            Assert.Equal(imported.Number, again.Selected.Number);

            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-sprite-import.png"));

            // Delete it again; a sprite used by a view is refused.
            vm.DeleteSpriteCommand.Execute(again.Selected);
            var afterDelete = Assert.IsType<SpriteFolderDocument>(vm.SelectedDocument);
            Assert.Equal(countBefore, afterDelete.Items.Count);
            vm.DeleteSpriteCommand.Execute(afterDelete.Items[3]);
            Assert.Equal(countBefore, Assert.IsType<SpriteFolderDocument>(vm.SelectedDocument).Items.Count);
            Assert.Contains("is used in the following places", vm.OutputText);
            return;

            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-sprite-import.png"));
        }
    }
}
