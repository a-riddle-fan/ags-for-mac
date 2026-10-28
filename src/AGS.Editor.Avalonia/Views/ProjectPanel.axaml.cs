using Avalonia.Controls;
using Avalonia.Input;
using AGS.Editor.Avalonia.ViewModels;

namespace AGS.Editor.Avalonia.Views
{
    /// <summary>The project tree panel (a dockable tool of the main window).</summary>
    public partial class ProjectPanel : UserControl
    {
        public ProjectPanel()
        {
            InitializeComponent();
            ProjectTree.DoubleTapped += OnProjectTreeDoubleTapped;
        }

        private void OnProjectTreeDoubleTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && ProjectTree.SelectedItem is ProjectTreeNode node)
                vm.OpenNode(node);
        }
    }
}
