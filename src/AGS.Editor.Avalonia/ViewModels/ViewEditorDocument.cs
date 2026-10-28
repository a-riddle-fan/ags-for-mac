using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using AGS.Editor.Avalonia.Services;
using AGS.Imaging;
using AGS.Native;
using AGS.Types;
using DrawingColor = System.Drawing.Color;

namespace AGS.Editor.Avalonia.ViewModels
{
    /// <summary>One frame cell of the view editor.</summary>
    public sealed partial class FrameTile : ObservableObject
    {
        public FrameTile(LoopRow loop, ViewFrame frame, Bitmap image)
        {
            Loop = loop;
            Frame = frame;
            Image = image;
        }

        public LoopRow Loop { get; }
        public ViewFrame Frame { get; }
        public Bitmap Image { get; }
        public double ScaleX => Frame.Flipped ? -1 : 1;
        public string Caption => $"{Frame.ID}" + (Frame.Delay != 0 ? $"  d{Frame.Delay}" : "") + (Frame.Sound > 0 ? "  ♪" : "");

        [ObservableProperty] private bool _isSelected;
    }

    /// <summary>One loop row of the view editor.</summary>
    public sealed class LoopRow
    {
        public LoopRow(ViewLoop loop) { Loop = loop; }
        public ViewLoop Loop { get; }
        public string Title => $"Loop {Loop.ID} ({Loop.DirectionDescription})" + (Loop.RunNextLoop ? " → runs next loop" : "");
        public ObservableCollection<FrameTile> Frames { get; } = new ObservableCollection<FrameTile>();
    }

    /// <summary>
    /// View editor (upstream Panes/ViewEditor + ViewLoopEditor + ViewPreview): loops with their frames as
    /// sprite thumbnails; select a frame for the property grid, add/insert/delete/flip/reverse frames, add
    /// and delete loops, double-click a frame to choose its sprite, and an animated preview of a loop.
    /// </summary>
    public sealed partial class ViewEditorDocument : DocumentViewModel, IDisposable
    {
        private readonly Game _game;
        private readonly SpriteFile _sprites;
        private readonly Action<object> _select;
        private readonly DrawingColor[] _palette;
        private readonly Dictionary<int, Bitmap> _cache = new Dictionary<int, Bitmap>();
        private readonly DispatcherTimer _timer;

        public ViewEditorDocument(View view, Game game, SpriteFile sprites, Action<object> select)
            : base("View: " + view.Name, view)
        {
            View = view;
            _game = game;
            _sprites = sprites;
            _select = select;
            _palette = game.Palette.Select(p => p.Colour).ToArray();
            // Preview runs at the game speed of 40 loops per second (upstream ViewPreview's timer).
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(25), DispatcherPriority.Background, (s, e) => TickPreview());
            Rebuild();
        }

        public View View { get; }

        public ObservableCollection<LoopRow> Loops { get; } = new ObservableCollection<LoopRow>();

        /// <summary>Raised after the view was changed here (needs saving).</summary>
        public event Action Edited;

        /// <summary>Set by the app: the sprite chooser (current sprite → chosen sprite or null).</summary>
        public Func<int, int?> PickSprite { get; set; }

        [ObservableProperty] private FrameTile _selected;
        [ObservableProperty] private LoopRow _selectedLoop;
        [ObservableProperty] private bool _hasSelection;
        [ObservableProperty] private bool _canDeleteLoop;

        // ---- preview (ViewPreview) ----
        [ObservableProperty] private bool _animate = true;
        [ObservableProperty] private int _previewSpeed = 5;
        [ObservableProperty] private Bitmap _previewImage;
        [ObservableProperty] private double _previewScaleX = 1;
        [ObservableProperty] private string _previewText = "";
        private int _previewFrame, _previewTicks;

        public Bitmap SpriteBitmap(int number)
        {
            if (_cache.TryGetValue(number, out Bitmap bitmap)) return bitmap;
            try
            {
                PixelImage image = _sprites?.LoadSprite(number, _palette);
                Sprite sprite = _game.RootSpriteFolder.FindSpriteByID(number, true);
                bitmap = image == null ? null : ImageConversion.ToBitmap(image, transparentMagenta: sprite == null || !sprite.AlphaChannel);
            }
            catch (Exception)
            {
                bitmap = null;
            }
            _cache[number] = bitmap;
            return bitmap;
        }

        /// <summary>Rebuilds rows and tiles from the view (keeps the selected frame when it still exists).</summary>
        public void Rebuild()
        {
            ViewLoop selLoop = Selected?.Loop.Loop ?? SelectedLoop?.Loop;
            int selFrame = Selected?.Frame.ID ?? -1;
            Loops.Clear();
            foreach (ViewLoop loop in View.Loops)
            {
                var row = new LoopRow(loop);
                foreach (ViewFrame frame in loop.Frames)
                    row.Frames.Add(new FrameTile(row, frame, SpriteBitmap(frame.Image)));
                Loops.Add(row);
            }
            CanDeleteLoop = View.Loops.Count > 0;
            LoopRow newLoop = Loops.FirstOrDefault(r => r.Loop == selLoop) ?? Loops.FirstOrDefault();
            FrameTile tile = newLoop?.Frames.FirstOrDefault(t => t.Frame.ID == selFrame);
            SelectedLoop = newLoop;
            SetSelected(tile, notify: false);
            RestartPreview();
        }

        private void SetSelected(FrameTile tile, bool notify)
        {
            foreach (FrameTile t in Loops.SelectMany(r => r.Frames))
                t.IsSelected = t == tile;
            Selected = tile;
            HasSelection = tile != null;
            if (tile != null)
                SelectedLoop = tile.Loop;
            if (notify)
                _select?.Invoke(tile != null ? tile.Frame : (object)SelectedLoop?.Loop ?? View);
        }

