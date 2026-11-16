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

// PicoCalc keyboard driver for .NET nanoFramework (RP2040 / RP2350)
//
// Hardware: the keyboard is scanned by an STM32 co-processor that exposes a
// small FIFO over I2C (second I2C controller of the RP2040/RP2350: SDA = GP6, SCL = GP7,
// slave address 0x1F).
// Reading register 0x09 returns 2 bytes: [state, code]. [0, 0] = FIFO empty.
//
//   state 1 = pressed (repeated while the key is held = auto-repeat)
//   state 2 = hold    (reported for held modifier keys)
//   state 3 = released
//
// Codes below were captured on a real PicoCalc (US QWERTY layout).
// The STM32 already applies Shift and Caps Lock to printable keys ('a' -> 'A',
// '1' -> '!'), but NOT Ctrl, which this driver handles itself.
//
// I2C bus id: the nanoFramework firmware numbering of the I2C buses changed on the Pico targets
// (older firmware: this keyboard is on bus 1; newer firmware: bus 2). The default constructor
// tries both and keeps the one where the keyboard answers (see BusId). To force a bus, use
// new PicoCalcKeyboard(busId, 0x1F).
//
// NuGet packages needed: nanoFramework.System.Device.I2c
//
// Usage:
//     var kb = new PicoCalcKeyboard();
//     kb.KeyDown += OnKeyDown;
//     kb.Start();
//     Thread.Sleep(Timeout.Infinite);

using System;
using System.Device.I2c;
using System.Threading;

namespace PicoCalcNano
{
    /// <summary>Raw key codes sent by the PicoCalc keyboard MCU.</summary>
    public static class PicoCalcKeys
    {
        // Control characters (ASCII)
        public const byte Backspace = 0x08;
        public const byte Tab = 0x09;
        public const byte Enter = 0x0A;

        // Navigation / editing
        public const byte Esc = 0xB1;
        public const byte Left = 0xB4;
        public const byte Up = 0xB5;
        public const byte Down = 0xB6;
        public const byte Right = 0xB7;
        public const byte Break = 0xD0;     // Shift + Esc
        public const byte Insert = 0xD1;    // Alt + I
        public const byte Home = 0xD2;      // Shift + Tab
        public const byte Delete = 0xD4;
        public const byte End = 0xD5;       // Shift + Del
        public const byte CapsLock = 0xC1;

        // Modifiers (tracked by the driver, never raised as KeyDown)
        public const byte Alt = 0xA1;
        public const byte ShiftLeft = 0xA2;
        public const byte ShiftRight = 0xA3;
        public const byte Ctrl = 0xA5;

        // Function keys. F6 = Shift+F1 ... F10 = Shift+F5.
        public const byte F1 = 0x81;
        public const byte F2 = 0x82;
        public const byte F3 = 0x83;
        public const byte F4 = 0x84;
        public const byte F5 = 0x85;
        public const byte F6 = 0x86;
        public const byte F7 = 0x87;
        public const byte F8 = 0x88;
        public const byte F9 = 0x89;
        public const byte F10 = 0x90;
    }

    public delegate void PicoCalcKeyEventHandler(object sender, PicoCalcKeyEventArgs e);

    public class PicoCalcKeyEventArgs : EventArgs
    {
        private readonly byte _code;
        private readonly char _char;
        private readonly bool _shift;
        private readonly bool _ctrl;
        private readonly bool _alt;
        private readonly bool _repeat;

        public PicoCalcKeyEventArgs(byte code, char ch, bool shift, bool ctrl, bool alt, bool repeat)
        {
            _code = code;
            _char = ch;
            _shift = shift;
            _ctrl = ctrl;
            _alt = alt;
            _repeat = repeat;
        }

        /// <summary>Raw code from the keyboard MCU (see PicoCalcKeys).</summary>
        public byte Code { get { return _code; } }

        /// <summary>Character produced by the key, or '\0' for non-character keys.
        /// With Ctrl held, letters give control codes (Ctrl+A = 0x01).</summary>
        public char Char { get { return _char; } }

        public bool HasChar { get { return _char != '\0'; } }
        public bool Shift { get { return _shift; } }
        public bool Ctrl { get { return _ctrl; } }
        public bool Alt { get { return _alt; } }

        /// <summary>True when the key is held and the event comes from auto-repeat.</summary>
        public bool IsRepeat { get { return _repeat; } }
    }

    public class PicoCalcKeyboard : IDisposable
    {
        private const byte RegFifo = 0x09;
        private const byte StatePressed = 1;
        private const byte StateHold = 2;
        private const byte StateReleased = 3;

        /// <summary>Pass this bus id to let the driver detect the bus (default constructor).</summary>
        public const int AutoDetect = -1;

        private readonly I2cDevice _i2c;
        private readonly int _busId;
        private readonly bool[] _isDown = new bool[256];

        private Thread _thread;
        private bool _running;

        private bool _shift;
        private bool _ctrl;
        private bool _alt;

        /// <summary>Raised from the polling thread when a key is pressed (and on auto-repeat).</summary>
        public event PicoCalcKeyEventHandler KeyDown;

        /// <summary>Raised from the polling thread when a key is released.</summary>
        public event PicoCalcKeyEventHandler KeyUp;

