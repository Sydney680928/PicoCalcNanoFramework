// PicoCalcCanvas - an off-screen drawing surface in RAM (RGB565, 2 bytes per pixel).
//
// Draw into it (pixels, lines, circles, triangles, text, other canvases), then send it to the
// screen in ONE transfer with DrawTo(). This is much faster than drawing shape by shape on the
// display, where every call costs a few milliseconds.
//
// Memory: width x height x 2 bytes (e.g. 120x120 = 28 KB, 320x320 = 200 KB: too big for most Picos).
// Colors are 0xRRGGBB (see PicoCalcColors); the canvas keeps 5/6/5 bits.
// All drawing is clipped to the canvas.
//
//     var canvas = new PicoCalcCanvas(120, 120);
//     canvas.Clear(PicoCalcColors.Black);
//     canvas.DrawLine(0, 0, 119, 119, PicoCalcColors.Red);
//     canvas.FillCircle(60, 60, 30, PicoCalcColors.Yellow);
//     canvas.DrawTo(display, 100, 100);
//
// Needs: PicoCalcDisplay.cs, PicoCalcFont.cs (namespace PicoCalc).

using System;

namespace PicoCalc
{
    public class PicoCalcCanvas
    {
        private readonly int _width;
        private readonly int _height;
        private readonly byte[] _data;

        public int Width { get { return _width; } }
        public int Height { get { return _height; } }

        public PicoCalcCanvas(int width, int height)
        {
            if (width < 1 || height < 1)
            {
                throw new ArgumentOutOfRangeException();
            }

            _width = width;
            _height = height;
            _data = new byte[width * height * 2];
        }

        // ---- color conversion -------------------------------------------------------------

        private static byte High(int rgb)
        {
            return (byte)(((rgb >> 16) & 0xF8) | (((rgb >> 8) & 0xFF) >> 5));
        }

        private static byte Low(int rgb)
        {
            return (byte)((((rgb >> 8) & 0x1C) << 3) | ((rgb & 0xFF) >> 3));
        }

        // ---- pixels ---------------------------------------------------------------------

        /// <summary>Fill the whole canvas with one color.</summary>
        public void Clear(int rgb)
        {
            _data[0] = High(rgb);
            _data[1] = Low(rgb);
            DoubleFill(0, _data.Length);
        }

        public void SetPixel(int x, int y, int rgb)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height)
            {
                return;
            }

