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

// "Digital rain" program for the PicoCalc: columns of characters that fall down the screen, with a white head and
// a green trail that fades out. Stand-alone nanoFramework application.
//
// NuGet packages: PicoCalcNano.Display and PicoCalcNano.Keyboard (with the nanoFramework packages they need).
//
// Keys: + and - change the number of falling columns, any other key stops the animation (the screen is cleared).
//
// How it goes fast: every call to the display costs a fixed time (window, command, transfer), so drawing glyph by
// glyph is slow. Here:
//   - at start, every character of CharSet is converted ONCE into display pixels, in the three colors (head, bright
//     green, dark green), and kept in memory (about 44 KB);
//   - when a column moves by one row, the WHOLE column (8 x 320 pixels) is rebuilt from these ready-made cells with
//     20 Array.Copy calls, and sent with ONE window and ONE transfer (PicoCalcDisplay.WriteRaw).
// The pixel format is the one of PicoCalcDisplay: 16 bits (RGB565, high byte first) or 24 bits (see Use16Bit).
// The text screen of the display driver has one color for the whole screen, so it is not used here.

using System;
using System.Diagnostics;
using System.Threading;
using PicoCalcNano;

namespace PicoCalcMatrix
{
    public class Program
    {
        private const int Columns = 40;                 // 320 / 8
        private const int Rows = 20;                    // 320 / 16
        private const int SpiClock = 62500000;          // try 25000000 or 40000000 to see if the speed changes
        private const int TickMs = 30;                  // duration of one animation step
        private const int StartMaxColumns = 20;         // columns falling at the same time (adjusted with + and -)
        private const int MinLength = 6;                // trail length in cells
        private const int MaxLength = 16;

        private const int HeadColor = 0xD8FFD8;         // almost white
        private const int BrightColor = 0x00FF46;       // classic matrix green
        private const int DimColor = 0x007A28;          // dark green

        private const int LevelHead = 0;
        private const int LevelBright = 1;
        private const int LevelDim = 2;

        // Characters that fall. Each one costs 3 ready-made cells (3 x 256 bytes in 16-bit format).
        private const string CharSet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ:.=*+-<>|/\\~^$%&?!#@";

        private static readonly Random _random = new Random();

        // Written by the keyboard thread, read by the animation loop
        private static readonly object _keyLock = new object();
        private static int _densityChange;
        private static bool _exitRequested;

        // Measure (written to the debug output): time spent building the columns and sending them
        private static long _buildTicks;
        private static long _sendTicks;
        private static int _columnsDrawn;
        private static string _statsLine1 = "";
        private static string _statsLine2 = "";
        private static string _statsLine3 = "";

        public static void Main()
        {
            // Gives the debugger time to take over
            Thread.Sleep(3000);

            PicoCalcDisplay display = new PicoCalcDisplay(SpiClock);
            PicoCalcKeyboard keyboard = null;

            try
            {
                display.Init();

                // Nothing else is on the display bus: keep CS low all the time instead of two slow GPIO writes
                // around every transfer (a window takes about 6 transfers)
                display.HoldChipSelect = true;

                display.FillScreen(PicoCalcColors.Black);

                keyboard = new PicoCalcKeyboard();
                keyboard.KeyDown += OnKeyDown;
                keyboard.Start();

                _statsLine3 = Calibrate(display);
                Debug.WriteLine("Matrix: send time (1/100 ms) for 1, 4, 12, 20 cells: " + _statsLine3);

                Animate(display);

                // Shows the last measure on the screen (useful when no debugger is attached); a key ends the program
                display.FillScreen(PicoCalcColors.Black);
                display.DrawText(8, 8, "Matrix - last measure", PicoCalcColors.Green, PicoCalcColors.Black, 1);
                display.DrawText(8, 40, _statsLine1, PicoCalcColors.White, PicoCalcColors.Black, 1);
                display.DrawText(8, 64, _statsLine2, PicoCalcColors.White, PicoCalcColors.Black, 1);
                display.DrawText(8, 88, "1/4/12/20 cells:" , PicoCalcColors.White, PicoCalcColors.Black, 1);
                display.DrawText(8, 112, _statsLine3, PicoCalcColors.White, PicoCalcColors.Black, 1);
                display.DrawText(8, 152, "Press a key to quit", PicoCalcColors.Green, PicoCalcColors.Black, 1);

                lock (_keyLock)
                {
                    _exitRequested = false;
                }

                while (true)
                {
                    Thread.Sleep(50);

                    lock (_keyLock)
                    {
                        if (_exitRequested)
                        {
                            break;
                        }
                    }
                }

                display.FillScreen(PicoCalcColors.Black);
            }
            finally
            {
                display.HoldChipSelect = false;

                if (keyboard != null)
                {
                    keyboard.Stop();
                    keyboard.Dispose();
                }

                display.Dispose();
            }
        }

