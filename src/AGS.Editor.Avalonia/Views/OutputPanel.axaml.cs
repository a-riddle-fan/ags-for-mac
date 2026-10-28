using Avalonia.Controls;
using AGS.Editor.Avalonia.ViewModels;

namespace AGS.Editor.Avalonia.Views
{
    /// <summary>The output panel with the build errors (a dockable tool of the main window).</summary>
    public partial class OutputPanel : UserControl
    {
        public OutputPanel()
        {
            InitializeComponent();
            ErrorList.DoubleTapped += (s, e) =>
            {
                if (DataContext is MainWindowViewModel vm)
                    vm.GoToMessage(ErrorList.SelectedItem as AGS.Types.CompileMessage);
            };
            OutputBox.PropertyChanged += (s, e) =>
            {
                if (e.Property == TextBox.TextProperty)
                    OutputBox.CaretIndex = OutputBox.Text?.Length ?? 0; // keep the newest output visible
            };
        }
    }
}
