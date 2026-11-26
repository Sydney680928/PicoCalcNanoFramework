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

// PicoCalcConsole - a standalone text console for the PicoCalc (.NET nanoFramework).
//
// Keyboard in, 40x20 text screen out, with line editing and history. It knows nothing about
// any particular application: use it for a REPL, a shell, a game menu, logs...
//
//     var console = new PicoCalcConsole();
//     console.Start();
//     console.WriteLine("Hello");
//     console.Write("> ");
//     string line = console.ReadLine();     // null if Break (Shift+Esc) or Ctrl+C was pressed
//
// The screen is refreshed by an internal thread; callers do not need to Flush or lock.
// Write/WriteLine can be called from any thread. ReadLine/ReadKey block the calling thread.
// CancelRead() can be called from another thread to end a ReadLine that is waiting (see its comment).
// Busy = true shows a blinking hourglass as the cursor (a program is running), Busy = false the usual block.
//
// Line editing in ReadLine: Left/Right, Home/End, Backspace, Delete, Up/Down = history,
// Esc = clear the line, Enter = validate.
//
// NuGet packages needed: PicoCalcNano.Keyboard and PicoCalcNano.Display (the latter brings the font).
//
// Colors: Foreground / Background are indexes (0 to 15, see PicoCalcPalette) in the palette of 16 colors; they can
// be changed at any time, before or after Start(). They apply to the text written afterwards (the text already on the
// screen keeps its colors). GetPaletteColor / SetPaletteColor read and change the RGB value of a palette entry.
// Clear() erases with the current background color. The line being typed in ReadLine uses the colors that are
// current while it is typed.

using System;
using System.Collections;
using System.Threading;

namespace PicoCalcNano
{
    public delegate void PicoCalcBreakHandler(object sender);

    public class PicoCalcConsole : IDisposable
    {
        private const int MaxLineLength = 200;
        private const int MaxHistory = 20;
        private const int MaxQueuedKeys = 64;

        private readonly PicoCalcDisplay _display;
        private readonly PicoCalcKeyboard _keyboard;
        private readonly object _lock = new object();          // protects the text screen
        private readonly Queue _keys = new Queue();            // key events waiting for ReadKey
        private readonly AutoResetEvent _keyArrived = new AutoResetEvent(false);
        private readonly ArrayList _history = new ArrayList();

        private PicoCalcTextScreen _screen;
        private Thread _refreshThread;
        private bool _running;
        private bool _blinkReset;
        private bool _graphics;   // true while the program draws directly on the display
        private bool _busy;       // true while the cursor is the hourglass

        // Hourglass cursor, 8x16 (a few blank rows at the top and the bottom)
        private static readonly byte[] HourglassGlyph = new byte[]
        {
            0x00,   // ........
            0x7E,   // .######.
            0x42,   // .#....#.
            0x42,   // .#....#.
            0x24,   // ..#..#..
            0x24,   // ..#..#..
            0x18,   // ...##...
            0x18,   // ...##...
            0x18,   // ...##...
            0x18,   // ...##...
            0x24,   // ..#..#..
            0x24,   // ..#..#..
            0x5A,   // .#.##.#.
            0x7E,   // .######.
            0x7E,   // .######.
            0x00    // ........
        };

        // A ReadLine in progress can be ended from another thread by CancelRead()
        private readonly object _readLock = new object();                          // protects _reading and _cancelRequested
        private readonly ManualResetEvent _readEnded = new ManualResetEvent(true); // set while no ReadLine is running
        private bool _reading;           // a ReadLine is in progress
        private bool _cancelRequested;   // CancelRead() was called during the current ReadLine
        private bool _readCancelled;     // the last ReadLine ended because of CancelRead()

        // Where the line being edited starts (screen coordinates, kept up to date when the screen scrolls)
        private int _inputX;
        private int _inputY;

        private int _foreground = PicoCalcPalette.White;
        private int _background = PicoCalcPalette.Black;

        /// <summary>
        /// Text color of what is written from now on: index in the palette (0 to 15, see PicoCalcPalette). Can be
        /// changed at any time, from any thread; the text already on the screen keeps its colors. Default White.
        /// </summary>
        public int Foreground
        {
            get { return _foreground; }
            set
            {
                lock (_lock)
                {
                    CheckPaletteIndex(value);
                    _foreground = value;

                    if (_screen != null)
                    {
                        _screen.Foreground = value;
                    }
                }
            }
        }

        /// <summary>
        /// Background color of what is written from now on (and of what Clear() erases): index in the palette
        /// (0 to 15, see PicoCalcPalette). Can be changed at any time, from any thread. Default Black.
        /// </summary>
        public int Background
        {
            get { return _background; }
            set
            {
                lock (_lock)
                {
                    CheckPaletteIndex(value);
                    _background = value;

                    if (_screen != null)
                    {
                        _screen.Background = value;
                    }
                }
            }
        }

