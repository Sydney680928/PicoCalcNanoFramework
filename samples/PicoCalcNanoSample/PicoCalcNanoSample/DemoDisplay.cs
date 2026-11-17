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

// Display demo (package PicoCalcNano.Display): colors and text sizes, an in-memory canvas, and the text screen
// with its hardware scrolling. Any key shows the next page, Esc goes back to the menu.

using System.Threading;
using PicoCalcNano;

namespace PicoCalcDemo
{
    public static class DemoDisplay
    {
        public static void Run()
        {
            Ui ui = new Ui();

            try
            {
                // Pages 1 and 2: direct drawing on the panel
                ui.Screen.PrepareDirectDrawing();

                PageColors(ui.Display);
                if (!ui.WaitKeyOrEsc())
                {
                    return;
                }

                PageCanvas(ui.Display);
                if (!ui.WaitKeyOrEsc())
                {
                    return;
                }

                // Page 3: the text screen, with hardware scrolling
                ui.Screen.RestoreAfterDirectDrawing();
                ui.Clear();
                ui.PrintLine("PicoCalcTextScreen: 40 x 20 text,");
                ui.PrintLine("hardware scrolling");
                ui.PrintLine("");

                for (int i = 1; i <= 40; i++)
                {
                    ui.PrintLine("Line " + i + " - the quick brown fox");
                    Thread.Sleep(60);
                }

                ui.PrintLine("");
                ui.PrintLine("Any key: back to the menu");
                ui.ReadKey();
            }
            finally
            {
                ui.Dispose();
            }
        }

        private static void PageColors(PicoCalcDisplay d)
        {
            d.FillScreen(PicoCalcColors.Black);
            d.DrawText(10, 10, "PicoCalcDisplay", PicoCalcColors.White, PicoCalcColors.Black, 2);
            d.DrawText(10, 48, "320 x 320 pixels, 8 x 16 font", PicoCalcColors.White, PicoCalcColors.Black, 1);

            int[] colors = new int[]
            {
                PicoCalcColors.White, PicoCalcColors.Red, PicoCalcColors.Green, PicoCalcColors.Blue,
                PicoCalcColors.Yellow, PicoCalcColors.Cyan, PicoCalcColors.Magenta, 0x808080
            };

            for (int i = 0; i < colors.Length; i++)
            {
                d.FillRect(i * 40, 80, 40, 60, colors[i]);
            }

            d.DrawText(10, 160, "Scale 1, accents: \u00e9\u00e8\u00e0\u00e7\u00fc", PicoCalcColors.White, PicoCalcColors.Black, 1);
            d.DrawText(10, 180, "Scale 2", PicoCalcColors.Yellow, PicoCalcColors.Black, 2);
            d.DrawText(10, 220, "Scale 3", PicoCalcColors.Cyan, PicoCalcColors.Black, 3);
            d.DrawText(10, 300, "Any key: next   Esc: back", PicoCalcColors.White, PicoCalcColors.Black, 1);
        }

        private static void PageCanvas(PicoCalcDisplay d)
        {
            // A 120 x 120 canvas is drawn in memory, then sent to the panel in one transfer
            PicoCalcCanvas c = new PicoCalcCanvas(120, 120);
            c.Clear(0x000040);
            c.DrawRect(0, 0, 120, 120, PicoCalcColors.White);
            c.DrawLine(0, 0, 119, 119, PicoCalcColors.Red);
            c.DrawLine(0, 119, 119, 0, PicoCalcColors.Green);
            c.FillCircle(60, 60, 30, PicoCalcColors.Yellow);
            c.DrawCircle(60, 60, 45, PicoCalcColors.Cyan);
            c.FillTriangle(10, 110, 30, 80, 50, 110, PicoCalcColors.Magenta);
            c.DrawText(66, 100, "Hi", PicoCalcColors.White, 1);

            d.FillScreen(PicoCalcColors.Black);
            d.DrawText(10, 10, "PicoCalcCanvas", PicoCalcColors.White, PicoCalcColors.Black, 2);
            d.DrawText(10, 48, "Drawn in memory, sent at once", PicoCalcColors.White, PicoCalcColors.Black, 1);
            c.DrawTo(d, 100, 100);
            d.DrawText(10, 300, "Any key: next   Esc: back", PicoCalcColors.White, PicoCalcColors.Black, 1);
        }
    }
}
