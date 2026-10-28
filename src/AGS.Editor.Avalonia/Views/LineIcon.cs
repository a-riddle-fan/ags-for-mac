using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace AGS.Editor.Avalonia.Views
{
    /// <summary>
    /// A thin line icon in the manner of the macOS system symbols: <see cref="Data"/> is drawn on a 24×24 grid,
    /// scaled to the control's size, stroked (and with <see cref="Filled"/> also filled) in the inherited text colour.
    /// </summary>
    public class LineIcon : Control
    {
        public static readonly StyledProperty<Geometry> DataProperty =
            AvaloniaProperty.Register<LineIcon, Geometry>(nameof(Data));

        public static readonly StyledProperty<bool> FilledProperty =
            AvaloniaProperty.Register<LineIcon, bool>(nameof(Filled));

        public static readonly StyledProperty<double> StrokeThicknessProperty =
            AvaloniaProperty.Register<LineIcon, double>(nameof(StrokeThickness), 1.7);

        public static readonly StyledProperty<IBrush> ForegroundProperty =
            TextElement.ForegroundProperty.AddOwner<LineIcon>();

        static LineIcon()
        {
            AffectsRender<LineIcon>(DataProperty, FilledProperty, StrokeThicknessProperty, ForegroundProperty);
        }

        public Geometry Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
        public bool Filled { get => GetValue(FilledProperty); set => SetValue(FilledProperty, value); }
        public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
        public IBrush Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

        protected override Size MeasureOverride(Size availableSize) => new Size(20, 20);

        public override void Render(DrawingContext context)
        {
            if (Data == null) return;
            double scale = System.Math.Min(Bounds.Width, Bounds.Height) / 24.0;
            if (scale <= 0) return;
            IBrush brush = Foreground ?? Brushes.Black;
            var offset = new Vector((Bounds.Width - 24 * scale) / 2, (Bounds.Height - 24 * scale) / 2);
            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset)))
            {
                var pen = new Pen(brush, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                context.DrawGeometry(Filled ? brush : null, pen, Data);
            }
        }
    }
}
