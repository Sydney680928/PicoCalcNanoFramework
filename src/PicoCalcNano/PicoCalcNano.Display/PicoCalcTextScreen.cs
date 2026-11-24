// Copyright 2026 Stéphane Sibué
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

// Text layer for the PicoCalc display (40 columns x 20 rows, 8x16 font).
//
// Characters are written into an in-memory grid; Flush() redraws only the cells
// that changed since the last flush.
//
// Colors: every cell has its own text color and background color, taken from a palette of 16 colors (see
// PicoCalcPalette). Foreground and Background (palette indexes 0 to 15) can be changed at any time: they apply to what
// is written AFTER the change, the text that is already on the screen keeps its colors. The RGB value of each entry
// can be read and changed with GetPaletteColor / SetPaletteColor.
//
// Scrolling uses the controller's hardware vertical scroll: the 20 text rows live in a
// ring in display memory, and a newline at the bottom just moves the start line
// (plus one blank row to draw), instead of redrawing the whole screen.
//
// Characters U+007F and U+0080 to U+009F can be redefined with PicoCalcSymbols; the cells that show one are
// redrawn at the next Flush() after a change.
//
// Not thread-safe: lock around it if several threads use it (PicoCalcConsole does).
// NuGet package needed: PicoCalcNano.Display (the font is included).

using System;

namespace PicoCalcNano
{
    /// <summary>
    /// The 16 entries of the palette of the text screen: same names and same numbers as the .NET ConsoleColor
    /// enumeration (default colors: those of the Windows console).
    /// </summary>
    public static class PicoCalcPalette
    {
        public const int Black = 0;
        public const int DarkBlue = 1;
        public const int DarkGreen = 2;
        public const int DarkCyan = 3;
        public const int DarkRed = 4;
        public const int DarkMagenta = 5;
        public const int DarkYellow = 6;
        public const int Gray = 7;
        public const int DarkGray = 8;
        public const int Blue = 9;
        public const int Green = 10;
        public const int Cyan = 11;
        public const int Red = 12;
        public const int Magenta = 13;
        public const int Yellow = 14;
        public const int White = 15;
    }

    public class PicoCalcTextScreen
    {
        public const int Columns = PicoCalcDisplay.Width / PicoCalcFont.GlyphWidth;   // 40
        public const int Rows = PicoCalcDisplay.Height / PicoCalcFont.GlyphHeight;    // 20

        /// <summary>Number of colors in the palette.</summary>
        public const int PaletteSize = 16;

        private readonly PicoCalcDisplay _display;
        private readonly char[] _cells = new char[Columns * Rows];    // wanted content, by LOGICAL row
        private readonly byte[] _attr = new byte[Columns * Rows];     // wanted colors, by LOGICAL row: (text << 4) | background
        private readonly char[] _shown = new char[Columns * Rows];    // what the panel shows, by PHYSICAL row
        private readonly byte[] _shownAttr = new byte[Columns * Rows];
        private int _cursorX;
        private int _cursorY;
        private int _top;                // physical row that holds logical row 0
        private int _shownTop;           // scroll start currently programmed in the controller
        private int _shownCursor = -1;   // PHYSICAL cell index where the cursor is drawn, -1 = none
        private readonly bool[] _need = new bool[Columns];
        private readonly char[] _runChars = new char[Columns];
        private bool _dirty = true;      // something changed since the last Flush()
        private byte[] _cursorGlyph;     // custom cursor shape, null = inverted block
        private int _symbolVersion;      // PicoCalcSymbols.Version at the last Flush()

        // Default palette: the 16 colors of the .NET ConsoleColor enumeration (same order, same names), with the values
        // of the default color scheme of the Windows console (Campbell); black is pure black.
        private readonly int[] _palette = new int[PaletteSize]
        {
            0x000000,   //  0 Black
            0x0037DA,   //  1 DarkBlue
            0x13A10E,   //  2 DarkGreen
            0x3A96DD,   //  3 DarkCyan
            0xC50F1F,   //  4 DarkRed
            0x881798,   //  5 DarkMagenta
            0xC19C00,   //  6 DarkYellow
            0xCCCCCC,   //  7 Gray
            0x767676,   //  8 DarkGray
            0x3B78FF,   //  9 Blue
            0x16C60C,   // 10 Green
            0x61D6D6,   // 11 Cyan
            0xE74856,   // 12 Red
            0xB4009E,   // 13 Magenta
            0xF9F1A5,   // 14 Yellow
            0xF2F2F2    // 15 White
        };

        private int _fg = 15;   // palette index of the current text color (white)
        private int _bg = 0;    // palette index of the current background color (black)

        public bool CursorVisible = true;

