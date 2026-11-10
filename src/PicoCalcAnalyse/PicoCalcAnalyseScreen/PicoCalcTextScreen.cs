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
// Needs: PicoCalcDisplay.cs, PicoCalcFont.cs (namespace PicoCalc).

using System;

namespace PicoCalc
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

            // Pass 3: draw the cells that differ
            for (int row = 0; row < Rows; row++)
            {
                int prow = (row + _top) % Rows;
                int lb = row * Columns;      // logical row start
                int pb = prow * Columns;     // physical row start

                for (int col = 0; col < Columns; col++)
                {
                    int li = lb + col;
                    int pi = pb + col;
                    bool isCursor = li == cursorLogical;
                    bool wasCursor = pi == _shownCursor;

                    if (_cells[li] != _shown[pi] || isCursor != wasCursor)
                    {
                        DrawCell(prow, col, _cells[li], isCursor);
                        _shown[pi] = _cells[li];
                    }
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