        /// <summary>Creates the driver and detects the I2C bus (tries 1 then 2) where the keyboard answers.</summary>
        /// <exception cref="InvalidOperationException">No keyboard answered on any bus.</exception>
        public PicoCalcKeyboard() : this(AutoDetect, 0x1F)
        {
        }

        /// <summary>Creates the driver on a given I2C bus id (or AutoDetect) and slave address.</summary>
        public PicoCalcKeyboard(int busId, int address)
        {
            if (busId == AutoDetect)
            {
                _i2c = Detect(address, out _busId);
            }
            else
            {
                _i2c = I2cDevice.Create(new I2cConnectionSettings(busId, address));
                _busId = busId;
            }
        }

        /// <summary>I2C bus id actually used (useful when detection is on).</summary>
        public int BusId { get { return _busId; } }

        public bool ShiftPressed { get { return _shift; } }
        public bool CtrlPressed { get { return _ctrl; } }
        public bool AltPressed { get { return _alt; } }

        // Tries the candidate bus ids one after the other; an id that does not exist on this firmware
        // may throw, and an id that points to another controller gets no answer from the keyboard.
        private static I2cDevice Detect(int address, out int busId)
        {
            for (int id = 1; id <= 2; id++)
            {
                I2cDevice device = null;

                try
                {
                    device = I2cDevice.Create(new I2cConnectionSettings(id, address));

                    // Sentinel values: a real answer overwrites them ([state, code], state is 0..3),
                    // a failed transfer leaves them untouched.
                    byte[] reg = new byte[] { RegFifo };
                    byte[] buf = new byte[] { 0xFF, 0xFF };
                    device.WriteRead(reg, buf);

                    if (buf[0] <= StateReleased)
                    {
                        busId = id;
                        return device;
                    }
                }
                catch
                {
                    // Invalid id on this firmware, or transfer error: try the next one
                }

                if (device != null)
                {
                    try
                    {
                        device.Dispose();
                    }
                    catch
                    {
                    }
                }
            }

            throw new InvalidOperationException("PicoCalc keyboard not found on I2C bus 1 or 2. Check the firmware version, or pass the bus id to the constructor.");
        }

        public void Start()
        {
            if (_running)
            {
                return;
            }

            _running = true;
            _thread = new Thread(PollLoop);
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;

            // Don't join if an event handler calls Stop() from the polling thread.
            if (_thread != null && Thread.CurrentThread != _thread)
            {
                _thread.Join();
            }

            _thread = null;
        }

        public void Dispose()
        {
            Stop();
            _i2c.Dispose();
        }

        private void PollLoop()
        {
            byte[] reg = new byte[] { RegFifo };
            byte[] buf = new byte[2];

            while (_running)
            {
                // Drain the FIFO (bounded, so Stop() stays responsive)
                for (int i = 0; i < 16 && _running; i++)
                {
                    // Clear first: on a failed transfer we must not re-process stale data.
                    buf[0] = 0;
                    buf[1] = 0;

                    try
                    {
                        _i2c.WriteRead(reg, buf);
                    }
                    catch
                    {
                        break;
                    }

                    if (buf[0] == 0 && buf[1] == 0)
                    {
                        break;
                    }

                    Process(buf[0], buf[1]);
                }

                Thread.Sleep(10);
            }
        }

        private void Process(byte state, byte code)
        {
            // Modifiers: update state only
            if (code == PicoCalcKeys.Alt)
            {
                _alt = state != StateReleased;
                return;
            }

            if (code == PicoCalcKeys.ShiftLeft || code == PicoCalcKeys.ShiftRight)
            {
                _shift = state != StateReleased;
                return;
            }

            if (code == PicoCalcKeys.Ctrl)
            {
                _ctrl = state != StateReleased;
                return;
            }

            if (state == StatePressed)
            {
                bool repeat = _isDown[code];
                _isDown[code] = true;
                Raise(KeyDown, code, repeat);
            }
            else if (state == StateReleased)
            {
                // The release may carry the other letter case if Shift was let go first.
                _isDown[code] = false;
                if (code >= 'a' && code <= 'z')
                {
                    _isDown[code - 32] = false;
                }
                else if (code >= 'A' && code <= 'Z')
                {
                    _isDown[code + 32] = false;
                }

                Raise(KeyUp, code, false);
            }
            // StateHold on a non-modifier key: nothing to do
        }

        private void Raise(PicoCalcKeyEventHandler handler, byte code, bool repeat)
        {
            if (handler == null)
            {
                return;
            }

            handler(this, new PicoCalcKeyEventArgs(code, ToChar(code), _shift, _ctrl, _alt, repeat));
        }

        private char ToChar(byte code)
        {
            // Special keys live at 0x80 and above; control characters below 0x20
            // are only valid for Backspace, Tab and Enter.
            if (code >= 0x80)
            {
                return '\0';
            }

            if (code < 0x20 && code != PicoCalcKeys.Backspace && code != PicoCalcKeys.Tab && code != PicoCalcKeys.Enter)
            {
                return '\0';
            }

            char ch = (char)code;

            if (_ctrl)
            {
                if (ch >= 'a' && ch <= 'z')
                {
                    ch = (char)(ch - 'a' + 1);
                }
                else if (ch >= 'A' && ch <= 'Z')
                {
                    ch = (char)(ch - 'A' + 1);
                }
            }

            return ch;
        }
    }
}
