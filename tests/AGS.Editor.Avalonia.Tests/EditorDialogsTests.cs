using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AGS.Editor;
using AGS.Editor.Avalonia.Services;
using AGS.Editor.Avalonia.ViewModels;
using AGS.Editor.Avalonia.Views;

namespace AGS.Editor.Avalonia.Tests
{
    public class EditorDialogsTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-dlg-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        private (MainWindow Window, MainWindowViewModel Vm, EditorDialogs Dialogs) Open()
        {
            string repo = TestPaths.RepoRoot();
            TestPaths.CopyDirectory(Path.Combine(repo, "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1200, Height = 800 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            return (window, vm, new EditorDialogs(() => window, () => vm.Project));
        }

        /// <summary>While the modal dialog runs, select an entry, optionally screenshot it, and press OK.</summary>
        private static void AnswerDialog(Window owner, int selectIndex, string screenshot = null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Window dialog = owner.OwnedWindows.Last();
                ListBox list = dialog.GetLogicalDescendants().OfType<ListBox>().First();
                list.SelectedIndex = selectIndex;
                dialog.UpdateLayout();
                if (screenshot != null)
                    dialog.CaptureRenderedFrame()?.Save(screenshot);
                Button ok = dialog.GetLogicalDescendants().OfType<Button>().Single(b => (string)b.Content == "OK");
                ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, DispatcherPriority.Background);
        }

        [AvaloniaFact]
        public void SpritePickerShowsPreviewAndReturnsChoice()
        {
            var (window, vm, dialogs) = Open();
            int expected = vm.Project.Game.RootSpriteFolder.GetAllSpritesFromAllSubFolders().OrderBy(s => s.Number).ElementAt(40).Number;
            string shot = TestPaths.Screenshot("phase2-sprite-picker.png");

            AnswerDialog(window, 40, shot);
            int? chosen = dialogs.PickSprite(0);

            Assert.Equal(expected, chosen);
            Assert.True(File.Exists(shot));
        }

