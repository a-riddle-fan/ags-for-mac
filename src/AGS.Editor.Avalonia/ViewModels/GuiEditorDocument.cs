using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using AGS.Editor.Avalonia.Services;
using AGS.Imaging;
using AGS.Native;
using AGS.Types;
using DrawingColor = System.Drawing.Color;

namespace AGS.Editor.Avalonia.ViewModels
{
    /// <summary>Kinds of controls the GUI editor can add (upstream GUIAddType).</summary>
    public enum GuiControlKind { Button, Label, TextBox, ListBox, Slider, InvWindow }

    /// <summary>
    /// GUI editor (upstream Panes/GUIEditor): shows a GUI with its controls; controls are selected, moved and
    /// resized with the mouse, added and deleted like in upstream's editor. GUIs live in Game.agf, so changes
    /// are saved with the project.
    /// </summary>
    public sealed partial class GuiEditorDocument : DocumentViewModel
    {
        private readonly Game _game;
        private readonly SpriteFile _sprites;
        private readonly Action<object> _select;
        private readonly DrawingColor[] _palette;
        private readonly Dictionary<int, Bitmap> _spriteCache = new Dictionary<int, Bitmap>();

        public GuiEditorDocument(GUI gui, Game game, SpriteFile sprites, Action<object> select)
            : base("GUI: " + gui.Name, gui)
        {
            Gui = gui;
            _game = game;
            _sprites = sprites;
            _select = select;
            _palette = game.Palette.Select(p => p.Colour).ToArray();
        }

        public GUI Gui { get; }

        /// <summary>Whether the native renderer (upstream GUI drawing, real fonts) is available for this game.</summary>
        public bool UseNativeRendering { get; set; }

        private Bitmap _rendered;
        private bool _renderedValid;

        /// <summary>
        /// The GUI drawn by upstream's GUI code with the game's fonts and sprites (null when unavailable;
        /// the canvas then draws an approximation itself).
        /// </summary>
        public Bitmap Rendered
        {
            get
            {
                if (!UseNativeRendering) return null;
                if (!_renderedValid)
                {
                    _renderedValid = true;
                    try
                    {
                        using PixelImage image = NativeGuiRenderer.Draw(Gui);
                        _rendered = ImageConversion.ToBitmap(image);
                    }
                    catch (Exception)
                    {
                        _rendered = null;
                    }
                }
                return _rendered;
            }
        }

        /// <summary>Marks the native rendering stale (after the GUI or a control changed).</summary>
        public void Invalidate() => _renderedValid = false;

        public bool IsTextWindow => Gui is TextWindowGUI;

        public IReadOnlyList<int> ZoomLevels { get; } = new[] { 1, 2, 3, 4 };

        [ObservableProperty] private int _zoom = 2;
        [ObservableProperty] private GUIControl _selectedControl;
        [ObservableProperty] private string _selectionText = "GUI";

        /// <summary>Raised when the GUI or a control changed (the project needs saving) or needs repainting.</summary>
        public event Action Changed;

        public int GuiWidth => Math.Max(1, Gui.EditorWidth);
        public int GuiHeight => Math.Max(1, Gui.EditorHeight);

        partial void OnZoomChanged(int value) => Changed?.Invoke();

        /// <summary>Controls in drawing order (upstream draws by ZOrder).</summary>
        public IEnumerable<GUIControl> ControlsInZOrder => Gui.Controls.OrderBy(c => c.ZOrder);

        public DrawingColor AgsColor(int colorNumber) =>
            AGSColor.ColorMapper?.MapAgsColourNumberToRgbColor(colorNumber) ?? DrawingColor.Black;

        /// <summary>A sprite as a bitmap (magenta transparent unless the sprite has alpha), or null.</summary>
        public Bitmap SpriteBitmap(int number)
        {
            if (number <= 0 || _sprites == null) return null;
            if (_spriteCache.TryGetValue(number, out Bitmap cached)) return cached;
            Bitmap bitmap = null;
            try
            {
                PixelImage image = _sprites.LoadSprite(number, _palette);
                if (image != null)
                {
                    Sprite sprite = _game.RootSpriteFolder.FindSpriteByID(number, true);
                    bitmap = ImageConversion.ToBitmap(image, transparentMagenta: sprite == null || !sprite.AlphaChannel);
                }
            }
            catch (Exception) { }
            _spriteCache[number] = bitmap;
            return bitmap;
        }