        /// <summary>
        /// Palette index (0 to 15, see PicoCalcPalette) of the text color of what is written from now on.
        /// Default 15 (white).
        /// </summary>
        public int Foreground
        {
            get { return _fg; }
            set
            {
                CheckIndex(value);
                _fg = value;
            }
        }

        /// <summary>
        /// Palette index (0 to 15, see PicoCalcPalette) of the background color of what is written from now on
        /// (and of what Clear() and ClearPanel() erase). Default 0 (black).
        /// </summary>
        public int Background
        {
            get { return _bg; }
            set
            {
                CheckIndex(value);
                _bg = value;
            }
        }

        /// <summary>Color (0xRRGGBB) of a palette entry, index 0 to 15.</summary>
        public int GetPaletteColor(int index)
        {
            CheckIndex(index);
            return _palette[index];
        }

        /// <summary>
        /// Changes a palette entry (index 0 to 15, color 0xRRGGBB). What is already on the screen in that color is
        /// redrawn with the new one at the next Flush().
        /// </summary>
        public void SetPaletteColor(int index, int rgb)
        {
            CheckIndex(index);

            if (_palette[index] == rgb)
            {
                return;
            }

            _palette[index] = rgb;

            for (int i = 0; i < _shown.Length; i++)
            {
                int attr = _shownAttr[i];
                if ((attr & 0x0F) == index || ((attr >> 4) == index && _shown[i] != ' '))
                {
                    _shown[i] = '\0';
                }
            }

            _dirty = true;
        }

        /// <summary>Index (0 to 15) of the palette entry nearest to a 0xRRGGBB color (the exact one if there is one); to
        /// turn an RGB color into something you can give to Foreground or Background.</summary>
        public int NearestColorIndex(int rgb)
        {
            int best = 0;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < PaletteSize; i++)
            {
                int color = _palette[i];

                if (color == (rgb & 0xFFFFFF))
                {
                    return i;
                }

                int dr = ((rgb >> 16) & 0xFF) - ((color >> 16) & 0xFF);
                int dg = ((rgb >> 8) & 0xFF) - ((color >> 8) & 0xFF);
                int db = (rgb & 0xFF) - (color & 0xFF);
                int distance = dr * dr + dg * dg + db * db;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        private static void CheckIndex(int index)
        {
            if (index < 0 || index >= PaletteSize)
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        // Colors of a cell that holds character c with the current colors. A blank cell has no text color of its
        // own (the text color is set equal to the background), so that a blank cell never needs a redraw because of
        // the text color.
        private byte AttrFor(char c)
        {
            return (byte)(c == ' ' ? (_bg << 4) | _bg : (_fg << 4) | _bg);
        }

        /// <summary>
        /// Shape of the cursor: 16 bytes (PicoCalcFont.GlyphHeight rows, 8 pixels per row, the most significant bit
        /// is the leftmost pixel), drawn in the text colors instead of the inverted block. null (default) = the
        /// usual inverted block. The cursor still blinks with CursorVisible.
        /// </summary>
        public byte[] CursorGlyph
        {
            get { return _cursorGlyph; }
            set
            {
                if (value != null && value.Length < PicoCalcFont.GlyphHeight)
                {
                    throw new ArgumentException();
                }

                _cursorGlyph = value;

                // Draw the cursor cell again with the new shape on the next Flush()
                if (_shownCursor >= 0)
                {
                    _shown[_shownCursor] = '\0';
                }

                _dirty = true;
            }
        }

        /// <summary>Number of scrolls since creation (lets callers track text that moved up).</summary>
        public int ScrollCount;

        public int CursorX { get { return _cursorX; } }
        public int CursorY { get { return _cursorY; } }

        public PicoCalcTextScreen(PicoCalcDisplay display)
        {
            _display = display;
            _symbolVersion = PicoCalcSymbols.Version;
            byte blank = AttrFor(' ');

            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = ' ';
                _attr[i] = blank;
                _shown[i] = '\0';   // forces a full draw on first Flush()
                _shownAttr[i] = blank;
            }
        }

        /// <summary>Paint the whole panel with the background color in one go (fast: uniform data)
        /// and mark every cell as already drawn, so only non-blank cells get drawn later.</summary>
        public void ClearPanel()
        {
            _display.FillScreen(_palette[_bg]);
            byte blank = AttrFor(' ');

            for (int i = 0; i < _shown.Length; i++)
            {
                _shown[i] = ' ';
                _shownAttr[i] = blank;
            }

            _shownCursor = -1;
            _dirty = true;
        }

        /// <summary>Call before drawing directly on the display (graphics): puts the scroll start
        /// back to 0 so that display coordinates match what is visible on the panel.</summary>
        public void PrepareDirectDrawing()
        {
            _display.SetScrollStart(0);
            _shownTop = 0;
        }

        /// <summary>Call after direct drawing: wipes the panel and redraws all the text on next Flush().</summary>
        public void RestoreAfterDirectDrawing()
        {
            _display.SetScrollStart(0);
            _shownTop = 0;
            ClearPanel();
        }

        /// <summary>Force the next Flush() to look at the grid again (e.g. after toggling CursorVisible).</summary>
        public void Invalidate()
        {
            _dirty = true;
        }

        /// <summary>Clear the grid (with the current background color) and home the cursor (drawn on next Flush).</summary>
        public void Clear()
        {
            _dirty = true;
            byte blank = AttrFor(' ');

            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = ' ';
                _attr[i] = blank;
            }

            _cursorX = 0;
            _cursorY = 0;
        }