            int i = (y * _width + x) * 2;
            _data[i] = High(rgb);
            _data[i + 1] = Low(rgb);
        }

        /// <summary>Color of a pixel as 0xRRGGBB (reduced to 5/6/5 bits precision), or 0 outside the canvas.</summary>
        public int GetPixel(int x, int y)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height)
            {
                return 0;
            }

            int i = (y * _width + x) * 2;
            int hi = _data[i];
            int lo = _data[i + 1];
            int r = hi & 0xF8;
            int g = ((hi & 0x07) << 5) | ((lo >> 5) << 2);
            int b = (lo & 0x1F) << 3;
            return (r << 16) | (g << 8) | b;
        }

        // ---- lines and rectangles ---------------------------------------------------------

        public void HLine(int x, int y, int length, int rgb)
        {
            FillSpan(x, y, length, rgb);
        }

        public void VLine(int x, int y, int length, int rgb)
        {
            if (x < 0 || x >= _width)
            {
                return;
            }

            if (y < 0)
            {
                length += y;
                y = 0;
            }

            if (y + length > _height)
            {
                length = _height - y;
            }

            byte hi = High(rgb);
            byte lo = Low(rgb);
            int i = (y * _width + x) * 2;
            for (int k = 0; k < length; k++)
            {
                _data[i] = hi;
                _data[i + 1] = lo;
                i += _width * 2;
            }
        }

        /// <summary>Line between two points (Bresenham).</summary>
        public void DrawLine(int x0, int y0, int x1, int y1, int rgb)
        {
            int dx = x1 > x0 ? x1 - x0 : x0 - x1;
            int dy = y1 > y0 ? y1 - y0 : y0 - y1;
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                SetPixel(x0, y0, rgb);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        /// <summary>Rectangle outline, 1 pixel wide.</summary>
        public void DrawRect(int x, int y, int w, int h, int rgb)
        {
            if (w <= 0 || h <= 0)
            {
                return;
            }

            HLine(x, y, w, rgb);
            if (h > 1)
            {
                HLine(x, y + h - 1, w, rgb);
            }

            if (h > 2)
            {
                VLine(x, y + 1, h - 2, rgb);
                if (w > 1)
                {
                    VLine(x + w - 1, y + 1, h - 2, rgb);
                }
            }
        }

        public void FillRect(int x, int y, int w, int h, int rgb)
        {
            for (int row = 0; row < h; row++)
            {
                FillSpan(x, y + row, w, rgb);
            }
        }

        // ---- circles and triangles ---------------------------------------------------------

        /// <summary>Circle outline (midpoint algorithm).</summary>
        public void DrawCircle(int cx, int cy, int r, int rgb)
        {
            if (r < 0)
            {
                return;
            }

            int x = r;
            int y = 0;
            int d = 1 - r;

            while (x >= y)
            {
                SetPixel(cx + x, cy + y, rgb);
                SetPixel(cx - x, cy + y, rgb);
                SetPixel(cx + x, cy - y, rgb);
                SetPixel(cx - x, cy - y, rgb);
                SetPixel(cx + y, cy + x, rgb);
                SetPixel(cx - y, cy + x, rgb);
                SetPixel(cx + y, cy - x, rgb);
                SetPixel(cx - y, cy - x, rgb);

                y++;
                if (d < 0)
                {
                    d += 2 * y + 1;
                }
                else
                {
                    x--;
                    d += 2 * (y - x) + 1;
                }
            }
        }

        public void FillCircle(int cx, int cy, int r, int rgb)
        {
            if (r < 0)
            {
                return;
            }

            int x = r;
            int y = 0;
            int d = 1 - r;

            while (x >= y)
            {
                FillSpan(cx - x, cy + y, 2 * x + 1, rgb);
                FillSpan(cx - x, cy - y, 2 * x + 1, rgb);
                FillSpan(cx - y, cy + x, 2 * y + 1, rgb);
                FillSpan(cx - y, cy - x, 2 * y + 1, rgb);

                y++;
                if (d < 0)
                {
                    d += 2 * y + 1;
                }
                else
                {
                    x--;
                    d += 2 * (y - x) + 1;
                }
            }
        }

        public void DrawTriangle(int x0, int y0, int x1, int y1, int x2, int y2, int rgb)
        {
            DrawLine(x0, y0, x1, y1, rgb);
            DrawLine(x1, y1, x2, y2, rgb);
            DrawLine(x2, y2, x0, y0, rgb);
        }

        public void FillTriangle(int x0, int y0, int x1, int y1, int x2, int y2, int rgb)
        {
            int t;

            // sort the vertices by y (y0 <= y1 <= y2)
            if (y0 > y1) { t = y0; y0 = y1; y1 = t; t = x0; x0 = x1; x1 = t; }
            if (y1 > y2) { t = y1; y1 = y2; y2 = t; t = x1; x1 = x2; x2 = t; }
            if (y0 > y1) { t = y0; y0 = y1; y1 = t; t = x0; x0 = x1; x1 = t; }

            for (int y = y0; y <= y2; y++)
            {
                int xa = Interpolate(x0, y0, x2, y2, y);                       // long edge
                int xb = y < y1 ? Interpolate(x0, y0, x1, y1, y)               // upper short edge
                                : Interpolate(x1, y1, x2, y2, y);              // lower short edge

                if (xa > xb)
                {
                    t = xa; xa = xb; xb = t;
                }

                FillSpan(xa, y, xb - xa + 1, rgb);
            }
        }

        // ---- text -----------------------------------------------------------------------

        /// <summary>Draw text with a transparent background (only the letters are drawn).
        /// scale = 1, 2, 3... (each font pixel becomes scale x scale pixels).</summary>
        public void DrawText(int x, int y, string text, int rgb, int scale)
        {
            DrawTextCore(x, y, text, rgb, scale, false, 0);
        }

        /// <summary>Draw text over a solid background color.</summary>
        public void DrawText(int x, int y, string text, int rgb, int bgRgb, int scale)
        {
            DrawTextCore(x, y, text, rgb, scale, true, bgRgb);
        }

        private void DrawTextCore(int x, int y, string text, int fg, int scale, bool opaque, int bg)
        {
            if (scale < 1)
            {
                scale = 1;
            }

            for (int i = 0; i < text.Length; i++)
            {
                int offset = PicoCalcFont.GlyphOffset(text[i]);
                int gx = x + i * PicoCalcFont.GlyphWidth * scale;

                if (opaque)
                {
                    FillRect(gx, y, PicoCalcFont.GlyphWidth * scale, PicoCalcFont.GlyphHeight * scale, bg);
                }

                for (int row = 0; row < PicoCalcFont.GlyphHeight; row++)
                {
                    int bits = PicoCalcFont.Data[offset + row];
                    if (bits == 0)
                    {
                        continue;
                    }

                    for (int col = 0; col < PicoCalcFont.GlyphWidth; col++)
                    {
                        if ((bits & (0x80 >> col)) != 0)
                        {
                            if (scale == 1)
                            {
                                SetPixel(gx + col, y + row, fg);
                            }
                            else
                            {
                                FillRect(gx + col * scale, y + row * scale, scale, scale, fg);
                            }
                        }
                    }
                }
            }
        }

        // ---- canvases and screen -----------------------------------------------------------

        /// <summary>Copy another canvas onto this one at (x, y). Clipped.</summary>
        public void DrawCanvas(PicoCalcCanvas source, int x, int y)
        {
            for (int row = 0; row < source._height; row++)
            {
                int dy = y + row;
                if (dy < 0 || dy >= _height)
                {
                    continue;
                }

                int sx = 0;
                int dx = x;
                int count = source._width;

                if (dx < 0) { sx = -dx; count += dx; dx = 0; }
                if (dx + count > _width) { count = _width - dx; }
                if (count <= 0)
                {
                    continue;
                }

                Array.Copy(source._data, (row * source._width + sx) * 2, _data, (dy * _width + dx) * 2, count * 2);
            }
        }

        /// <summary>Copy another canvas onto this one; pixels equal to transparentRgb are left untouched
        /// (sprites with a transparent background). Slower than the opaque version.</summary>
        public void DrawCanvas(PicoCalcCanvas source, int x, int y, int transparentRgb)
        {
            byte thi = High(transparentRgb);
            byte tlo = Low(transparentRgb);

            for (int row = 0; row < source._height; row++)
            {
                int dy = y + row;
                if (dy < 0 || dy >= _height)
                {
                    continue;
                }

                for (int col = 0; col < source._width; col++)
                {
                    int dx = x + col;
                    if (dx < 0 || dx >= _width)
                    {
                        continue;
                    }

                    int si = (row * source._width + col) * 2;
                    if (source._data[si] == thi && source._data[si + 1] == tlo)
                    {
                        continue;
                    }

                    int di = (dy * _width + dx) * 2;
                    _data[di] = source._data[si];
                    _data[di + 1] = source._data[si + 1];
                }
            }
        }

        /// <summary>Send the whole canvas to the screen with its top-left corner at (x, y).
        /// The canvas must fit entirely on the screen.</summary>
        public void DrawTo(PicoCalcDisplay display, int x, int y)
        {
            if (x < 0 || y < 0 || x + _width > PicoCalcDisplay.Width || y + _height > PicoCalcDisplay.Height)
            {
                throw new ArgumentOutOfRangeException();
            }

            if (display.Use16Bit)
            {
                display.WriteRaw(x, y, _width, _height, _data);
                return;
            }

            // 18-bit display: expand each pixel to 3 bytes (R, G, B)
            byte[] converted = new byte[_width * _height * 3];
            int d = 0;
            for (int i = 0; i < _data.Length; i += 2)
            {
                int hi = _data[i];
                int lo = _data[i + 1];
                converted[d++] = (byte)(hi & 0xF8);
                converted[d++] = (byte)(((hi & 0x07) << 5) | ((lo >> 5) << 2));
                converted[d++] = (byte)((lo & 0x1F) << 3);
            }

            display.WriteRaw(x, y, _width, _height, converted);
        }

        // ---- helpers --------------------------------------------------------------------

        // Fill a horizontal run (clipped): write the first pixel, then double it with Array.Copy.
        private void FillSpan(int x, int y, int length, int rgb)
        {
            if (y < 0 || y >= _height || length <= 0)
            {
                return;
            }

            if (x < 0)
            {
                length += x;
                x = 0;
            }

            if (x + length > _width)
            {
                length = _width - x;
            }

            if (length <= 0)
            {
                return;
            }

            int start = (y * _width + x) * 2;
            _data[start] = High(rgb);
            _data[start + 1] = Low(rgb);
            DoubleFill(start, length * 2);
        }

        // _data[start..start+2) holds one pixel: replicate it over 'bytes' bytes.
        private void DoubleFill(int start, int bytes)
        {
            int filled = 2;
            while (filled < bytes)
            {
                int n = filled <= bytes - filled ? filled : bytes - filled;
                Array.Copy(_data, start, _data, start + filled, n);
                filled += n;
            }
        }

        private static int Interpolate(int xa, int ya, int xb, int yb, int y)
        {
            if (yb == ya)
            {
                return xa;
            }

            return xa + (xb - xa) * (y - ya) / (yb - ya);
        }
    }
}
