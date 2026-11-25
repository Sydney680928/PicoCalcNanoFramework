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

// PicoCalcNano demo: a menu that starts the demo of one package.
//
//   F1  Keyboard    F2  Display    F3  Sound    F4  Console    F5  SD card    F6  Serial    F7  Symbols
//   (F6 is Shift + F1 and F7 is Shift + F2 on the PicoCalc keyboard)
//
// Each demo opens what it needs and releases it (Dispose) when you leave it with Esc: a pin or a bus can only be
// opened once, so the menu and the demos never keep a display or a keyboard open at the same time.
//
// NuGet packages: PicoCalcNano.Keyboard, PicoCalcNano.Display, PicoCalcNano.Console, PicoCalcNano.SdCard,
// PicoCalcNano.Sound. Needs a nanoFramework firmware CLR 1.18.0.96 or later (for the sound).
// The serial demo also needs the nanoFramework.System.IO.Ports NuGet package.

using System;
using System.Diagnostics;
using System.Threading;
using PicoCalcNano;

namespace PicoCalcDemo
{
    public class Program
    {
        public static void Main()
        {
            // Gives the debugger time to take over
            Thread.Sleep(3000);

            while (true)
            {
                byte choice = ShowMenu();

                try
                {
                    switch (choice)
                    {
                        case PicoCalcKeys.F1:
                            DemoKeyboard.Run();
                            break;
                        case PicoCalcKeys.F2:
                            DemoDisplay.Run();
                            break;
                        case PicoCalcKeys.F3:
                            DemoSound.Run();
                            break;
                        case PicoCalcKeys.F4:
                            DemoConsole.Run();
                            break;
                        case PicoCalcKeys.F5:
                            DemoSd.Run();
                            break;
                        case PicoCalcKeys.F6:
                            DemoSerial.Run();
                            break;
                        case PicoCalcKeys.F7:
                            DemoSymbols.Run();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // The demo has released its resources (finally blocks): back to the menu
                    Debug.WriteLine("Demo failed: " + ex.Message);
                }
            }
        }

        // Shows the menu and returns the function key that was pressed (F1 to F7)
        private static byte ShowMenu()
        {
            Ui ui = new Ui();
            try
            {
                ui.PrintLine("");
                ui.PrintLine("  PicoCalcNano demo");
                ui.PrintLine("  -----------------");
                ui.PrintLine("");
                ui.PrintLine("  F1   Keyboard");
                ui.PrintLine("  F2   Display");
                ui.PrintLine("  F3   Sound");
                ui.PrintLine("  F4   Console");
                ui.PrintLine("  F5   SD card");
                ui.PrintLine("  F6   Serial (Shift + F1)");
                ui.PrintLine("  F7   Symbols (Shift + F2)");
                ui.PrintLine("");
                ui.PrintLine("  Esc leaves a demo and comes back here.");

                while (true)
                {
                    PicoCalcKeyEventArgs e = ui.ReadKey();
                    if (e.Code >= PicoCalcKeys.F1 && e.Code <= PicoCalcKeys.F7)
                    {
                        return e.Code;
                    }
                }
            }
            finally
            {
                ui.Dispose();
            }
        }
    }
}
