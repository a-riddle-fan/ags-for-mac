using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using AGS.Imaging;
using AGS.Types;

namespace AGS.Native
{
    /// <summary>
    /// Draws GUIs exactly as the upstream editor's GUI preview (Common GUI classes, the game's fonts and
    /// sprites) through libagsnative. Process-wide like upstream's native editor state: <see cref="Init"/>
    /// prepares one game; call <see cref="UpdateFont"/> after font settings change.
    /// </summary>
    public static class NativeGuiRenderer
    {
        /// <summary>Sets up drawing for <paramref name="game"/> in <paramref name="gameDirectory"/> (fonts, sprites).</summary>
        public static unsafe void Init(Game game, string gameDirectory)
        {
            NativeLibraryInfo.EnsureCompatible();
            var palette = new NativeMethods.Rgba[256];
            for (int i = 0; i < 256 && i < game.Palette.Length; i++)
            {
                System.Drawing.Color c = game.Palette[i].Colour;
                palette[i] = new NativeMethods.Rgba { R = c.R, G = c.G, B = c.B, A = 255 };
            }
            fixed (NativeMethods.Rgba* pal = palette)
                NativeImage.Check(NativeMethods.GuiInit(gameDirectory, (int)game.Settings.ColorDepth * 8,
                    game.Settings.AntiAliasFonts ? 1 : 0, game.Settings.ColorDepth == GameColorDepth.Palette ? pal : null));
            foreach (Font font in game.Fonts)
                UpdateFont(font);
        }

        /// <summary>(Re)loads a font with its current settings (upstream GameFontUpdated).</summary>
        public static void UpdateFont(Font font)
        {
            var info = new NativeMethods.FontInfo
            {
                PointSize = font.PointSize,
                SizeMultiplier = font.SizeMultiplier,
                VerticalOffset = font.VerticalOffset,
                LineSpacing = font.LineSpacing,
                CharacterSpacing = font.CharacterSpacing,
                HeightDefinedBy = (int)font.HeightDefinedBy,
                AscenderFixup = font.TTFMetricsFixup == FontMetricsFixup.SetAscenderToHeight ? 1 : 0,
                OutlineStyle = font.OutlineStyle == FontOutlineStyle.Automatic ? 2 : font.OutlineStyle == FontOutlineStyle.UseOutlineFont ? 1 : 0,
                OutlineFont = font.OutlineFont,
                AutoOutlineStyle = (int)font.AutoOutlineStyle,
                AutoOutlineThickness = font.AutoOutlineThickness,
            };
            NativeImage.Check(NativeMethods.GuiSetFont(font.ID, in info));
            // as GameFontUpdated: report what was loaded
            var metrics = GetFontMetrics(font.ID);
            font.FamilyName = metrics.Name;
            font.Height = metrics.RealHeight;
        }

        /// <summary>
        /// NativeMethods.FindTTFSizeForHeight: the font size giving <paramref name="pixelHeight"/> for a TTF file in the
        /// game folder (e.g. "agsfnt3.ttf"). Needs <see cref="Init"/>.
        /// </summary>
        public static int FindTtfSizeForHeight(string fileName, int pixelHeight)
        {
            NativeImage.Check(NativeMethods.FontFindTtfSize(fileName, pixelHeight, out int size));
            return size;
        }

        /// <summary>
        /// NativeMethods.ImportSCIFont: converts a Sierra SCI font file into an AGS bitmap font (WFN) file.
        /// Throws <see cref="AGSEditorException"/> with upstream's message if the file isn't a valid SCI font.
        /// </summary>
        public static void ImportSciFont(string sciFile, string wfnFile)
        {
            if (NativeMethods.FontImportSci(sciFile, wfnFile) != NativeMethods.AGSN_OK)
                throw new AGSEditorException(NativeMethods.GetLastError());
        }

