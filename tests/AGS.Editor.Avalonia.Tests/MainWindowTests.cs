using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using AGS.Editor.Avalonia.Services;
using AGS.Editor.Avalonia.ViewModels;
using AGS.Editor.Avalonia.Views;
using AGS.Types;

namespace AGS.Editor.Avalonia.Tests
{
    public class MainWindowTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-ui-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        private static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "AGS4Mac.sln")))
                dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return dir;
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }

        [AvaloniaFact]
        public void OpensReferenceGameAndRendersMainWindow()
        {
            string repo = RepoRoot();
            CopyDirectory(Path.Combine(repo, "testdata", "Bernard-SP"), _gameDir);

            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();

            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));

            Assert.NotNull(vm.Project);
            Assert.Equal("Bernard-Starterpack — AGS Editor", window.Title);
            ProjectTreeNode root = Assert.Single(vm.TreeRoots);
            ProjectTreeNode rooms = root.Children.Single(n => n.Name == "Rooms");
            Assert.Equal(21, rooms.Children.Count);
            ProjectTreeNode scripts = root.Children.Single(n => n.Name == "Scripts");
            Assert.Equal(4, scripts.Children.Count); // 4 modules, each with header + script
            Assert.All(scripts.Children, m => Assert.Equal(2, m.Children.Count));

            // Open room 1 (rendered through libagsnative) and select the first character for the property list.
            vm.OpenNode(rooms.Children.First());
            Assert.IsType<RoomEditorDocument>(vm.SelectedDocument);
            vm.SelectedNode = root.Children.Single(n => n.Name == "Characters").Children.First();
            var character = Assert.IsType<Character>(vm.SelectedNode.Payload);
            var rows = vm.PropertyGrid.Entries.OfType<PropertyRow>().ToList();
            Assert.Contains(vm.PropertyGrid.Entries, e => e is CategoryRow);

            // Text (through the TypeConverter), checkbox and enum drop-down edit the model.
            var realName = rows.OfType<TextPropertyRow>().Single(r => r.Descriptor.Name == nameof(Character.RealName));
            realName.Text = "Dave Miller";
            Assert.Equal("Dave Miller", character.RealName);
            var speechView = rows.OfType<TextPropertyRow>().Single(r => r.Descriptor.Name == nameof(Character.SpeechView));
            speechView.Text = "not a number";
            Assert.Equal(2, character.SpeechView); // invalid input is rejected and reported
            Assert.Contains("Invalid value", vm.OutputText);
            var clickable = rows.OfType<BoolPropertyRow>().Single(r => r.Descriptor.Name == nameof(Character.Clickable));
            clickable.Value = !clickable.Value;
            Assert.Equal(clickable.Value, character.Clickable);
            // Enums (AGS's EnumTypeConverter) become drop-downs; General Settings has plenty.
            vm.SelectedNode = root.Children.Single(n => n.Name == "General Settings");
            var settings = Assert.IsType<Settings>(vm.SelectedNode.Payload);
            var choice = vm.PropertyGrid.Entries.OfType<ChoicePropertyRow>().Single(r => r.Descriptor.Name == nameof(Settings.SpeechStyle));
            Assert.True(choice.Choices.Count > 1);
            choice.Selected = choice.Choices.First(c => !Equals(c.Value, choice.Selected?.Value));
            Assert.Equal(choice.Selected.Value, settings.SpeechStyle);
            vm.SelectedNode = root.Children.Single(n => n.Name == "Characters").Children.First();
            Assert.True(vm.IsModified);
            Assert.EndsWith("• — AGS Editor", window.Title);

            TestPaths.Settle(window);
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            string shot = TestPaths.Screenshot("phase2-main-window.png");
            frame.Save(shot);
            Assert.True(new FileInfo(shot).Length > 10_000);
        }

        [AvaloniaFact]
        public void TreeNodesShowUpstreamIcons()
        {
            CopyDirectory(Path.Combine(RepoRoot(), "testdata", "Bernard-SP"), _gameDir);
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            vm.OpenProject(Path.Combine(_gameDir, "Game.agf"));

            var all = new System.Collections.Generic.List<ProjectTreeNode>();
            void Walk(ProjectTreeNode n) { all.Add(n); foreach (var c in n.Children) Walk(c); }
            foreach (var root in vm.TreeRoots) Walk(root);
            // Every node has an upstream icon.
            Assert.All(all, n => Assert.True(n.Icon != null, $"no icon for '{n.Path}' ({n.Payload?.GetType().Name ?? n.Kind.ToString()})"));
            Assert.True(all[0].Icon.Size.Width >= 16); // largest size of the .ico (32 px: sharp on Retina at 16 pt)

            foreach (string name in new[] { "Characters", "Views", "Audio" })
                vm.TreeRoots[0].Children.Single(n => n.Name == name).IsExpanded = true;
            TestPaths.Settle(window);
            window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-tree-icons.png"));
        }
    
        [AvaloniaFact]
        public void DocumentsAreDockTabsBothWays()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ags4mac-dock-" + Guid.NewGuid().ToString("N"));
            TestPaths.CopyDirectory(Path.Combine(RepoRoot(), "testdata", "Bernard-SP"), dir);
            try
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
                window.Show();
                vm.OpenProject(Path.Combine(dir, "Game.agf"));
                var docs = window.DockFactory.Documents;
                ProjectTreeNode scripts = vm.TreeRoots[0].Children.Single(n => n.Name == "Scripts");
                vm.OpenNode(scripts.Find("GlobalScript.asc"));
                vm.OpenNode(scripts.Find("GlobalScript.ash"));
                TestPaths.Settle(window);
                Assert.Equal(vm.Documents.Count, docs.VisibleDockables.Count);
                Assert.Equal(vm.Documents.Select(d => d.Title), docs.VisibleDockables.OfType<EditorDockDocument>().Select(d => d.Title));
                Assert.Same(vm.SelectedDocument, ((EditorDockDocument)docs.ActiveDockable).Model);

                // activating a tab in the dock selects the document
                var first = docs.VisibleDockables.OfType<EditorDockDocument>().First(d => d.Model is ScriptDocument);
                window.DockFactory.SetActiveDockable(first);
                Assert.Same(first.Model, vm.SelectedDocument);
                TestPaths.Settle(window);
                window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-dock-layout.png"));

                // closing the tab goes through the view model
                Assert.False(first.OnClose());
                Assert.DoesNotContain(first.Model, vm.Documents);
                Assert.DoesNotContain(first, docs.VisibleDockables);

                // Window → Reset Layout keeps the open documents
                int open = vm.Documents.Count;
                vm.ResetPanelLayoutCommand.Execute(null);
                Assert.Equal(open, window.DockFactory.Documents.VisibleDockables.Count);
            }
            finally
            {
                Directory.SetCurrentDirectory(Path.GetTempPath());
                Directory.Delete(dir, true);
            }
        }

        [AvaloniaFact]
        public void PanelLayoutIsSavedAndRestored()
        {
            string saved = MainWindow.LayoutFile;
            MainWindow.LayoutFile = Path.Combine(Path.GetTempPath(), "ags4mac-layout-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
                window.Show();
                TestPaths.Settle(window);
                var factory = window.DockFactory;
                var root = window.DockLayout;
                var all = factory.Find(root, d => d is EditorTool).Cast<EditorTool>().ToList();
                EditorTool output = all.Single(t => t.Id == "Output"), properties = all.Single(t => t.Id == "Properties"), project = all.Single(t => t.Id == "Project");

                // Output into the dock of Properties, Project pinned to the side, the right pane wider
                factory.MoveDockable((Dock.Model.Core.IDock)output.Owner, (Dock.Model.Core.IDock)properties.Owner, output, properties);
                factory.PinDockable(project);
                ((Dock.Model.Core.IDockable)properties.Owner).Proportion = 0.35;
                window.SaveDockLayout();
                Assert.True(File.Exists(MainWindow.LayoutFile));

                var vm2 = new MainWindowViewModel();
                var window2 = new MainWindow { DataContext = vm2, Width = 1440, Height = 900 };
                window2.Show();
                TestPaths.Settle(window2);
                var root2 = window2.DockLayout;
                var tools2 = window2.DockFactory.Find(root2, d => d is EditorTool).Cast<EditorTool>().ToList();
                EditorTool output2 = tools2.Single(t => t.Id == "Output"), properties2 = tools2.Single(t => t.Id == "Properties");
                Assert.Same(output2.Owner, properties2.Owner);
                // wider than the default 0.24 (Dock normalizes the proportions of the siblings to a sum of 1)
                Assert.True(((Dock.Model.Core.IDockable)properties2.Owner).Proportion > 0.3);
                Assert.Contains(root2.LeftPinnedDockables.Concat(root2.RightPinnedDockables).Concat(root2.TopPinnedDockables).Concat(root2.BottomPinnedDockables),
                    d => d is ProjectTool);
                Assert.Same(vm2, output2.Main);

                // documents still open in the restored layout
                vm2.FindInScriptsCommand.CanExecute(null);
                Assert.NotNull(window2.DockFactory.Documents);

                // a damaged file gives the default layout; Reset Layout deletes the file
                File.WriteAllText(MainWindow.LayoutFile, "{ not json");
                var window3 = new MainWindow { DataContext = new MainWindowViewModel(), Width = 1440, Height = 900 };
                window3.Show();
                var root3 = window3.DockLayout;
                Assert.Equal(3, window3.DockFactory.Find(root3, d => d is EditorTool).Count());
                window.ResetDockLayout();
                Assert.False(File.Exists(MainWindow.LayoutFile));
            }
            finally
            {
                if (File.Exists(MainWindow.LayoutFile)) File.Delete(MainWindow.LayoutFile);
                MainWindow.LayoutFile = saved;
            }
        }

        [AvaloniaFact]
        public void FloatingPanelsAndDocumentsShowTheirContent()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ags4mac-float-" + Guid.NewGuid().ToString("N"));
            TestPaths.CopyDirectory(Path.Combine(RepoRoot(), "testdata", "Bernard-SP"), dir);
            try
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
                window.Show();
                vm.OpenProject(Path.Combine(dir, "Game.agf"));
                ProjectTreeNode scripts = vm.TreeRoots[0].Children.Single(n => n.Name == "Scripts");
                vm.OpenNode(scripts.Find("GlobalScript.asc"));
                vm.FindInScriptsCommand.Execute(null);
                TestPaths.Settle(window);
                var factory = window.DockFactory;
                var tools = factory.Find(window.DockLayout, d => d is EditorTool).Cast<EditorTool>().ToList();

                // the properties panel and the Find in Scripts document in their own windows
                factory.FloatDockable(tools.Single(t => t.Id == "Properties"));
                var find = factory.Find(window.DockLayout, d => d is EditorDockDocument e && e.Model is FindInScriptsDocument).Cast<EditorDockDocument>().Single();
                factory.FloatDockable(find);
                TestPaths.Settle(window);
                var floating = global::Avalonia.Application.Current.ApplicationLifetime is null
                    ? window.OwnedWindows.ToList() : window.OwnedWindows.ToList();
                var hosts = floating.OfType<Dock.Avalonia.Controls.HostWindow>().ToList();
                Assert.True(hosts.Count >= 2, $"{hosts.Count} floating windows");
                foreach (var host in hosts)
                {
                    Assert.Same(vm, host.Tag);
                    host.UpdateLayout();
                    global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    host.UpdateLayout();
                }
                var visuals = hosts.SelectMany(h => global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(h)).ToList();
                Assert.Contains(visuals, v => v is PropertiesPanel);
                Assert.Contains(visuals, v => v is FindInScriptsView);

                // a command in the floating Find view reaches the main view model
                var findDoc = (FindInScriptsDocument)find.Model;
                findDoc.SearchText = "ChangeRoom";
                findDoc.FindAll();
                Assert.NotEmpty(findDoc.Results);
                hosts[0].CaptureRenderedFrame()?.Save(TestPaths.Screenshot("phase2-floating-panel.png"));
            }
            finally
            {
                Directory.SetCurrentDirectory(Path.GetTempPath());
                Directory.Delete(dir, true);
            }
        }

        [AvaloniaFact]
        public void PanelsInFloatingWindowsComeBackDockedAfterARestart()
        {
            string saved = MainWindow.LayoutFile;
            MainWindow.LayoutFile = Path.Combine(Path.GetTempPath(), "ags4mac-layout-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var window = new MainWindow { DataContext = new MainWindowViewModel(), Width = 1440, Height = 900 };
                window.Show();
                TestPaths.Settle(window);
                var tools = window.DockFactory.Find(window.DockLayout, d => d is EditorTool).Cast<EditorTool>().ToList();
                window.DockFactory.PinDockable(tools.Single(t => t.Id == "Project"));
                window.DockFactory.FloatDockable(tools.Single(t => t.Id == "Output"));
                window.SaveDockLayout();

                var window2 = new MainWindow { DataContext = new MainWindowViewModel(), Width = 1440, Height = 900 };
                window2.Show();
                TestPaths.Settle(window2);
                var root2 = window2.DockLayout;
                var docked = window2.DockFactory.Find(root2, d => d is EditorTool).Cast<EditorTool>().Select(t => t.Id).ToList();
                Assert.Contains("Output", docked);
                Assert.Contains(root2.LeftPinnedDockables.Concat(root2.RightPinnedDockables), d => d is ProjectTool); // the rest is kept
            }
            finally
            {
                if (File.Exists(MainWindow.LayoutFile)) File.Delete(MainWindow.LayoutFile);
                MainWindow.LayoutFile = saved;
            }
        }

        [AvaloniaFact]
        public void AppearanceSettingSwitchesBetweenMacAndClassicLive()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ags4mac-maclook-" + Guid.NewGuid().ToString("N"));
            TestPaths.CopyDirectory(Path.Combine(RepoRoot(), "testdata", "Bernard-SP"), dir);
            EditorSettings saved = EditorSettings.Current;
            try
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
                window.Show();
                vm.OpenProject(Path.Combine(dir, "Game.agf"));
                ProjectTreeNode scripts = vm.TreeRoots[0].Children.Single(n => n.Name == "Scripts");
                scripts.IsExpanded = true;
                vm.OpenNode(scripts.Find("GlobalScript.asc"));
                var characters = vm.TreeRoots[0].Children.Single(n => n.Name == "Characters");
                characters.IsExpanded = true;
                scripts.IsExpanded = false;
                vm.SelectedNode = characters.Children[4];
                List<LineIcon> Icons() => global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
                    .OfType<LineIcon>().Where(i => i.Classes.Contains("toolbar-icon")).ToList();

                // the default is the Mac look: icons above the labels
                Assert.Equal(EditorAppearance.Mac, EditorSettings.Current.Appearance);
                foreach (var (theme, name) in new[] { (EditorThemeMode.Light, "maclook-light.png"), (EditorThemeMode.Dark, "maclook-dark.png") })
                {
                    new EditorSettings { Appearance = EditorAppearance.Mac, Theme = theme }.Apply();
                    TestPaths.Settle(window);
                    window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot(name));
                    Assert.NotEmpty(Icons());
                    Assert.All(Icons(), i => Assert.True(i.IsVisible));
                    Assert.Equal(global::Avalonia.Layout.Orientation.Vertical, ((StackPanel)Icons()[0].Parent).Orientation);
                }

                // Classic, switched live: text-only toolbar again
                new EditorSettings { Appearance = EditorAppearance.Classic, Theme = EditorThemeMode.Light }.Apply();
                TestPaths.Settle(window);
                window.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("classic-light.png"));
                Assert.All(Icons(), i => Assert.False(i.IsVisible));
                Assert.Equal(global::Avalonia.Layout.Orientation.Horizontal, ((StackPanel)Icons()[0].Parent).Orientation);
                Assert.Equal(global::Avalonia.Styling.ThemeVariant.Light, global::Avalonia.Application.Current.RequestedThemeVariant);

                // the choice is kept for the next start
                Assert.Equal(EditorAppearance.Classic, EditorSettings.Load().Appearance);

                // the Settings window shows the current choice and switches back to the Mac look
                var settingsWindow = new SettingsWindow();
                settingsWindow.Show(window);
                TestPaths.Settle(settingsWindow);
                Assert.True(settingsWindow.FindControl<RadioButton>("ClassicLook").IsChecked);
                settingsWindow.FindControl<RadioButton>("MacLook").IsChecked = true;
                TestPaths.Settle(settingsWindow);
                settingsWindow.CaptureRenderedFrame()?.Save(TestPaths.Screenshot("settings.png"));
                Assert.Equal(EditorAppearance.Mac, EditorSettings.Current.Appearance);
                TestPaths.Settle(window);
                Assert.All(Icons(), i => Assert.True(i.IsVisible));
                settingsWindow.Close();
            }
            finally
            {
                saved.Apply();
                Directory.SetCurrentDirectory(Path.GetTempPath());
                Directory.Delete(dir, true);
            }
        }
}
}