        public void SetCursor(int x, int y)
        {
            _dirty = true;
            if (x < 0) x = 0;
            if (x >= Columns) x = Columns - 1;
            if (y < 0) y = 0;
            if (y >= Rows) y = Rows - 1;
            _cursorX = x;
            _cursorY = y;
        }

        /// <summary>Write one character at the cursor. Handles \n, \r, backspace (0x08) and tab.</summary>
        public void Write(char c)
        {
            _dirty = true;

            if (c == '\n')
            {
                NewLine();
                return;
            }

            if (c == '\r')
            {
                _cursorX = 0;
                return;
            }

            if (c == '\b')
            {
                Backspace();
                return;
            }

            if (c == '\t')
            {
                int next = (_cursorX / 4 + 1) * 4;
                while (_cursorX < next)
                {
                    Write(' ');
                }

                return;
            }

            if (c < ' ')
            {
                return;   // other control characters are ignored
            }

            int index = _cursorY * Columns + _cursorX;
            _cells[index] = c;
            _attr[index] = AttrFor(c);
            _cursorX++;
            if (_cursorX >= Columns)
            {
                NewLine();
            }
        }

        public void Write(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                Write(s[i]);
            }
        }

        public void WriteLine(string s)
        {
            Write(s);
            NewLine();
        }

        public void Backspace()
        {
            _dirty = true;

            if (_cursorX > 0)
            {
                _cursorX--;
            }
            else if (_cursorY > 0)
            {
                _cursorY--;
                _cursorX = Columns - 1;
            }
            else
            {
                return;
            }

            int index = _cursorY * Columns + _cursorX;
            _cells[index] = ' ';
            _attr[index] = AttrFor(' ');
        }

        public void NewLine()
        {
            _dirty = true;
            _cursorX = 0;
            _cursorY++;
            if (_cursorY >= Rows)
            {
                Scroll();
                _cursorY = Rows - 1;
            }
        }

        private void Scroll()
        {
            Array.Copy(_cells, Columns, _cells, 0, Columns * (Rows - 1));
            Array.Copy(_attr, Columns, _attr, 0, Columns * (Rows - 1));
            int last = Columns * (Rows - 1);
            byte blank = AttrFor(' ');

            for (int i = 0; i < Columns; i++)
            {
                _cells[last + i] = ' ';
                _attr[last + i] = blank;
            }

            _top = (_top + 1) % Rows;
            ScrollCount++;
        }

