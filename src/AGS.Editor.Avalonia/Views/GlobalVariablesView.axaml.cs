using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AGS.Editor.Avalonia.ViewModels;

namespace AGS.Editor.Avalonia.Views
{
    /// <summary>The global variables list.</summary>
    public partial class GlobalVariablesView : UserControl
    {
        public GlobalVariablesView()
        {
            InitializeComponent();
        }

        /// <summary>The main view model, carried in the window's Tag (main window and floating windows).</summary>
        private MainWindowViewModel Main => TopLevel.GetTopLevel(this)?.Tag as MainWindowViewModel;

        private void OnGlobalVariableDoubleTapped(object sender, TappedEventArgs e) => Main?.GlobalVariableActionCommand.Execute("edit");
    }
}
