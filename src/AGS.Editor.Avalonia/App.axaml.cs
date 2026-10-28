using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AGS.Editor.Avalonia.ViewModels;
using AGS.Editor.Avalonia.Views;

namespace AGS.Editor.Avalonia
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            ApplySettings();
            if (!_settingsHooked)
            {
                _settingsHooked = true; // static event, applies to whichever App is current (tests make several)
                Services.EditorSettings.Changed += ApplySettings;
            }
        }

        /// <summary>Applies the look and colour scheme chosen in Settings (live, no restart).</summary>
        public static void ApplySettings()
        {
            Application app = Current;
            if (app == null) return;
            Services.EditorSettings settings = Services.EditorSettings.Current;
            SetMacLook(settings.Appearance == Services.EditorAppearance.Mac);
            app.RequestedThemeVariant = settings.Theme switch
            {
                Services.EditorThemeMode.Light => global::Avalonia.Styling.ThemeVariant.Light,
                Services.EditorThemeMode.Dark => global::Avalonia.Styling.ThemeVariant.Dark,
                _ => global::Avalonia.Styling.ThemeVariant.Default,
            };
        }

        /// <summary>Opens the Settings window (app menu → Settings…, ⌘,).</summary>
        public static void ShowSettings()
        {
            var owner = (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var window = new SettingsWindow();
            if (owner != null && owner.IsVisible) window.Show(owner);
            else window.Show();
        }

        private void OnSettingsMenuClick(object sender, System.EventArgs e) => ShowSettings();

        private static bool _settingsHooked;
        private global::Avalonia.Styling.IStyle _macLook;

        /// <summary>Switches the macOS look (Styles/MacLook.axaml) on or off; off is the classic look. Purely visual.</summary>
        public static void SetMacLook(bool on)
        {
            if (Current is not App app) return;
            app._macLook ??= (global::Avalonia.Styling.IStyle)AvaloniaXamlLoader.Load(new System.Uri("avares://AGSEditor/Styles/MacLook.axaml"));
            bool active = app.Styles.Contains(app._macLook);
            if (on && !active) app.Styles.Add(app._macLook);
            else if (!on && active) app.Styles.Remove(app._macLook);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var viewModel = new MainWindowViewModel();
                desktop.MainWindow = new MainWindow { DataContext = viewModel };
                // ⌘Q: keep the panel arrangement (closing the window saves it as well)
                desktop.ShutdownRequested += (s, e) => (desktop.MainWindow as MainWindow)?.SaveDockLayout();
                var dialogs = new Services.EditorDialogs(() => desktop.MainWindow, () => viewModel.Project);
                dialogs.Install();
                viewModel.Confirm = dialogs.Confirm;
                viewModel.PromptText = (title, text) => dialogs.PromptLine(title, text);
                viewModel.PickSpriteImportOptions = dialogs.PickSpriteImportOptions;
                viewModel.PickSprite = dialogs.PickSprite;
                viewModel.EditGlobalVariable = dialogs.EditGlobalVariable;
                viewModel.AskTtfSize = dialogs.AskTtfSize;
                viewModel.PickFileSync = dialogs.PickFile;
                viewModel.AskSpeechNumbering = dialogs.AskSpeechNumbering;
                viewModel.PickSaveFileSync = dialogs.PickSaveFile;
                dialogs.ApplySchemaRenames = viewModel.ApplySchemaRenames;
                viewModel.AskAssignToView = dialogs.AskAssignToView;
                viewModel.ChooseFromList = dialogs.ChooseFromList;
                viewModel.AskSpriteSheet = dialogs.AskSpriteSheet;
                viewModel.AskSpriteExport = dialogs.AskSpriteExport;
                viewModel.PickFolderSync = dialogs.PickFolderSync;
                viewModel.AskReplaceFolder = dialogs.AskReplaceFolder;
                viewModel.InstallScriptHooks();
                viewModel.InstallFontHooks();

                // Game.agf opened from the Finder (double-click, "Open With", dropping on the Dock icon)
                if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
                {
                    activatable.Activated += (s, e) =>
                    {
                        if (e is FileActivatedEventArgs files)
                        {
                            string path = files.Files
                                .Select(f => global::Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(f))
                                .FirstOrDefault(p => p != null && p.EndsWith(".agf", System.StringComparison.OrdinalIgnoreCase));
                            if (path != null)
                                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => viewModel.OpenProjectFromFinder(path));
                        }
                    };
                }

                string project = desktop.Args?.FirstOrDefault(a => a.EndsWith(".agf", System.StringComparison.OrdinalIgnoreCase));
                if (project != null)
                    viewModel.OpenProject(project);
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