        [AvaloniaFact]
        public void ViewPickerReturnsViewNumber()
        {
            var (window, vm, dialogs) = Open();
            AnswerDialog(window, 3); // entry 0 is "(none)", so entry 3 is the third view
            int expected = vm.Project.Game.RootViewFolder.AllItemsFlat.OrderBy(v => v.ID).ElementAt(2).ID;

            Assert.Equal(expected, dialogs.PickView(0));
        }
        [AvaloniaFact]
        public void RoomMessagesEditorEditsAndAddsMessages()
        {
            var (window, vm, dialogs) = Open();
            AGS.Types.Character second = vm.Project.Game.RootCharacterFolder.AllItemsFlat.OrderBy(c => c.ID).ElementAt(1);
            var messages = new System.Collections.Generic.List<AGS.Types.RoomMessage>
            {
                new AGS.Types.RoomMessage(0) { Text = "Hello" },
                new AGS.Types.RoomMessage(1) { Text = "World" },
            };
            string shot = TestPaths.Screenshot("phase3-room-messages.png");
            Dispatcher.UIThread.Post(() =>
            {
                Window dialog = window.OwnedWindows.Last();
                ListBox list = dialog.GetLogicalDescendants().OfType<ListBox>().Single();
                Assert.Equal(2, list.ItemCount);
                list.SelectedIndex = 1;
                Assert.Equal("World", dialog.GetLogicalDescendants().OfType<TextBox>().Single().Text);
                dialog.GetLogicalDescendants().OfType<TextBox>().Single().Text = "Hi there";
                dialog.GetLogicalDescendants().OfType<ComboBox>().Single().SelectedIndex = 2; // second character speaks
                dialog.GetLogicalDescendants().OfType<CheckBox>().Single(c => ((string)c.Content).StartsWith("Display next")).IsChecked = true;
                Button add = dialog.GetLogicalDescendants().OfType<Button>().Single(b => (string)b.Content == "Add message");
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(2, list.SelectedIndex);
                dialog.GetLogicalDescendants().OfType<TextBox>().Single().Text = "Third";
                Assert.True(dialog.GetLogicalDescendants().OfType<Button>().Single(b => (string)b.Content == "Delete message").IsEnabled);
                list.SelectedIndex = 0;
                Assert.False(dialog.GetLogicalDescendants().OfType<Button>().Single(b => (string)b.Content == "Delete message").IsEnabled);
                dialog.UpdateLayout();
                dialog.CaptureRenderedFrame()?.Save(shot);
                dialog.GetLogicalDescendants().OfType<Button>().Single(b => (string)b.Content == "OK").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, DispatcherPriority.Background);
            dialogs.EditRoomMessages(messages);

            Assert.Equal(3, messages.Count);
            Assert.Equal("Hello", messages[0].Text);
            Assert.False(messages[0].ShowAsSpeech);
            Assert.Equal(("Hi there", true, second.ID, true), (messages[1].Text, messages[1].ShowAsSpeech, messages[1].CharacterID, messages[1].DisplayNextMessageAfter));
            Assert.Equal((2, "Third"), (messages[2].ID, messages[2].Text));
        }

        [AvaloniaFact]
        public void TtfSizeDialogReturnsPixelHeight()
        {
            var (window, vm, dialogs) = Open();
            Dispatcher.UIThread.Post(() =>
            {
                Window dialog = window.OwnedWindows.Last();
                var radios = dialog.GetLogicalDescendants().OfType<RadioButton>().ToList();
                var numbers = dialog.GetLogicalDescendants().OfType<NumericUpDown>().ToList();
                Assert.True(numbers[0].IsEnabled);
                Assert.False(numbers[1].IsEnabled);
                radios[1].IsChecked = true;
                Assert.True(numbers[1].IsEnabled);
                numbers[1].Value = 24;
                dialog.UpdateLayout();
                dialog.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-ttf-size.png"));
                dialog.GetLogicalDescendants().OfType<Button>().Single(b => (string)b.Content == "OK").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, DispatcherPriority.Background);
            Assert.Equal((true, 24), dialogs.AskTtfSize(10));
        }

        [AvaloniaFact]
        public void SpeechNumberingDialogReturnsOptions()
        {
            var (window, vm, dialogs) = Open();
            AGS.Types.Character third = vm.Project.Game.RootCharacterFolder.AllItemsFlat.ElementAt(2);
            Dispatcher.UIThread.Post(() =>
            {
                Window dialog = window.OwnedWindows.Last();
                var checks = dialog.GetLogicalDescendants().OfType<CheckBox>().ToList();
                checks[0].IsChecked = true;   // narrator
                checks[1].IsChecked = false;  // combine identical lines
                dialog.GetLogicalDescendants().OfType<ComboBox>().Single().SelectedIndex = 4; // third character
                dialog.UpdateLayout();
                dialog.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase5-number-speech.png"));
                dialog.GetLogicalDescendants().OfType<Button>().Single(b => (string)b.Content == "OK").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, DispatcherPriority.Background);
            Assert.Equal((true, false, false, (int?)third.ID), dialogs.AskSpeechNumbering(vm.Project.Game));
        }

        [AvaloniaFact]
        public void SpriteSheetDialogPreviewsTheTiles()
        {
            var (window, vm, dialogs) = Open();
            // the game's first character frame, tiled 3x2 into a sheet
            string sheetFile = Path.Combine(_gameDir, "sheet.png");
            using (var sheet = new AGS.Imaging.PixelImage(60, 40, AGS.Imaging.ImagePixelFormat.Bgra32))
            {
                for (int y = 0; y < 40; y++)
                    for (int x = 0; x < 60; x++)
                    {
                        Span<byte> px = sheet.GetRow(y).Slice(x * 4, 4);
                        bool odd = ((x / 20) + (y / 20)) % 2 == 1;
                        px[0] = odd ? (byte)200 : (byte)40; px[1] = 120; px[2] = odd ? (byte)40 : (byte)200; px[3] = 255;
                    }
                new AGS.Native.NativeImageCodec().Encode(sheet, sheetFile);
            }
            Dispatcher.UIThread.Post(() =>
            {
                Window dialog = window.OwnedWindows.Last();
                var numbers = dialog.GetLogicalDescendants().OfType<NumericUpDown>().ToList(); // x, y, w, h, mx, my, max
                numbers[2].Value = 20;
                numbers[3].Value = 20;
                var canvas = dialog.GetLogicalDescendants().OfType<Canvas>().Single();
                Assert.Equal(6, canvas.Children.Count);
                Assert.Contains("6 sprite(s)", dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).First(t => t?.Contains("sprite(s)") == true));
                dialog.UpdateLayout();
                dialog.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-sprite-sheet.png"));
                dialog.GetLogicalDescendants().OfType<Button>().Single(b => (string)b.Content == "OK").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, DispatcherPriority.Background);
            AGS.Editor.Utils.SpriteSheet result = dialogs.AskSpriteSheet(sheetFile);
            Assert.NotNull(result);
            Assert.Equal(6, result.GetSpriteSelections(new System.Drawing.Size(60, 40)).Count());
        }

        [AvaloniaFact]
        public void SpriteExportDialogReturnsTheOptions()
        {
            var (window, vm, dialogs) = Open();
            Dispatcher.UIThread.Post(() =>
            {
                Window dialog = window.OwnedWindows.Last();
                var boxes = dialog.GetLogicalDescendants().OfType<TextBox>().ToList();
                boxes[1].Text = "spr_%Number%_%Width%";
                dialog.GetLogicalDescendants().OfType<RadioButton>().Single(r => (string)r.Content == "Root sprite folder").IsChecked = true;
                foreach (CheckBox c in dialog.GetLogicalDescendants().OfType<CheckBox>().Where(c => ((string)c.Content).StartsWith("Include") || ((string)c.Content).Contains("is valid") || ((string)c.Content).StartsWith("Set exported")))
                    c.IsChecked = true;
                dialog.UpdateLayout();
                dialog.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-sprite-export.png"));
                dialog.GetLogicalDescendants().OfType<Button>().Single(b => (string)b.Content == "OK").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, DispatcherPriority.Background);
            var result = dialogs.AskSpriteExport(_gameDir);
            Assert.NotNull(result);
            Assert.Equal((Path.Combine(_gameDir, "spr_%Number%_%Width%"), true, true, AGS.Editor.SpriteExport.SkipIf.SourceValid, true, false), result.Value);
        }

        [AvaloniaFact]
        public void CustomPropertiesDialogEditsValuesFromTheSchema()
        {
            var (window, vm, dialogs) = Open();
            AGS.Types.Game game = vm.Project.Game;
            game.PropertySchema.PropertyDefinitions.Add(new AGS.Types.CustomPropertySchemaItem
            {
                Name = "IsTalkative", Type = AGS.Types.CustomPropertyType.Boolean, DefaultValue = "0", AppliesToCharacters = true,
            });
            game.PropertySchema.PropertyDefinitions.Add(new AGS.Types.CustomPropertySchemaItem
            {
                Name = "Coins", Type = AGS.Types.CustomPropertyType.Number, DefaultValue = "0", AppliesToCharacters = true,
            });
            game.PropertySchema.PropertyDefinitions.Add(new AGS.Types.CustomPropertySchemaItem
            {
                Name = "RoomOnly", Type = AGS.Types.CustomPropertyType.Text, DefaultValue = "", AppliesToRooms = true,
                AppliesToCharacters = false, AppliesToHotspots = false, AppliesToInvItems = false, AppliesToObjects = false, // all default to true
            });
            AGS.Types.Character character = game.RootCharacterFolder.AllItemsFlat.First();
            string shot = TestPaths.Screenshot("phase2-custom-properties.png");

            Dispatcher.UIThread.Post(() =>
            {
                Window dialog = window.OwnedWindows.Last();
                var grid = (PropertyGridViewModel)dialog.GetLogicalDescendants().OfType<PropertyGridView>().Single().DataContext;
                Assert.Equal(new[] { "Coins", "IsTalkative" }, grid.Entries.OfType<PropertyRow>().Select(r => r.Name));
                grid.Entries.OfType<TextPropertyRow>().Single(r => r.Name == "Coins").Text = "42";
                grid.Entries.OfType<BoolPropertyRow>().Single(r => r.Name == "IsTalkative").Value = true;
                dialog.UpdateLayout();
                dialog.CaptureRenderedFrame()?.Save(shot);
                Button ok = dialog.GetLogicalDescendants().OfType<Button>().Single(b => (string)b.Content == "OK");
                ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, DispatcherPriority.Background);
            dialogs.EditCustomProperties(character.Properties, character);

            Assert.Equal("42", character.Properties.PropertyValues["Coins"].Value);
            Assert.Equal("1", character.Properties.PropertyValues["IsTalkative"].Value);
        }

        [AvaloniaFact]
        public void SchemaEditorAddsAPropertyThroughTheItemDialog()
        {
            var (window, vm, dialogs) = Open();
            AGS.Types.Game game = vm.Project.Game;
            AGS.Types.Character character = game.RootCharacterFolder.AllItemsFlat.First();
            string shot = TestPaths.Screenshot("phase2-property-schema.png");

            int listed = -2;
            // custom properties dialog → Edit Schema... → Add new property... → fill in → OK → OK → OK
            void Later(Action action) => Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
            Window Top()
            {
                Window w = window;
                while (w.OwnedWindows.LastOrDefault(x => x.IsVisible) is Window c) w = c;
                return w;
            }
            void Click(Window w, string content) =>
                w.GetLogicalDescendants().OfType<Button>().First(b => (string)b.Content == content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Later(() => Click(Top(), "Edit Schema..."));            // opens the schema editor (modal; continues below)
            Later(() => Later(() => Click(Top(), "Add new property...")));
            Later(() => Later(() => Later(() =>
            {
                Window item = Top();
                var boxes = item.GetLogicalDescendants().OfType<TextBox>().ToList();
                boxes[0].Text = "Weight";
                boxes[2].Text = "5";
                item.GetLogicalDescendants().OfType<ComboBox>().Single().SelectedItem = AGS.Types.CustomPropertyType.Number;
                Click(item, "OK");
                int tries = 0;
                void FinishWhenListed()
                {
                    Window schema = Top();
                    var list = schema.GetLogicalDescendants().OfType<ListBox>().FirstOrDefault();
                    // poll with a timer: the nested dialog frames only exit when the dispatcher queue runs empty
                    if ((list == null || list.ItemCount == 0) && tries++ < 100)
                    {
                        DispatcherTimer.RunOnce(FinishWhenListed, TimeSpan.FromMilliseconds(10));
                        return;
                    }
                    listed = list?.ItemCount ?? -1;
                    schema.UpdateLayout();
                    schema.CaptureRenderedFrame()?.Save(shot);
                    Click(schema, "OK");
                    Later(() => Click(Top(), "OK"));
                }
                DispatcherTimer.RunOnce(FinishWhenListed, TimeSpan.FromMilliseconds(10));
            })));
            dialogs.EditCustomProperties(character.Properties, character);

            Assert.Equal(1, listed); // the schema list shows the new property
            AGS.Types.CustomPropertySchemaItem added = Assert.Single(game.PropertySchema.PropertyDefinitions);
            Assert.Equal(("Weight", AGS.Types.CustomPropertyType.Number, "5"), (added.Name, added.Type, added.DefaultValue));
        }

        [AvaloniaFact]
        public void SpriteImportOptionsReturnsChosenMode()
        {
            var (window, vm, dialogs) = Open();
            string shot = TestPaths.Screenshot("phase2-sprite-import-options.png");
            Dispatcher.UIThread.Post(() =>
            {
                Window dialog = window.OwnedWindows.Last();
                RadioButton topLeft = dialog.GetLogicalDescendants().OfType<RadioButton>().Single(r => (string)r.Content == "Top-left pixel");
                topLeft.IsChecked = true;
                dialog.GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked = false;
                dialog.UpdateLayout();
                dialog.CaptureRenderedFrame()?.Save(shot);
                Button ok = dialog.GetLogicalDescendants().OfType<Button>().Single(b => (string)b.Content == "OK");
                ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, DispatcherPriority.Background);
            var result = dialogs.PickSpriteImportOptions(AGS.Types.SpriteImportTransparency.LeaveAsIs, true, 2);
            Assert.Equal((AGS.Types.SpriteImportTransparency.TopLeft, false), result);
        }
    }

    internal static class TestPaths
    {
        /// <summary>
        /// Lets the UI settle: runs pending dispatcher jobs (the dock realizes document content a frame later), lays
        /// the window out, and runs the jobs that the layout queued.
        /// </summary>
        public static void Settle(global::Avalonia.Controls.Window window)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        /// <summary>
        /// Where a screenshot goes: docs/screenshots when AGS4MAC_SCREENSHOTS=1 (to update the documentation),
        /// otherwise a temporary folder — the capture still runs, but test runs don't change the repository.
        /// </summary>
        public static string Screenshot(string fileName)
        {
            string dir = Environment.GetEnvironmentVariable("AGS4MAC_SCREENSHOTS") == "1"
                ? Path.Combine(RepoRoot(), "docs", "screenshots")
                : Path.Combine(Path.GetTempPath(), "ags4mac-screenshots");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, fileName);
        }

        /// <summary>A failed build's exit code, error messages and the end of its log (the start is just setup).</summary>
        public static string BuildFailure(AGS.Editor.Build.BuildResult result)
        {
            string[] lines = (result.Log ?? string.Empty).Split('\n');
            return $"exit code {result.ExitCode}\n" +
                string.Join("\n", result.Messages.Select(m => $"{m.GetType().Name}: {m.ScriptName}({m.LineNumber}) {m.Message}")) +
                "\n--- log (end) ---\n" + string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 60)));
        }

        public static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "AGS4Mac.sln")))
                dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return dir;
        }

