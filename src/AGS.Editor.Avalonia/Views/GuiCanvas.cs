using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AGS.Editor.Avalonia.ViewModels;
using AGS.Types;
using DrawingColor = System.Drawing.Color;

namespace AGS.Editor.Avalonia.Views
{
    /// <summary>
    /// Draws a GUI like upstream's GUIEditor panel: background colour/image, controls with their images,
    /// colours and texts, a dashed frame around every control and the selection with its resize handle.
    /// The GUI itself comes from upstream's GUI code via libagsnative; without it, an approximation with
    /// the UI font is drawn.
    /// </summary>
    public sealed class GuiCanvas : Control
    {
        private GuiEditorDocument _doc;

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_doc != null) _doc.Changed -= OnChanged;
            _doc = DataContext as GuiEditorDocument;
            if (_doc != null) _doc.Changed += OnChanged;
            OnChanged();
        }

        private void OnChanged()
        {
            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize) =>
            _doc == null ? default : new Size(_doc.GuiWidth * _doc.Zoom, _doc.GuiHeight * _doc.Zoom);

        private static Color ToColor(DrawingColor c) => Color.FromArgb(255, c.R, c.G, c.B);

        public override void Render(DrawingContext context)
        {
            if (_doc == null) return;
            GUI gui = _doc.Gui;
            double z = _doc.Zoom;
            var bounds = new Rect(0, 0, _doc.GuiWidth * z, _doc.GuiHeight * z);
            using (context.PushClip(bounds))
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            {
                // Checkerboard shows transparency (colour 0 means "no background" in AGS).
                for (int y = 0; y * 8 < bounds.Height; y++)
                    for (int x = 0; x * 8 < bounds.Width; x++)
                        context.FillRectangle((x + y) % 2 == 0 ? Brushes.LightGray : Brushes.White, new Rect(x * 8, y * 8, 8, 8));
                Bitmap rendered = _doc.Rendered;
                if (rendered != null)
                {
                    // Upstream's own GUI drawing (real AGS fonts and sprites).
                    context.DrawImage(rendered, new Rect(0, 0, rendered.PixelSize.Width, rendered.PixelSize.Height), bounds);
                }
                else
                {
                    if (gui.BackgroundColor != 0)
                        context.FillRectangle(new SolidColorBrush(ToColor(_doc.AgsColor(gui.BackgroundColor))), bounds);
                    DrawSprite(context, gui.BackgroundImage, 0, 0, null);
                    if (gui is NormalGUI normal && normal.BorderColor != 0)
                        context.DrawRectangle(new Pen(new SolidColorBrush(ToColor(_doc.AgsColor(normal.BorderColor))), z), bounds.Deflate(z / 2));

                    foreach (GUIControl control in _doc.ControlsInZOrder)
                        DrawControl(context, control);
                }

                var frame = new Pen(new SolidColorBrush(Color.FromArgb(160, 128, 128, 128)), 1, DashStyle.Dash);
                foreach (GUIControl control in _doc.ControlsInZOrder)
                    context.DrawRectangle(frame, Rect(control));
                if (_doc.AddRectangle is System.Drawing.Rectangle add)
                {
                    var rect = new Rect(add.Left * z, add.Top * z, add.Width * z, add.Height * z);
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb(60, 30, 144, 255)), rect);
                    context.DrawRectangle(new Pen(Brushes.DodgerBlue, 1, DashStyle.Dash), rect);
                }
                foreach (GUIControl other in _doc.Selection)
                {
                    if (other != _doc.SelectedControl)
                        context.DrawRectangle(new Pen(other.Locked ? Brushes.Red : Brushes.SkyBlue, 2), Rect(other));
                }
                if (_doc.SelectedControl is GUIControl selected)
                {
                    Rect r = Rect(selected);
                    context.DrawRectangle(new Pen(selected.Locked ? Brushes.Red : Brushes.DodgerBlue, 2), r);
                    if (!selected.Locked)
                        context.FillRectangle(Brushes.DodgerBlue, new Rect(r.Right - 4, r.Bottom - 4, 8, 8));
                }
            }
        }

        private Rect Rect(GUIControl c) => new Rect(c.Left * _doc.Zoom, c.Top * _doc.Zoom, Math.Max(1, c.Width) * _doc.Zoom, Math.Max(1, c.Height) * _doc.Zoom);

        private void DrawSprite(DrawingContext context, int sprite, int left, int top, GUIControl stretchTo)
        {
            Bitmap bitmap = _doc.SpriteBitmap(sprite);
            if (bitmap == null) return;
            double z = _doc.Zoom;
            var size = bitmap.PixelSize;
            context.DrawImage(bitmap, new Rect(0, 0, size.Width, size.Height),
                stretchTo != null ? Rect(stretchTo) : new Rect(left * z, top * z, size.Width * z, size.Height * z));
        }

        private void DrawText(DrawingContext context, string text, int font, int color, Rect area, FrameAlignment alignment, bool wrap)
        {
            if (string.IsNullOrEmpty(text)) return;
            var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                Typeface.Default, _doc.FontSize(font) * _doc.Zoom, new SolidColorBrush(ToColor(_doc.AgsColor(color))))
            {
                MaxTextWidth = wrap ? Math.Max(1, area.Width) : double.PositiveInfinity,
                Trimming = TextTrimming.None,
            };
            string a = alignment.ToString();
            double x = a.EndsWith("Right") ? area.Right - formatted.Width : a.EndsWith("Center") ? area.X + (area.Width - formatted.Width) / 2 : area.X;
            double y = a.StartsWith("Bottom") ? area.Bottom - formatted.Height : a.StartsWith("Middle") ? area.Y + (area.Height - formatted.Height) / 2 : area.Y;
            using (context.PushClip(area))
                context.DrawText(formatted, new Point(x, y));
        }

        private void DrawControl(DrawingContext context, GUIControl control)
        {
            Rect r = Rect(control);
            double z = _doc.Zoom;
            switch (control)
            {
                case GUIButton button:
                    if (button.Image > 0)
                        DrawSprite(context, button.Image, control.Left, control.Top, button.ClipImage ? null : control);
                    else
                    {
                        context.FillRectangle(new SolidColorBrush(ToColor(_doc.AgsColor(button.BackgroundColor))), r);
                        context.DrawRectangle(new Pen(new SolidColorBrush(ToColor(_doc.AgsColor(button.BorderColor))), z), r.Deflate(z / 2));
                    }
                    DrawText(context, button.Text, button.Font, button.TextColor,
                        r.Deflate(new Thickness(button.TextPaddingHorizontal * z, button.TextPaddingVertical * z)), button.TextAlignment, button.WrapText);
                    break;
                case GUILabel label:
                    DrawText(context, label.Text, label.Font, label.TextColor, r, label.TextAlignment, wrap: true);
                    break;
                case GUITextBox textBox:
                    if (textBox.ShowBorder)
                        context.DrawRectangle(new Pen(new SolidColorBrush(ToColor(_doc.AgsColor(textBox.TextColor))), z), r.Deflate(z / 2));
                    DrawText(context, string.IsNullOrEmpty(textBox.Text) ? "" : textBox.Text, textBox.Font, textBox.TextColor, r.Deflate(2 * z), FrameAlignment.MiddleLeft, false);
                    break;
                case GUIListBox listBox:
                    if (listBox.ShowBorder)
                        context.DrawRectangle(new Pen(new SolidColorBrush(ToColor(_doc.AgsColor(listBox.TextColor))), z), r.Deflate(z / 2));
                    break;
                case GUISlider slider:
                    if (slider.BackgroundImage > 0)
                        DrawSprite(context, slider.BackgroundImage, 0, 0, control);
                    else
                        context.FillRectangle(new SolidColorBrush(ToColor(_doc.AgsColor(slider.BackgroundColor))), r);
                    break;
                case GUIInventory _:
                    context.DrawRectangle(new Pen(Brushes.Gray, 1, DashStyle.Dot), r);
                    break;
                case GUITextWindowEdge edge:
                    DrawSprite(context, edge.Image, control.Left, control.Top, null);
                    break;
            }
        }
    }
}
