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
    /// <summary>
    /// Room editor (Phase 3): the room's background with switchable layers (area masks, objects,
    /// characters starting here, edges), zoom, and click selection for the property grid. Keeps the
    /// natively loaded room open so property changes can be saved back to the .crm.
    /// </summary>
    public enum RoomEditMode { Select, Paint }
    public enum MaskTool { Freehand, Rectangle, Fill }

    public sealed partial class RoomEditorDocument : DocumentViewModel, IDisposable
    {
        // Area colours for mask overlays (index = area number modulo 16).
        private static readonly DrawingColor[] AreaColors =
        {
            DrawingColor.Transparent, DrawingColor.FromArgb(0, 120, 255), DrawingColor.FromArgb(0, 200, 0),
            DrawingColor.FromArgb(255, 60, 60), DrawingColor.FromArgb(255, 200, 0), DrawingColor.FromArgb(200, 0, 255),
            DrawingColor.FromArgb(0, 220, 220), DrawingColor.FromArgb(255, 120, 0), DrawingColor.FromArgb(120, 255, 120),
            DrawingColor.FromArgb(255, 100, 200), DrawingColor.FromArgb(100, 100, 255), DrawingColor.FromArgb(180, 180, 0),
            DrawingColor.FromArgb(0, 140, 120), DrawingColor.FromArgb(160, 80, 40), DrawingColor.FromArgb(220, 220, 220),
            DrawingColor.FromArgb(120, 0, 60),
        };

        private readonly NativeRoom _native;
        private readonly Game _game;
        private readonly SpriteFile _sprites;
        private readonly Action<object> _select;
        private readonly DrawingColor[] _palette;
        private readonly Dictionary<RoomMaskType, PixelImage> _masks = new Dictionary<RoomMaskType, PixelImage>();
        private readonly List<(object Item, System.Drawing.Rectangle Bounds)> _hitTargets = new List<(object, System.Drawing.Rectangle)>();

        public RoomEditorDocument(UnloadedRoom unloaded, NativeRoom native, Room room, Game game, SpriteFile sprites, Action<object> select)
            : base($"Room {unloaded.Number}", unloaded)
        {
            Unloaded = unloaded;
            _native = native;
            Room = room;
            _game = game;
            _sprites = sprites;
            _select = select;
            _palette = game.Palette.Select(p => p.Colour).ToArray();
            foreach (RoomMaskType type in Enum.GetValues<RoomMaskType>())
            {
                try { _masks[type] = native.GetMask(type); } catch (Exception) { }
            }
            Render();
        }

        public UnloadedRoom Unloaded { get; }
        public Room Room { get; }

        /// <summary>True when room properties were changed and the .crm needs saving.</summary>
        public bool IsModified { get; set; }

        public string Info => $"{Room.Width}×{Room.Height}, {Room.ColorDepth}-bit, {Room.BackgroundCount} background(s), " +
            $"{Room.Objects.Count} objects, {Room.Messages.Count} messages — click to select";

        public IReadOnlyList<int> BackgroundFrames => Enumerable.Range(0, Math.Max(1, Room.BackgroundCount)).ToList();

        public IReadOnlyList<int> ZoomLevels { get; } = new[] { 1, 2, 3, 4, 6 };

        [ObservableProperty] private Bitmap _image;
        [ObservableProperty] private double _imageWidth;
        [ObservableProperty] private double _imageHeight;
        [ObservableProperty] private int _zoom = 2;
        [ObservableProperty] private int _backgroundFrame;
        [ObservableProperty] private bool _showObjects = true;
        [ObservableProperty] private bool _showCharacters = true;
        [ObservableProperty] private bool _showEdges = true;
        [ObservableProperty] private bool _showWalkableAreas;
        [ObservableProperty] private bool _showHotspots;
        [ObservableProperty] private bool _showRegions;
        [ObservableProperty] private bool _showWalkBehinds;
        [ObservableProperty] private string _selectionText = "Room";

        // ---- mask painting (upstream room editor's area drawing tools) ----

        public IReadOnlyList<RoomEditMode> EditModes { get; } = Enum.GetValues<RoomEditMode>();
        public IReadOnlyList<RoomMaskType> PaintLayers { get; } = Enum.GetValues<RoomMaskType>();
        public IReadOnlyList<MaskTool> MaskTools { get; } = Enum.GetValues<MaskTool>();

        [ObservableProperty] private RoomEditMode _editMode = RoomEditMode.Select;
        [ObservableProperty] private RoomMaskType _paintLayer = RoomMaskType.WalkableAreas;
        [ObservableProperty] private MaskTool _tool = MaskTool.Freehand;
        [ObservableProperty] private int _areaNumber = 1;
        [ObservableProperty] private bool _canUndo;

        public bool IsPainting => EditMode == RoomEditMode.Paint;

        /// <summary>Highest area number of the paint layer (upstream limits: 49 hotspots, 15 others).</summary>
        public int MaxAreaNumber => PaintLayer == RoomMaskType.Hotspots ? Room.Hotspots.Count - 1 : 15;

        partial void OnEditModeChanged(RoomEditMode value)
        {
            OnPropertyChanged(nameof(IsPainting));
            if (value == RoomEditMode.Paint)
                ShowLayer(PaintLayer);
        }

        partial void OnPaintLayerChanged(RoomMaskType value)
        {
            OnPropertyChanged(nameof(MaxAreaNumber));
            AreaNumber = Math.Min(AreaNumber, MaxAreaNumber);
            if (IsPainting)
                ShowLayer(value);
        }

        private void ShowLayer(RoomMaskType layer)
        {
            switch (layer)
            {
                case RoomMaskType.WalkableAreas: ShowWalkableAreas = true; break;
                case RoomMaskType.Hotspots: ShowHotspots = true; break;
                case RoomMaskType.Regions: ShowRegions = true; break;
                case RoomMaskType.WalkBehinds: ShowWalkBehinds = true; break;
            }
        }

        private readonly Stack<(RoomMaskType Layer, PixelImage Mask)> _undo = new Stack<(RoomMaskType, PixelImage)>();
        private PixelImage _strokeStart; // mask before the current rectangle stroke
        private int _lastX, _lastY, _startX, _startY;
        private bool _stroking;

        private int MaskScale(RoomMaskType type) => type == RoomMaskType.WalkBehinds ? 1 : Math.Max(1, Room.MaskResolution);

        public void BeginStroke(int x, int y)
        {
            if (!_masks.TryGetValue(PaintLayer, out PixelImage mask)) return;
            _undo.Push((PaintLayer, mask.Clone()));
            CanUndo = true;
            int s = MaskScale(PaintLayer);
            _startX = _lastX = x / s;
            _startY = _lastY = y / s;
            _stroking = true;
            switch (Tool)
            {
                case MaskTool.Freehand:
                    MaskPainter.Line(mask, _lastX, _lastY, _lastX, _lastY, (byte)AreaNumber);
                    break;
                case MaskTool.Rectangle:
                    _strokeStart = mask.Clone();
                    MaskPainter.Rectangle(mask, _startX, _startY, _startX, _startY, (byte)AreaNumber);
                    break;
                case MaskTool.Fill:
                    MaskPainter.Fill(mask, _startX, _startY, (byte)AreaNumber);
                    _stroking = false;
                    Commit();
                    return;
            }
            Render();
        }

        public void ContinueStroke(int x, int y)
        {
            if (!_stroking || !_masks.TryGetValue(PaintLayer, out PixelImage mask)) return;
            int s = MaskScale(PaintLayer);
            int mx = x / s, my = y / s;
            if (Tool == MaskTool.Freehand)
            {
                if (mx == _lastX && my == _lastY) return;
                MaskPainter.Line(mask, _lastX, _lastY, mx, my, (byte)AreaNumber);
            }
            else if (Tool == MaskTool.Rectangle)
            {
                _strokeStart.Pixels.CopyTo(mask.Pixels);
                MaskPainter.Rectangle(mask, _startX, _startY, mx, my, (byte)AreaNumber);
            }
            _lastX = mx;
            _lastY = my;
            Render();
        }

        public void EndStroke()
        {
            if (!_stroking) return;
            _stroking = false;
            _strokeStart = null;
            Commit();
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            var (layer, mask) = _undo.Pop();
            _masks[layer] = mask;
            _native.SetMask(layer, mask);
            CanUndo = _undo.Count > 0;
            IsModified = true;
            Render();
        }

        private static string MaskName(RoomMaskType type) => type switch
        {
            RoomMaskType.Hotspots => "Hotspots",
            RoomMaskType.Regions => "Regions",
            RoomMaskType.WalkableAreas => "Walkable Areas",
            RoomMaskType.WalkBehinds => "Walk-behinds",
            _ => string.Empty,
        };

        /// <summary>
        /// BaseAreasEditorFilter.ImportMaskFromFile + agsnative import_area_mask/validate_mask: an indexed image of the
        /// mask's size replaces the current layer's mask (undoable). Returns null, or a message for the user when the
        /// image was refused or had invalid colours (those become 0, as upstream).
        /// </summary>
        public string ImportMask(PixelImage image)
        {
            RoomMaskType type = PaintLayer;
            if (!_masks.TryGetValue(type, out PixelImage mask)) return "The room has no such mask.";
            int maskFactor = MaskScale(type);
            if (image.Width != Room.Width / maskFactor || image.Height != Room.Height / maskFactor)
                return $"This image cannot be imported as a {MaskName(type)} mask, because it does not match the size of the room background (accounting to Mask Resolution setting)." +
                    $"\nImage size: {image.Width} x {image.Height}" +
                    $"\nRoom size: {Room.Width} x {Room.Height}" +
                    $"\nExpected mask size: {Room.Width / maskFactor} x {Room.Height / maskFactor}";
            if (image.PixelFormat != ImagePixelFormat.Indexed8)
                return "This is not a valid mask bitmap. Masks must be indexed colour images (1-, 4-, or 8-bit) that use the first colours in the palette to draw the room areas.";

            _undo.Push((type, mask.Clone()));
            CanUndo = true;
            int maxColour = type == RoomMaskType.Hotspots ? 50 : 16; // MAX_ROOM_HOTSPOTS : MAX_WALK_AREAS
            bool errFound = false;
            for (int y = 0; y < mask.Height; y++)
            {
                ReadOnlySpan<byte> src = image.GetRow(y);
                Span<byte> dst = mask.GetRow(y);
                for (int x = 0; x < mask.Width; x++)
                {
                    byte v = src[x];
                    if (v >= maxColour) { v = 0; errFound = true; }
                    dst[x] = v;
                }
            }
            PaintLayer = type;
            Commit();
            if (!errFound) return null;
            string name = MaskName(type);
            return $"Invalid colours were found in the {name} mask. They have now been removed." +
                "\n\nWhen drawing a mask in an external image editor, you need to make " +
                "sure that the image is set as 256-colour (Indexed Palette), and that " +
                $"you use the first {maxColour} colours in the palette for drawing {name}. Palette " +
                "entry 0 corresponds to No Area, palette index 1 corresponds to area 1, and " +
                "so forth.";
        }

        /// <summary>export_area_mask: the current layer's mask as an 8-bit image with the game palette.</summary>
        public PixelImage ExportMask()
        {
            if (!_masks.TryGetValue(PaintLayer, out PixelImage mask)) return null;
            var palette = _game.Palette.Select(p => p.Colour).ToList();
            return new PixelImage(mask.Width, mask.Height, ImagePixelFormat.Indexed8, mask.Pixels, palette);
        }

        // Hands the edited mask to the native room (saved with the .crm).
        private void Commit()
        {
            _native.SetMask(PaintLayer, _masks[PaintLayer]);
            IsModified = true;
            MaskChanged?.Invoke();
            Render();
        }

        /// <summary>Raised after a mask was painted (to mark the project modified).</summary>
        public event Action MaskChanged;

        /// <summary>Area number of a mask at a room coordinate (for tests and tooltips).</summary>
        public int GetArea(RoomMaskType type, int x, int y) =>
            _masks.TryGetValue(type, out PixelImage mask) ? MaskValueAt(mask, type, x, y) : 0;

        partial void OnZoomChanged(int value) => UpdateSize();
        partial void OnBackgroundFrameChanged(int value)
        {
            OnPropertyChanged(nameof(CanDeleteBackground));
            Render();
        }
        partial void OnShowObjectsChanged(bool value) => Render();
        partial void OnShowCharactersChanged(bool value) => Render();
        partial void OnShowEdgesChanged(bool value) => Render();
        partial void OnShowWalkableAreasChanged(bool value) => Render();
        partial void OnShowHotspotsChanged(bool value) => Render();
        partial void OnShowRegionsChanged(bool value) => Render();
        partial void OnShowWalkBehindsChanged(bool value) => Render();

        private void UpdateSize()
        {
            ImageWidth = Room.Width * Zoom;
            ImageHeight = Room.Height * Zoom;
        }

        /// <summary>Composes background and enabled layers into <see cref="Image"/>.</summary>
        public void Render()
        {
            PixelImage canvas = PixelConversion.ToBgra32(_native.GetBackground(Math.Clamp(BackgroundFrame, 0, Math.Max(0, Room.BackgroundCount - 1))));

            if (ShowWalkBehinds) Overlay(canvas, RoomMaskType.WalkBehinds);
            if (ShowWalkableAreas) Overlay(canvas, RoomMaskType.WalkableAreas);
            if (ShowRegions) Overlay(canvas, RoomMaskType.Regions);
            if (ShowHotspots) Overlay(canvas, RoomMaskType.Hotspots);

            _hitTargets.Clear();
            if (ShowObjects)
            {
                foreach (RoomObject obj in Room.Objects)
                {
                    PixelImage sprite = LoadSprite(obj.Image);
                    if (sprite == null) continue;
                    // AGS places objects by their bottom-left corner.
                    var bounds = new System.Drawing.Rectangle(obj.StartX, obj.StartY - sprite.Height, sprite.Width, sprite.Height);
                    Blit(canvas, sprite, bounds.X, bounds.Y, obj.Visible ? 255 : 110);
                    _hitTargets.Add((obj, bounds));
                }
            }
            if (ShowCharacters)
            {
                foreach (Character c in _game.RootCharacterFolder.AllItemsFlat.Where(c => c.StartingRoom == Room.Number))
                {
                    ViewFrame frame = _game.FindViewByID(c.NormalView)?.Loops.FirstOrDefault()?.Frames.FirstOrDefault();
                    PixelImage sprite = frame == null ? null : LoadSprite(frame.Image);
                    if (sprite == null) continue;
                    // Characters are placed by their bottom centre.
                    var bounds = new System.Drawing.Rectangle(c.StartX - sprite.Width / 2, c.StartY - sprite.Height, sprite.Width, sprite.Height);
                    Blit(canvas, sprite, bounds.X, bounds.Y, 255);
                    _hitTargets.Add((c, bounds));
                }
            }
            if (ShowWalkBehinds)
            {
                // Baselines of the walk-behind areas painted in this room, in the area's colour.
                foreach (int area in WalkBehindAreasInUse())
                {
                    DrawingColor c = AreaColors[area % AreaColors.Length];
                    HLine(canvas, Room.WalkBehinds[area].Baseline, DrawingColor.FromArgb(255, c.R, c.G, c.B));
                }
            }
            if (ShowEdges)
            {
                var edge = DrawingColor.FromArgb(220, 255, 220, 0);
                VLine(canvas, Room.LeftEdgeX, edge);
                VLine(canvas, Room.RightEdgeX, edge);
                HLine(canvas, Room.TopEdgeY, edge);
                HLine(canvas, Room.BottomEdgeY, edge);
            }

            Image = ImageConversion.ToBitmap(canvas);
            UpdateSize();
        }

        /// <summary>Selects what is under a room coordinate: object/character, else hotspot, else the room.</summary>
        public object SelectAt(int x, int y)
        {
            object hit = null;
            for (int i = _hitTargets.Count - 1; i >= 0 && hit == null; i--)
                if (_hitTargets[i].Bounds.Contains(x, y))
                    hit = _hitTargets[i].Item;

            if (hit == null && _masks.TryGetValue(RoomMaskType.Hotspots, out PixelImage hotspots))
            {
                int area = MaskValueAt(hotspots, RoomMaskType.Hotspots, x, y);
                if (area > 0 && area < Room.Hotspots.Count)
                    hit = Room.Hotspots[area];
            }
            hit ??= Room;

            SelectionText = hit switch
            {
                RoomObject o => $"Object {o.ID}: {o.Name}",
                Character c => $"Character {c.ID}: {c.ScriptName}",
                RoomHotspot h => $"Hotspot {h.ID}: {h.Name} ({h.Description})",
                _ => "Room",
            };
            _select?.Invoke(hit);
            return hit;
        }

        private object _dragItem;
        private int _dragOffsetX, _dragOffsetY;

        /// <summary>Raised after an object or character was moved with the mouse.</summary>
        public event Action<object> ItemMoved;

        /// <summary>A draggable line: one of the room edges or a walk-behind baseline.</summary>
        public sealed class RoomLine
        {
            public RoomLine(string name, bool vertical, Func<int> get, Action<int> set, object owner)
            {
                Name = name; Vertical = vertical; Get = get; Set = set; Owner = owner;
            }
            public string Name { get; }
            public bool Vertical { get; }
            public Func<int> Get { get; }
            public Action<int> Set { get; }
            public object Owner { get; }
        }

        /// <summary>Walk-behind areas (1..) that have pixels in the mask.</summary>
        public IReadOnlyList<int> WalkBehindAreasInUse()
        {
            var used = new SortedSet<int>();
            if (_masks.TryGetValue(RoomMaskType.WalkBehinds, out PixelImage mask))
            {
                for (int y = 0; y < mask.Height; y++)
                    foreach (byte v in mask.GetRow(y).Slice(0, mask.Width))
                        if (v > 0 && v < Room.WalkBehinds.Count) used.Add(v);
            }
            return used.ToList();
        }

        /// <summary>The edge or baseline line near a room coordinate (within a few screen pixels), or null.</summary>
        public RoomLine LineAt(int x, int y)
        {
            int tolerance = Math.Max(1, 4 / Math.Max(1, Zoom));
            var lines = new List<RoomLine>();
            if (ShowEdges)
            {
                lines.Add(new RoomLine("Left edge", true, () => Room.LeftEdgeX, v => Room.LeftEdgeX = v, Room));
                lines.Add(new RoomLine("Right edge", true, () => Room.RightEdgeX, v => Room.RightEdgeX = v, Room));
                lines.Add(new RoomLine("Top edge", false, () => Room.TopEdgeY, v => Room.TopEdgeY = v, Room));
                lines.Add(new RoomLine("Bottom edge", false, () => Room.BottomEdgeY, v => Room.BottomEdgeY = v, Room));
            }
            if (ShowWalkBehinds)
            {
                foreach (int area in WalkBehindAreasInUse())
                {
                    RoomWalkBehind wb = Room.WalkBehinds[area];
                    lines.Add(new RoomLine($"Walk-behind {area} baseline", false, () => wb.Baseline, v => wb.Baseline = v, wb));
                }
            }
            return lines
                .Select(l => (Line: l, Distance: Math.Abs((l.Vertical ? x : y) - l.Get())))
                .Where(t => t.Distance <= tolerance)
                .OrderBy(t => t.Distance)
                .Select(t => t.Line)
                .FirstOrDefault();
        }

        private RoomLine _dragLine;

        /// <summary>
        /// Starts dragging at a room coordinate: the object/character there (after selecting it), else an
        /// edge or walk-behind baseline near it (upstream's Edges and Walk-behinds modes).
        /// </summary>
        public void BeginDrag(int x, int y)
        {
            object item = SelectAt(x, y);
            _dragLine = null;
            switch (item)
            {
                case RoomObject o: _dragItem = o; _dragOffsetX = x - o.StartX; _dragOffsetY = y - o.StartY; break;
                case Character c: _dragItem = c; _dragOffsetX = x - c.StartX; _dragOffsetY = y - c.StartY; break;
                default:
                    _dragItem = null;
                    _dragLine = LineAt(x, y);
                    if (_dragLine != null)
                    {
                        _dragItem = _dragLine;
                        SelectionText = $"{_dragLine.Name}: {_dragLine.Get()}";
                        _select?.Invoke(_dragLine.Owner);
                    }
                    break;
            }
        }

        /// <summary>Moves the dragged item so that the grab point follows the mouse.</summary>
        public void DragTo(int x, int y)
        {
            if (_dragLine != null)
            {
                int value = _dragLine.Vertical ? Math.Clamp(x, 0, Room.Width - 1) : Math.Clamp(y, 0, Room.Height - 1);
                if (value == _dragLine.Get()) return;
                _dragLine.Set(value);
                SelectionText = $"{_dragLine.Name}: {value}";
                IsModified = true;
                Render();
                return;
            }
            int nx = x - _dragOffsetX, ny = y - _dragOffsetY;
            switch (_dragItem)
            {
                case RoomObject o when o.StartX != nx || o.StartY != ny:
                    o.StartX = nx; o.StartY = ny;
                    IsModified = true;
                    break;
                case Character c when c.StartX != nx || c.StartY != ny:
                    c.StartX = nx; c.StartY = ny; // characters live in Game.agf, saved with the project
                    break;
                default:
                    return;
            }
            Render();
        }

        public void EndDrag()
        {
            if (_dragItem != null)
                ItemMoved?.Invoke(_dragLine?.Owner ?? _dragItem);
            _dragItem = null;
            _dragLine = null;
        }

        public bool IsDragging => _dragItem != null;

        /// <summary>
        /// Imports an image as background <paramref name="frame"/> (the current count adds a frame), following
        /// upstream RoomSettingsEditor.ImportBackground: size checks and questions, masks cleared and edges
        /// reset when the room size changes. <paramref name="ask"/> answers yes/no questions; problems are
        /// returned as a message (null on success).
        /// </summary>
        public string ImportBackground(string file, int frame, Func<string, bool> ask, bool useExactPalette = false)
        {
            int width, height, bits;
            using (PixelImage probe = new NativeImageCodec().Decode(file).Frames[0])
            {
                width = Math.Max(probe.Width, _game.MinRoomWidth);
                height = Math.Max(probe.Height, _game.MinRoomHeight);
                bits = PixelImage.GetBytesPerPixel(probe.PixelFormat) * 8;
            }
            bool deleteExtraFrames = false, roomSizeChanged = false;
            if (_game.Settings.ColorDepth == GameColorDepth.Palette && bits > 8)
                return "You cannot import a hi-colour or true-colour image into a 256-colour game.";
            if (width != Room.Width || height != Room.Height || (frame == 0 && Room.Resolution != RoomResolution.Real))
            {
                if (frame > 0)
                    return "This image is a different size to the main background image. All backgrounds for the room must be of the same size." +
                        Environment.NewLine + Environment.NewLine + "Main background: " + Room.Width + " x " + Room.Height + "; this image: " + width + " x " + height;
                if (Room.BackgroundCount > 1)
                {
                    if (!ask("Changing the size of the main background will mean that all extra backgrounds are deleted. Are you sure you want to do this?"))
                        return null;
                    deleteExtraFrames = true;
                }
                if (!ask("The new background is a different size to the old one. If you import it, all your regions, hotspots and walkable areas will be cleared, and edges reset to defaults. Do you want to proceed?"))
                    return null;
                roomSizeChanged = true;
            }

            int lost = _native.ImportBackground(Room, frame, file, _game, useExactPalette);
            if (deleteExtraFrames)
                while (Room.BackgroundCount > 1)
                    _native.DeleteBackground(Room, 1);
            if (roomSizeChanged)
            {
                // RoomSettingsEditor.OnRoomSizeChanged
                Room.LeftEdgeX = 0;
                Room.RightEdgeX = Room.Width - 1;
                Room.TopEdgeY = 0;
                Room.BottomEdgeY = Room.Height - 1;
            }
            AfterBackgroundsChanged(frame);
            return lost > 0 ? "WARNING: This image uses more colours than are allocated to backgrounds. Some colours will be lost." : null;
        }

        /// <summary>Deletes an extra background frame (the main background can only be replaced).</summary>
        public void DeleteBackground(int frame)
        {
            if (frame <= 0 || frame >= Room.BackgroundCount) return;
            _native.DeleteBackground(Room, frame);
            AfterBackgroundsChanged(Math.Min(frame, Room.BackgroundCount - 1));
        }

        private void AfterBackgroundsChanged(int showFrame)
        {
            _masks.Clear();
            foreach (RoomMaskType type in Enum.GetValues<RoomMaskType>())
            {
                try { _masks[type] = _native.GetMask(type); } catch (Exception) { }
            }
            _undo.Clear();
            CanUndo = false;
            IsModified = true;
            OnPropertyChanged(nameof(BackgroundFrames));
            OnPropertyChanged(nameof(Info));
            OnPropertyChanged(nameof(CanDeleteBackground));
            BackgroundFrame = showFrame;
            Render();
        }

        public bool CanDeleteBackground => Room.BackgroundCount > 1 && BackgroundFrame > 0;

        /// <summary>Writes the room's editable data back to its .crm (as the upstream editor's save).</summary>
        public void Save(string fileName, System.Text.Encoding gameTextEncoding)
        {
            _native.ApplyRoom(Room, gameTextEncoding);
            _native.Save(fileName);
            IsModified = false;
        }

        public void Dispose() => _native.Dispose();

        private int MaskValueAt(PixelImage mask, RoomMaskType type, int x, int y)
        {
            int scale = type == RoomMaskType.WalkBehinds ? 1 : Math.Max(1, Room.MaskResolution);
            int mx = x / scale, my = y / scale;
            if (mx < 0 || my < 0 || mx >= mask.Width || my >= mask.Height) return 0;
            return mask.GetRow(my)[mx];
        }

        private void Overlay(PixelImage canvas, RoomMaskType type)
        {
            if (!_masks.TryGetValue(type, out PixelImage mask)) return;
            for (int y = 0; y < canvas.Height; y++)
            {
                Span<byte> row = canvas.GetRow(y);
                for (int x = 0; x < canvas.Width; x++)
                {
                    int area = MaskValueAt(mask, type, x, y);
                    if (area == 0) continue;
                    DrawingColor c = AreaColors[area % AreaColors.Length];
                    BlendPixel(row, x, c.B, c.G, c.R, 120);
                }
            }
        }

        private PixelImage LoadSprite(int number)
        {
            try
            {
                PixelImage sprite = _sprites?.LoadSprite(number, _palette);
                return sprite == null ? null : PixelConversion.ToBgra32(sprite);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Copies a sprite onto the canvas; magenta is AGS's transparent colour for non-alpha sprites.
        private static void Blit(PixelImage canvas, PixelImage sprite, int left, int top, int opacity)
        {
            for (int sy = 0; sy < sprite.Height; sy++)
            {
                int y = top + sy;
                if (y < 0 || y >= canvas.Height) continue;
                Span<byte> src = sprite.GetRow(sy);
                Span<byte> dst = canvas.GetRow(y);
                for (int sx = 0; sx < sprite.Width; sx++)
                {
                    int x = left + sx;
                    if (x < 0 || x >= canvas.Width) continue;
                    int p = sx * 4;
                    if (src[p] == 255 && src[p + 1] == 0 && src[p + 2] == 255) continue;
                    int alpha = src[p + 3] * opacity / 255;
                    if (alpha == 0) continue;
                    BlendPixel(dst, x, src[p], src[p + 1], src[p + 2], alpha);
                }
            }
        }

        private static void BlendPixel(Span<byte> row, int x, byte b, byte g, byte r, int alpha)
        {
            int p = x * 4;
            row[p] = (byte)((b * alpha + row[p] * (255 - alpha)) / 255);
            row[p + 1] = (byte)((g * alpha + row[p + 1] * (255 - alpha)) / 255);
            row[p + 2] = (byte)((r * alpha + row[p + 2] * (255 - alpha)) / 255);
            row[p + 3] = 255;
        }

        private static void VLine(PixelImage canvas, int x, DrawingColor c)
        {
            if (x < 0 || x >= canvas.Width) return;
            for (int y = 0; y < canvas.Height; y++)
                BlendPixel(canvas.GetRow(y), x, c.B, c.G, c.R, c.A);
        }

        private static void HLine(PixelImage canvas, int y, DrawingColor c)
        {
            if (y < 0 || y >= canvas.Height) return;
            Span<byte> row = canvas.GetRow(y);
            for (int x = 0; x < canvas.Width; x++)
                BlendPixel(row, x, c.B, c.G, c.R, c.A);
        }
    }
}
