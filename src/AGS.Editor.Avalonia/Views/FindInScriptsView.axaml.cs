using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AGS.Editor.Avalonia.ViewModels;

namespace AGS.Editor.Avalonia.Views
{
    /// <summary>Find / Replace in all scripts and the results list.</summary>
    public partial class FindInScriptsView : UserControl
    {
        public FindInScriptsView()
        {
            InitializeComponent();
        }

        /// <summary>The main view model, carried in the window's Tag (main window and floating windows).</summary>
        private MainWindowViewModel Main => TopLevel.GetTopLevel(this)?.Tag as MainWindowViewModel;

        private FindInScriptsDocument Doc => DataContext as FindInScriptsDocument;
        private void OnFindAll(object sender, RoutedEventArgs e) => Doc?.FindAll();
        private void OnReplaceAll(object sender, RoutedEventArgs e) => Doc?.ReplaceAll();
        private void OnFindKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Doc?.FindAll(); e.Handled = true; }
        }
        private void OnFindResultDoubleTapped(object sender, TappedEventArgs e) => Doc?.Open(Doc.Selected);
    }
}
