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
// Scrolling uses the controller's hardware vertical scroll: the 20 text rows live in a
// ring in display memory, and a newline at the bottom just moves the start line
// (plus one blank row to draw), instead of redrawing the whole screen.
//
// Not thread-safe: lock around it if several threads use it (PicoCalcConsole does).
// NuGet package needed: PicoCalcNano.Display (the font is included).

using System;

namespace PicoCalcNano
{
    public class PicoCalcTextScreen
    {
        public const int Columns = PicoCalcDisplay.Width / PicoCalcFont.GlyphWidth;   // 40
        public const int Rows = PicoCalcDisplay.Height / PicoCalcFont.GlyphHeight;    // 20

        private readonly PicoCalcDisplay _display;
        private readonly char[] _cells = new char[Columns * Rows];    // wanted content, by LOGICAL row
        private readonly char[] _shown = new char[Columns * Rows];    // what the panel shows, by PHYSICAL row
        private int _cursorX;
        private int _cursorY;
        private int _top;                // physical row that holds logical row 0
        private int _shownTop;           // scroll start currently programmed in the controller
        private int _shownCursor = -1;   // PHYSICAL cell index where the cursor is drawn, -1 = none
        private readonly bool[] _need = new bool[Columns];
        private readonly char[] _runChars = new char[Columns];
        private bool _dirty = true;      // something changed since the last Flush()

        public int ForegroundColor = PicoCalcColors.White;
        public int BackgroundColor = PicoCalcColors.Black;
        public bool CursorVisible = true;

        /// <summary>Number of scrolls since creation (lets callers track text that moved up).</summary>
        public int ScrollCount;

        public int CursorX { get { return _cursorX; } }
        public int CursorY { get { return _cursorY; } }

        public PicoCalcTextScreen(PicoCalcDisplay display)
        {
            _display = display;
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = ' ';
                _shown[i] = '\0';   // forces a full draw on first Flush()
            }
        }

        /// <summary>Paint the whole panel with the background color in one go (fast: uniform data)
        /// and mark every cell as already drawn, so only non-blank cells get drawn later.</summary>
        public void ClearPanel()
        {
            _display.FillScreen(BackgroundColor);
            for (int i = 0; i < _shown.Length; i++)
            {
                _shown[i] = ' ';
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

        /// <summary>Clear the grid and home the cursor (drawn on next Flush).</summary>
        public void Clear()
        {
            _dirty = true;
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = ' ';
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

            _cells[_cursorY * Columns + _cursorX] = c;
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

            _cells[_cursorY * Columns + _cursorX] = ' ';
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
            int last = Columns * (Rows - 1);
            for (int i = 0; i < Columns; i++)
            {
                _cells[last + i] = ' ';
            }

            _top = (_top + 1) % Rows;
            ScrollCount++;
        }

        /// <summary>Redraw the cells that changed since the last call (and the cursor).</summary>
        public void Flush()
        {
            if (!_dirty)
            {
                return;
            }

            _dirty = false;

            int cursorLogical = CursorVisible ? _cursorY * Columns + _cursorX : -1;

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
                    if (_cells[lb + col] != _shown[pb + col])
                    {
                        diff++;
                    }

                    if (_shown[pb + col] != ' ')
                    {
                        shownLength = col + 1;
                    }
                }

                if (diff > 3 && shownLength > 0)
                {
                    _display.FillRect(0, prow * PicoCalcFont.GlyphHeight, shownLength * PicoCalcFont.GlyphWidth, PicoCalcFont.GlyphHeight, BackgroundColor);
                    for (int col = 0; col < shownLength; col++)
                    {
                        _shown[pb + col] = ' ';
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

            // Pass 3: draw what differs. Neighbouring cells of a line are sent together (one window,
            // one transfer: every SPI call has a fixed cost); the cursor cell is drawn on its own.
            for (int row = 0; row < Rows; row++)
            {
                int prow = (row + _top) % Rows;
                int lb = row * Columns;      // logical row start
                int pb = prow * Columns;     // physical row start

                for (int col = 0; col < Columns; col++)
                {
                    _need[col] = _cells[lb + col] != _shown[pb + col] || ((lb + col) == cursorLogical) != ((pb + col) == _shownCursor);
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
                        DrawCell(prow, c, _cells[lb + c], true);
                        _shown[pb + c] = _cells[lb + c];
                        c++;
                        continue;
                    }

                    // Run from c to 'end': extend over small gaps of unchanged cells (cheaper than a new transfer)
                    int end = c;
                    int scan = c + 1;
                    while (scan < Columns && scan - end <= 4)
                    {
                        if (lb + scan == cursorLogical)
                        {
                            break;
                        }

                        if (_need[scan])
                        {
                            end = scan;
                        }

                        scan++;
                    }

                    int count = end - c + 1;
                    for (int i = 0; i < count; i++)
                    {
                        _runChars[i] = _cells[lb + c + i];
                        _shown[pb + c + i] = _cells[lb + c + i];
                    }

                    _display.DrawGlyphs(c * PicoCalcFont.GlyphWidth, prow * PicoCalcFont.GlyphHeight, _runChars, 0, count, ForegroundColor, BackgroundColor);
                    c = end + 1;
                }
            }

            _shownCursor = cursorLogical < 0 ? -1 : ((_cursorY + _top) % Rows) * Columns + _cursorX;
        }

        private void DrawCell(int physicalRow, int col, char c, bool inverted)
        {
            int fg = inverted ? BackgroundColor : ForegroundColor;
            int bg = inverted ? ForegroundColor : BackgroundColor;

            _display.DrawGlyph(
                col * PicoCalcFont.GlyphWidth,
                physicalRow * PicoCalcFont.GlyphHeight,
                PicoCalcFont.Data,
                PicoCalcFont.GlyphOffset(c),
                fg,
                bg);
        }
    }
}
