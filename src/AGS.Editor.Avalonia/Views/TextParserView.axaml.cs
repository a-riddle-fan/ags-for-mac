using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AGS.Editor.Avalonia.ViewModels;

namespace AGS.Editor.Avalonia.Views
{
    /// <summary>The text parser word list.</summary>
    public partial class TextParserView : UserControl
    {
        public TextParserView()
        {
            InitializeComponent();
        }

        /// <summary>The main view model, carried in the window's Tag (main window and floating windows).</summary>
        private MainWindowViewModel Main => TopLevel.GetTopLevel(this)?.Tag as MainWindowViewModel;

        private void OnParserAddWord(object sender, RoutedEventArgs e) => Main?.TextParserAction("add");
        private void OnParserAddSynonym(object sender, RoutedEventArgs e) => Main?.TextParserAction("synonym");
        private void OnParserDelete(object sender, RoutedEventArgs e) => Main?.TextParserAction("delete");
    }
}
