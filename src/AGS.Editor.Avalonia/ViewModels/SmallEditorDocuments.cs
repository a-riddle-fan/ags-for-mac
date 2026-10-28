using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using AGS.Types;
using DrawingColor = System.Drawing.Color;

namespace AGS.Editor.Avalonia.ViewModels
{
    // ---- Text parser (upstream Panes/TextParserEditor) --------------------------------------------

    public sealed class TextParserWordRow
    {
        public TextParserWordRow(TextParserWord word) { Word = word; }
        public TextParserWord Word { get; }
        public int Group => Word.WordGroup;
        public string Text => Word.Word;
        public string Type => Word.Type.ToString();
    }

    /// <summary>Word list sorted by word group; words are edited in the property grid.</summary>
    public sealed partial class TextParserDocument : DocumentViewModel
    {
        private readonly TextParser _parser;
        private readonly Action<object> _select;

        public TextParserDocument(TextParser parser, Action<object> select) : base("Text Parser", parser)
        {
            _parser = parser;
            _select = select;
            Rebuild();
        }

        public ObservableCollection<TextParserWordRow> Words { get; } = new ObservableCollection<TextParserWordRow>();

        [ObservableProperty] private TextParserWordRow _selected;
        [ObservableProperty] private string _findText = "";

        public event Action Edited;

        partial void OnSelectedChanged(TextParserWordRow value) => _select?.Invoke(value?.Word ?? (object)_parser);