        /// <summary>The font size to draw a GUI font with (pixels at zoom 1); TTF point size, else a WFN-like default.</summary>
        public double FontSize(int font)
        {
            AGS.Types.Font f = _game.Fonts.FirstOrDefault(x => x.ID == font);
            int size = f != null && f.PointSize > 0 ? f.PointSize : 8;
            return Math.Max(6, size * Math.Max(1, f?.SizeMultiplier ?? 1));
        }

        // ---- adding by drawing a rectangle (upstream GUIEditor add modes) ----

        /// <summary>The control kind to add with the next mouse drag (null: select mode).</summary>
        [ObservableProperty] private GuiControlKind? _pendingAdd;

        /// <summary>The rectangle being drawn for a new control (GUI coordinates), or null.</summary>
        public System.Drawing.Rectangle? AddRectangle { get; private set; }

        private int _addStartX, _addStartY;

        partial void OnPendingAddChanged(GuiControlKind? value)
        {
            AddRectangle = null;
            SelectionText = value == null ? SelectionText : $"Draw the new {value} on the GUI (Esc cancels)";
        }

        // ---- selection and mouse editing ----

        private GUIControl _dragControl;
        private bool _resizing;
        private int _dragOffsetX, _dragOffsetY;

        /// <summary>The topmost control at a GUI coordinate, or null.</summary>
        public GUIControl ControlAt(int x, int y) =>
            ControlsInZOrder.Reverse().FirstOrDefault(c => x >= c.Left && y >= c.Top && x < c.Left + c.Width && y < c.Top + c.Height);

        /// <summary>All selected controls (upstream _selected); <see cref="SelectedControl"/> is the last one picked.</summary>
        public List<GUIControl> Selection { get; } = new List<GUIControl>();

        /// <summary>
        /// Selects the control at a GUI coordinate (or the GUI itself) for the property grid. With
        /// <paramref name="toggle"/> (⇧/⌘-click) the control is added to or removed from the selection.
        /// </summary>
        public object SelectAt(int x, int y, bool toggle = false)
        {
            GUIControl control = ControlAt(x, y);
            if (toggle && control != null)
            {
                if (Selection.Contains(control))
                {
                    Selection.Remove(control);
                    SetPrimary(Selection.LastOrDefault());
                }
                else
                {
                    Selection.Add(control);
                    SetPrimary(control);
                }
                return control;
            }
            // clicking into an already selected group keeps it (so it can be dragged together)
            if (control == null || !Selection.Contains(control))
            {
                Selection.Clear();
                if (control != null) Selection.Add(control);
            }
            SetPrimary(control);
            return control ?? (object)Gui;
        }

        private void SetPrimary(GUIControl control)
        {
            if (SelectedControl == control)
                Changed?.Invoke();
            else
                SelectedControl = control;
        }

        partial void OnSelectedControlChanged(GUIControl value)
        {
            if (value != null && !Selection.Contains(value))
            {
                Selection.Clear();
                Selection.Add(value);
            }
            if (value == null && Selection.Count > 0 && Selection.All(c => !Gui.Controls.Contains(c)))
                Selection.Clear();
            SelectionText = value == null ? $"GUI {Gui.ID}: {Gui.Name}" : $"{value.ControlType} {value.ID}: {value.Name}";
            _select?.Invoke(value ?? (object)Gui);
            Changed?.Invoke();
        }

        /// <summary>True when (x, y) is on the resize handle (bottom-right corner) of the selected control.</summary>
        public bool IsOnResizeHandle(int x, int y)
        {
            GUIControl c = SelectedControl;
            if (c == null) return false;
            int tolerance = Math.Max(2, 6 / Math.Max(1, Zoom));
            return Math.Abs(x - (c.Left + c.Width)) <= tolerance && Math.Abs(y - (c.Top + c.Height)) <= tolerance;
        }

        private Dictionary<GUIControl, (int Left, int Top)> _dragStart;
        private int _dragStartX, _dragStartY;

        public void BeginDrag(int x, int y, bool toggle = false)
        {
            if (PendingAdd != null)
            {
                _addStartX = x;
                _addStartY = y;
                AddRectangle = new System.Drawing.Rectangle(x, y, 0, 0);
                return;
            }
            if (IsOnResizeHandle(x, y))
            {
                _dragControl = SelectedControl;
                _resizing = true;
            }
            else
            {
                SelectAt(x, y, toggle);
                _dragControl = toggle ? null : SelectedControl;
                _resizing = false;
                if (_dragControl != null)
                {
                    _dragOffsetX = x - _dragControl.Left;
                    _dragOffsetY = y - _dragControl.Top;
                    // all selected, unlocked controls move together
                    _dragStartX = x;
                    _dragStartY = y;
                    _dragStart = Selection.Where(c => !c.Locked).ToDictionary(c => c, c => (c.Left, c.Top));
                }
            }
            if (_dragControl != null && _dragControl.Locked)
                _dragControl = null; // upstream: locked controls can't be moved or resized
        }

