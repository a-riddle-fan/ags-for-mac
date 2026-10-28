using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AGS.Editor.Avalonia.ViewModels;

namespace AGS.Editor.Avalonia.Views
{
    public partial class GuiEditorView : UserControl
    {
        public GuiEditorView()
        {
            InitializeComponent();
            Canvas.PointerPressed += (s, e) =>
            {
                if (Doc is GuiEditorDocument doc && TryGetGuiPoint(e, out int x, out int y))
                {
                    _lastX = x;
                    _lastY = y;
                    doc.BeginDrag(x, y, e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Meta));
                    e.Pointer.Capture(Canvas);
                    Canvas.Focus();
                }
            };
            Canvas.PointerMoved += (s, e) =>
            {
                if (Doc is not GuiEditorDocument doc || !TryGetGuiPoint(e, out int x, out int y)) return;
                Coordinates.Text = $"x {x}, y {y}";
                _lastX = x;
                _lastY = y;
                if (e.GetCurrentPoint(Canvas).Properties.IsLeftButtonPressed)
                    doc.DragTo(x, y);
                else
                    Canvas.Cursor = new Cursor(doc.PendingAdd != null ? StandardCursorType.Cross
                        : doc.IsOnResizeHandle(x, y) ? StandardCursorType.BottomRightCorner : StandardCursorType.Arrow);
            };
            Canvas.Focusable = true;
            Canvas.KeyDown += (s, e) =>
            {
                if (Doc is not GuiEditorDocument doc) return;
                if (e.Key == Key.Escape && doc.PendingAdd != null) { doc.PendingAdd = null; Canvas.Cursor = new Cursor(StandardCursorType.Arrow); e.Handled = true; }
                else if ((e.Key == Key.Delete || e.Key == Key.Back) && doc.SelectedControl != null) { doc.DeleteSelected(); e.Handled = true; }
                else if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Meta)) { doc.CopySelection(); e.Handled = true; }
                else if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Meta)) { doc.Paste(_lastX, _lastY); e.Handled = true; }
            };
            Canvas.PointerReleased += (s, e) =>
            {
                Doc?.EndDrag();
                e.Pointer.Capture(null);
            };
        }

        private GuiEditorDocument Doc => DataContext as GuiEditorDocument;
        private int _lastX = 4, _lastY = 4;

        private void OnAlignLeft(object sender, RoutedEventArgs e) => Doc?.Align(left: true);
        private void OnAlignTop(object sender, RoutedEventArgs e) => Doc?.Align(left: false);
        private void OnDistributeH(object sender, RoutedEventArgs e) => Doc?.Distribute(horizontally: true);
        private void OnDistributeV(object sender, RoutedEventArgs e) => Doc?.Distribute(horizontally: false);
        private void OnCopy(object sender, RoutedEventArgs e) => Doc?.CopySelection();
        private void OnPaste(object sender, RoutedEventArgs e) => Doc?.Paste(_lastX, _lastY);

        private bool TryGetGuiPoint(PointerEventArgs e, out int x, out int y)
        {
            x = y = 0;
            if (Doc is not GuiEditorDocument doc) return false;
            var p = e.GetPosition(Canvas);
            x = (int)Math.Floor(p.X / doc.Zoom);
            y = (int)Math.Floor(p.Y / doc.Zoom);
            return true;
        }

        private void OnAdd(object sender, RoutedEventArgs e)
        {
            if (Doc is not GuiEditorDocument doc || sender is not Button { Tag: string tag }) return;
            // Like upstream: choose the control type, then draw its rectangle on the GUI.
            doc.PendingAdd = Enum.Parse<GuiControlKind>(tag);
            Canvas.Cursor = new Cursor(StandardCursorType.Cross);
            Canvas.Focus();
        }

        private void OnDelete(object sender, RoutedEventArgs e) => Doc?.DeleteSelected();
        private void OnFront(object sender, RoutedEventArgs e) => Doc?.MoveSelectedInZOrder(toFront: true);
        private void OnBack(object sender, RoutedEventArgs e) => Doc?.MoveSelectedInZOrder(toFront: false);
    }
}
