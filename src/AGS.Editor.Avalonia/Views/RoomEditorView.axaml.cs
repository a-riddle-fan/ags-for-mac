using Avalonia.Controls;
using Avalonia.Input;
using AGS.Editor.Avalonia.ViewModels;

namespace AGS.Editor.Avalonia.Views
{
    public partial class RoomEditorView : UserControl
    {
        public RoomEditorView()
        {
            InitializeComponent();
            RoomImage.PointerPressed += (s, e) =>
            {
                if (DataContext is RoomEditorDocument room && TryGetRoomPoint(e, out int x, out int y))
                {
                    if (room.IsPainting) room.BeginStroke(x, y);
                    else room.BeginDrag(x, y);
                    e.Pointer.Capture(RoomImage);
                }
            };
            RoomImage.PointerMoved += (s, e) =>
            {
                if (!TryGetRoomPoint(e, out int x, out int y)) return;
                Coordinates.Text = $"x {x}, y {y}";
                if (DataContext is not RoomEditorDocument room) return;
                if (!e.GetCurrentPoint(RoomImage).Properties.IsLeftButtonPressed)
                {
                    // Resize cursor over draggable edges and walk-behind baselines.
                    RoomEditorDocument.RoomLine line = room.IsPainting ? null : room.LineAt(x, y);
                    RoomImage.Cursor = line == null ? new Cursor(StandardCursorType.Cross)
                        : new Cursor(line.Vertical ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth);
                    return;
                }
                if (room.IsPainting) room.ContinueStroke(x, y);
                else if (room.IsDragging) room.DragTo(x, y);
            };
            RoomImage.PointerReleased += (s, e) =>
            {
                if (DataContext is RoomEditorDocument room)
                {
                    if (room.IsPainting) room.EndStroke();
                    else room.EndDrag();
                }
                e.Pointer.Capture(null);
            };
        }

        private void OnUndo(object sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            (DataContext as RoomEditorDocument)?.Undo();
        }

        private bool TryGetRoomPoint(PointerEventArgs e, out int x, out int y)
        {
            x = y = 0;
            if (DataContext is not RoomEditorDocument room || room.Zoom <= 0) return false;
            var p = e.GetPosition(RoomImage);
            x = (int)(p.X / room.Zoom);
            y = (int)(p.Y / room.Zoom);
            return x >= 0 && y >= 0 && x < room.Room.Width && y < room.Room.Height;
        }
    }
}
