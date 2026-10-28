using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using AGS.Editor.Avalonia.ViewModels;
using AGS.Editor.Avalonia.Views;
using AGS.Native;
using AGS.Types;

namespace AGS.Editor.Avalonia.Tests
{
    public class RoomEditorTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-roomed-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        [AvaloniaFact]
        public void RoomMessagesFromThePropertyGridAreSaved()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            // the room messages hook as App installs it (reset at the end: static hooks outlive this test's window)
            var dialogs = new AGS.Editor.Avalonia.Services.EditorDialogs(() => window, () => vm.Project);
            RoomMessagesUIEditor.ShowRoomMessagesEditor = messages => dialogs.EditRoomMessages(messages);
            try
            {
            ProjectTreeNode roomNode = vm.TreeRoots[0].Children.Single(n => n.Name == "Rooms").Children.First(n => ((UnloadedRoom)n.Payload).Number == 6);
            vm.OpenNode(roomNode);
            var doc = Assert.IsType<RoomEditorDocument>(vm.SelectedDocument);
            Room room = Assert.IsType<Room>(vm.PropertyGrid.Component);
            int before = room.Messages.Count;
            PropertyRow messages = vm.PropertyGrid.Entries.OfType<PropertyRow>().Single(r => r.Name == "Messages");
            Assert.True(messages.HasEditorButton);
            Assert.Equal("(Collection)", Assert.IsType<TextPropertyRow>(messages).Text);

            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var dialog = window.OwnedWindows.Last();
                var buttons = global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(dialog).OfType<global::Avalonia.Controls.Button>().ToList();
                buttons.Single(b => (string)b.Content == "Add message").RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
                global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(dialog).OfType<global::Avalonia.Controls.TextBox>().Single().Text = "A new room message";
                buttons.Single(b => (string)b.Content == "OK").RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
            }, global::Avalonia.Threading.DispatcherPriority.Background);
            messages.OpenEditorCommand.Execute(null);

            Assert.Equal(before + 1, room.Messages.Count);
            Assert.True(doc.IsModified && vm.IsModified);
            vm.SaveCommand.Execute(null);
            var unloaded = (UnloadedRoom)roomNode.Payload;
            using NativeRoom reloaded = NativeRoom.Load(Path.Combine(_gameDir, unloaded.FileName));
            Room again = reloaded.ToRoom(unloaded, vm.Project.Game.TextEncoding);
            Assert.Equal(before + 1, again.Messages.Count);
            Assert.Equal("A new room message", again.Messages[before].Text);
            }
            finally
            {
                RoomMessagesUIEditor.ShowRoomMessagesEditor = null;
            }
        }

        [AvaloniaFact]
        public void ExportAndImportAMask()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            ProjectTreeNode roomNode = vm.TreeRoots[0].Children.Single(n => n.Name == "Rooms").Children.First(n => ((UnloadedRoom)n.Payload).Number == 6);
            vm.OpenNode(roomNode);
            var room = Assert.IsType<RoomEditorDocument>(vm.SelectedDocument);
            room.EditMode = RoomEditMode.Paint;
            room.PaintLayer = RoomMaskType.WalkableAreas;

            // Export: an 8-bit image of the mask's size with the game palette
            string exported = Path.Combine(_gameDir, "walk.png");
            vm.PickSaveFileSync = (title, suggested, type, ext) => exported;
            vm.ExportMaskCommand.Execute(room);
            AGS.Imaging.PixelImage image = new NativeImageCodec().Decode(exported).Frames[0];
            int scale = room.Room.MaskResolution;
            Assert.Equal(AGS.Imaging.ImagePixelFormat.Indexed8, image.PixelFormat);
            Assert.Equal((room.Room.Width / scale, room.Room.Height / scale), (image.Width, image.Height));
            int before = room.GetArea(RoomMaskType.WalkableAreas, 4 * scale, 4 * scale);
            Assert.Equal(before, image.GetRow(4)[4]);

            // Edit outside AGS: area 3 in a corner, one invalid colour (200)
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    image.GetRow(y)[x] = 3;
            image.GetRow(10)[10] = 200;
            string edited = Path.Combine(_gameDir, "walk-edited.png");
            new NativeImageCodec().Encode(image, edited);
            string message = room.ImportMask(new NativeImageCodec().Decode(edited).Frames[0]);
            Assert.Contains("Invalid colours were found in the Walkable Areas mask", message);
            Assert.Equal(3, room.GetArea(RoomMaskType.WalkableAreas, 1 * scale, 1 * scale));
            Assert.Equal(0, room.GetArea(RoomMaskType.WalkableAreas, 10 * scale, 10 * scale));
            Assert.True(room.CanUndo);

            // Wrong size: refused with upstream's message
            using (var small = new AGS.Imaging.PixelImage(10, 10, AGS.Imaging.ImagePixelFormat.Indexed8))
                Assert.Contains("does not match the size of the room background", room.ImportMask(small));

            vm.SaveCommand.Execute(null);
            var unloaded = (UnloadedRoom)roomNode.Payload;
            using NativeRoom reloaded = NativeRoom.Load(Path.Combine(_gameDir, unloaded.FileName));
            Assert.Equal(3, reloaded.GetMask(RoomMaskType.WalkableAreas).GetRow(1)[1]);
        }

        [AvaloniaFact]
        public void SelectEditAndSaveARoomObject()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));

            // The room with the most objects.
            ProjectTreeNode roomNode = null;
            int most = -1;
            foreach (ProjectTreeNode n in vm.TreeRoots[0].Children.Single(n => n.Name == "Rooms").Children)
            {
                using NativeRoom probe = NativeRoom.Load(Path.Combine(_gameDir, ((UnloadedRoom)n.Payload).FileName));
                int count = probe.ToRoom((UnloadedRoom)n.Payload, AnsiEncoding.Default).Objects.Count;
                if (count > most) { most = count; roomNode = n; }
            }
            vm.OpenNode(roomNode);
            var room = Assert.IsType<RoomEditorDocument>(vm.SelectedDocument);
            Assert.NotNull(room.Image);
            Assert.Equal(room.Room.Width * room.Zoom, room.ImageWidth);

            room.ShowHotspots = true;
            room.ShowWalkableAreas = true;

            // Click into the first object (bottom-left anchored), which selects it for the property grid.
            RoomObject obj = room.Room.Objects.First(o => o.Visible);
            object hit = room.SelectAt(obj.StartX + 1, obj.StartY - 2);
            Assert.IsType<RoomObject>(hit);
            Assert.IsType<RoomObject>(vm.PropertyGrid.Component);
            var picked = (RoomObject)hit;

            var startX = vm.PropertyGrid.Entries.OfType<TextPropertyRow>().Single(r => r.Descriptor.Name == nameof(RoomObject.StartX));
            int newX = picked.StartX + 7;
            startX.Text = newX.ToString();
            Assert.True(room.IsModified);

            // Dragging moves the object by the mouse delta.
            room.BeginDrag(picked.StartX + 1, picked.StartY - 2);
            room.DragTo(picked.StartX + 1 + 5, picked.StartY - 2 - 3);
            room.EndDrag();
            newX += 5;
            int newY = picked.StartY;
            Assert.Equal(newX, picked.StartX);

            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase3-room-editor.png"));

            vm.SaveCommand.Execute(null);
            Assert.False(room.IsModified);
            var unloaded = (UnloadedRoom)roomNode.Payload;
            using NativeRoom reloaded = NativeRoom.Load(Path.Combine(_gameDir, unloaded.FileName));
            RoomObject saved = reloaded.ToRoom(unloaded, AnsiEncoding.Default).Objects[picked.ID];
            Assert.Equal((newX, newY), (saved.StartX, saved.StartY));
        }

        [AvaloniaFact]
        public void PaintMasksUndoSaveAndBuild()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            ProjectTreeNode roomNode = vm.TreeRoots[0].Children.Single(n => n.Name == "Rooms").Children.First(n => ((UnloadedRoom)n.Payload).Number == 6);
            vm.OpenNode(roomNode);
            var room = Assert.IsType<RoomEditorDocument>(vm.SelectedDocument);

            room.EditMode = RoomEditMode.Paint;
            room.PaintLayer = RoomMaskType.Hotspots;
            Assert.True(room.ShowHotspots);
            room.AreaNumber = 5;
            room.Tool = MaskTool.Rectangle;
            room.BeginStroke(20, 20);
            room.ContinueStroke(60, 50);
            room.EndStroke();
            Assert.Equal(5, room.GetArea(RoomMaskType.Hotspots, 40, 35));
            Assert.True(room.IsModified && vm.IsModified);

            room.Undo();
            Assert.NotEqual(5, room.GetArea(RoomMaskType.Hotspots, 40, 35));
            room.BeginStroke(20, 20);
            room.ContinueStroke(60, 50);
            room.EndStroke();

            // Flood fill a walkable area region at the top of the room (outside the floor).
            room.PaintLayer = RoomMaskType.WalkableAreas;
            room.AreaNumber = 3;
            room.Tool = MaskTool.Fill;
            int before = room.GetArea(RoomMaskType.WalkableAreas, 5, 5);
            room.BeginStroke(5, 5);
            Assert.Equal(3, room.GetArea(RoomMaskType.WalkableAreas, 5, 5));
            Assert.NotEqual(3, before);

            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase3-mask-painting.png"));

            vm.SaveCommand.Execute(null);
            var unloaded = (UnloadedRoom)roomNode.Payload;
            using (NativeRoom reloaded = NativeRoom.Load(Path.Combine(_gameDir, unloaded.FileName)))
            {
                var hotspots = reloaded.GetMask(RoomMaskType.Hotspots);
                int scale = reloaded.MaskResolution;
                Assert.Equal(5, hotspots.GetRow(35 / scale)[40 / scale]);
                Assert.Equal(3, reloaded.GetMask(RoomMaskType.WalkableAreas).GetRow(5 / scale)[5 / scale]);
            }

            // The upstream toolchain accepts the painted room.
            AGS.Editor.Build.AgsBuildToolchain toolchain = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            var result = toolchain.Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.True(result.Succeeded, TestPaths.BuildFailure(result));
        }

        [AvaloniaFact]
        public void DragEdgesAndWalkBehindBaselines()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));

            // The first room with painted walk-behind areas.
            RoomEditorDocument room = null;
            ProjectTreeNode roomNode = null;
            foreach (ProjectTreeNode n in vm.TreeRoots[0].Children.Single(n => n.Name == "Rooms").Children)
            {
                vm.OpenNode(n);
                var doc = Assert.IsType<RoomEditorDocument>(vm.SelectedDocument);
                if (doc.WalkBehindAreasInUse().Count > 0) { room = doc; roomNode = n; break; }
                vm.CloseDocumentCommand.Execute(doc);
            }
            Assert.NotNull(room);
            room.ShowWalkBehinds = true;
            room.Zoom = 1;

            // Left edge: grab it (no object there) and move it 9 pixels right.
            int left = room.Room.LeftEdgeX;
            int y = room.Room.Height / 2;
            Assert.Equal("Left edge", room.LineAt(left + 1, y)?.Name);
            room.BeginDrag(left + 1, y);
            Assert.True(room.IsDragging);
            room.DragTo(left + 9, y);
            room.EndDrag();
            Assert.Equal(left + 9, room.Room.LeftEdgeX);
            Assert.True(room.IsModified);

            // Walk-behind baseline: moved by dragging its line.
            int area = room.WalkBehindAreasInUse()[0];
            RoomWalkBehind wb = room.Room.WalkBehinds[area];
            int baseline = Math.Clamp(wb.Baseline, 20, room.Room.Height - 40);
            wb.Baseline = baseline;
            // A point on the baseline that no object/character covers.
            int x = Enumerable.Range(room.Room.LeftEdgeX + 10, Math.Max(1, room.Room.RightEdgeX - room.Room.LeftEdgeX - 20))
                .First(px => !(room.SelectAt(px, baseline) is RoomObject or Character));
            RoomEditorDocument.RoomLine line = room.LineAt(x, baseline);
            Assert.NotNull(line);
            Assert.Same(wb, line.Owner);
            room.BeginDrag(x, baseline);
            Assert.True(room.IsDragging);
            room.DragTo(x, baseline + 15);
            room.EndDrag();
            Assert.Equal(baseline + 15, wb.Baseline);

            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase3-edges-baselines.png"));

            vm.SaveCommand.Execute(null);
            var unloaded = (UnloadedRoom)roomNode.Payload;
            using NativeRoom reloaded = NativeRoom.Load(Path.Combine(_gameDir, unloaded.FileName));
            Room saved = reloaded.ToRoom(unloaded, AnsiEncoding.Default);
            Assert.Equal(left + 9, saved.LeftEdgeX);
            Assert.Equal(baseline + 15, saved.WalkBehinds[area].Baseline);
        }

        [AvaloniaFact]
        public async System.Threading.Tasks.Task ImportAddAndDeleteBackgrounds()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            ProjectTreeNode roomNode = vm.TreeRoots[0].Children.Single(n => n.Name == "Rooms").Children.First();
            vm.OpenNode(roomNode);
            var room = Assert.IsType<RoomEditorDocument>(vm.SelectedDocument);
            var unloaded = (UnloadedRoom)roomNode.Payload;
            int width = room.Room.Width, height = room.Room.Height;

            // Export the main background and add it back as a second frame: identical pixels.
            string exported = Path.Combine(_gameDir, "bg.png");
            using (NativeRoom probe = NativeRoom.Load(Path.Combine(_gameDir, unloaded.FileName)))
            using (AGS.Imaging.PixelImage bg = probe.GetBackground(0))
                new NativeImageCodec().Encode(bg, exported);
            vm.PickOpenFiles = (title, ext) => System.Threading.Tasks.Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(new[] { exported });
            await vm.AddBackgroundCommand.ExecuteAsync(room);
            Assert.Equal(2, room.Room.BackgroundCount);
            Assert.Equal(1, room.BackgroundFrame);
            Assert.True(room.CanDeleteBackground);

            vm.SaveCommand.Execute(null);
            using (NativeRoom reloaded = NativeRoom.Load(Path.Combine(_gameDir, unloaded.FileName)))
            {
                Assert.Equal(2, reloaded.BackgroundCount);
                using AGS.Imaging.PixelImage f0 = reloaded.GetBackground(0);
                using AGS.Imaging.PixelImage f1 = reloaded.GetBackground(1);
                Assert.Equal(f0.Pixels.ToArray(), f1.Pixels.ToArray());
            }

            // Delete the extra frame again.
            vm.DeleteBackgroundCommand.Execute(room);
            Assert.Equal(1, room.Room.BackgroundCount);

            // A bigger main background resizes the room, clears the masks and resets the edges.
            string bigger = Path.Combine(_gameDir, "bigger.png");
            using (var image = new AGS.Imaging.PixelImage(width + 80, height + 20, AGS.Imaging.ImagePixelFormat.Bgra32))
            {
                image.Pixels.Fill(0x80);
                new NativeImageCodec().Encode(image, bigger);
            }
            vm.PickOpenFiles = (title, ext) => System.Threading.Tasks.Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(new[] { bigger });
            room.BackgroundFrame = 0;
            await vm.ImportBackgroundCommand.ExecuteAsync(room);
            Assert.Equal((width + 80, height + 20), (room.Room.Width, room.Room.Height));
            Assert.Equal(width + 80 - 1, room.Room.RightEdgeX);
            Assert.Empty(room.WalkBehindAreasInUse());

            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase3-background-import.png"));

            vm.SaveCommand.Execute(null);
            using (NativeRoom reloaded = NativeRoom.Load(Path.Combine(_gameDir, unloaded.FileName)))
            {
                Assert.Equal((width + 80, height + 20), (reloaded.Width, reloaded.Height));
                Room saved = reloaded.ToRoom(unloaded, AnsiEncoding.Default);
                Assert.Equal(height + 20 - 1, saved.BottomEdgeY);
            }
            AGS.Editor.Build.AgsBuildToolchain toolchain = AGS.Editor.Build.AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            Assert.NotNull(toolchain);
            var result = toolchain.Build(Path.Combine(_gameDir, "Game.agf"));
            Assert.True(result.Succeeded, TestPaths.BuildFailure(result));
        }
    }
}
