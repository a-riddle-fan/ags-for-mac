using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using AGS.Editor.Avalonia.Services;
using AGS.Imaging;
using AGS.Native;

namespace AGS.Editor.Avalonia.ViewModels
{
    /// <summary>
    /// Font preview (upstream Panes/FontEditor): sample text and the character table drawn with the game font
    /// by upstream's font code.
    /// </summary>
    public sealed partial class FontDocument : DocumentViewModel
    {
        public FontDocument(AGS.Types.Font font)
            : base($"Font {font.ID}: {font.Name}", font)
        {
            Font = font;
            Refresh();
        }

        public AGS.Types.Font Font { get; }

        public IReadOnlyList<int> ZoomLevels { get; } = new[] { 1, 2, 3, 4 };

        [ObservableProperty] private int _zoom = 2;
        [ObservableProperty] private string _sampleText = "The quick brown fox jumps over the lazy dog. Äöü ß 0123456789 !?";
        [ObservableProperty] private Bitmap _sample;
        [ObservableProperty] private double _sampleWidth;
        [ObservableProperty] private double _sampleHeight;
        [ObservableProperty] private Bitmap _characters;
        [ObservableProperty] private double _charactersWidth;
        [ObservableProperty] private double _charactersHeight;
        [ObservableProperty] private string _info = "";

        partial void OnZoomChanged(int value) => Refresh();
        partial void OnSampleTextChanged(string value) => RenderSample();

        /// <summary>Redraws everything (after font settings changed).</summary>
        public void Refresh()
        {
            try
            {
                var m = NativeGuiRenderer.GetFontMetrics(Font.ID);
                Info = $"{(string.IsNullOrEmpty(m.Name) ? Font.ProjectFilename : m.Name)} · {(m.IsBitmapFont ? "bitmap (WFN)" : "TrueType")} · " +
                       $"height {m.Height} px (real {m.RealHeight}), line spacing {m.LineSpacing} · characters up to {m.TopmostChar}";
                RenderSample();
                // Character table: 16 per row from 32 up to the highest code (at most 255, as the ANSI table).
                int last = Math.Min(255, Math.Max(32, m.TopmostChar));
                var table = new StringBuilder();
                for (int row = 32; row <= last; row += 16)
                {
                    for (int c = row; c < row + 16 && c <= last; c++)
                        table.Append((char)c).Append(' ');
                    table.Append('\n');
                }
                (Characters, CharactersWidth, CharactersHeight) = Render(table.ToString(), 0);
            }
            catch (Exception ex)
            {
                Info = "Preview unavailable: " + ex.Message;
            }
        }

        private void RenderSample()
        {
            try
            {
                (Sample, SampleWidth, SampleHeight) = Render(SampleText, 320);
            }
            catch (Exception)
            {
                Sample = null;
            }
        }

        private (Bitmap, double, double) Render(string text, int maxWidth)
        {
            using PixelImage image = NativeGuiRenderer.DrawText(Font.ID, string.IsNullOrEmpty(text) ? " " : text, maxWidth);
            return (ImageConversion.ToBitmap(image), image.Width * Zoom, image.Height * Zoom);
        }
    }
}
