using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    /// <summary>One sprite in the sprite manager grid.</summary>
    public sealed class SpriteThumbnail
    {
        public SpriteThumbnail(Sprite sprite, Bitmap image)
        {
            Sprite = sprite;
            Image = image;
        }

        public Sprite Sprite { get; }
        public Bitmap Image { get; }
        public int Number => Sprite.Number;
        public string Caption => $"{Sprite.Number}";
        public string Details => $"{Sprite.Width}×{Sprite.Height}, {Sprite.ColorDepth}-bit{(Sprite.AlphaChannel ? ", alpha" : "")}";
    }

    /// <summary>
    /// Sprite manager view of one sprite folder (upstream SpriteSelector): thumbnails read from the
    /// game's acsprset.spr through upstream's sprite reader.
    /// </summary>
    public sealed partial class SpriteFolderDocument : DocumentViewModel
    {
        private readonly Action<Sprite> _onSelected;

        public SpriteFolderDocument(ISpriteFolder folder, Game game, SpriteFile sprites, Action<Sprite> onSelected)
            : base("Sprites: " + folder.Name, folder)
        {
            Folder = folder;
            _onSelected = onSelected;
            DrawingColor[] palette = game.Palette.Select(p => p.Colour).ToArray();
            foreach (Sprite sprite in folder.Sprites.OrderBy(s => s.Number))
            {
                Bitmap image = null;
                try
                {
                    PixelImage pixels = sprites?.LoadSprite(sprite.Number, palette);
                    if (pixels != null)
                        image = ImageConversion.ToBitmap(pixels, transparentMagenta: !sprite.AlphaChannel);
                }
                catch (Exception)
                {
                    // Unreadable sprites show as empty cells, like the upstream editor's placeholder.
                }
                Items.Add(new SpriteThumbnail(sprite, image));
            }
        }

        public ISpriteFolder Folder { get; }

        public ObservableCollection<SpriteThumbnail> Items { get; } = new ObservableCollection<SpriteThumbnail>();

        public string Summary => $"{Items.Count} sprites in \"{Folder.Name}\"{(Folder.SubFolders.Count > 0 ? $", {Folder.SubFolders.Count} subfolders" : "")}";

        [ObservableProperty]
        private SpriteThumbnail _selected;

        /// <summary>All selected thumbnails (⌘/⇧-click), bound to the list.</summary>
        public global::Avalonia.Collections.AvaloniaList<SpriteThumbnail> SelectedItems { get; } = new global::Avalonia.Collections.AvaloniaList<SpriteThumbnail>();

        /// <summary>The selected sprites in display order (or the single selection).</summary>
        public IReadOnlyList<Sprite> SelectedSprites =>
            (SelectedItems.Count > 0 ? SelectedItems.Distinct().OrderBy(t => Items.IndexOf(t)).Select(t => t.Sprite) : Selected != null ? new[] { Selected.Sprite } : Array.Empty<Sprite>()).ToList();

        partial void OnSelectedChanged(SpriteThumbnail value)
        {
            if (value != null)
                _onSelected?.Invoke(value.Sprite);
        }
    }
}