        /// <summary>Selects a frame (property grid shows it).</summary>
        public void SelectFrame(FrameTile tile)
        {
            SetSelected(tile, notify: true);
            RestartPreview();
        }

        /// <summary>Selects a loop without a frame (property grid shows the loop).</summary>
        public void SelectLoop(LoopRow row)
        {
            SelectedLoop = row;
            SetSelected(null, notify: true);
            RestartPreview();
        }

        private void Change()
        {
            Rebuild();
            Edited?.Invoke();
            _select?.Invoke(Selected != null ? Selected.Frame : (object)SelectedLoop?.Loop ?? View);
        }

        private void SelectAfterChange(ViewLoop loop, int frame)
        {
            LoopRow row = Loops.FirstOrDefault(r => r.Loop == loop);
            SetSelected(row?.Frames.FirstOrDefault(t => t.Frame.ID == frame), notify: true);
        }

        public void NewFrame(LoopRow row)
        {
            if (row == null) return;
            int index = ViewOperations.InsertNewFrames(_game, row.Loop, row.Loop.Frames.Count);
            Change();
            SelectAfterChange(row.Loop, index);
        }

        public void InsertFrame(bool before)
        {
            if (Selected == null) return;
            ViewLoop loop = Selected.Loop.Loop;
            int at = Selected.Frame.ID;
            int index = ViewOperations.InsertNewFrames(_game, loop, before ? at - 1 : at);
            Change();
            SelectAfterChange(loop, index);
        }

        public void DeleteSelectedFrame()
        {
            if (Selected == null) return;
            ViewLoop loop = Selected.Loop.Loop;
            int at = Selected.Frame.ID;
            ViewOperations.DeleteFrames(loop, new[] { at });
            Selected = null;
            Change();
            SelectAfterChange(loop, Math.Min(at, loop.Frames.Count - 1));
        }

        public void FlipSelectedFrame()
        {
            if (Selected == null) return;
            ViewLoop loop = Selected.Loop.Loop;
            int at = Selected.Frame.ID;
            ViewOperations.FlipFrames(loop, new[] { at });
            Change();
            SelectAfterChange(loop, at);
        }

        public void ReverseLoop()
        {
            if (SelectedLoop == null) return;
            ViewOperations.ReverseFrames(SelectedLoop.Loop, Enumerable.Range(0, SelectedLoop.Loop.Frames.Count));
            Change();
        }

        public void AddLoop()
        {
            ViewLoop loop = ViewOperations.AddLoop(View);
            Selected = null;
            SelectedLoop = null;
            Change();
            SelectLoop(Loops.First(r => r.Loop == loop));
        }

        public void DeleteLastLoop()
        {
            ViewOperations.DeleteLastLoop(View);
            Selected = null;
            SelectedLoop = null;
            Change();
        }

        /// <summary>ViewLoopEditor_MouseDoubleClick: choose the frame's sprite.</summary>
        public void ChooseSprite(FrameTile tile)
        {
            if (tile == null || PickSprite == null) return;
            SelectFrame(tile);
            int initial = tile.Frame.Image;
            if (initial == 0 && tile.Frame.ID > 0)
                initial = tile.Loop.Loop.Frames[tile.Frame.ID - 1].Image;
            int? chosen = PickSprite(initial);
            if (chosen == null) return;
            tile.Frame.Image = chosen.Value;
            Change();
            SelectAfterChange(tile.Loop.Loop, tile.Frame.ID);
        }

        /// <summary>After property grid edits.</summary>
        public void Refresh()
        {
            ViewLoop loop = Selected?.Loop.Loop;
            int frame = Selected?.Frame.ID ?? -1;
            Rebuild();
            if (loop != null)
            {
                LoopRow row = Loops.FirstOrDefault(r => r.Loop == loop);
                SetSelected(row?.Frames.FirstOrDefault(t => t.Frame.ID == frame), notify: false);
            }
        }

        partial void OnAnimateChanged(bool value) => RestartPreview();

        private void RestartPreview()
        {
            _previewFrame = 0;
            _previewTicks = 0;
            ShowPreviewFrame();
            ViewLoop loop = SelectedLoop?.Loop;
            if (Animate && loop != null && loop.Frames.Count > 1 && Selected == null)
                _timer.Start();
            else
                _timer.Stop();
        }

        private void ShowPreviewFrame()
        {
            ViewLoop loop = SelectedLoop?.Loop;
            ViewFrame frame = Selected?.Frame ?? (loop != null && loop.Frames.Count > 0 ? loop.Frames[Math.Min(_previewFrame, loop.Frames.Count - 1)] : null);
            PreviewImage = frame == null ? null : SpriteBitmap(frame.Image);
            PreviewScaleX = frame != null && frame.Flipped ? -1 : 1;
            PreviewText = frame == null ? "No frames" : $"Loop {loop?.ID}, frame {frame.ID}, sprite {frame.Image}";
        }

        /// <summary>One game tick of the preview: a frame lasts speed + its delay ticks (as the engine).</summary>
        internal void TickPreview()
        {
            ViewLoop loop = SelectedLoop?.Loop;
            if (loop == null || loop.Frames.Count == 0) return;
            ViewFrame frame = loop.Frames[Math.Min(_previewFrame, loop.Frames.Count - 1)];
            if (++_previewTicks <= Math.Max(0, PreviewSpeed + frame.Delay)) return;
            _previewTicks = 0;
            _previewFrame = (_previewFrame + 1) % loop.Frames.Count;
            ShowPreviewFrame();
        }

        public void Dispose() => _timer.Stop();
    }
}