        /// <summary>Redraw the cells that changed since the last call (and the cursor).</summary>
        public void Flush()
        {
            // A user-defined symbol changed (PicoCalcSymbols): the cells that show one are drawn again
            int symbolVersion = PicoCalcSymbols.Version;
            if (symbolVersion != _symbolVersion)
            {
                _symbolVersion = symbolVersion;

                for (int i = 0; i < _shown.Length; i++)
                {
                    if (PicoCalcSymbols.IsSymbol(_shown[i]))
                    {
                        _shown[i] = '\0';
                        _dirty = true;
                    }
                }
            }

            if (!_dirty)
            {
                return;
            }

            _dirty = false;

            int cursorLogical = CursorVisible ? _cursorY * Columns + _cursorX : -1;
            byte blankAttr = AttrFor(' ');

            // Pass 1: rows that change a lot (new line after a scroll, recalled history...) get the
            // area of their old text wiped with ONE fast fill BEFORE scrolling and drawing, so
            // stale text never stays visible while the new text is drawn cell by cell.
            for (int row = 0; row < Rows; row++)
            {
                int prow = (row + _top) % Rows;
                int lb = row * Columns;
                int pb = prow * Columns;

                int diff = 0;
                int shownLength = 0;   // width (in cells) of the old text
                for (int col = 0; col < Columns; col++)
                {
                    if (_cells[lb + col] != _shown[pb + col] || _attr[lb + col] != _shownAttr[pb + col])
                    {
                        diff++;
                    }

                    if (_shown[pb + col] != ' ' || _shownAttr[pb + col] != blankAttr)
                    {
                        shownLength = col + 1;
                    }
                }

                if (diff > 3 && shownLength > 0)
                {
                    _display.FillRect(0, prow * PicoCalcFont.GlyphHeight, shownLength * PicoCalcFont.GlyphWidth, PicoCalcFont.GlyphHeight, _palette[_bg]);
                    for (int col = 0; col < shownLength; col++)
                    {
                        _shown[pb + col] = ' ';
                        _shownAttr[pb + col] = blankAttr;
                    }

                    if (_shownCursor >= pb && _shownCursor < pb + shownLength)
                    {
                        _shownCursor = -1;   // the fill erased the cursor too
                    }
                }
            }

            // Pass 2: scroll
            if (_top != _shownTop)
            {
                _display.SetScrollStart(_top * PicoCalcFont.GlyphHeight);
                _shownTop = _top;
            }

            // Pass 3: draw what differs. Neighbouring cells of a line that have the same colors are sent together
            // (one window, one transfer: every SPI call has a fixed cost); the cursor cell is drawn on its own.
            for (int row = 0; row < Rows; row++)
            {
                int prow = (row + _top) % Rows;
                int lb = row * Columns;      // logical row start
                int pb = prow * Columns;     // physical row start

                for (int col = 0; col < Columns; col++)
                {
                    _need[col] = _cells[lb + col] != _shown[pb + col] || _attr[lb + col] != _shownAttr[pb + col]
                        || ((lb + col) == cursorLogical) != ((pb + col) == _shownCursor);
                }

                int c = 0;
                while (c < Columns)
                {
                    if (!_need[c])
                    {
                        c++;
                        continue;
                    }

                    if (lb + c == cursorLogical)
                    {
                        DrawCell(prow, c, _cells[lb + c], _attr[lb + c], true);
                        _shown[pb + c] = _cells[lb + c];
                        _shownAttr[pb + c] = _attr[lb + c];
                        c++;
                        continue;
                    }

                    // Run from c to 'end': extend over small gaps of unchanged cells (cheaper than a new transfer),
                    // as long as the cells have the same background and the same text color (a blank cell has no
                    // text color: it fits in any run of its background).
                    int runBg = _attr[lb + c] & 0x0F;
                    int scanFg = _cells[lb + c] == ' ' ? -1 : _attr[lb + c] >> 4;
                    int endFg = scanFg;
                    int end = c;
                    int scan = c + 1;

                    while (scan < Columns && scan - end <= 4)
                    {
                        if (lb + scan == cursorLogical)
                        {
                            break;
                        }

                        int attr = _attr[lb + scan];
                        if ((attr & 0x0F) != runBg)
                        {
                            break;
                        }

                        if (_cells[lb + scan] != ' ')
                        {
                            int fg = attr >> 4;
                            if (scanFg < 0)
                            {
                                scanFg = fg;
                            }
                            else if (scanFg != fg)
                            {
                                break;
                            }
                        }

                        if (_need[scan])
                        {
                            end = scan;
                            endFg = scanFg;
                        }

                        scan++;
                    }

                    int count = end - c + 1;
                    for (int i = 0; i < count; i++)
                    {
                        _runChars[i] = _cells[lb + c + i];
                        _shown[pb + c + i] = _cells[lb + c + i];
                        _shownAttr[pb + c + i] = _attr[lb + c + i];
                    }

                    int runFg = endFg < 0 ? runBg : endFg;
                    _display.DrawGlyphs(c * PicoCalcFont.GlyphWidth, prow * PicoCalcFont.GlyphHeight, _runChars, 0, count, _palette[runFg], _palette[runBg]);
                    c = end + 1;
                }
            }

            _shownCursor = cursorLogical < 0 ? -1 : ((_cursorY + _top) % Rows) * Columns + _cursorX;
        }

        // Draws one cell. The inverted cell (the cursor) swaps its two colors; on a blank cell, which has no text
        // color of its own, the cursor takes the current text color.
        private void DrawCell(int physicalRow, int col, char c, byte attr, bool inverted)
        {
            int cellFg = c == ' ' ? _fg : attr >> 4;
            int cellBg = attr & 0x0F;

            if (inverted && _cursorGlyph != null)
            {
                // Custom cursor shape (for example an hourglass), in the normal colors
                _display.DrawGlyph(
                    col * PicoCalcFont.GlyphWidth,
                    physicalRow * PicoCalcFont.GlyphHeight,
                    _cursorGlyph,
                    0,
                    _palette[cellFg],
                    _palette[cellBg]);
                return;
            }

            int fg = inverted ? cellBg : cellFg;
            int bg = inverted ? cellFg : cellBg;

            _display.DrawGlyph(
                col * PicoCalcFont.GlyphWidth,
                physicalRow * PicoCalcFont.GlyphHeight,
                PicoCalcFont.Data,
                PicoCalcFont.GlyphOffset(c),
                _palette[fg],
                _palette[bg]);
        }
    }
}
