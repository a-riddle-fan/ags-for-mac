using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AGS.Editor.Avalonia.ViewModels;

namespace AGS.Editor.Avalonia.Views
{
    public partial class ViewEditorView : UserControl
    {
        public ViewEditorView()
        {
            InitializeComponent();
        }

        private ViewEditorDocument Doc => DataContext as ViewEditorDocument;

        private void OnFramePressed(object sender, PointerPressedEventArgs e)
        {
            if (Doc is not ViewEditorDocument doc || (sender as Control)?.DataContext is not FrameTile tile) return;
            if (e.ClickCount >= 2)
                doc.ChooseSprite(tile);
            else
                doc.SelectFrame(tile);
            e.Handled = true;
        }

        private void OnLoopTitlePressed(object sender, PointerPressedEventArgs e)
        {
            if ((sender as Control)?.DataContext is LoopRow row)
                Doc?.SelectLoop(row);
        }

        private void OnNewFrame(object sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is LoopRow row)
                Doc?.NewFrame(row);
        }

        private void OnNewLoop(object sender, RoutedEventArgs e) => Doc?.AddLoop();
        private void OnDeleteLoop(object sender, RoutedEventArgs e) => Doc?.DeleteLastLoop();
        private void OnInsertBefore(object sender, RoutedEventArgs e) => Doc?.InsertFrame(before: true);
        private void OnInsertAfter(object sender, RoutedEventArgs e) => Doc?.InsertFrame(before: false);
        private void OnDeleteFrame(object sender, RoutedEventArgs e) => Doc?.DeleteSelectedFrame();
        private void OnFlip(object sender, RoutedEventArgs e) => Doc?.FlipSelectedFrame();
        private void OnReverse(object sender, RoutedEventArgs e) => Doc?.ReverseLoop();
    }
}