        private static void CheckPaletteIndex(int index)
        {
            if (index < 0 || index >= PicoCalcTextScreen.PaletteSize)
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>Number of colors in the palette (16).</summary>
        public int PaletteSize { get { return PicoCalcTextScreen.PaletteSize; } }

        /// <summary>Color (0xRRGGBB) of a palette entry, index 0 to 15. Needs Start() to have been called.</summary>
        public int GetPaletteColor(int index)
        {
            lock (_lock)
            {
                return _screen.GetPaletteColor(index);
            }
        }

        /// <summary>Index (0 to 15) of the palette entry nearest to a 0xRRGGBB color. Needs Start() to have been called.</summary>
        public int NearestColorIndex(int rgb)
        {
            lock (_lock)
            {
                return _screen.NearestColorIndex(rgb);
            }
        }

        /// <summary>Changes the color (0xRRGGBB) of a palette entry, index 0 to 15. What is on the screen in that
        /// color is redrawn with the new one. Needs Start() to have been called.</summary>
        public void SetPaletteColor(int index, int rgb)
        {
            lock (_lock)
            {
                _screen.SetPaletteColor(index, rgb);
            }
        }

        /// <summary>Raised (on the keyboard thread) when Break (Shift+Esc) or Ctrl+C is pressed,
        /// whether or not ReadLine is waiting. Typical use: stop a running script.</summary>
        public event PicoCalcBreakHandler BreakPressed;

        /// <summary>The display, for direct drawing between BeginGraphics() and EndGraphics().</summary>
        public PicoCalcDisplay Display { get { return _display; } }

        /// <summary>The keyboard owned by the console, for the features that do not go through the console
        /// (for instance ReadBattery()). Do not call Start(), Stop() or Dispose() on it, and do not subscribe to
        /// KeyDown: the console depends on its own use of the keyboard.</summary>
        public PicoCalcKeyboard Keyboard { get { return _keyboard; } }

        public int Columns { get { return PicoCalcTextScreen.Columns; } }
        public int Rows { get { return PicoCalcTextScreen.Rows; } }

        /// <summary>True if a key event is waiting for ReadKey().</summary>
        public bool KeyAvailable
        {
            get
            {
                lock (_keys)
                {
                    return _keys.Count > 0;
                }
            }
        }

        public PicoCalcConsole() : this(62500000)
        {
        }

        /// <param name="spiFrequency">SPI clock; the firmware caps anything above 62.5 MHz.</param>
        public PicoCalcConsole(int spiFrequency)
        {
            _display = new PicoCalcDisplay(spiFrequency);
            _keyboard = new PicoCalcKeyboard();
        }

        /// <summary>Initialise the display, clear it, start the keyboard and the refresh thread.</summary>
        public void Start()
        {
            _display.Init();

            _screen = new PicoCalcTextScreen(_display);
            _screen.Foreground = _foreground;
            _screen.Background = _background;
            if (_busy)
            {
                _screen.CursorGlyph = HourglassGlyph;
            }

            _screen.ClearPanel();
            _screen.Flush();

            _keyboard.KeyDown += OnKeyDown;
            _keyboard.Start();

            _running = true;
            _refreshThread = new Thread(new ThreadStart(RefreshLoop));
            _refreshThread.Start();
        }

        public void Write(string text)
        {
            lock (_lock)
            {
                _screen.Write(text);
            }
        }

        public void Write(char c)
        {
            lock (_lock)
            {
                _screen.Write(c);
            }
        }

        public void WriteLine(string text)
        {
            lock (_lock)
            {
                _screen.WriteLine(text);
            }
        }

        public void WriteLine()
        {
            lock (_lock)
            {
                _screen.NewLine();
            }
        }

        /// <summary>Clear the screen and put the cursor at the top left.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _screen.Clear();
            }
        }

        public void SetCursor(int x, int y)
        {
            lock (_lock)
            {
                _screen.SetCursor(x, y);
            }
        }

        public bool CursorVisible
        {
            get { return _screen.CursorVisible; }
            set
            {
                lock (_lock)
                {
                    _screen.CursorVisible = value;
                    _screen.Invalidate();
                }
            }
        }

        /// <summary>
        /// Busy indicator. When true, the cursor is a blinking hourglass instead of the block: use it while a
        /// program is running. When false (default), the usual blinking block. Can be set from any thread, also
        /// before Start().
        /// </summary>
        public bool Busy
        {
            get { return _busy; }
            set
            {
                lock (_lock)
                {
                    _busy = value;

                    if (_screen != null)
                    {
                        _screen.CursorGlyph = value ? HourglassGlyph : null;
                        _blinkReset = true;   // the new cursor is shown at once, then blinks
                    }
                }
            }
        }

