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

// PicoCalcSymbols - user-defined characters (like SYMBOL on the Amstrad CPC).
//
// 33 character codes have no picture in the font and can be redefined: U+007F and U+0080 to U+009F. Once a code is
// defined, it is drawn like any other character by everything that uses the font: the text screen
// (PicoCalcTextScreen, PicoCalcConsole), PicoCalcDisplay.DrawText and PicoCalcCanvas.DrawText.
//
//     // 8 bytes: one byte per row, bit 7 = leftmost pixel. Each row is drawn twice (8x8 shape stretched to 8x16).
//     PicoCalcSymbols.Define(PicoCalcSymbols.First, new byte[] { 0x66, 0xFF, 0xFF, 0xFF, 0x7E, 0x3C, 0x18, 0x00 });
//
//     // 16 bytes: the full 8x16 shape of the font
//     // Or 8 or 16 strings of 8 characters, '#' = pixel on (anything else = off):
//     PicoCalcSymbols.Define((char)0x80, new string[] { "..####..", ".#....#.", "#.#..#.#", "#......#",
//                                                       "#.#..#.#", "#..##..#", ".#....#.", "..####.." });
//
//     console.Write("I " + PicoCalcSymbols.First + " you");
//
// Redefining a code changes the characters that are already on the text screen at its next refresh.
// The definitions are lost when the program stops (they live in RAM, in the font table).
// Not thread-safe against drawing: define symbols before using them, or accept that a symbol may be seen half
// changed once.
//
// NuGet package needed: PicoCalcNano.Display (the font is included).

using System;

namespace PicoCalcNano
{
    public static class PicoCalcSymbols
    {
        /// <summary>First redefinable code (U+007F).</summary>
        public const char First = (char)0x7F;

        /// <summary>Last redefinable code (U+009F).</summary>
        public const char Last = (char)0x9F;

        /// <summary>Number of redefinable codes (33).</summary>
        public const int Count = 33;

        private static readonly object _lock = new object();
        private static int _version;

        /// <summary>Incremented each time a symbol changes; the text screen uses it to redraw the symbols it shows.</summary>
        public static int Version
        {
            get { return _version; }
        }

        /// <summary>True if the code can be redefined.</summary>
        public static bool IsSymbol(char code)
        {
            return code >= First && code <= Last;
        }

        /// <summary>
        /// Defines the shape of a symbol. 'rows' has 16 bytes (the 8x16 shape) or 8 bytes (an 8x8 shape, each row is
        /// drawn twice). One byte per row from the top, bit 7 is the leftmost pixel.
        /// </summary>
        public static void Define(char code, byte[] rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException();
            }

            if (rows.Length != 8 && rows.Length != PicoCalcFont.GlyphHeight)
            {
                throw new ArgumentException();
            }

            CheckCode(code);

            lock (_lock)
            {
                int offset = PicoCalcFont.GlyphOffset(code);

                if (rows.Length == PicoCalcFont.GlyphHeight)
                {
                    Array.Copy(rows, 0, PicoCalcFont.Data, offset, PicoCalcFont.GlyphHeight);
                }
                else
                {
                    for (int row = 0; row < 8; row++)
                    {
                        PicoCalcFont.Data[offset + row * 2] = rows[row];
                        PicoCalcFont.Data[offset + row * 2 + 1] = rows[row];
                    }
                }

                _version++;
            }
        }

        /// <summary>
        /// Defines a symbol from 8 or 16 strings of 8 characters: '#' is a pixel that is on, any other character is
        /// off. Easier to read than bytes.
        /// </summary>
        public static void Define(char code, string[] pattern)
        {
            if (pattern == null)
            {
                throw new ArgumentNullException();
            }

            if (pattern.Length != 8 && pattern.Length != PicoCalcFont.GlyphHeight)
            {
                throw new ArgumentException();
            }

            byte[] rows = new byte[pattern.Length];

            for (int row = 0; row < pattern.Length; row++)
            {
                string line = pattern[row];
                int bits = 0;

                for (int col = 0; col < PicoCalcFont.GlyphWidth; col++)
                {
                    if (col < line.Length && line[col] == '#')
                    {
                        bits |= 0x80 >> col;
                    }
                }

                rows[row] = (byte)bits;
            }

            Define(code, rows);
        }

        /// <summary>Makes a symbol blank again (as it is when the program starts).</summary>
        public static void Clear(char code)
        {
            Define(code, new byte[PicoCalcFont.GlyphHeight]);
        }

        /// <summary>Makes all the symbols blank again.</summary>
        public static void ClearAll()
        {
            lock (_lock)
            {
                for (char code = First; code <= Last; code++)
                {
                    int offset = PicoCalcFont.GlyphOffset(code);

                    for (int row = 0; row < PicoCalcFont.GlyphHeight; row++)
                    {
                        PicoCalcFont.Data[offset + row] = 0;
                    }
                }

                _version++;
            }
        }

        private static void CheckCode(char code)
        {
            if (!IsSymbol(code))
            {
                throw new ArgumentOutOfRangeException();
            }
        }
    }
}
