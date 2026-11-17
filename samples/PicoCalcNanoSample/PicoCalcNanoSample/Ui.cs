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

// Ui: a display, a text screen and a keyboard opened together and released together, with a small key queue.
// Used by the menu and by the demos that print text. The console demo does not use it, because PicoCalcConsole
// creates and owns its own display and keyboard.

using System;
using System.Threading;
using PicoCalcNano;

namespace PicoCalcDemo
{
    public class Ui : IDisposable
    {
        private const int QueueSize = 32;

        private readonly object _lock = new object();
        private readonly PicoCalcKeyEventArgs[] _queue = new PicoCalcKeyEventArgs[QueueSize];
        private int _head;
        private int _count;

        public PicoCalcDisplay Display { get; private set; }
        public PicoCalcTextScreen Screen { get; private set; }
        public PicoCalcKeyboard Keyboard { get; private set; }

        /// <summary>When false (default), the auto-repeat of a held key is ignored.</summary>
        public bool AllowRepeat;

        public Ui()
        {
            Display = new PicoCalcDisplay(62500000);

            try
            {
                Display.Init();
                Screen = new PicoCalcTextScreen(Display);
                Screen.CursorVisible = false;
                Screen.ClearPanel();
                Screen.Flush();
                Keyboard = new PicoCalcKeyboard();
            }
            catch
            {
                // Do not leave the display pins open if something failed
                Display.Dispose();
                throw;
            }

            Keyboard.KeyDown += OnKeyDown;
            Keyboard.Start();
        }

        // Called on the keyboard thread
        private void OnKeyDown(object sender, PicoCalcKeyEventArgs e)
        {
            if (e.IsRepeat && !AllowRepeat)
            {
                return;
            }

            lock (_lock)
            {
                if (_count < QueueSize)
                {
                    _queue[(_head + _count) % QueueSize] = e;
                    _count++;
                }
            }
        }

        /// <summary>True if a key press is waiting.</summary>
        public bool KeyAvailable
        {
            get
            {
                lock (_lock)
                {
                    return _count > 0;
                }
            }
        }

        /// <summary>Waits for the next key press.</summary>
        public PicoCalcKeyEventArgs ReadKey()
        {
            while (true)
            {
                lock (_lock)
                {
                    if (_count > 0)
                    {
                        PicoCalcKeyEventArgs e = _queue[_head];
                        _head = (_head + 1) % QueueSize;
                        _count--;
                        return e;
                    }
                }

                Thread.Sleep(10);
            }
        }

        /// <summary>Forgets the key presses that are waiting.</summary>
        public void ClearKeys()
        {
            lock (_lock)
            {
                _head = 0;
                _count = 0;
            }
        }

        /// <summary>Writes text at the cursor and shows it.</summary>
        public void Print(string text)
        {
            Screen.Write(text);
            Screen.Flush();
        }

        /// <summary>Writes a line of text and shows it (the screen scrolls by itself).</summary>
        public void PrintLine(string text)
        {
            Screen.WriteLine(text);
            Screen.Flush();
        }

        /// <summary>Clears the text screen.</summary>
        public void Clear()
        {
            Screen.Clear();
            Screen.Flush();
        }

        /// <summary>Waits for a key. Returns false if it was Esc (the demo should end).</summary>
        public bool WaitKeyOrEsc()
        {
            return ReadKey().Code != PicoCalcKeys.Esc;
        }

        public void Dispose()
        {
            Keyboard.Dispose();
            Display.Dispose();
        }
    }
}