        /// <summary>Enter graphics mode: the text refresh is paused so that you can draw on Display
        /// (screen coordinates 0..319). Call EndGraphics() to get the text screen back as it was.</summary>
        public void BeginGraphics()
        {
            lock (_lock)
            {
                _graphics = true;
                _screen.PrepareDirectDrawing();
            }
        }

        /// <summary>Leave graphics mode: the panel is wiped and the text screen is redrawn.</summary>
        public void EndGraphics()
        {
            lock (_lock)
            {
                _screen.RestoreAfterDirectDrawing();
                _graphics = false;
            }
        }

        /// <summary>Forget the key presses that are waiting for ReadKey().</summary>
        public void ClearKeys()
        {
            lock (_keys)
            {
                _keys.Clear();
            }
        }

        /// <summary>Wait for the next key press (auto-repeat included). Modifier keys alone are not reported.</summary>
        public PicoCalcKeyEventArgs ReadKey()
        {
            while (true)
            {
                lock (_keys)
                {
                    if (_keys.Count > 0)
                    {
                        return (PicoCalcKeyEventArgs)_keys.Dequeue();
                    }
                }

                _keyArrived.WaitOne();
            }
        }

        // Like ReadKey(), but returns null as soon as CancelRead() has been called (used by ReadLine only).
        private PicoCalcKeyEventArgs ReadKeyOrCancel()
        {
            while (true)
            {
                lock (_readLock)
                {
                    if (_cancelRequested)
                    {
                        return null;
                    }
                }

                lock (_keys)
                {
                    if (_keys.Count > 0)
                    {
                        return (PicoCalcKeyEventArgs)_keys.Dequeue();
                    }
                }

                _keyArrived.WaitOne();
            }
        }

        public string ReadLine(string prompt)
        {
            Write(prompt);
            return ReadLine();
        }

        /// <summary>
        /// Read a line with editing. Returns the text when Enter is pressed,
        /// or null if Break / Ctrl+C is pressed (the line is abandoned),
        /// or null if another thread called CancelRead() (then ReadCancelled is true).
        /// </summary>
        public string ReadLine()
        {
            lock (_readLock)
            {
                _reading = true;
                _cancelRequested = false;
                _readCancelled = false;
                _readEnded.Reset();
            }

            try
            {
                return ReadLineCore();
            }
            finally
            {
                lock (_readLock)
                {
                    _reading = false;
                    _cancelRequested = false;
                }

                _readEnded.Set();
            }
        }

        /// <summary>True if the last ReadLine returned null because CancelRead() was called
        /// (false if it ended with Enter or Break).</summary>
        public bool ReadCancelled
        {
            get { return _readCancelled; }
        }

        /// <summary>
        /// Ends the ReadLine that is waiting, from ANOTHER thread (not from the thread that is in ReadLine).
        /// ReadLine returns null and ReadCancelled becomes true; the text typed so far stays on the screen and the
        /// cursor goes to the next line, so that the output of a program starts on a clean line. The line is not
        /// added to the history. Call ReadLine again to get the input back.
        /// Does nothing if no ReadLine is waiting. Waits (up to one second) until ReadLine has really ended, so the
        /// caller can write to the console right after. Returns false only if that wait timed out.
        /// Note: if the user validates the line (Enter) at the very moment of the call, ReadLine returns that line
        /// normally and ReadCancelled stays false.
        /// </summary>
        public bool CancelRead()
        {
            return CancelRead(1000);
        }

        /// <summary>Same as CancelRead(), with a time limit in milliseconds for the wait.</summary>
        public bool CancelRead(int timeoutMs)
        {
            lock (_readLock)
            {
                if (!_reading)
                {
                    return true;
                }

                _cancelRequested = true;
            }

            _keyArrived.Set();   // wakes up ReadLine if it is waiting for a key
            return _readEnded.WaitOne(timeoutMs, false);
        }