        public static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }

    /// <summary>Drives four nested modal dialogs on the shared UI thread: not run in parallel with other tests.</summary>
    [Collection(NonParallelUiCollection.Name)]
    public class SchemaRenameDialogTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-schema-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        [AvaloniaFact]
        public void SchemaEditorRenamesAPropertyEverywhere()
        {
            TestPaths.CopyDirectory(Path.Combine(TestPaths.RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1200, Height = 800 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));
            var dialogs = new EditorDialogs(() => window, () => vm.Project);
            AGS.Types.Game game = vm.Project.Game;
            game.PropertySchema.PropertyDefinitions.Add(new AGS.Types.CustomPropertySchemaItem { Name = "Weight", Type = AGS.Types.CustomPropertyType.Number, DefaultValue = "0" });
            System.Collections.Generic.List<CustomPropertyDefChange> applied = null;
            dialogs.ApplySchemaRenames = (schema, changes) => { applied = changes; vm.ApplySchemaRenames(schema, changes); };

            // React to whichever dialog is on top, by title, until the schema editor is closed.
            Window Top()
            {
                Window w = window;
                while (w.OwnedWindows.LastOrDefault(x => x.IsVisible) is Window c) w = c;
                return w;
            }
            void Click(Window w, string content) =>
                w.GetLogicalDescendants().OfType<Button>().First(b => (string)b.Content == content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var done = new System.Collections.Generic.HashSet<string>();
            var started = System.Diagnostics.Stopwatch.StartNew();
            string stalledAt = null;
            var warnings = new System.Collections.Generic.List<string>();
            var originalWarning = AGS.Types.EditorMessages.ShowWarning;
            AGS.Types.EditorMessages.ShowWarning = (message, title) => warnings.Add(title + ": " + message);
            void Step()
            {
                Window top = Top();
                string key = top.Title
                    + (top.Title == "Edit property" && done.Contains("Confirm rename") ? "+renamed" : "")
                    + (top.Title == "Custom properties schema" && done.Contains("Edit property+renamed") ? "+finish" : "");
                if (started.Elapsed > TimeSpan.FromSeconds(60))
                {
                    // give up without hanging: close the dialogs, so EditPropertySchema returns and the asserts fail
                    stalledAt ??= key;
                    for (Window w = Top(); w != window; w = Top()) w.Close();
                    return;
                }
                if (top == window)
                {
                    if (done.Contains("Rename custom properties")) return; // the last dialog was answered
                    DispatcherTimer.RunOnce(Step, TimeSpan.FromMilliseconds(10)); // not open yet
                    return;
                }
                // schedule the next step first: a click may open a modal dialog and only return when it closes
                DispatcherTimer.RunOnce(Step, TimeSpan.FromMilliseconds(10));
                // the confirmation closes before its Rename handler has enabled the name field: wait for that
                if (key == "Edit property+renamed" && !top.GetLogicalDescendants().OfType<TextBox>().First().IsEnabled)
                    return;
                if (!done.Contains(key))
                {
                    done.Add(key);
                    switch (key)
                    {
                        case "Custom properties schema":
                            top.GetLogicalDescendants().OfType<ListBox>().Single().SelectedIndex = 0;
                            Click(top, "Edit property...");
                            break;
                        case "Edit property":
                            Assert.False(top.GetLogicalDescendants().OfType<TextBox>().First().IsEnabled);
                            Click(top, "Rename...");
                            break;
                        case "Confirm rename":
                        case "Rename custom properties":
                            Click(top, "OK");
                            break;
                        case "Edit property+renamed":
                            var nameBox = top.GetLogicalDescendants().OfType<TextBox>().First();
                            Assert.True(nameBox.IsEnabled);
                            nameBox.Text = "Mass";
                            Click(top, "OK");
                            break;
                        case "Custom properties schema+finish": // back in the schema editor after the rename
                            Click(top, "OK");
                            break;
                    }
                }
            }
            DispatcherTimer.RunOnce(Step, TimeSpan.FromMilliseconds(10));
            bool ok;
            try { ok = dialogs.EditPropertySchema(game.PropertySchema); }
            finally { AGS.Types.EditorMessages.ShowWarning = originalWarning; }
            Assert.True(ok, $"stalled at '{stalledAt}' after: {string.Join(" > ", done)}; warnings: {string.Join(" | ", warnings)}");

            Assert.NotNull(applied);
            Assert.Contains(applied, c => c.Type == CustomPropertyDefChangeType.Edit && c.OriginalName == "Weight" && c.NewName == "Mass");
            Assert.Contains(game.PropertySchema.PropertyDefinitions, p => p.Name == "Mass");
            Assert.DoesNotContain(game.PropertySchema.PropertyDefinitions, p => p.Name == "Weight");
            Assert.Contains("Custom properties renamed", vm.OutputText);
        }

    }

    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class NonParallelUiCollection
    {
        public const string Name = "Non-parallel UI";
    }
}
