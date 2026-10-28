using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AGS.Editor.Avalonia.ViewModels;

namespace AGS.Editor.Avalonia.Views
{
    /// <summary>The palette (colour finder) view.</summary>
    public partial class PaletteView : UserControl
    {
        public PaletteView()
        {
            InitializeComponent();
        }

        /// <summary>The main view model, carried in the window's Tag (main window and floating windows).</summary>
        private MainWindowViewModel Main => TopLevel.GetTopLevel(this)?.Tag as MainWindowViewModel;

        private void OnPaletteSlotPressed(object sender, PointerPressedEventArgs e)
        {
            if ((sender as Control)?.DataContext is PaletteSlot slot && DataContext is PaletteDocument doc)
                doc.SelectSlot(slot);
        }
    }
}