        public void DragTo(int x, int y)
        {
            if (PendingAdd != null && AddRectangle != null)
            {
                // GetSelectionRectangle: normalized rectangle between press and current point
                AddRectangle = System.Drawing.Rectangle.FromLTRB(Math.Min(_addStartX, x), Math.Min(_addStartY, y),
                    Math.Max(_addStartX, x), Math.Max(_addStartY, y));
                Changed?.Invoke();
                return;
            }
            GUIControl c = _dragControl;
            if (c == null) return;
            if (_resizing)
            {
                int w = Math.Max(1, x - c.Left), h = Math.Max(1, y - c.Top);
                if (w == c.Width && h == c.Height) return;
                c.Width = w;
                c.Height = h;
                Invalidate();
            }
            else
            {
                int left = x - _dragOffsetX, top = y - _dragOffsetY;
                if (left == c.Left && top == c.Top) return;
                if (_dragStart != null && _dragStart.Count > 1)
                {
                    foreach (var pair in _dragStart)
                    {
                        pair.Key.Left = pair.Value.Left + (x - _dragStartX);
                        pair.Key.Top = pair.Value.Top + (y - _dragStartY);
                    }
                }
                else
                {
                    c.Left = left;
                    c.Top = top;
                }
                Invalidate();
            }
            Changed?.Invoke();
        }

        public void EndDrag()
        {
            if (PendingAdd is GuiControlKind kind && AddRectangle is System.Drawing.Rectangle r)
            {
                AddRectangle = null;
                PendingAdd = null; // revert back to select mode, as upstream
                if (r.Width >= 2 && r.Height >= 2) // upstream ignores tiny rectangles
                    AddControl(kind, r.Left, r.Top, r.Width, r.Height);
                else
                    Changed?.Invoke();
                return;
            }
            bool moved = _dragControl != null;
            _dragControl = null;
            _dragStart = null;
            _resizing = false;
            if (moved)
                Edited?.Invoke();
        }

        public bool IsDragging => _dragControl != null || AddRectangle != null;

        /// <summary>Raised after the GUI was edited here (moved/resized, added, deleted, z-order): needs saving.</summary>
        public event Action Edited;

        // ---- adding and deleting (GUIEditor.CreateNewControl / DeleteControlClick) ----

        /// <summary>Adds a control of <paramref name="kind"/> at the given GUI rectangle; returns it.</summary>
        public GUIControl AddControl(GuiControlKind kind, int left, int top, int width, int height)
        {
            if (!(Gui is NormalGUI))
                throw new AGSEditorException("Controls can only be added to normal GUIs.");
            GUIControl newControl;
            switch (kind)
            {
                case GuiControlKind.Button: newControl = new GUIButton(left, top, width, height); break;
                case GuiControlKind.Label:
                    newControl = new GUILabel(left, top, width, height);
                    newControl.Clickable = false; // Labels are non-clickable by default.
                    break;
                case GuiControlKind.TextBox: newControl = new GUITextBox(left, top, width, height); break;
                case GuiControlKind.ListBox: newControl = new GUIListBox(left, top, width, height); break;
                case GuiControlKind.Slider: newControl = new GUISlider(left, top, width, height); break;
                case GuiControlKind.InvWindow: newControl = new GUIInventory(left, top, width, height); break;
                default: throw new AGSEditorException("Unknown control type added: " + kind);
            }
            newControl.Name = ScriptNames.GetFirstAvailableScriptName(_game, newControl.ControlType);
            newControl.ZOrder = Gui.Controls.Count;
            newControl.Parent = Gui;
            newControl.ID = Gui.Controls.Count;
            Gui.Controls.Add(newControl);
            Invalidate();
            _game.NotifyClientsGUIControlAddedOrRemoved(Gui, newControl);
            SelectedControl = newControl;
            Changed?.Invoke();
            Edited?.Invoke();
            return newControl;
        }