        /// <summary>Find word: selects the first word that matches (case-insensitive) as typed.</summary>
        partial void OnFindTextChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            TextParserWordRow match = Words.FirstOrDefault(w => string.Equals(w.Text, value.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? Words.FirstOrDefault(w => (w.Text ?? "").StartsWith(value.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match != null)
                Selected = match;
        }

        /// <summary>Upstream sorts by word group only (TextParserWordComparer); the list keeps insertion order within a group.</summary>
        public void Rebuild(TextParserWord select = null)
        {
            Words.Clear();
            foreach (TextParserWord word in _parser.Words.OrderBy(w => w.WordGroup))
                Words.Add(new TextParserWordRow(word));
            if (select != null)
                Selected = Words.FirstOrDefault(r => r.Word == select);
        }

        /// <summary>Whether the selected word may be deleted (the special groups keep at least one word).</summary>
        public bool CanDelete(TextParserWordRow row)
        {
            if (row == null) return false;
            int group = row.Group;
            if (group == TextParser.WORD_GROUP_ID_ANYWORD || group == TextParser.WORD_GROUP_ID_IGNORE || group == TextParser.WORD_GROUP_ID_REST_OF_LINE)
                return _parser.CountWordsInWordGroup(group) > 1;
            return true;
        }

        public TextParserWord AddWord(string text)
        {
            var word = new TextParserWord(_parser.GetAvailableWordGroupID(), text ?? "", TextParserWordType.Normal);
            _parser.Words.Add(word);
            Rebuild(word);
            Edited?.Invoke();
            return word;
        }

        public TextParserWord AddSynonym(string text)
        {
            if (Selected == null) return null;
            var word = new TextParserWord(Selected.Word.WordGroup, text ?? "", Selected.Word.Type);
            _parser.Words.Add(word);
            Rebuild(word);
            Edited?.Invoke();
            return word;
        }

        public void DeleteSelected()
        {
            if (!CanDelete(Selected)) return;
            _parser.Words.Remove(Selected.Word);
            Rebuild();
            Edited?.Invoke();
        }
    }

    // ---- Lip sync (upstream Panes/LipSyncEditor) -------------------------------------------------

    public sealed class LipSyncFrameRow : ObservableObject
    {
        private readonly LipSync _lipSync;
        private readonly Action _changed;

        public LipSyncFrameRow(LipSync lipSync, int frame, Action changed)
        {
            _lipSync = lipSync;
            Frame = frame;
            _changed = changed;
        }

        public int Frame { get; }

        public string Letters
        {
            get => _lipSync.CharactersPerFrame[Frame];
            set
            {
                if (_lipSync.CharactersPerFrame[Frame] == value) return;
                _lipSync.CharactersPerFrame[Frame] = value ?? string.Empty;
                OnPropertyChanged();
                _changed();
            }
        }
    }

    /// <summary>The 20 frames with the letters (separated by '/') that map to them; Type/DefaultFrame in the grid.</summary>
    public sealed class LipSyncDocument : DocumentViewModel
    {
        public LipSyncDocument(LipSync lipSync) : base("Lip Sync", lipSync)
        {
            LipSync = lipSync;
            for (int i = 0; i < lipSync.CharactersPerFrame.Length; i++)
                Frames.Add(new LipSyncFrameRow(lipSync, i, () => Edited?.Invoke()));
        }

        public LipSync LipSync { get; }
        public ObservableCollection<LipSyncFrameRow> Frames { get; } = new ObservableCollection<LipSyncFrameRow>();
        public event Action Edited;
    }

    // ---- Palette & colour finder (upstream Panes/PaletteEditor) ----------------------------------

    public sealed partial class PaletteSlot : ObservableObject
    {
        public PaletteSlot(PaletteEntry entry) { Entry = entry; }
        public PaletteEntry Entry { get; }
        public int Index => Entry.Index;
        public IBrush Brush => new SolidColorBrush(Color.FromRgb(Entry.Colour.R, Entry.Colour.G, Entry.Colour.B));
        /// <summary>Background slots are marked as in upstream's palette panel.</summary>
        public bool IsBackground => Entry.ColourType == PaletteColourType.Background;
        public void Refresh() { OnPropertyChanged(nameof(Brush)); OnPropertyChanged(nameof(IsBackground)); }
        [ObservableProperty] private bool _isSelected;
    }

    /// <summary>
    /// 256-colour palette (8-bit games) with selection for the property grid, and the colour finder that
    /// converts between RGB and AGS colour numbers (AgsColourNumberToColorDirect / ColorToAgsColourNumberDirect).
    /// </summary>
    public sealed partial class PaletteDocument : DocumentViewModel
    {
        private readonly Game _game;
        private readonly Action<object> _select;
        private bool _updating;

        public PaletteDocument(Game game, Action<object> select) : base("Colours", game.Palette)
        {
            _game = game;
            _select = select;
            foreach (PaletteEntry entry in game.Palette)
                Slots.Add(new PaletteSlot(entry));
            IsPaletteGame = game.Settings.ColorDepth == GameColorDepth.Palette;
            SetFromRgb(255, 255, 255);
        }

        public ObservableCollection<PaletteSlot> Slots { get; } = new ObservableCollection<PaletteSlot>();
        public bool IsPaletteGame { get; }

        [ObservableProperty] private int _red;
        [ObservableProperty] private int _green;
        [ObservableProperty] private int _blue;
        [ObservableProperty] private int _colourNumber;
        [ObservableProperty] private string _webColour = "";
        [ObservableProperty] private IBrush _preview;

        public void SelectSlot(PaletteSlot slot)
        {
            foreach (PaletteSlot s in Slots)
                s.IsSelected = s == slot;
            _select?.Invoke(slot?.Entry);
            if (slot != null)
                SetFromRgb(slot.Entry.Colour.R, slot.Entry.Colour.G, slot.Entry.Colour.B);
        }

        public void RefreshSlots()
        {
            foreach (PaletteSlot s in Slots) s.Refresh();
        }

        partial void OnRedChanged(int value) => FromRgbFields();
        partial void OnGreenChanged(int value) => FromRgbFields();
        partial void OnBlueChanged(int value) => FromRgbFields();

        private void FromRgbFields()
        {
            if (!_updating) SetFromRgb(Red, Green, Blue);
        }

        partial void OnColourNumberChanged(int value)
        {
            if (_updating) return;
            int n = value < 0 || value > 65535 ? 0 : value;
            DrawingColor c = (AGSColor.ColorMapper as AGS.Editor.ColorMapper)?.AgsColourNumberToColorDirect(n) ?? DrawingColor.Black;
            SetFromRgb(c.R, c.G, c.B, keepNumber: true);
        }

        private void SetFromRgb(int r, int g, int b, bool keepNumber = false)
        {
            _updating = true;
            try
            {
                Red = Math.Clamp(r, 0, 255);
                Green = Math.Clamp(g, 0, 255);
                Blue = Math.Clamp(b, 0, 255);
                var color = DrawingColor.FromArgb(Red, Green, Blue);
                if (!keepNumber)
                    ColourNumber = (AGSColor.ColorMapper as AGS.Editor.ColorMapper)?.ColorToAgsColourNumberDirect(color) ?? 0;
                WebColour = $"#{Red:X2}{Green:X2}{Blue:X2}";
                Preview = new SolidColorBrush(Color.FromRgb((byte)Red, (byte)Green, (byte)Blue));
            }
            finally
            {
                _updating = false;
            }
        }
    }
}