        private static void OnKeyDown(object sender, PicoCalcKeyEventArgs e)
        {
            lock (_keyLock)
            {
                if (e.HasChar && e.Char == '+')
                {
                    _densityChange++;
                }
                else if (e.HasChar && e.Char == '-')
                {
                    _densityChange--;
                }
                else if (!e.IsRepeat)
                {
                    _exitRequested = true;
                }
            }
        }

        private static void Animate(PicoCalcDisplay display)
        {
            int bpp = display.Use16Bit ? 2 : 3;
            int cellBytes = PicoCalcFont.GlyphWidth * PicoCalcFont.GlyphHeight * bpp;
            int variants = CharSet.Length;

            byte[][] glyphs = BuildGlyphs(bpp, cellBytes, variants);   // index: level * variants + variant
            byte[] blank = new byte[cellBytes];                        // black is all zero in both formats
            byte[] strip = new byte[Rows * cellBytes];                 // one whole column, sent in one go
            byte[] variant = new byte[Columns * Rows];                 // which character is shown in each cell

            int[] head = new int[Columns];                // row of the head, -1 = column not falling
            int[] length = new int[Columns];
            int[] period = new int[Columns];              // the column moves every 'period' ticks
            int[] wait = new int[Columns];                // ticks before an idle column starts

            for (int c = 0; c < Columns; c++)
            {
                head[c] = -1;
                wait[c] = _random.Next(30);
            }

            int maxColumns = StartMaxColumns;
            int falling = 0;
            int tick = 0;
            bool exit = false;

            // Measure: average time of a tick, written to the debug output
            long busyTicks = 0;
            int measured = 0;

            while (!exit)
            {
                long tickStart = DateTime.UtcNow.Ticks;

                for (int c = 0; c < Columns; c++)
                {
                    if (head[c] < 0)
                    {
                        // Idle column: it starts after a random wait, if there is room
                        if (wait[c] > 0)
                        {
                            wait[c]--;
                        }
                        else if (falling < maxColumns)
                        {
                            head[c] = 0;
                            length[c] = MinLength + _random.Next(MaxLength - MinLength + 1);
                            period[c] = 1 + _random.Next(3);
                            falling++;
                            variant[c * Rows] = (byte)_random.Next(variants);
                            DrawColumn(display, c, 0, length[c], variant, glyphs, variants, blank, strip, cellBytes);
                        }

                        continue;
                    }

                    if (tick % period[c] != 0)
                    {
                        continue;
                    }

                    // The head goes one row down
                    int h = head[c] + 1;
                    int len = length[c];
                    head[c] = h;

                    if (h < Rows)
                    {
                        variant[c * Rows + h] = (byte)_random.Next(variants);
                    }

                    // One cell of the trail changes its character (a small flicker, free: the column is redrawn anyway)
                    int flicker = h - 1 - _random.Next(len - 1);
                    if (flicker >= 0 && flicker < Rows)
                    {
                        variant[c * Rows + flicker] = (byte)_random.Next(variants);
                    }

                    DrawColumn(display, c, h, len, variant, glyphs, variants, blank, strip, cellBytes);

                    if (h - len + 1 >= Rows)
                    {
                        // The whole trail has left the screen (the column has just been drawn empty)
                        head[c] = -1;
                        wait[c] = _random.Next(40);
                        falling--;
                    }
                }

                tick++;

                // Keys: + / - change the density, anything else leaves
                lock (_keyLock)
                {
                    maxColumns += _densityChange;
                    _densityChange = 0;
                    exit = _exitRequested;
                }

                if (maxColumns > Columns)
                {
                    maxColumns = Columns;
                }
                else if (maxColumns < 1)
                {
                    maxColumns = 1;
                }

                long spentTicks = DateTime.UtcNow.Ticks - tickStart;
                busyTicks += spentTicks;
                measured++;

                if (measured == 200)
                {
                    _statsLine1 = (busyTicks / measured / TimeSpan.TicksPerMillisecond) + " ms/tick (" + TickMs + " asked), " + falling + " col.";
                    Debug.WriteLine("Matrix: " + _statsLine1);

                    if (_columnsDrawn > 0)
                    {
                        // Tenths of a millisecond per column
                        _statsLine2 = "build " + (_buildTicks * 10 / _columnsDrawn / TimeSpan.TicksPerMillisecond) +
                            " send " + (_sendTicks * 10 / _columnsDrawn / TimeSpan.TicksPerMillisecond) +
                            " (1/10 ms), " + (_columnsDrawn / measured) + " col/tick";
                        Debug.WriteLine("  per column drawn: " + _statsLine2);
                    }

                    busyTicks = 0;
                    measured = 0;
                    _buildTicks = 0;
                    _sendTicks = 0;
                    _columnsDrawn = 0;
                }

                // Wait for the end of the tick (if drawing took longer, go straight on)
                int spent = (int)(spentTicks / TimeSpan.TicksPerMillisecond);
                if (spent < TickMs)
                {
                    Thread.Sleep(TickMs - spent);
                }
            }
        }

