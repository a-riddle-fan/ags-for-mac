using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AgsImage = AGS.Imaging.IImage;
using AGS.Native;
using AGS.Types;
using DrawingColor = System.Drawing.Color;
using DrawingSize = System.Drawing.Size;

namespace AGS.Editor.Avalonia.Services
{
    /// <summary>
    /// Avalonia dialogs behind the AGS.Types property editors. The upstream editor plugs its WinForms
    /// dialogs into static delegates on the UITypeEditors (e.g. ViewUIEditor.ViewSelectionGUI); this class
    /// does the same. The delegates are synchronous, so dialogs run modally in a nested dispatcher frame.
    /// </summary>
    public sealed class EditorDialogs
    {
        private readonly Func<Window> _owner;
        private readonly Func<GameProject> _project;

        public EditorDialogs(Func<Window> owner, Func<GameProject> project)
        {
            _owner = owner;
            _project = project;
        }

        /// <summary>Registers the dialogs with the AGS.Types editor hooks.</summary>
        public void Install()
        {
            MultiLineStringUIEditor.MultilineStringGUI = (title, text) => EditText(title, text, multiline: true);
            ColorUIEditor.ColorGUI = PickColor;
            ViewUIEditor.ViewSelectionGUI = PickView;
            SpriteSelectUIEditor.SpriteSelectionGUI = PickSprite;
            CustomResolutionUIEditor.CustomResolutionSetGUI = PickResolution;
            StringListUIEditor.StringListGUI = PickFromList;
            AGS.Types.PropertyGridExtras.SpriteFileNameEditor.SpriteFileNameGUI = PickSpriteSourceFile;
            CustomPropertiesUIEditor.CustomPropertiesGUI = EditCustomProperties;
            AudioClipSourceFileUIEditor.AudioClipSourceFileGUI = ReplaceAudioClipSource;
            RoomMessagesUIEditor.ShowRoomMessagesEditor = messages => EditRoomMessages(messages);
        }

