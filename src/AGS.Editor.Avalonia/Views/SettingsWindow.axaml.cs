using Avalonia.Controls;
using Avalonia.Interactivity;
using AGS.Editor.Avalonia.Services;

namespace AGS.Editor.Avalonia.Views
{
    /// <summary>The editor's Settings window: look (Mac / Classic) and colour scheme, applied live.</summary>
    public partial class SettingsWindow : Window
    {
        private bool _loading;

        public SettingsWindow()
        {
            InitializeComponent();
            _loading = true;
            EditorSettings settings = EditorSettings.Current;
            MacLook.IsChecked = settings.Appearance == EditorAppearance.Mac;
            ClassicLook.IsChecked = settings.Appearance == EditorAppearance.Classic;
            SystemTheme.IsChecked = settings.Theme == EditorThemeMode.System;
            LightTheme.IsChecked = settings.Theme == EditorThemeMode.Light;
            DarkTheme.IsChecked = settings.Theme == EditorThemeMode.Dark;
            _loading = false;
        }

        private void OnChanged(object sender, RoutedEventArgs e)
        {
            if (_loading || sender is not RadioButton { IsChecked: true } choice) return;
            // the newly checked button reports before its group partner is unchecked: go by the sender
            EditorSettings current = EditorSettings.Current;
            new EditorSettings
            {
                Appearance = choice == ClassicLook ? EditorAppearance.Classic : choice == MacLook ? EditorAppearance.Mac : current.Appearance,
                Theme = choice == LightTheme ? EditorThemeMode.Light : choice == DarkTheme ? EditorThemeMode.Dark
                    : choice == SystemTheme ? EditorThemeMode.System : current.Theme,
            }.Apply();
        }
    }
}
