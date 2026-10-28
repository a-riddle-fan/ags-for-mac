using System;
using System.Collections.Generic;
using AGS.Imaging;

namespace AGS.Editor.Avalonia.Services
{
    /// <summary>Drawing primitives on 8-bit area masks (coordinates in mask pixels, clipped).</summary>
    public static class MaskPainter
    {
        public static void Plot(PixelImage mask, int x, int y, byte value)
        {
            if (x < 0 || y < 0 || x >= mask.Width || y >= mask.Height) return;
            mask.GetRow(y)[x] = value;
        }

        /// <summary>Bresenham line, as the upstream editor's freehand and line tools draw.</summary>
        public static void Line(PixelImage mask, int x0, int y0, int x1, int y1, byte value)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                Plot(mask, x0, y0, value);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        public static void Rectangle(PixelImage mask, int x0, int y0, int x1, int y1, byte value)
        {
            int left = Math.Max(0, Math.Min(x0, x1)), right = Math.Min(mask.Width - 1, Math.Max(x0, x1));
            int top = Math.Max(0, Math.Min(y0, y1)), bottom = Math.Min(mask.Height - 1, Math.Max(y0, y1));
            for (int y = top; y <= bottom; y++)
            {
                Span<byte> row = mask.GetRow(y);
                for (int x = left; x <= right; x++)
                    row[x] = value;
            }
        }

        /// <summary>4-connected flood fill of the area under (x, y).</summary>
        public static void Fill(PixelImage mask, int x, int y, byte value)
        {
            if (x < 0 || y < 0 || x >= mask.Width || y >= mask.Height) return;
            byte target = mask.GetRow(y)[x];
            if (target == value) return;
            var stack = new Stack<(int X, int Y)>();
            stack.Push((x, y));
            while (stack.Count > 0)
            {
                var (px, py) = stack.Pop();
                Span<byte> row = mask.GetRow(py);
                int left = px, right = px;
                while (left > 0 && row[left - 1] == target) left--;
                while (right < mask.Width - 1 && row[right + 1] == target) right++;
                for (int i = left; i <= right; i++)
                {
                    row[i] = value;
                    if (py > 0 && mask.GetRow(py - 1)[i] == target) stack.Push((i, py - 1));
                    if (py < mask.Height - 1 && mask.GetRow(py + 1)[i] == target) stack.Push((i, py + 1));
                }
            }
        }
    }
}
