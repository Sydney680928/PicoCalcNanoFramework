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

// Serial demo (nanoFramework.System.IO.Ports): echo between the PicoCalc and a PC over the USB-C port.
//
// The USB-C port of the PicoCalc goes through a USB-serial chip (CH340) to a UART of the Pico, which nanoFramework
// calls COM1. On the PC it shows up as an ordinary COM port: open it in a terminal (PuTTY, Tera Term...) with the
// same speed, 8 data bits, no parity, 1 stop bit, no flow control.
//
//   - what you type on the PC is shown on the PicoCalc screen and sent back (echo);
//   - what you type on the PicoCalc keyboard is shown on its screen and sent to the PC.
//
// COM2 is NOT used: it is the channel of the nanoFramework debugger (see docs/serial.md).

using System;
using System.IO.Ports;
using System.Threading;
using PicoCalcNano;

namespace PicoCalcDemo
{
    public static class DemoSerial
    {
        private const string PortName = "COM1";

        // Same speed as the one set in the terminal on the PC
        private const int BaudRate = 115200;

        public static void Run()
        {
            Ui ui = new Ui();
            SerialPort port = null;

            try
            {
                ui.PrintLine("Serial demo");
                ui.PrintLine("");

                try
                {
                    port = new SerialPort(PortName, BaudRate);
                    port.NewLine = "\r\n";
                    port.Open();
                }
                catch (Exception ex)
                {
                    ui.PrintLine("Cannot open " + PortName + ":");
                    ui.PrintLine(ex.Message);
                    ui.PrintLine("");
                    ui.PrintLine("Any key: back to the menu");
                    ui.ReadKey();
                    return;
                }

                ui.PrintLine(PortName + " " + BaudRate + " 8N1");
                ui.PrintLine("Open the USB-C port of the");
                ui.PrintLine("PicoCalc in a terminal on the PC.");
                ui.PrintLine("Type on either side. Esc: menu");
                ui.PrintLine("");

                byte[] one = new byte[1];
                byte[] buffer = new byte[64];

                while (true)
                {
                    bool changed = false;

                    // Keys typed on the PicoCalc: shown here and sent to the PC
                    while (ui.KeyAvailable)
                    {
                        PicoCalcKeyEventArgs e = ui.ReadKey();

                        if (e.Code == PicoCalcKeys.Esc)
                        {
                            return;
                        }

                        if (e.Code == PicoCalcKeys.Enter)
                        {
                            ui.Screen.NewLine();
                            Send(port, one, 0x0D);
                            Send(port, one, 0x0A);
                            changed = true;
                        }
                        else if (e.Code == PicoCalcKeys.Backspace)
                        {
                            ui.Screen.Backspace();
                            EraseOnPc(port, one);
                            changed = true;
                        }
                        else if (e.HasChar && e.Char >= ' ' && e.Char < 0x7F)
                        {
                            ui.Screen.Write(e.Char);
                            Send(port, one, (byte)e.Char);
                            changed = true;
                        }
                    }

                    // Bytes received from the PC: shown here and echoed back
                    int available = port.BytesToRead;
                    if (available > 0)
                    {
                        int count = port.Read(buffer, 0, available > buffer.Length ? buffer.Length : available);

                        for (int i = 0; i < count; i++)
                        {
                            byte b = buffer[i];

                            if (b == 0x0D)
                            {
                                ui.Screen.NewLine();
                                Send(port, one, 0x0D);
                                Send(port, one, 0x0A);
                            }
                            else if (b == 0x08 || b == 0x7F)
                            {
                                ui.Screen.Backspace();
                                EraseOnPc(port, one);
                            }
                            else if (b >= 0x20 && b < 0x7F)
                            {
                                ui.Screen.Write((char)b);
                                Send(port, one, b);
                            }
                            // Other bytes (LF, control codes) are ignored
                        }

                        changed = true;
                    }

                    if (changed)
                    {
                        ui.Screen.Flush();
                    }
                    else
                    {
                        Thread.Sleep(10);
                    }
                }
            }
            finally
            {
                if (port != null)
                {
                    try
                    {
                        port.Close();
                    }
                    catch
                    {
                    }

                    // One owner per resource: release the port so that the demo can be started again
                    port.Dispose();
                }

                ui.Dispose();
            }
        }

        private static void Send(SerialPort port, byte[] one, byte value)
        {
            one[0] = value;
            port.Write(one, 0, 1);
        }

        // Backspace, space, backspace: erases the last character in a terminal
        private static void EraseOnPc(SerialPort port, byte[] one)
        {
            Send(port, one, 0x08);
            Send(port, one, 0x20);
            Send(port, one, 0x08);
        }
    }
}