        /// <summary>DeleteControlClick: deletes the selected controls except locked ones; returns whether any was deleted.</summary>
        public bool DeleteSelected()
        {
            if (!(Gui is NormalGUI) || SelectedControl == null)
                return false;
            var toDelete = (Selection.Count > 0 ? Selection : new List<GUIControl> { SelectedControl }).Where(c => !c.Locked).ToList();
            if (toDelete.Count == 0)
                return false;
            foreach (GUIControl control in toDelete)
            {
                _game.NotifyClientsGUIControlAddedOrRemoved(Gui, control);
                Gui.DeleteControl(control);
                control.MemberOf?.RemoveFromGroup(control);
                Selection.Remove(control);
            }
            Invalidate();
            SelectedControl = Selection.LastOrDefault();
            Changed?.Invoke();
            Edited?.Invoke();
            return true;
        }

        // ---- align / distribute (GUIEditor context menu) ----

        /// <summary>AlignLeftClick / AlignTopClick: unlocked selected controls take the primary control's left or top.</summary>
        public void Align(bool left)
        {
            if (SelectedControl == null || Selection.Count < 2) return;
            foreach (GUIControl c in Selection)
            {
                if (c.Locked) continue;
                if (left) c.Left = SelectedControl.Left;
                else c.Top = SelectedControl.Top;
            }
            Invalidate();
            Changed?.Invoke();
            Edited?.Invoke();
        }

        /// <summary>DistributeHorizClick / DistributeVertiClick (3 or more controls).</summary>
        public void Distribute(bool horizontally)
        {
            if (Selection.Count < 3) return;
            var sorted = Selection.ToList();
            sorted.Sort(horizontally ? (Comparison<GUIControl>)GUIControl.CompareByLeft : GUIControl.CompareByTop);
            int spacing = horizontally
                ? (sorted[sorted.Count - 1].Left - sorted[0].Left) / (sorted.Count - 1)
                : (sorted[sorted.Count - 1].Top - sorted[0].Top) / (sorted.Count - 1);
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].Locked) continue;
                if (horizontally) sorted[i].Left = sorted[0].Left + spacing * i;
                else sorted[i].Top = sorted[0].Top + spacing * i;
            }
            Invalidate();
            Changed?.Invoke();
            Edited?.Invoke();
        }

        // ---- copy / paste (CopyControlClick / PasteControlClick) ----

        private static List<GUIControl> _clipboard;

        public static bool CanPaste => _clipboard != null && _clipboard.Count > 0;

        public void CopySelection()
        {
            if (SelectedControl == null) return;
            _clipboard = Selection.Select(c => (GUIControl)c.Clone()).ToList();
        }

        /// <summary>Pastes copies at a GUI position, keeping their relative positions; they become the selection.</summary>
        public void Paste(int pasteAtX, int pasteAtY)
        {
            if (!CanPaste || !(Gui is NormalGUI)) return;
            Selection.Clear();
            int refX = _clipboard[0].Left, refY = _clipboard[0].Top;
            foreach (GUIControl source in _clipboard)
            {
                var newControl = (GUIControl)source.Clone();
                newControl.Name = ScriptNames.GetFirstAvailableScriptName(_game, newControl.ControlType);
                newControl.ZOrder = Gui.Controls.Count;
                newControl.ID = Gui.Controls.Count;
                newControl.MemberOf = null;
                newControl.Locked = false;
                newControl.Left = pasteAtX + (source.Left - refX);
                newControl.Top = pasteAtY + (source.Top - refY);
                newControl.Parent = Gui;
                Gui.Controls.Add(newControl);
                Selection.Add(newControl);
                _game.NotifyClientsGUIControlAddedOrRemoved(Gui, newControl);
            }
            Invalidate();
            SetPrimary(Selection.Last());
            Changed?.Invoke();
            Edited?.Invoke();
        }

        /// <summary>Bring to Front / Send to Back (GUIEditor.ChangeControlZOrder).</summary>
        public void MoveSelectedInZOrder(bool toFront)
        {
            GUIControl control = SelectedControl;
            if (control == null) return;
            int oldZ = control.ZOrder;
            int newZ = toFront ? Gui.Controls.Count - 1 : 0;
            if (oldZ == newZ) return;
            foreach (GUIControl other in Gui.Controls)
            {
                if (other == control) continue;
                if (toFront && other.ZOrder > oldZ) other.ZOrder--;
                else if (!toFront && other.ZOrder < oldZ) other.ZOrder++;
            }
            control.ZOrder = newZ;
            Invalidate();
            Changed?.Invoke();
            Edited?.Invoke();
        }

        /// <summary>Repaint after property changes from the grid.</summary>
        public void Refresh()
        {
            _spriteCache.Clear();
            Invalidate();
            OnPropertyChanged(nameof(GuiWidth));
            OnPropertyChanged(nameof(GuiHeight));
            Changed?.Invoke();
        }
    }
}
