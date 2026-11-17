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

// Keyboard demo (package PicoCalcNano.Keyboard): shows every key press with its code, its character and the
// modifiers. The text is drawn with the display package, which is only used here as an output.

using PicoCalcNano;

namespace PicoCalcDemo
{
    public static class DemoKeyboard
    {
        public static void Run()
        {
            Ui ui = new Ui();
            ui.AllowRepeat = true;

            try
            {
                ui.PrintLine("Keyboard demo (I2C bus " + ui.Keyboard.BusId + ")");
                ui.PrintLine("Press keys. Shift+Esc leaves.");
                ui.PrintLine("");
                ui.PrintLine("code  char  modifiers");

                while (true)
                {
                    PicoCalcKeyEventArgs e = ui.ReadKey();
                    if (e.Code == PicoCalcKeys.Break)
                    {
                        break;
                    }

                    string ch = (e.HasChar && e.Char >= ' ') ? "'" + e.Char.ToString() + "'" : "---";

                    string mods = "";
                    if (e.Shift)
                    {
                        mods += "Shift ";
                    }

                    if (e.Ctrl)
                    {
                        mods += "Ctrl ";
                    }

                    if (e.Alt)
                    {
                        mods += "Alt ";
                    }

                    if (e.IsRepeat)
                    {
                        mods += "(repeat)";
                    }

                    ui.PrintLine("0x" + e.Code.ToString("X2") + "  " + ch + "  " + mods);
                }
            }
            finally
            {
                ui.Dispose();
            }
        }
    }
}