        private string ReadLineCore()
        {
            string text = "";
            int index = 0;
            int historyPos = _history.Count;
            string saved = "";

            lock (_lock)
            {
                _inputX = _screen.CursorX;
                _inputY = _screen.CursorY;
            }

            while (true)
            {
                PicoCalcKeyEventArgs e = ReadKeyOrCancel();
                if (e == null)
                {
                    // CancelRead() was called: keep the typed text on the screen and go to the next line
                    lock (_lock)
                    {
                        PlaceCursor(text.Length);
                        _screen.NewLine();
                    }

                    _readCancelled = true;
                    return null;
                }

                string before = text;
                int code = e.Code;

                if (IsBreak(e))
                {
                    lock (_lock)
                    {
                        PlaceCursor(text.Length);
                        _screen.WriteLine("^C");
                    }

                    return null;
                }

                if (code == PicoCalcKeys.Enter)
                {
                    lock (_lock)
                    {
                        PlaceCursor(text.Length);
                        _screen.NewLine();
                    }

                    if (text.Length > 0 && (_history.Count == 0 || (string)_history[_history.Count - 1] != text))
                    {
                        _history.Add(text);
                        if (_history.Count > MaxHistory)
                        {
                            _history.RemoveAt(0);
                        }
                    }

                    return text;
                }
                else if (code == PicoCalcKeys.Left)
                {
                    if (index > 0) index--;
                }
                else if (code == PicoCalcKeys.Right)
                {
                    if (index < text.Length) index++;
                }
                else if (code == PicoCalcKeys.Home)
                {
                    index = 0;
                }
                else if (code == PicoCalcKeys.End)
                {
                    index = text.Length;
                }
                else if (code == PicoCalcKeys.Backspace)
                {
                    if (index > 0)
                    {
                        text = text.Substring(0, index - 1) + text.Substring(index);
                        index--;
                    }
                }
                else if (code == PicoCalcKeys.Delete)
                {
                    if (index < text.Length)
                    {
                        text = text.Substring(0, index) + text.Substring(index + 1);
                    }
                }
                else if (code == PicoCalcKeys.Esc)
                {
                    text = "";
                    index = 0;
                }
                else if (code == PicoCalcKeys.Up)
                {
                    if (historyPos > 0)
                    {
                        if (historyPos == _history.Count)
                        {
                            saved = text;
                        }

                        historyPos--;
                        text = (string)_history[historyPos];
                        index = text.Length;
                    }
                }
                else if (code == PicoCalcKeys.Down)
                {
                    if (historyPos < _history.Count)
                    {
                        historyPos++;
                        text = historyPos == _history.Count ? saved : (string)_history[historyPos];
                        index = text.Length;
                    }
                }
                else if (e.HasChar && e.Char >= ' ' && !e.Ctrl && !e.Alt)
                {
                    if (text.Length < MaxLineLength)
                    {
                        text = text.Substring(0, index) + e.Char.ToString() + text.Substring(index);
                        index++;
                    }
                }

                lock (_lock)
                {
                    RedrawInput(text, before.Length, index);
                }
            }
        }

        public void Dispose()
        {
            _running = false;
            _keyboard.Dispose();
            _display.Dispose();
        }

        private static bool IsBreak(PicoCalcKeyEventArgs e)
        {
            return e.Code == PicoCalcKeys.Break || (e.Ctrl && e.Char == (char)3);
        }

        // Rewrite the edited line from its start (only the cells that differ are really redrawn),
        // blank what is left of a longer previous version, then put the cursor at 'index'.
        // Must be called with _lock held.
        private void RedrawInput(string text, int previousLength, int index)
        {
            int scrollsBefore = _screen.ScrollCount;

            _screen.SetCursor(_inputX, _inputY);
            _screen.Write(text);
            for (int i = text.Length; i < previousLength; i++)
            {
                _screen.Write(' ');
            }

            // If writing scrolled the screen, the line start moved up with it
            _inputY -= _screen.ScrollCount - scrollsBefore;
            if (_inputY < 0)
            {
                _inputY = 0;
            }

            PlaceCursor(index);
        }

        // Put the screen cursor on the character 'index' of the edited line. Must be called with _lock held.
        private void PlaceCursor(int index)
        {
            int position = _inputX + index;
            _screen.SetCursor(position % Columns, _inputY + position / Columns);
            _blinkReset = true;
        }

        private void OnKeyDown(object sender, PicoCalcKeyEventArgs e)
        {
            if (IsBreak(e))
            {
                PicoCalcBreakHandler handler = BreakPressed;
                if (handler != null)
                {
                    handler(this);
                }
            }

            lock (_keys)
            {
                if (_keys.Count < MaxQueuedKeys)
                {
                    _keys.Enqueue(e);
                }
            }

            _keyArrived.Set();
        }

        private void RefreshLoop()
        {
            int tick = 0;

            while (_running)
            {
                lock (_lock)
                {
                    if (!_graphics)
                    {
                        if (_blinkReset)
                        {
                            _blinkReset = false;
                            tick = 0;
                            _screen.CursorVisible = true;
                            _screen.Invalidate();
                        }

                        tick++;
                        if (tick >= 25)   // about every 500 ms
                        {
                            tick = 0;
                            _screen.CursorVisible = !_screen.CursorVisible;
                            _screen.Invalidate();
                        }

                        _screen.Flush();
                    }
                }

                Thread.Sleep(20);
            }
        }
    }
}
