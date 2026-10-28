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
    public class GuiEditorTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-gui-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        [AvaloniaFact]
        public void EditControlsOfAGui()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));

            // The normal GUI with the most buttons (the MMM verb GUI).
            GUI gui = vm.Project.Game.RootGUIFolder.AllItemsFlat.OfType<NormalGUI>().OrderByDescending(g => g.Controls.OfType<GUIButton>().Count()).First();
            ProjectTreeNode node = vm.TreeRoots[0].Children.Single(n => n.Name == "GUIs").Children.Single(n => n.Payload == gui);
            vm.OpenNode(node);
            var doc = Assert.IsType<GuiEditorDocument>(vm.SelectedDocument);
            Assert.Same(gui, vm.PropertyGrid.Component);
            Assert.NotNull(doc.Rendered); // upstream's GUI drawing with AGS fonts

            // Click into a button: selected for the property grid.
            GUIButton button = gui.Controls.OfType<GUIButton>().First(b => !b.Locked);
            Assert.Same(button, doc.SelectAt(button.Left + 1, button.Top + 1));
            Assert.Same(button, vm.PropertyGrid.Component);

            // Drag it by (3, 2), resize it via the corner handle.
            int left = button.Left, top = button.Top, width = button.Width, height = button.Height;
            doc.BeginDrag(left + 1, top + 1);
            doc.DragTo(left + 4, top + 3);
            doc.EndDrag();
            Assert.Equal((left + 3, top + 2), (button.Left, button.Top));
            Assert.True(vm.IsModified);
            Assert.True(doc.IsOnResizeHandle(button.Left + button.Width, button.Top + button.Height));
            doc.BeginDrag(button.Left + button.Width, button.Top + button.Height);
            doc.DragTo(button.Left + width + 5, button.Top + height + 1);
            doc.EndDrag();
            Assert.Equal((width + 5, height + 1), (button.Width, button.Height));

            // Add a label (named like upstream), bring the button to front, delete the label.
            int count = gui.Controls.Count;
            GUIControl label = doc.AddControl(GuiControlKind.Label, 2, 2, 40, 10);
            Assert.Equal((count, count, false), (label.ID, label.ZOrder, label.Clickable));
            Assert.StartsWith("Label", label.Name);
            doc.SelectedControl = button;
            doc.MoveSelectedInZOrder(toFront: true);
            Assert.Equal(gui.Controls.Count - 1, button.ZOrder);
            Assert.Equal(Enumerable.Range(0, gui.Controls.Count), gui.Controls.Select(c => c.ZOrder).OrderBy(z => z));

            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase3-gui-editor.png"));

            doc.SelectedControl = label;
            Assert.True(doc.DeleteSelected());
            Assert.Equal(count, gui.Controls.Count);

            // Drawing a new control: choose the type, drag a rectangle (any direction).
            doc.PendingAdd = GuiControlKind.Button;
            doc.BeginDrag(30, 20);
            doc.DragTo(12, 8);
            Assert.Equal(new System.Drawing.Rectangle(12, 8, 18, 12), doc.AddRectangle);
            doc.EndDrag();
            Assert.Null(doc.PendingAdd);
            var drawn = Assert.IsType<GUIButton>(doc.SelectedControl);
            Assert.Equal((12, 8, 18, 12), (drawn.Left, drawn.Top, drawn.Width, drawn.Height));
            // A tiny rectangle adds nothing (upstream).
            doc.PendingAdd = GuiControlKind.Label;
            doc.BeginDrag(5, 5);
            doc.DragTo(6, 5);
            doc.EndDrag();
            Assert.Equal(count + 1, gui.Controls.Count);
            Assert.True(doc.DeleteSelected());

            vm.SaveCommand.Execute(null);
            var reloaded = GameProject.Load(Path.Combine(_gameDir, "Game.agf"), new AGS.Editor.Hosting.ConsoleEditorHost(TextWriter.Null));
            GUIButton saved = reloaded.Game.RootGUIFolder.AllItemsFlat.Single(g => g.ID == gui.ID).Controls.OfType<GUIButton>().Single(b => b.Name == button.Name);
            Assert.Equal((left + 3, top + 2, width + 5, height + 1), (saved.Left, saved.Top, saved.Width, saved.Height));
            var toolchain = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            var result = toolchain.Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.True(result.Succeeded, TestPaths.BuildFailure(result));
        }

        [AvaloniaFact]
        public void SaveGuiShowsTextsInTheGameFonts()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            GUI gui = vm.Project.Game.RootGUIFolder.AllItemsFlat.Single(g => g.Name == "gSave");
            vm.OpenNode(vm.TreeRoots[0].Children.Single(n => n.Name == "GUIs").Children.Single(n => n.Payload == gui));
            var doc = Assert.IsType<GuiEditorDocument>(vm.SelectedDocument);
            Assert.NotNull(doc.Rendered);
            var before = doc.Rendered;

            // Editing a label text re-renders.
            GUILabel label = gui.Controls.OfType<GUILabel>().First();
            doc.SelectedControl = label;
            vm.PropertyGrid.Entries.OfType<TextPropertyRow>().Single(r => r.Name == "Text").Text = "Spielstand speichern";
            Assert.NotSame(before, doc.Rendered);

            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase3-gui-editor-fonts.png"));
        }

        [AvaloniaFact]
        public void FontPreviewShowsSampleAndCharacterTable()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            ProjectTreeNode fonts = vm.TreeRoots[0].Children.Single(n => n.Name == "Fonts");
            vm.OpenNode(fonts.Children[1]);
            var doc = Assert.IsType<FontDocument>(vm.SelectedDocument);
            Assert.NotNull(doc.Sample);
            Assert.NotNull(doc.Characters);
            Assert.Contains("height", doc.Info);
            double width = doc.SampleWidth;
            doc.SampleText = "Hallo";
            Assert.True(doc.SampleWidth < width);
            double charsHeight = doc.CharactersHeight;
            doc.Zoom = 4;
            Assert.Equal(charsHeight * 2, doc.CharactersHeight);

            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-font-preview.png"));
        }

        [AvaloniaFact]
        public void MultiSelectAlignDistributeCopyPaste()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            GUI gui = vm.Project.Game.RootGUIFolder.AllItemsFlat.OfType<NormalGUI>().OrderByDescending(g => g.Width * g.Height).First();
            vm.OpenNode(vm.TreeRoots[0].Children.Single(n => n.Name == "GUIs").Children.Single(n => n.Payload == gui));
            var doc = Assert.IsType<GuiEditorDocument>(vm.SelectedDocument);

            // Three new buttons at different places.
            GUIControl a = doc.AddControl(GuiControlKind.Button, 10, 10, 20, 10);
            GUIControl b = doc.AddControl(GuiControlKind.Button, 40, 25, 20, 10);
            GUIControl c = doc.AddControl(GuiControlKind.Button, 100, 40, 20, 10);
            doc.SelectAt(11, 11);
            doc.SelectAt(41, 26, toggle: true);
            doc.SelectAt(101, 41, toggle: true);
            Assert.Equal(new[] { a, b, c }, doc.Selection);
            Assert.Same(c, doc.SelectedControl);

            // Distribute horizontally: even spacing between the outermost; align top to the last picked (c).
            doc.Distribute(horizontally: true);
            Assert.Equal((10, 55, 100), (a.Left, b.Left, c.Left));
            doc.Align(left: false);
            Assert.Equal((40, 40, 40), (a.Top, b.Top, c.Top));

            // Dragging one moves the whole selection.
            doc.BeginDrag(a.Left + 1, a.Top + 1);
            doc.DragTo(a.Left + 6, a.Top + 3);
            doc.EndDrag();
            Assert.Equal((15, 60, 105), (a.Left, b.Left, c.Left));
            Assert.Equal((42, 42, 42), (a.Top, b.Top, c.Top));

            // Copy and paste at another place: relative positions kept, new names and IDs.
            int count = gui.Controls.Count;
            doc.CopySelection();
            doc.Paste(5, 5);
            Assert.Equal(count + 3, gui.Controls.Count);
            Assert.Equal(new[] { 5, 50, 95 }, doc.Selection.Select(x => x.Left));
            Assert.All(doc.Selection, x => Assert.DoesNotContain(x.Name, new[] { a.Name, b.Name, c.Name }));
            Assert.Equal(Enumerable.Range(0, gui.Controls.Count), gui.Controls.Select(x => x.ID));

            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase3-gui-multiselect.png"));

            // Delete removes the whole selection.
            Assert.True(doc.DeleteSelected());
            Assert.Equal(count, gui.Controls.Count);
        }
    }
}
