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

// Console demo (package PicoCalcNano.Console): a small command interpreter. PicoCalcConsole creates and owns its
// own display and keyboard, so this demo does not use the Ui class. "bye" returns to the menu.

using System;
using PicoCalcNano;

namespace PicoCalcDemo
{
    public static class DemoConsole
    {
        public static void Run()
        {
            PicoCalcConsole console = new PicoCalcConsole();

            try
            {
                console.Start();
                console.WriteLine("PicoCalc console demo");
                console.WriteLine("Type help. 'bye' returns to the menu.");
                console.WriteLine();

                while (true)
                {
                    string line = console.ReadLine("> ");
                    if (line == null)
                    {
                        continue;   // Break or Ctrl+C
                    }

                    line = line.Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    if (line == "help")
                    {
                        console.WriteLine("help  clear  bye");
                        console.WriteLine("echo <text>");
                        console.WriteLine("calc <a> <op> <b>   (+ - * /)");
                        console.WriteLine("graph   (drawing test)");
                        console.WriteLine("Up / Down = history");
                    }
                    else if (line == "clear")
                    {
                        console.Clear();
                    }
                    else if (line.StartsWith("echo "))
                    {
                        console.WriteLine(line.Substring(5));
                    }
                    else if (line.StartsWith("calc "))
                    {
                        Calc(console, line.Substring(5));
                    }
                    else if (line == "graph")
                    {
                        Graph(console);
                    }
                    else if (line == "bye")
                    {
                        break;
                    }
                    else
                    {
                        console.WriteLine("Unknown command: " + line);
                    }
                }
            }
            finally
            {
                console.Dispose();
            }
        }

        private static void Calc(PicoCalcConsole console, string text)
        {
            string[] parts = text.Split(new char[] { ' ' });
            if (parts.Length != 3 || parts[1].Length != 1)
            {
                console.WriteLine("Usage: calc 2 + 3");
                return;
            }

            try
            {
                double a = double.Parse(parts[0]);
                double b = double.Parse(parts[2]);
                char op = parts[1][0];

                if (op == '+')
                {
                    console.WriteLine((a + b).ToString());
                }
                else if (op == '-')
                {
                    console.WriteLine((a - b).ToString());
                }
                else if (op == '*')
                {
                    console.WriteLine((a * b).ToString());
                }
                else if (op == '/')
                {
                    console.WriteLine(b == 0 ? "Division by zero" : (a / b).ToString());
                }
                else
                {
                    console.WriteLine("Unknown operator: " + op);
                }
            }
            catch
            {
                console.WriteLine("Usage: calc 2 + 3");
            }
        }

        // The console hands the screen to our drawing code, then takes it back
        private static void Graph(PicoCalcConsole console)
        {
            console.BeginGraphics();

            PicoCalcDisplay d = console.Display;
            d.FillScreen(PicoCalcColors.Black);
            d.FillRect(50, 50, 100, 60, PicoCalcColors.Red);
            d.FillRect(120, 90, 100, 60, PicoCalcColors.Blue);
            d.DrawRect(40, 40, 190, 120, PicoCalcColors.White);
            d.HLine(0, 200, 320, PicoCalcColors.Green);
            d.VLine(160, 0, 320, PicoCalcColors.Green);
            d.DrawText(10, 302, "Press a key", PicoCalcColors.White, PicoCalcColors.Black, 1);

            console.ClearKeys();    // forget the keys typed before (like the Enter that started the command)
            console.ReadKey();      // the picture stays until a key is pressed
            console.EndGraphics();
        }
    }
}