        /// <summary>
        /// RoomMessagesUIEditor hook (upstream RoomMessagesEditor): the room's message list with text, "display as"
        /// (normal text or a character's speech) and the two flags. Messages are edited in place like upstream;
        /// "Add message" appends, "Delete message" only removes the last one (message numbers stay stable).
        /// </summary>
        public void EditRoomMessages(IList<RoomMessage> messages)
        {
            Game game = _project()?.Game;
            if (messages == null) return;
            var rows = new System.Collections.ObjectModel.ObservableCollection<MessageRow>();
            var list = new ListBox { MinHeight = 260, ItemsSource = rows };
            void Repopulate(int select)
            {
                rows.Clear();
                foreach (RoomMessage m in messages)
                    rows.Add(new MessageRow(m));
                list.SelectedIndex = select;
            }
            var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90 };
            var displayAs = new ComboBox { HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch };
            var characters = game?.RootCharacterFolder.AllItemsFlat.OrderBy(c => c.ID).ToList() ?? new List<Character>();
            displayAs.ItemsSource = new[] { "Normal text" }.Concat(characters.Select(c => "Character " + c.RealName)).ToList();
            var autoRemove = new CheckBox { Content = "Automatically remove message after a time limit" };
            var showNext = new CheckBox { Content = "Display next message after this one" };
            var details = new StackPanel { Spacing = 6, IsEnabled = false, Children =
            {
                new TextBlock { Text = "Message text:" }, text,
                new TextBlock { Text = "Display as:" }, displayAs, autoRemove, showNext,
            } };
            bool loading = false;
            RoomMessage Current() => (list.SelectedItem as MessageRow)?.Message;
            list.SelectionChanged += (s, e) =>
            {
                if (loading) return; // a row being replaced while typing
                RoomMessage m = Current();
                details.IsEnabled = m != null;
                if (m == null) return;
                loading = true;
                text.Text = m.Text;
                autoRemove.IsChecked = m.AutoRemoveAfterTime;
                showNext.IsChecked = m.DisplayNextMessageAfter;
                displayAs.SelectedIndex = m.ShowAsSpeech && m.CharacterID >= 0 && m.CharacterID < characters.Count ? m.CharacterID + 1 : 0;
                loading = false;
            };
            text.PropertyChanged += (s, e) =>
            {
                if (e.Property != TextBox.TextProperty || loading || Current() is not RoomMessage m) return;
                m.Text = text.Text ?? string.Empty;
                // show the new text in the list (rows are immutable: replace this one)
                int index = list.SelectedIndex;
                loading = true;
                rows[index] = new MessageRow(m);
                list.SelectedIndex = index;
                loading = false;
            };
            autoRemove.IsCheckedChanged += (s, e) => { if (!loading && Current() is RoomMessage m) m.AutoRemoveAfterTime = autoRemove.IsChecked == true; };
            showNext.IsCheckedChanged += (s, e) => { if (!loading && Current() is RoomMessage m) m.DisplayNextMessageAfter = showNext.IsChecked == true; };
            displayAs.SelectionChanged += (s, e) =>
            {
                if (loading || Current() is not RoomMessage m || displayAs.SelectedIndex < 0) return;
                m.ShowAsSpeech = displayAs.SelectedIndex > 0;
                m.CharacterID = displayAs.SelectedIndex - 1;
            };
            var add = new Button { Content = "Add message" };
            var delete = new Button { Content = "Delete message" };
            add.Click += (s, e) =>
            {
                messages.Add(new RoomMessage(messages.Count));
                Repopulate(messages.Count - 1);
                text.Focus();
            };
            delete.Click += (s, e) =>
            {
                if (messages.Count == 0) return;
                if (Confirm("Delete message", "Are you sure you want to delete this message?"))
                {
                    messages.RemoveAt(messages.Count - 1);
                    Repopulate(messages.Count - 1);
                }
            };
            // as upstream: only the last message can be deleted
            list.SelectionChanged += (s, e) => delete.IsEnabled = list.SelectedIndex >= 0 && list.SelectedIndex == messages.Count - 1;
            delete.IsEnabled = false;
            Repopulate(messages.Count > 0 ? 0 : -1);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0), Children = { add, delete } };
            var left = new DockPanel { Width = 300, Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(buttons, global::Avalonia.Controls.Dock.Bottom);
            left.Children.Add(buttons);
            left.Children.Add(list);
            var panel = new DockPanel();
            DockPanel.SetDock(left, global::Avalonia.Controls.Dock.Left);
            panel.Children.Add(left);
            panel.Children.Add(details);
            ShowModal("Room messages", panel, () => { }, 720, 420);
        }

        private sealed class MessageRow
        {
            public MessageRow(RoomMessage message) { Message = message; }
            public RoomMessage Message { get; }
            public override string ToString()
            {
                string t = (Message.Text ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
                return $"{Message.ID}: {(t.Length > 50 ? t.Substring(0, 50) + "…" : t)}";
            }
        }

        /// <summary>
        /// CustomPropertiesUIEditor hook (upstream CustomPropertiesEditor): a property grid of the schema properties
        /// that apply to the object; values are changed directly, like in the upstream dialog (OK only closes).
        /// </summary>
        public void EditCustomProperties(CustomProperties properties, object objectThatHasProperties)
        {
            Game game = _project()?.Game;
            if (game == null || properties == null) return;
            var grid = new ViewModels.PropertyGridViewModel(message => EditorMessages.ShowWarning(message, "Custom properties"));
            var empty = new TextBlock { Text = "The game has no custom properties for this kind of item. Use \"Edit Schema...\" to add some.", TextWrapping = TextWrapping.Wrap };
            void Refresh()
            {
                properties.SyncWithSchema();
                grid.Component = null;
                grid.Component = CustomPropertiesEditing.CreatePropertyBag(game, properties, objectThatHasProperties);
                empty.IsVisible = grid.Entries.Count == 0;
            }
            Refresh();
            var editSchema = new Button { Content = "Edit Schema...", HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left };
            editSchema.Click += (s, e) =>
            {
                if (EditPropertySchema(game.PropertySchema))
                    Refresh();
            };
            var panel = new DockPanel();
            DockPanel.SetDock(editSchema, global::Avalonia.Controls.Dock.Bottom);
            DockPanel.SetDock(empty, global::Avalonia.Controls.Dock.Top);
            panel.Children.Add(editSchema);
            panel.Children.Add(empty);
            panel.Children.Add(new Views.PropertyGridView { DataContext = grid, MinHeight = 200 });
            ShowModal("Custom properties", panel, () => { }, 460, 440);
        }

        /// <summary>
        /// CustomPropertySchemaEditor + CustomPropertySchemaItemEditor: add, edit and delete schema properties on a
        /// copy that replaces the schema on OK. Renaming existing properties (which rewrites every room) is not
        /// available yet. Returns true when the schema was changed.
        /// </summary>
        public bool EditPropertySchema(CustomPropertySchema schema)
        {
            var copy = new CustomPropertySchema(schema);
            var changes = new List<CustomPropertyDefChange>(); // CustomPropertySchemaEditor.SchemaChanges
            var list = new ListBox { MinHeight = 220 };
            void Repopulate()
            {
                list.ItemsSource = copy.PropertyDefinitions.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new SchemaRow(i)).ToList();
            }
            Repopulate();
            var add = new Button { Content = "Add new property..." };
            var edit = new Button { Content = "Edit property..." };
            var delete = new Button { Content = "Delete property..." };
            add.Click += (s, e) =>
            {
                var item = new CustomPropertySchemaItem { Type = CustomPropertyType.Boolean };
                if (EditSchemaItem(item, isNewItem: true, copy))
                {
                    copy.PropertyDefinitions.Add(item);
                    changes.Add(new CustomPropertyDefChange(CustomPropertyDefChangeType.Add, null, item.Name));
                    Repopulate();
                }
            };
            edit.Click += (s, e) =>
            {
                if (list.SelectedItem is not SchemaRow row) return;
                string oldName = row.Item.Name;
                if (EditSchemaItem(row.Item, isNewItem: false, copy))
                {
                    changes.Add(new CustomPropertyDefChange(CustomPropertyDefChangeType.Edit, oldName, row.Item.Name));
                    Repopulate();
                }
            };
            delete.Click += (s, e) =>
            {
                if (list.SelectedItem is SchemaRow row &&
                    Confirm("Confirm delete", "Are you sure you want to delete this entry from the schema? It will be removed from all objects in the game that currently have this property set."))
                {
                    copy.PropertyDefinitions.Remove(row.Item);
                    changes.Add(new CustomPropertyDefChange(CustomPropertyDefChangeType.Remove, row.Item.Name, null));
                    Repopulate();
                }
            };
            list.DoubleTapped += (s, e) => edit.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0), Children = { add, edit, delete } };
            var panel = new DockPanel();
            DockPanel.SetDock(buttons, global::Avalonia.Controls.Dock.Bottom);
            panel.Children.Add(buttons);
            panel.Children.Add(list);
            if (!ShowModal("Custom properties schema", panel, () => { }, 620, 420))
                return false;
            // CustomPropertiesComponent.ShowSchemaEditor
            if (!CustomPropertySchemaChanges.HasRenames(changes))
            {
                schema.CopyFrom(copy);
                return true;
            }
            if (!Confirm("Rename custom properties", "You have renamed some of the custom properties. Editor will have to apply modifications to each object in game which has these properties, including objects in Rooms. This process may take some time, because each Room will have to be loaded and resaved.\n\n"
                + "If you cancel this operation now, the Custom Properties Schema will be reverted to its previous state in order to prevent mismatches."))
                return false;
            if (ApplySchemaRenames == null)
            {
                EditorMessages.ShowWarning("Renaming properties is not available here.", "Custom properties");
                return false;
            }
            ApplySchemaRenames(copy, changes);
            return true;
        }

        /// <summary>Set by the app: applies a schema with renamed properties to the game and all rooms.</summary>
        public Action<CustomPropertySchema, List<CustomPropertyDefChange>> ApplySchemaRenames { get; set; }

        private sealed class SchemaRow
        {
            public SchemaRow(CustomPropertySchemaItem item) { Item = item; }
            public CustomPropertySchemaItem Item { get; }
            public override string ToString() =>
                $"{Item.Name}  ({Item.Type}, default \"{Item.DefaultValue}\", applies to {Item.AppliesToAsString}" +
                (Item.Type == CustomPropertyType.Text ? (Item.Translated ? ", translated)" : ")") : ")") +
                (string.IsNullOrEmpty(Item.Description) ? "" : " — " + Item.Description);
        }

        /// <summary>CustomPropertySchemaItemEditor, with its validation; edits <paramref name="item"/> on OK.</summary>
        private bool EditSchemaItem(CustomPropertySchemaItem item, bool isNewItem, CustomPropertySchema schema)
        {
            var name = new TextBox { Text = item.Name, IsEnabled = isNewItem };
            var description = new TextBox { Text = item.Description };
            var types = new[] { CustomPropertyType.Boolean, CustomPropertyType.Number, CustomPropertyType.Text };
            var type = new ComboBox { ItemsSource = types, SelectedItem = item.Type, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch };
            var defaultValue = new TextBox { Text = item.DefaultValue };
            var characters = new CheckBox { Content = "Characters", IsChecked = item.AppliesToCharacters };
            var hotspots = new CheckBox { Content = "Hotspots", IsChecked = item.AppliesToHotspots };
            var inventory = new CheckBox { Content = "Inventory items", IsChecked = item.AppliesToInvItems };
            var objects = new CheckBox { Content = "Objects", IsChecked = item.AppliesToObjects };
            var rooms = new CheckBox { Content = "Rooms", IsChecked = item.AppliesToRooms };
            var translated = new CheckBox { Content = "Translated (text properties)", IsChecked = item.Translated };
            void UpdateTranslated() => translated.IsEnabled = (CustomPropertyType?)type.SelectedItem == CustomPropertyType.Text;
            type.SelectionChanged += (s, e) => UpdateTranslated();
            UpdateTranslated();
            var panel = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    Labeled("Name", name), Labeled("Description", description), Labeled("Type", type), Labeled("Default", defaultValue),
                    new TextBlock { Text = "Applies to:", Margin = new Thickness(0, 6, 0, 0) },
                    new WrapPanel { Children = { characters, hotspots, inventory, objects, rooms } },
                    translated,
                },
            };
            if (!isNewItem)
            {
                // CustomPropertySchemaItemEditor.btnRename_Click
                var rename = new Button { Content = "Rename..." };
                rename.Click += (s, e) =>
                {
                    if (Confirm("Confirm rename", "Are you sure you want to rename this property? Editor will automatically replace it in all objects in the game that currently have this property set (including rooms)."
                        + "\nBut you will have to fix all of its uses in script by hand!"))
                    {
                        name.IsEnabled = true;
                        rename.IsEnabled = false;
                    }
                };
                panel.Children.Insert(1, rename);
            }

            while (ShowModal(isNewItem ? "New property" : "Edit property", panel, () => { }, 460, 420))
            {
                string newName = name.Text?.Trim() ?? string.Empty;
                var propertyType = (CustomPropertyType)type.SelectedItem;
                string defaultText = defaultValue.Text ?? string.Empty;
                string error = null;
                if (newName == string.Empty)
                    error = "You must enter a name for the new property.";
                else if (schema.PropertyDefinitions.Find(pd => pd != item && pd.Name.ToLowerInvariant() == newName.ToLowerInvariant()) != null)
                    error = "You already have a property with this name.";
                else if (propertyType == CustomPropertyType.Boolean && defaultText != string.Empty && defaultText != "1" && defaultText != "0")
                    error = "The default value for a Boolean item must be 0 or 1.";
                else if (propertyType == CustomPropertyType.Number && !int.TryParse(defaultText, out _))
                    error = "The default value for a Number item must be a valid integer.";
                if (error != null)
                {
                    EditorMessages.ShowWarning(error, "Validation error");
                    continue;
                }
                item.Name = newName;
                item.Description = description.Text ?? string.Empty;
                item.Type = propertyType;
                item.DefaultValue = defaultText;
                item.AppliesToCharacters = characters.IsChecked == true;
                item.AppliesToHotspots = hotspots.IsChecked == true;
                item.AppliesToInvItems = inventory.IsChecked == true;
                item.AppliesToObjects = objects.IsChecked == true;
                item.AppliesToRooms = rooms.IsChecked == true;
                item.Translated = translated.IsChecked == true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// SpriteFileNameEditor hook: the OS open panel for one image file. Paths inside the project are stored
        /// relative (with '\\', see ProjectPaths.ToStored); upstream's FileNameEditor stores the absolute path.
        /// </summary>
        public string PickSpriteSourceFile(string currentFileName, string fileFilter)
        {
            string path = PickFileBlocking("Select sprite source image", "Images",
                new[] { "png", "bmp", "pcx", "gif", "jpg", "jpeg", "tif", "tiff" });
            string projectDir = _project()?.Directory;
            if (path == null) return null;
            return projectDir == null ? path : ProjectPaths.ToStored(ProjectPaths.GetRelativeToBasePath(path, projectDir));
        }

        /// <summary>AudioClipSourceFileUIEditor hook (AudioComponent.ReplaceAudioClipSource).</summary>
        public AudioClip ReplaceAudioClipSource(AudioClip clip)
        {
            GameProject project = _project();
            if (project == null || clip == null) return clip;
            string selectedFile = PickFileBlocking("Select audio to add", "Audio", AudioOperations.SupportedExtensions);
            if (string.IsNullOrEmpty(selectedFile)) return clip; // cancelled
            try
            {
                AudioOperations.SetAudioClipSourceFile(project, clip, selectedFile);
            }
            catch (Exception ex)
            {
                EditorMessages.ShowWarning("Failed to replace audio clip source file: " + ex.Message, "Audio");
            }
            return clip;
        }

        /// <summary>
        /// The OS open panel for one file, waited for synchronously (property editor hooks are synchronous).
        /// An extension "*" allows any file name (the macOS panel only filters by extension).
        /// </summary>
        private string PickFileBlocking(string title, string typeName, string[] extensions)
        {
            Window owner = _owner();
            if (owner == null) return null;
            var options = new global::Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new global::Avalonia.Platform.Storage.FilePickerFileType(typeName) { Patterns = extensions.Select(e => e == "*" ? "*" : "*." + e).ToArray() },
                },
            };
            Task<IReadOnlyList<global::Avalonia.Platform.Storage.IStorageFile>> task = owner.StorageProvider.OpenFilePickerAsync(options);
            if (!task.IsCompleted)
            {
                var frame = new DispatcherFrame();
                task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
                Dispatcher.UIThread.PushFrame(frame);
            }
            var file = task.IsCompletedSuccessfully ? task.Result.FirstOrDefault() : null;
            return file == null ? null : global::Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(file);
        }

        // --- hooks -------------------------------------------------------------------------------

        public string EditText(string title, string text, bool multiline)
        {
            var box = new TextBox
            {
                Text = text ?? string.Empty,
                AcceptsReturn = multiline,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinHeight = multiline ? 160 : 0,
            };
            string result = null;
            ShowModal(title ?? "Edit text", box, () => result = box.Text, 460, multiline ? 300 : 150);
            return result ?? text; // cancelled: keep the original
        }

        public DrawingColor? PickColor(DrawingColor? current)
        {
            var c = current ?? DrawingColor.Black;
            var view = new ColorView
            {
                Color = Color.FromArgb(255, c.R, c.G, c.B),
                IsAlphaEnabled = false,
                IsAlphaVisible = false,
            };
            DrawingColor? result = null;
            ShowModal("Choose colour", view, () => result = DrawingColor.FromArgb(view.Color.R, view.Color.G, view.Color.B), 520, 470);
            return result;
        }

        public int PickView(int currentView)
        {
            Game game = _project()?.Game;
            if (game == null) return currentView;
            var items = new List<(string Label, int Value)> { ("(none)", 0) };
            items.AddRange(game.RootViewFolder.AllItemsFlat.OrderBy(v => v.ID).Select(v => ($"{v.ID}: {v.Name}", v.ID)));
            return PickFromItems("Select view", items, currentView, null, out int chosen) ? chosen : currentView;
        }

        public int? PickSprite(int? currentSprite)
        {
            GameProject project = _project();
            if (project == null) return null;
            var slots = project.Game.RootSpriteFolder.GetAllSpritesFromAllSubFolders().OrderBy(s => s.Number)
                .Select(s => ($"{s.Number}  ({s.Width}×{s.Height}, {s.ColorDepth}-bit)", s.Number)).ToList();

            SpriteFile sprites = null;
            try { sprites = SpriteFile.OpenInProject(project.Directory); } catch (Exception) { /* no previews */ }
            var palette = project.Game.Palette.Select(p => p.Colour).ToArray();
            try
            {
                Func<int, AgsImage> preview = slot =>
                {
                    try { return sprites?.LoadSprite(slot, palette); } catch (Exception) { return null; }
                };
                return PickFromItems("Select sprite", slots, currentSprite ?? 0, preview, out int chosen) ? chosen : (int?)null;
            }
            finally
            {
                sprites?.Dispose();
            }
        }

        public DrawingSize PickResolution(DrawingSize current)
        {
            var width = new NumericUpDown { Value = current.Width, Minimum = 1, Maximum = 10000, FormatString = "0" };
            var height = new NumericUpDown { Value = current.Height, Minimum = 1, Maximum = 10000, FormatString = "0" };
            var panel = new StackPanel { Spacing = 8, Children = { Labeled("Width", width), Labeled("Height", height) } };
            DrawingSize result = current;
            ShowModal("Custom resolution", panel, () => result = new DrawingSize((int)(width.Value ?? current.Width), (int)(height.Value ?? current.Height)), 320, 200);
            return result;
        }

        /// <summary>StringListUIEditor hook: a checked list; returns the joined selection.</summary>
        public string PickFromList(string value, IList<string> valueList, Predicate<string> canUncheck)
        {
            var selected = new HashSet<string>((value ?? string.Empty).Split(StringListUIEditor.Separators, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            var boxes = valueList.Select(v => new CheckBox { Content = v, IsChecked = selected.Contains(v) }).ToList();
            foreach (CheckBox box in boxes)
            {
                box.IsCheckedChanged += (s, e) =>
                {
                    if (box.IsChecked == false && !canUncheck((string)box.Content))
                        box.IsChecked = true;
                };
            }
            string result = value;
            ShowModal("Select", new StackPanel { Spacing = 4, Children = { } }.With(p => p.Children.AddRange(boxes)),
                () => result = string.Join(StringListUIEditor.Separators[0], boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content)), 320, 120 + 32 * boxes.Count);
            return result;
        }

        /// <summary>
        /// Sprite import options (the transparency part of upstream's SpriteImportWindow); null when cancelled.
        /// </summary>
        public (SpriteImportTransparency Transparency, bool ImportAlpha)? PickSpriteImportOptions(
            SpriteImportTransparency transparency, bool importAlpha, int fileCount)
        {
            // SpriteImportWindow's radio buttons (palette index modes only make sense in 256-colour games)
            var modes = new List<(SpriteImportTransparency Mode, string Label)>
            {
                (SpriteImportTransparency.LeaveAsIs, "Leave as-is"),
                (SpriteImportTransparency.TopLeft, "Top-left pixel"),
                (SpriteImportTransparency.BottomLeft, "Bottom-left pixel"),
                (SpriteImportTransparency.TopRight, "Top-right pixel"),
                (SpriteImportTransparency.BottomRight, "Bottom-right pixel"),
                (SpriteImportTransparency.NoTransparency, "No transparency"),
            };
            if (_project()?.Game.Settings.ColorDepth == GameColorDepth.Palette)
                modes.Insert(1, (SpriteImportTransparency.PaletteIndex0, "Palette index 0"));
            var buttons = modes.Select(m => new RadioButton { Content = m.Label, GroupName = "transparency", IsChecked = m.Mode == transparency }).ToList();
            if (!buttons.Any(b => b.IsChecked == true)) buttons[0].IsChecked = true;
            var alpha = new CheckBox { Content = "Import alpha channel (32-bit games)", IsChecked = importAlpha };
            var description = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 };
            void Describe()
            {
                int i = buttons.FindIndex(b => b.IsChecked == true);
                description.Text = i < 0 ? "" : (typeof(SpriteImportTransparency).GetField(modes[i].Mode.ToString())
                    .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
                    .OfType<System.ComponentModel.DescriptionAttribute>().FirstOrDefault()?.Description ?? "");
            }
            foreach (RadioButton b in buttons) b.IsCheckedChanged += (s, e) => Describe();
            Describe();
            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = $"Transparent colour ({fileCount} file{(fileCount == 1 ? "" : "s")}):", FontWeight = FontWeight.SemiBold });
            panel.Children.AddRange(buttons);
            panel.Children.Add(description);
            panel.Children.Add(alpha);
            (SpriteImportTransparency, bool)? result = null;
            ShowModal("Import sprites", panel, () =>
            {
                int i = buttons.FindIndex(b => b.IsChecked == true);
                result = (modes[Math.Max(0, i)].Mode, alpha.IsChecked == true);
            }, 440, 420);
            return result;
        }

        /// <summary>The OS folder panel, waited for synchronously; null when cancelled.</summary>
        public string PickFolderSync(string title, string startFolder)
        {
            Window owner = _owner();
            if (owner == null) return null;
            var options = new global::Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = title, AllowMultiple = false };
            if (startFolder != null && System.IO.Directory.Exists(startFolder))
            {
                Task<global::Avalonia.Platform.Storage.IStorageFolder> start = owner.StorageProvider.TryGetFolderFromPathAsync(new Uri(startFolder));
                WaitFor(start);
                if (start.IsCompletedSuccessfully) options.SuggestedStartLocation = start.Result;
            }
            Task<IReadOnlyList<global::Avalonia.Platform.Storage.IStorageFolder>> task = owner.StorageProvider.OpenFolderPickerAsync(options);
            WaitFor(task);
            var folder = task.IsCompletedSuccessfully ? task.Result.FirstOrDefault() : null;
            return folder == null ? null : global::Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(folder);
        }

        /// <summary>Upstream SpriteExportDialog: folder and file name pattern, source folder, skip rules and sprite properties.</summary>
        public (string ExportPath, bool UseRootFolder, bool Recurse, SpriteExport.SkipIf SkipIf, bool UpdateSource, bool ResetTiles)? AskSpriteExport(string gameDirectory)
        {
            var folder = new TextBox { Text = gameDirectory, Width = 360 };
            var browse = new Button { Content = "..." };
            browse.Click += (s, e) =>
            {
                string chosen = PickFolderSync("Export sprites to folder", folder.Text);
                if (chosen != null) folder.Text = chosen;
            };
            var fileName = new TextBox { Text = "%Number%", Width = 360 };
            var thisFolder = new RadioButton { Content = "This sprite folder", GroupName = "exportfrom", IsChecked = true };
            var rootFolder = new RadioButton { Content = "Root sprite folder", GroupName = "exportfrom" };
            var recurse = new CheckBox { Content = "Include sub-folders" };
            var skipValid = new CheckBox { Content = "Sprite source file is valid" };
            var skipLocal = new CheckBox { Content = "Sprite source file is inside the project folder" };
            var update = new CheckBox { Content = "Set exported file as sprite source" };
            var reset = new CheckBox { Content = "Reset tile settings" };
            TextBlock Header(string text) => new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
            var panel = new StackPanel { Spacing = 4, Children =
            {
                Header("Save as"),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = "Directory:", Width = 70, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center }, folder, browse } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = "Filename:", Width = 70, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center }, fileName } },
                new TextBlock { Text = "Tokens: %Number%, %Width%, %Height%, %ColorDepth% … (any sprite property); the extension is added (.bmp or .png).", Opacity = 0.6, FontSize = 11, TextWrapping = TextWrapping.Wrap },
                Header("Export from"), thisFolder, rootFolder, recurse,
                Header("Skip if..."), skipValid, skipLocal,
                Header("Sprite properties"), update, reset,
            } };
            (string, bool, bool, SpriteExport.SkipIf, bool, bool)? result = null;
            ShowModal("Sprite export", panel, () =>
            {
                result = (System.IO.Path.Combine(folder.Text ?? string.Empty, fileName.Text ?? "%Number%"), rootFolder.IsChecked == true, recurse.IsChecked == true,
                    (skipValid.IsChecked == true ? SpriteExport.SkipIf.SourceValid : 0) | (skipLocal.IsChecked == true ? SpriteExport.SkipIf.SourceLocal : 0),
                    update.IsChecked == true, reset.IsChecked == true);
            }, 560, 560);
            return result;
        }

        /// <summary>ReplaceFolderDialog: the part of a path to replace and its replacement; null when cancelled.</summary>
        public (string Old, string New)? AskReplaceFolder(string title, string info, string oldPath, string newPath)
        {
            var oldBox = new TextBox { Text = oldPath ?? string.Empty };
            var newBox = new TextBox { Text = newPath ?? string.Empty };
            var browse = new Button { Content = "..." };
            browse.Click += (s, e) =>
            {
                string chosen = PickFolderSync("Choose the new folder", _project()?.Directory);
                if (chosen != null)
                {
                    string dir = _project()?.Directory;
                    newBox.Text = dir == null ? chosen : ProjectPaths.ToStored(ProjectPaths.GetRelativeToBasePath(chosen, dir));
                }
            };
            var newRow = new DockPanel();
            DockPanel.SetDock(browse, global::Avalonia.Controls.Dock.Right);
            newRow.Children.Add(browse);
            newRow.Children.Add(newBox);
            var panel = new StackPanel { Spacing = 6, Children =
            {
                new TextBlock { Text = info, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Replace this part of the path:" }, oldBox,
                new TextBlock { Text = "With:" }, newRow,
            } };
            (string, string)? result = null;
            ShowModal(title, panel, () => result = (oldBox.Text ?? string.Empty, newBox.Text ?? string.Empty), 560, 300);
            return result;
        }

        // SpriteImportWindow remembers its tiling settings
        private static (int X, int Y, int W, int H, int MX, int MY, SpriteImportTilingDirection Dir, int Max) _lastTiling = (0, 0, 32, 32, 0, 0, SpriteImportTilingDirection.Right, 1000);

        /// <summary>
        /// The tiled part of upstream's SpriteImportWindow: offset, tile size, margin between tiles, direction and the
        /// maximum number of tiles, with a preview of the image and the tiles that will be cut (SpriteSheet).
        /// Returns the sprite sheet or null when cancelled.
        /// </summary>
        public AGS.Editor.Utils.SpriteSheet AskSpriteSheet(string imageFile)
        {
            AGS.Imaging.PixelImage image = new NativeImageCodec().Decode(imageFile).Frames[0];
            var size = new System.Drawing.Size(image.Width, image.Height);
            NumericUpDown Number(int value, int min = 0) => new NumericUpDown { Value = value, Minimum = min, Maximum = 100000, Increment = 1, FormatString = "0", Width = 110 };
            var t = _lastTiling;
            var offsetX = Number(t.X); var offsetY = Number(t.Y);
            var width = Number(Math.Min(t.W, image.Width), 1); var height = Number(Math.Min(t.H, image.Height), 1);
            var marginX = Number(t.MX); var marginY = Number(t.MY);
            var direction = new ComboBox { ItemsSource = new[] { "Right", "Down" }, SelectedIndex = t.Dir == SpriteImportTilingDirection.Down ? 1 : 0, Width = 110 };
            var max = Number(t.Max, 1);
            var count = new TextBlock { Opacity = 0.8 };

            int zoom = Math.Max(1, Math.Min(4, 480 / Math.Max(image.Width, image.Height)));
            var picture = new Image { Source = ImageConversion.ToBitmap(image, transparentMagenta: true), Width = image.Width * zoom, Height = image.Height * zoom, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapInterpolationMode(picture, global::Avalonia.Media.Imaging.BitmapInterpolationMode.None);
            var overlay = new Canvas { Width = image.Width * zoom, Height = image.Height * zoom, IsHitTestVisible = false };
            var preview = new ScrollViewer
            {
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new Panel { Children = { picture, overlay } },
                Background = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),
            };

            AGS.Editor.Utils.SpriteSheet Sheet() => new AGS.Editor.Utils.SpriteSheet(
                new System.Drawing.Point((int)(offsetX.Value ?? 0), (int)(offsetY.Value ?? 0)),
                new System.Drawing.Size((int)(width.Value ?? 1), (int)(height.Value ?? 1)),
                new System.Drawing.Size((int)(marginX.Value ?? 0), (int)(marginY.Value ?? 0)),
                direction.SelectedIndex == 1 ? SpriteImportTilingDirection.Down : SpriteImportTilingDirection.Right,
                (int)(max.Value ?? 1));
            void Refresh()
            {
                overlay.Children.Clear();
                var rects = Sheet().GetSpriteSelections(size).ToList();
                foreach (System.Drawing.Rectangle r in rects)
                {
                    var box = new Border { Width = r.Width * zoom, Height = r.Height * zoom, BorderBrush = Brushes.HotPink, BorderThickness = new Thickness(1) };
                    Canvas.SetLeft(box, r.X * zoom);
                    Canvas.SetTop(box, r.Y * zoom);
                    overlay.Children.Add(box);
                }
                count.Text = $"{rects.Count} sprite(s) will be imported from the {image.Width}×{image.Height} image.";
            }
            foreach (NumericUpDown n in new[] { offsetX, offsetY, width, height, marginX, marginY, max })
                n.ValueChanged += (s, e) => Refresh();
            direction.SelectionChanged += (s, e) => Refresh();
            Refresh();

            Control Pair(string label, Control a, Control b) => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children =
                { new TextBlock { Text = label, Width = 110, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center }, a, b } };
            var settings = new StackPanel { Spacing = 6, Width = 360, Margin = new Thickness(0, 0, 12, 0), Children =
            {
                Pair("Offset (x, y):", offsetX, offsetY),
                Pair("Tile size (w, h):", width, height),
                Pair("Margin (x, y):", marginX, marginY),
                Pair("Direction:", direction, new Control()),
                Pair("Max. tiles:", max, new Control()),
                count,
            } };
            var panel = new DockPanel();
            DockPanel.SetDock(settings, global::Avalonia.Controls.Dock.Left);
            panel.Children.Add(settings);
            panel.Children.Add(preview);
            AGS.Editor.Utils.SpriteSheet result = null;
            ShowModal("Import from sprite sheet", panel, () =>
            {
                result = Sheet();
                _lastTiling = ((int)(offsetX.Value ?? 0), (int)(offsetY.Value ?? 0), (int)(width.Value ?? 1), (int)(height.Value ?? 1),
                    (int)(marginX.Value ?? 0), (int)(marginY.Value ?? 0), direction.SelectedIndex == 1 ? SpriteImportTilingDirection.Down : SpriteImportTilingDirection.Right, (int)(max.Value ?? 1));
            }, 980, 620);
            image.Dispose();
            return result;
        }

        /// <summary>GlobalVariableDialog: name, type, default value, array; validated like upstream. True on OK.</summary>
        public bool EditGlobalVariable(GlobalVariable variable)
        {
            Game game = _project()?.Game;
            if (game == null) return false;
            var types = GlobalVariableEditing.VariableTypes(GlobalVariableEditing.ApiTypes());
            var name = new TextBox { Text = variable.Name ?? string.Empty };
            var type = new ComboBox { ItemsSource = types, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                SelectedItem = types.FirstOrDefault(t => t.TypeName == variable.Type) ?? types[0] };
            var defaultValue = new TextBox { Text = variable.DefaultValue ?? string.Empty };
            var array = new ComboBox { ItemsSource = new[] { "No array", "Fixed-size array", "Dynamic array" },
                SelectedIndex = (int)variable.ArrayType, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch };
            var size = new NumericUpDown { Value = Math.Max(1, variable.ArraySize), Minimum = 1, Maximum = int.MaxValue, FormatString = "0" };
            void Update()
            {
                var t = (GlobalVariableEditing.VariableType)type.SelectedItem;
                bool isArray = array.SelectedIndex != 0;
                size.IsEnabled = array.SelectedIndex == 1;
                defaultValue.IsEnabled = !isArray && t != null && t.CanHaveDefaultValue;
                if (!defaultValue.IsEnabled) defaultValue.Text = string.Empty;
            }
            type.SelectionChanged += (s, e) => Update();
            array.SelectionChanged += (s, e) => Update();
            Update();
            var panel = new StackPanel
            {
                Spacing = 8,
                Children = { Labeled("Name", name), Labeled("Type", type), Labeled("Default", defaultValue), Labeled("Array", array), Labeled("Size", size) },
            };
            while (ShowModal(string.IsNullOrEmpty(variable.Name) ? "New global variable" : "Edit global variable", panel, () => { }, 420, 320))
            {
                string error = GlobalVariableEditing.Apply(game, variable, name.Text, (GlobalVariableEditing.VariableType)type.SelectedItem,
                    defaultValue.Text, (GlobalVariableArrayType)Math.Max(0, array.SelectedIndex), (int)(size.Value ?? 1));
                if (error == null)
                    return true;
                EditorMessages.ShowWarning(error, "Global variable");
            }
            return false;
        }

        /// <summary>Single-line text input; returns null when cancelled.</summary>
        public string PromptLine(string title, string text)
        {
            var box = new TextBox { Text = text ?? string.Empty };
            string result = null;
            ShowModal(title, box, () => result = box.Text, 420, 150);
            return result;
        }

        /// <summary>
        /// ImportTTFDialog: nominal (point) size, or the full pixel height to find the closest size for.
        /// Returns null when cancelled.
        /// </summary>
        public (bool PixelHeight, int Value)? AskTtfSize(int currentSize)
        {
            var nominal = new RadioButton { Content = "Nominal size:", IsChecked = true, GroupName = "ttfsize" };
            var pixel = new RadioButton { Content = "Find closest size for this full height:", GroupName = "ttfsize" };
            var nominalValue = new NumericUpDown { Value = currentSize, Minimum = 1, Maximum = int.MaxValue, Increment = 1, FormatString = "0", Width = 120 };
            var pixelValue = new NumericUpDown { Value = currentSize, Minimum = 1, Maximum = int.MaxValue, Increment = 1, FormatString = "0", Width = 120, IsEnabled = false };
            nominal.IsCheckedChanged += (s, e) =>
            {
                nominalValue.IsEnabled = nominal.IsChecked == true;
                pixelValue.IsEnabled = nominal.IsChecked != true;
            };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), RowSpacing = 8 };
            var header = new TextBlock { Text = "Select the method to import this TTF font:" };
            Grid.SetColumnSpan(header, 2);
            grid.Children.Add(header);
            Grid.SetRow(nominal, 1); grid.Children.Add(nominal);
            Grid.SetRow(nominalValue, 1); Grid.SetColumn(nominalValue, 1); grid.Children.Add(nominalValue);
            Grid.SetRow(pixel, 2); grid.Children.Add(pixel);
            Grid.SetRow(pixelValue, 2); Grid.SetColumn(pixelValue, 1); grid.Children.Add(pixelValue);
            (bool, int)? result = null;
            ShowModal("Import TTF font", grid, () =>
            {
                bool byPixels = pixel.IsChecked == true;
                result = (byPixels, (int)((byPixels ? pixelValue.Value : nominalValue.Value) ?? currentSize));
            }, 480, 210);
            return result;
        }

        /// <summary>
        /// The two pages of upstream's "Number Speech Lines" wizard (AutoNumberSpeechWizardPage/Page2) in one dialog.
        /// Returns (narrator, combine identical lines, remove numbering, character ID or null for all) or null.
        /// </summary>
        public (bool DoNarrator, bool CombineIdenticalLines, bool RemoveNumbering, int? CharacterID)? AskSpeechNumbering(Game game)
        {
            TextBlock Text(string t) => new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap };
            var narrator = new CheckBox { Content = "Yes, I will have voice files for the narrator, so number them too." };
            var combine = new CheckBox { Content = "Yes, give identical lines of text the same voice files.", IsChecked = true };
            var characters = game.RootCharacterFolder.AllItemsFlat.ToList();
            var which = new ComboBox
            {
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                ItemsSource = new[] { "Re-number everything", "Only re-number the narrator" }
                    .Concat(characters.Select(c => "Only re-number " + c.RealName)).ToList(),
                SelectedIndex = 0,
            };
            var remove = new CheckBox { Content = "Remove speech numbers from all game text for the above character(s)" };
            var panel = new StackPanel { Spacing = 8, Children =
            {
                Text("This process automatically assigns speech numbers to all displayable text in the game. It will modify your dialogs and scripts in order to assign a unique number to each line of text for each character."),
                Text("WARNING: It is STRONGLY RECOMMENDED that you back up your game before continuing, just in case anything goes wrong. Running this process will overwrite any existing speech lines and re-number them all from scratch."),
                Text("Do you want lines of text said by the narrator (Display commands, NARRATOR dialog lines) to be numbered? Enable this option if you intend to have voice speech for your narrator as well as characters."),
                narrator,
                Text("Do you want to use the same voice file if the same character says the same line in two different places? For example, if you had two different lines of script where EGO says \"Hello\", do you want them to be assigned the same voice file?"),
                combine,
                Text("Which characters text to you want to re-number?"),
                which,
                Text("If you have already numbered some speech lines but no longer want them, you can select to remove the speech numbering rather than adding it."),
                remove,
            } };
            (bool, bool, bool, int?)? result = null;
            ShowModal("Number Speech Lines", new ScrollViewer { Content = panel }, () =>
            {
                int? characterID = which.SelectedIndex switch
                {
                    <= 0 => null,
                    1 => Character.NARRATOR_CHARACTER_ID,
                    int i => characters[i - 2].ID,
                };
                result = (narrator.IsChecked == true, combine.IsChecked == true, remove.IsChecked == true, characterID);
            }, 640, 600);
            return result;
        }

        /// <summary>A list to choose one entry from (double-click or OK); returns the index or null.</summary>
        public int? ChooseFromList(string title, IReadOnlyList<string> items)
        {
            var list = new ListBox { ItemsSource = items, SelectedIndex = 0, MinHeight = 260 };
            int? result = null;
            ShowModal(title, list, () => result = list.SelectedIndex >= 0 ? list.SelectedIndex : null, 420, 380, list);
            return result;
        }

        // AssignToView remembers the last destination
        private static int _lastViewNumber, _lastLoopNumber;

        /// <summary>
        /// Upstream AssignToView dialog: destination view and loop, overwrite or add to the loop, what to do when the
        /// loop is full, reverse order and flipped. Returns null when cancelled.
        /// </summary>
        public (int View, int Loop, bool AddToExisting, bool ContinueIntoNextLoop, bool Reverse, bool Flip)? AskAssignToView(Game game)
        {
            var views = game.RootViewFolder.AllItemsFlat.OrderBy(v => v.ID).ToList();
            if (views.Count == 0)
            {
                EditorMessages.ShowWarning("The game has no views.", "Assign Sprites to View");
                return null;
            }
            var view = new ComboBox
            {
                ItemsSource = views.Select(v => $"{v.ID}: {v.Name}").ToList(),
                SelectedIndex = Math.Max(0, views.FindIndex(v => v.ID == _lastViewNumber)),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
            };
            var loop = new NumericUpDown { Value = _lastLoopNumber, Minimum = 0, Maximum = 999, Increment = 1, FormatString = "0", Width = 120 };
            var overwrite = new RadioButton { Content = "Overwrite existing loop", GroupName = "assignmode", IsChecked = true };
            var addToExisting = new RadioButton { Content = "Add frames to existing loop", GroupName = "assignmode" };
            var stop = new RadioButton { Content = "Stop assigning at the last frame", GroupName = "assignfull", IsChecked = true };
            var overwriteNext = new RadioButton { Content = "Automatically overwrite the next loop as well", GroupName = "assignfull" };
            var reverse = new CheckBox { Content = "Add frames in reverse order" };
            var flipped = new CheckBox { Content = "Set all new frames as Flipped" };
            var panel = new StackPanel { Spacing = 6, Children =
            {
                new TextBlock { Text = "The selected sprites will be assigned to the view you choose below:", TextWrapping = TextWrapping.Wrap },
                Labeled("View:", view), Labeled("Loop:", loop),
                new TextBlock { Text = "How would you like to assign the new frames?", Margin = new Thickness(0, 6, 0, 0) },
                overwrite, addToExisting,
                new TextBlock { Text = "If there aren't enough frames available, then:", Margin = new Thickness(0, 6, 0, 0) },
                stop, overwriteNext, reverse, flipped,
            } };
            (int, int, bool, bool, bool, bool)? result = null;
            ShowModal("Assign Sprites to View", panel, () =>
            {
                int viewNumber = views[Math.Max(0, view.SelectedIndex)].ID;
                int loopNumber = (int)(loop.Value ?? 0);
                _lastViewNumber = viewNumber;
                _lastLoopNumber = loopNumber;
                result = (viewNumber, loopNumber, addToExisting.IsChecked == true, overwriteNext.IsChecked == true, reverse.IsChecked == true, flipped.IsChecked == true);
            }, 480, 470);
            return result;
        }

        /// <summary>The OS save panel, waited for synchronously; null when cancelled.</summary>
        public string PickSaveFile(string title, string suggestedPath, string typeName, string extension)
        {
            Window owner = _owner();
            if (owner == null) return null;
            var options = new global::Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = System.IO.Path.GetFileName(suggestedPath),
                DefaultExtension = extension,
                FileTypeChoices = new[] { new global::Avalonia.Platform.Storage.FilePickerFileType(typeName) { Patterns = new[] { "*." + extension } } },
            };
            string folder = System.IO.Path.GetDirectoryName(suggestedPath);
            if (folder != null)
            {
                Task<global::Avalonia.Platform.Storage.IStorageFolder> folderTask = owner.StorageProvider.TryGetFolderFromPathAsync(new Uri(folder));
                WaitFor(folderTask);
                if (folderTask.IsCompletedSuccessfully) options.SuggestedStartLocation = folderTask.Result;
            }
            Task<global::Avalonia.Platform.Storage.IStorageFile> task = owner.StorageProvider.SaveFilePickerAsync(options);
            WaitFor(task);
            var file = task.IsCompletedSuccessfully ? task.Result : null;
            return file == null ? null : global::Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(file);
        }

        private static void WaitFor(Task task)
        {
            if (task.IsCompleted) return;
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }

        /// <summary>The OS open panel for one file, waited for synchronously; null when cancelled.</summary>
        public string PickFile(string title, string typeName, string[] extensions) => PickFileBlocking(title, typeName, extensions);

        /// <summary>Yes/no confirmation (OK = yes).</summary>
        public bool Confirm(string title, string question)
        {
            var text = new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap };
            return ShowModal(title, text, () => { }, 420, 170);
        }

        // --- building blocks -----------------------------------------------------------------------

        private bool PickFromItems(string title, IReadOnlyList<(string Label, int Value)> items, int current,
            Func<int, AgsImage> preview, out int chosen)
        {
            var list = new ListBox { ItemsSource = items.Select(i => i.Label).ToList(), MinHeight = 320 };
            int index = Math.Max(0, items.ToList().FindIndex(i => i.Value == current));
            list.SelectedIndex = index;

            Control content = list;
            if (preview != null)
            {
                var image = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, MaxHeight = 280 };
                RenderOptions.SetBitmapInterpolationMode(image, global::Avalonia.Media.Imaging.BitmapInterpolationMode.None);
                void Update()
                {
                    if (list.SelectedIndex < 0) { image.Source = null; return; }
                    AgsImage img = preview(items[list.SelectedIndex].Value);
                    image.Source = img == null ? null : ImageConversion.ToBitmap(img, transparentMagenta: true);
                }
                list.SelectionChanged += (s, e) => Update();
                Update();
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*") };
                grid.Children.Add(list);
                var frame = new Border { Child = image, Margin = new Thickness(12, 0, 0, 0), Background = Brushes.DimGray, Padding = new Thickness(8) };
                Grid.SetColumn(frame, 1);
                grid.Children.Add(frame);
                content = grid;
            }

            int result = current;
            bool ok = ShowModal(title, content, () => { if (list.SelectedIndex >= 0) result = items[list.SelectedIndex].Value; },
                preview != null ? 640 : 360, 460, list);
            chosen = result;
            return ok;
        }

        private static Control Labeled(string label, Control control)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*") };
            grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center });
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }

        /// <summary>Shows content with OK/Cancel modally; runs <paramref name="onOk"/> and returns true on OK.</summary>
        private bool ShowModal(string title, Control content, Action onOk, double width, double height, ListBox doubleClickAccepts = null)
        {
            var dialog = new Window
            {
                Title = title,
                Width = width,
                Height = height,
                CanResize = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };
            bool accepted = false;
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80 };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
            ok.Click += (s, e) => { accepted = true; dialog.Close(); };
            cancel.Click += (s, e) => dialog.Close();
            if (doubleClickAccepts != null)
                doubleClickAccepts.DoubleTapped += (s, e) => { accepted = true; dialog.Close(); };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right, Children = { cancel, ok } };
            DockPanel.SetDock(buttons, global::Avalonia.Controls.Dock.Bottom);
            buttons.Margin = new Thickness(0, 12, 0, 0);
            var root = new DockPanel { Margin = new Thickness(16), Children = { buttons, content } };
            dialog.Content = root;

            // Nested dialogs (e.g. the schema editor from the custom properties dialog) belong to the topmost one.
            Window owner = _owner();
            while (owner?.OwnedWindows.LastOrDefault(w => w.IsVisible) is Window child)
                owner = child;
            RunModal(dialog, owner);
            root.Children.Remove(content); // content may be shown again (validation loops)
            if (accepted)
                onOk();
            return accepted;
        }

        /// <summary>Shows a dialog modally and blocks (pumping UI events) until it closes.</summary>
        private static void RunModal(Window dialog, Window owner)
        {
            Task task = owner != null ? dialog.ShowDialog(owner) : ShowUnowned(dialog);
            if (task.IsCompleted) return;
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.UIThread.PushFrame(frame);
        }

        private static Task ShowUnowned(Window dialog)
        {
            var done = new TaskCompletionSource<object>();
            dialog.Closed += (s, e) => done.TrySetResult(null);
            dialog.Show();
            return done.Task;
        }
    }

    internal static class ControlExtensions
    {
        public static T With<T>(this T control, Action<T> configure)
        {
            configure(control);
            return control;
        }
    }
}
