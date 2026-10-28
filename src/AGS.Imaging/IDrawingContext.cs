using System.Drawing;

namespace AGS.Imaging
{
    /// <summary>
    /// A surface to draw on. Replaces System.Drawing.Graphics in editor interfaces; the Avalonia
    /// front end implements it over a Skia canvas.
    /// </summary>
    public interface IDrawingContext
    {
        void DrawImage(IImage image, Rectangle destination);
        void FillRectangle(Color color, Rectangle rectangle);
        void DrawRectangle(Color color, Rectangle rectangle, float lineWidth);
        void DrawLine(Color color, Point from, Point to, float lineWidth);
    }
}