        /// <summary>Metrics of a loaded font: height, line spacing, highest character code, bitmap (WFN) or not, name.</summary>
        public static unsafe (int Height, int RealHeight, int LineSpacing, int TopmostChar, bool IsBitmapFont, string Name) GetFontMetrics(int font)
        {
            NativeImage.Check(NativeMethods.FontGetMetrics(font, out NativeMethods.FontMetrics m));
            string name = Marshal.PtrToStringUTF8((IntPtr)m.Name) ?? string.Empty;
            return (m.Height, m.RealHeight, m.LineSpacing, m.TopmostChar, m.IsBitmapFont != 0, name);
        }

        /// <summary>Text drawn with a game font (white, dark grey outline), wrapped at <paramref name="maxWidth"/> if &gt; 0.</summary>
        public static PixelImage DrawText(int font, string text, int maxWidth = 0, bool drawOutline = true)
        {
            IntPtr handle = NativeMethods.FontDrawText(font, text ?? string.Empty, maxWidth, drawOutline ? 1 : 0);
            if (handle == IntPtr.Zero)
                throw new AGSEditorException(NativeMethods.GetLastError());
            try
            {
                return NativeImage.ToPixelImage(handle, usePalette: false, fallbackPalette: null);
            }
            finally
            {
                NativeMethods.ImageFree(handle);
            }
        }

        /// <summary>
        /// Draws <paramref name="gui"/> at its size (upstream ConvertGUIToBinaryFormat + drawGUI);
        /// <paramref name="highlightControl"/> is the index of a control to highlight, or -1.
        /// </summary>
        public static unsafe PixelImage Draw(GUI gui, int highlightControl = -1)
        {
            var desc = new NativeMethods.GuiDesc { BgColor = gui.BackgroundColor, BgImage = gui.BackgroundImage };
            if (gui is NormalGUI normal)
            {
                desc.X = normal.Left; desc.Y = normal.Top; desc.Width = normal.Width; desc.Height = normal.Height;
                desc.Clickable = normal.Clickable ? 1 : 0; desc.Visible = normal.Visible ? 1 : 0;
                desc.PopupY = normal.PopupYPos; desc.PopupStyle = (int)normal.PopupStyle; desc.ZOrder = normal.ZOrder;
                desc.FgColor = normal.BorderColor; desc.Transparency = normal.Transparency;
            }
            else if (gui is TextWindowGUI textWindow)
            {
                desc.IsTextWindow = 1;
                desc.Width = textWindow.EditorWidth; desc.Height = textWindow.EditorHeight;
                desc.Padding = textWindow.Padding; desc.FgColor = textWindow.TextColor;
            }

            var texts = new List<IntPtr>();
            var controls = new NativeMethods.GuiControl[gui.Controls.Count];
            try
            {
                for (int i = 0; i < gui.Controls.Count; i++)
                    controls[i] = ToNative(gui.Controls[i], texts);
                IntPtr handle;
                fixed (NativeMethods.GuiControl* p = controls)
                    handle = NativeMethods.GuiDraw(in desc, p, controls.Length, highlightControl);
                if (handle == IntPtr.Zero)
                    throw new AGSEditorException(NativeMethods.GetLastError());
                try
                {
                    return NativeImage.ToPixelImage(handle, usePalette: false, fallbackPalette: null);
                }
                finally
                {
                    NativeMethods.ImageFree(handle);
                }
            }
            finally
            {
                foreach (IntPtr text in texts)
                    Marshal.FreeCoTaskMem(text);
            }
        }

