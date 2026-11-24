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

// User-defined symbols demo (PicoCalcSymbols, package PicoCalcNano.Display): redefinable characters, written to the
// text screen like any other character. Press a key to redefine the heart while it is on the screen.

using System;
using PicoCalcNano;

namespace PicoCalcDemo
{
    public static class DemoSymbols
    {
        public static void Run()
        {
            Ui ui = new Ui();

            try
            {
                char heart = PicoCalcSymbols.First;                 // U+007F
                char smiley = (char)(PicoCalcSymbols.First + 1);    // U+0080
                char arrow = (char)(PicoCalcSymbols.First + 2);     // U+0081
                char battery = (char)(PicoCalcSymbols.First + 3);   // U+0082

                // 8 bytes: an 8x8 shape, each row is drawn twice (like SYMBOL on the Amstrad)
                PicoCalcSymbols.Define(heart, new byte[] { 0x66, 0xFF, 0xFF, 0xFF, 0x7E, 0x3C, 0x18, 0x00 });

                // Strings: '#' = pixel on
                PicoCalcSymbols.Define(smiley, new string[]
                {
                    "..####..",
                    ".#....#.",
                    "#.#..#.#",
                    "#......#",
                    "#.#..#.#",
                    "#..##..#",
                    ".#....#.",
                    "..####.."
                });

                PicoCalcSymbols.Define(arrow, new byte[] { 0x00, 0x08, 0x04, 0xFE, 0x04, 0x08, 0x00, 0x00 });

                // 16 bytes: the full 8x16 shape
                PicoCalcSymbols.Define(battery, new byte[]
                {
                    0x00, 0x18, 0x18, 0x7E, 0x42, 0x42, 0x7E, 0x7E,
                    0x7E, 0x7E, 0x7E, 0x7E, 0x7E, 0x7E, 0x00, 0x00
                });

                ui.PrintLine("Symbols demo");
                ui.PrintLine("");
                ui.PrintLine("I " + heart + " the PicoCalc " + smiley);
                ui.PrintLine("Next " + arrow + " " + arrow + " " + arrow);
                ui.PrintLine("Battery " + battery + battery + battery);
                ui.PrintLine("");
                ui.PrintLine(heart.ToString() + heart + heart + heart + heart + heart + heart + heart + heart + heart);
                ui.PrintLine("");
                ui.PrintLine("Any key: the heart changes");
                ui.PrintLine("Esc: back to the menu");

                if (ui.ReadKey().Code == PicoCalcKeys.Esc)
                {
                    return;
                }

                // The hearts that are already on the screen change at the next refresh
                PicoCalcSymbols.Define(heart, new byte[] { 0x66, 0x99, 0x81, 0x81, 0x42, 0x24, 0x18, 0x00 });
                ui.Screen.Flush();

                ui.PrintLine("");
                ui.PrintLine("Redefined: an empty heart.");

                ui.WaitKeyOrEsc();
            }
            finally
            {
                PicoCalcSymbols.ClearAll();
                ui.Dispose();
            }
        }
    }
}