        // Measures WriteRaw for a window of 1, 4, 12 and 20 cells (black data, 8 pixels wide): the time of an
        // empty call and the time per byte can be read from the results. Returns "a b c d" in 1/100 ms.
        private static string Calibrate(PicoCalcDisplay display)
        {
            int cellBytes = PicoCalcFont.GlyphWidth * PicoCalcFont.GlyphHeight * (display.Use16Bit ? 2 : 3);
            int[] cells = new int[] { 1, 4, 12, 20 };
            string result = "";

            for (int i = 0; i < cells.Length; i++)
            {
                byte[] data = new byte[cells[i] * cellBytes];
                const int Repeats = 40;

                long start = DateTime.UtcNow.Ticks;
                for (int n = 0; n < Repeats; n++)
                {
                    display.WriteRaw(0, 0, PicoCalcFont.GlyphWidth, cells[i] * PicoCalcFont.GlyphHeight, data);
                }

                long elapsed = DateTime.UtcNow.Ticks - start;
                result += (i > 0 ? " " : "") + (elapsed * 100 / Repeats / TimeSpan.TicksPerMillisecond);
            }

            return result;
        }

        // Rebuilds the whole column c from the ready-made cells and sends it: the head is at row h and the trail
        // goes up from it over 'len' cells; everything else is black.
        private static void DrawColumn(PicoCalcDisplay display, int c, int h, int len, byte[] variant, byte[][] glyphs, int variants, byte[] blank, byte[] strip, int cellBytes)
        {
            int half = len / 2;
            long t0 = DateTime.UtcNow.Ticks;

            for (int r = 0; r < Rows; r++)
            {
                int d = h - r;
                byte[] source;

                if (d < 0 || d >= len)
                {
                    source = blank;
                }
                else
                {
                    int level = d == 0 ? LevelHead : (d < half ? LevelBright : LevelDim);
                    source = glyphs[level * variants + variant[c * Rows + r]];
                }

                Array.Copy(source, 0, strip, r * cellBytes, cellBytes);
            }

            long t1 = DateTime.UtcNow.Ticks;
            display.WriteRaw(c * PicoCalcFont.GlyphWidth, 0, PicoCalcFont.GlyphWidth, Rows * PicoCalcFont.GlyphHeight, strip);
            long t2 = DateTime.UtcNow.Ticks;

            _buildTicks += t1 - t0;
            _sendTicks += t2 - t1;
            _columnsDrawn++;
        }

        // Converts each character of CharSet into display pixels, in the three colors, on a black background.
        private static byte[][] BuildGlyphs(int bpp, int cellBytes, int variants)
        {
            int[] colors = new int[] { HeadColor, BrightColor, DimColor };
            byte[][] glyphs = new byte[3 * variants][];

            for (int level = 0; level < 3; level++)
            {
                byte r = (byte)((colors[level] >> 16) & 0xFF);
                byte g = (byte)((colors[level] >> 8) & 0xFF);
                byte b = (byte)(colors[level] & 0xFF);

                for (int v = 0; v < variants; v++)
                {
                    byte[] cell = new byte[cellBytes];
                    int offset = PicoCalcFont.GlyphOffset(CharSet[v]);
                    int p = 0;

                    for (int row = 0; row < PicoCalcFont.GlyphHeight; row++)
                    {
                        int bits = PicoCalcFont.Data[offset + row];

                        for (int col = 0; col < PicoCalcFont.GlyphWidth; col++)
                        {
                            if ((bits & (0x80 >> col)) != 0)
                            {
                                if (bpp == 2)
                                {
                                    // Same conversion as PicoCalcDisplay: RGB565, high byte first
                                    cell[p] = (byte)((r & 0xF8) | (g >> 5));
                                    cell[p + 1] = (byte)(((g & 0x1C) << 3) | (b >> 3));
                                }
                                else
                                {
                                    cell[p] = r;
                                    cell[p + 1] = g;
                                    cell[p + 2] = b;
                                }
                            }

                            p += bpp;
                        }
                    }

                    glyphs[level * variants + v] = cell;
                }
            }

            return glyphs;
        }
    }
}