        private static NativeMethods.GuiControl ToNative(GUIControl control, List<IntPtr> texts)
        {
            IntPtr Text(string s)
            {
                IntPtr p = Marshal.StringToCoTaskMemUTF8(s ?? string.Empty);
                texts.Add(p);
                return p;
            }
            var c = new NativeMethods.GuiControl
            {
                Id = control.ID, ZOrder = control.ZOrder, X = control.Left, Y = control.Top, Width = control.Width, Height = control.Height,
                SolidBackground = control.SolidBackground ? 1 : 0, ShowBorder = control.ShowBorder ? 1 : 0,
                BackColor = control.BackgroundColor, BorderColor = control.BorderColor, BorderWidth = control.BorderWidth,
                PaddingX = control.PaddingX, PaddingY = control.PaddingY,
            };
            switch (control)
            {
                case GUIButton b:
                    c.Type = NativeMethods.AGSN_GUI_BUTTON;
                    c.BorderShadeColor = b.BorderShadeColor; c.TextColor = b.TextColor; c.TextOutlineColor = b.TextOutlineColor;
                    c.MouseoverBackColor = b.MouseOverBackgroundColor; c.PushedBackColor = b.PushedBackgroundColor;
                    c.MouseoverBorderColor = b.MouseOverBorderColor; c.PushedBorderColor = b.PushedBorderColor;
                    c.MouseoverTextColor = b.MouseOverTextColor; c.PushedTextColor = b.PushedTextColor;
                    c.DynamicColors = b.ColorStyle == ButtonColorStyle.Dynamic || b.ColorStyle == ButtonColorStyle.DynamicFlat ? 1 : 0;
                    c.FlatStyle = b.ColorStyle == ButtonColorStyle.DynamicFlat ? 1 : 0;
                    c.Font = b.Font; c.Image = b.Image; c.MouseoverImage = b.MouseoverImage; c.PushedImage = b.PushedImage;
                    c.TextAlignment = (int)b.TextAlignment; c.WrapText = b.WrapText ? 1 : 0; c.ClipImage = b.ClipImage ? 1 : 0;
                    c.Text = Text(b.Text);
                    break;
                case GUILabel l:
                    c.Type = NativeMethods.AGSN_GUI_LABEL;
                    c.TextColor = l.TextColor; c.TextOutlineColor = l.TextOutlineColor; c.Font = l.Font;
                    c.TextAlignment = (int)l.TextAlignment; c.Text = Text(l.Text);
                    break;
                case GUITextBox t:
                    c.Type = NativeMethods.AGSN_GUI_TEXTBOX;
                    c.TextColor = t.TextColor; c.TextOutlineColor = t.TextOutlineColor; c.Font = t.Font; c.TextAlignment = (int)t.TextAlignment;
                    break;
                case GUIListBox lb:
                    c.Type = NativeMethods.AGSN_GUI_LISTBOX;
                    c.TextColor = lb.TextColor; c.TextOutlineColor = lb.TextOutlineColor; c.Font = lb.Font;
                    c.SelectedTextColor = lb.SelectedTextColor; c.SelectedBgColor = lb.SelectedBackgroundColor;
                    c.TextAlignment = (int)lb.TextAlignment; c.Translated = lb.Translated ? 1 : 0; c.ShowArrows = lb.ShowScrollArrows ? 1 : 0;
                    break;
                case GUISlider s:
                    c.Type = NativeMethods.AGSN_GUI_SLIDER;
                    c.MinValue = s.MinValue; c.MaxValue = s.MaxValue; c.Value = s.Value; c.HandleImage = s.HandleImage;
                    c.HandleOffset = s.HandleOffset; c.HandleColor = s.HandleColor; c.BorderShadeColor = s.BorderShadeColor; c.BgImage = s.BackgroundImage;
                    break;
                case GUIInventory inv:
                    c.Type = NativeMethods.AGSN_GUI_INVWINDOW;
                    c.CharacterId = inv.CharacterID; c.ItemWidth = inv.ItemWidth; c.ItemHeight = inv.ItemHeight;
                    break;
                case GUITextWindowEdge edge:
                    c.Type = NativeMethods.AGSN_GUI_TEXTWINDOWEDGE;
                    c.Image = edge.Image;
                    break;
                default:
                    throw new AGSEditorException("Unknown GUI control type: " + control.GetType().Name);
            }
            return c;
        }
    }
}
