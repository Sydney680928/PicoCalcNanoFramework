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

// PicoCalc SD card driver (low level, SPI mode) for .NET nanoFramework
//
// Hardware (PicoCalc carrier board, SD slot on SPI0):
//   MISO = GP16, CS = GP17 (driven manually here), SCK = GP18, MOSI = GP19
//   nanoFramework SPI bus ids are 1-BASED: id 1 = SPI0 (the SD card), id 2 = SPI1 (the display).
//
// This class only knows how to talk to the card and read / write 512-byte blocks.
// File systems (FAT16 / FAT32) come on top of it, in a separate class.
//
// Supported cards:
//   - SD v1 "standard capacity" (up to 2 GB, byte addressing, usually FAT16)
//   - SD v2 standard capacity
//   - SDHC (up to 32 GB, block addressing, usually FAT32)
//   SDXC / exFAT cards are not supported.
//
// Protocol summary (SPI mode): at least 74 clocks with CS high, CMD0 (go idle),
// CMD8 (voltage check, only SD v2 answers), ACMD41 (initialisation loop),
// CMD58 (read OCR: bit 30 = SDHC), CMD9 (read CSD: capacity), CMD17 (read one block).
// The clock stays low during initialisation (see InitClocks), then switches to the data clock.
//
// NuGet packages needed: nanoFramework.System.Device.Spi, nanoFramework.System.Device.Gpio
//
// Usage:
//     var sd = new PicoCalcSdCard();
//     if (sd.Init())
//     {
//         byte[] block = new byte[512];
//         sd.ReadBlock(0, block);
//     }
//     else
//     {
//         Debug.WriteLine(sd.ErrorMessage);
//     }

using System;
using System.Device.Gpio;
using System.Device.Spi;
using System.Threading;

namespace PicoCalcNano
{
    public enum PicoCalcSdType
    {
        Unknown = 0,
        SdV1 = 1,   // standard capacity, SD spec 1.x, byte addressing
        SdV2 = 2,   // standard capacity, SD spec 2.0, byte addressing
        SdHc = 3    // high capacity, block addressing
    }

    public class PicoCalcSdCard : IDisposable
    {
        /// <summary>nanoFramework SPI bus id of the SD slot (SPI0 = 1).</summary>
        public static int SpiBusId = 1;

        public const int PinCs = 17;
        public const int BlockSize = 512;

        // Clocks tried during initialisation, in order. The SD spec asks for 400 kHz at most.
        // On this firmware, some requested frequencies (150, 200, 300, 400 kHz) make the card
        // answers be read shifted by one bit (CMD0 gives 0x00 instead of 0x01), while 100 kHz,
        // 250 kHz and everything from 500 kHz up read correctly.
        private static readonly int[] InitClocks = new int[] { 250000, 100000, 500000 };

        private readonly GpioController _gpio;
        private readonly GpioPin _cs;
        private readonly int _dataClock;
        private SpiDevice _spi;

        private readonly byte[] _tx1 = new byte[1];
        private readonly byte[] _rx1 = new byte[1];
        private readonly byte[] _ff = new byte[BlockSize];
        private readonly byte[] _cmd = new byte[6];
        private readonly byte[] _csd = new byte[16];

        private PicoCalcSdType _type = PicoCalcSdType.Unknown;
        private long _capacity;
        private string _error = "";

        /// <summary>Type of the card (valid after a successful Init).</summary>
        public PicoCalcSdType CardType { get { return _type; } }

        /// <summary>Capacity in bytes, read from the CSD register.</summary>
        public long CapacityBytes { get { return _capacity; } }

        /// <summary>Number of 512-byte blocks.</summary>
        public uint BlockCount { get { return (uint)(_capacity / BlockSize); } }

        /// <summary>Last error message set by Init or ReadBlock.</summary>
        public string ErrorMessage { get { return _error; } }

        public PicoCalcSdCard() : this(10000000)
        {
        }

        /// <param name="dataClockFrequency">SPI clock after initialisation (old cards: 25 MHz max).</param>
        public PicoCalcSdCard(int dataClockFrequency)
        {
            _dataClock = dataClockFrequency;

            for (int i = 0; i < _ff.Length; i++)
            {
                _ff[i] = 0xFF;
            }

            _gpio = new GpioController();
            _cs = _gpio.OpenPin(PinCs, PinMode.Output);
            _cs.Write(PinValue.High);
        }

        /// <summary>Initialises the card. Returns false (and sets ErrorMessage) on failure.</summary>
        public bool Init()
        {
            string all = "";
            for (int i = 0; i < InitClocks.Length; i++)
            {
                if (InitAt(InitClocks[i]))
                {
                    return true;
                }

                all += InitClocks[i] + " Hz : " + _error + " | ";
            }

            _error = all;
            return false;
        }

        private bool InitAt(int initClock)
        {
            _type = PicoCalcSdType.Unknown;
            _capacity = 0;
            _error = "";

            OpenSpi(initClock);
            _cs.Write(PinValue.High);

            // At least 74 clock pulses with CS high: 10 bytes = 80 clocks
            _spi.Write(new SpanByte(_ff, 0, 10));

            // CMD0: go idle, expected answer 0x01
            byte r = 0xFF;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                r = Command(0, 0, 0x95);
                if (r == 0x01)
                {
                    break;
                }

                Thread.Sleep(20);
            }

            if (r != 0x01)
            {
                _error = "CMD0: no answer from the card (R1 = " + Hex(r) + "). Card missing or not inserted properly?";
                return false;
            }

            // CMD8: only SD v2 cards answer; v1 cards answer "illegal command" (0x05)
            bool v2 = false;
            r = SendCommand(8, 0x000001AA, 0x87);
            if (r == 0x05)
            {
                v2 = false;
            }
            else if (r == 0x01)
            {
                byte b0 = Xfer(0xFF);
                byte b1 = Xfer(0xFF);
                byte b2 = Xfer(0xFF);
                byte b3 = Xfer(0xFF);
                if (b2 != 0x01 || b3 != 0xAA)
                {
                    Deselect();
                    _error = "CMD8: inconsistent answer (" + Hex(b0) + " " + Hex(b1) + " " + Hex(b2) + " " + Hex(b3) + ")";
                    return false;
                }

                v2 = true;
            }
            else
            {
                Deselect();
                _error = "CMD8: unexpected answer (R1 = " + Hex(r) + ")";
                return false;
            }

            Deselect();

            // ACMD41 loop until the card leaves the idle state (up to ~2.5 s)
            uint arg = v2 ? 0x40000000u : 0u;
            bool ready = false;
            byte r55 = 0xFF;
            for (int i = 0; i < 120; i++)
            {
                r55 = Command(55, 0, 0x01);
                r = Command(41, arg, 0x01);
                if (r == 0x00)
                {
                    ready = true;
                    break;
                }

                Thread.Sleep(10);
            }

            if (!ready)
            {
                _error = "ACMD41: the card does not leave the idle state (CMD55 R1 = " + Hex(r55) + ", ACMD41 R1 = " + Hex(r) + ")";
                return false;
            }

            if (v2)
            {
                // CMD58: OCR register, bit 30 (CCS) = high capacity
                r = SendCommand(58, 0, 0x01);
                if (r != 0x00)
                {
                    Deselect();
                    _error = "CMD58: error (R1 = " + Hex(r) + ")";
                    return false;
                }

                byte ocr0 = Xfer(0xFF);
                Xfer(0xFF);
                Xfer(0xFF);
                Xfer(0xFF);
                Deselect();
                _type = (ocr0 & 0x40) != 0 ? PicoCalcSdType.SdHc : PicoCalcSdType.SdV2;
            }
            else
            {
                _type = PicoCalcSdType.SdV1;
            }

            // Standard capacity cards: force the block length to 512 bytes
            if (_type != PicoCalcSdType.SdHc)
            {
                r = Command(16, BlockSize, 0x01);
                if (r != 0x00)
                {
                    _error = "CMD16: error (R1 = " + Hex(r) + ")";
                    return false;
                }
            }

            // Initialisation done: switch to the data clock
            OpenSpi(_dataClock);

            if (!ReadCsd())
            {
                return false;
            }

            return true;
        }

        /// <summary>Reads one 512-byte block into buffer (at least 512 bytes long).</summary>
        public bool ReadBlock(uint block, byte[] buffer)
        {
            if (_type == PicoCalcSdType.Unknown)
            {
                _error = "Card not initialised";
                return false;
            }

            if (buffer == null || buffer.Length < BlockSize)
            {
                _error = "Buffer too small (512 bytes minimum)";
                return false;
            }

            uint address = _type == PicoCalcSdType.SdHc ? block : block * BlockSize;
            byte r = SendCommand(17, address, 0x01);
            if (r != 0x00)
            {
                Deselect();
                _error = "CMD17: error (R1 = " + Hex(r) + ")";
                return false;
            }

            bool ok = ReadDataBlock(buffer, BlockSize);
            Deselect();
            return ok;
        }

        /// <summary>Writes one 512-byte block (CMD24). WARNING: this overwrites data on the card.</summary>
        public bool WriteBlock(uint block, byte[] buffer)
        {
            if (_type == PicoCalcSdType.Unknown)
            {
                _error = "Card not initialised";
                return false;
            }

            if (buffer == null || buffer.Length < BlockSize)
            {
                _error = "Buffer too small (512 bytes minimum)";
                return false;
            }

            uint address = _type == PicoCalcSdType.SdHc ? block : block * BlockSize;
            byte r = SendCommand(24, address, 0x01);
            if (r != 0x00)
            {
                Deselect();
                _error = "CMD24: error (R1 = " + Hex(r) + ")";
                return false;
            }

            Xfer(0xFF);     // one byte of gap
            Xfer(0xFE);     // start of data token
            _spi.Write(new SpanByte(buffer, 0, BlockSize));
            Xfer(0xFF);     // CRC, ignored in SPI mode
            Xfer(0xFF);

            // Data response: xxx00101 = accepted
            byte response = Xfer(0xFF);
            if ((response & 0x1F) != 0x05)
            {
                Deselect();
                _error = "Write rejected (response " + Hex(response) + ")";
                return false;
            }

            // The card keeps MISO low while it is busy programming the block
            bool done = false;
            for (int i = 0; i < 2000; i++)
            {
                if (Xfer(0xFF) == 0xFF)
                {
                    done = true;
                    break;
                }
            }

            Deselect();
            if (!done)
            {
                _error = "Write: the card stays busy (timeout)";
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            if (_spi != null)
            {
                _spi.Dispose();
                _spi = null;
            }

            _cs.Write(PinValue.High);
            _cs.Dispose();
        }

        // ---------------------------------------------------------------- CSD

        private bool ReadCsd()
        {
            byte r = SendCommand(9, 0, 0x01);
            if (r != 0x00)
            {
                Deselect();
                _error = "CMD9: error (R1 = " + Hex(r) + ")";
                return false;
            }

            bool ok = ReadDataBlock(_csd, 16);
            Deselect();
            if (!ok)
            {
                return false;
            }

            uint structure = GetBits(_csd, 127, 126);
            if (structure == 1)
            {
                // CSD v2 (SDHC): capacity = (C_SIZE + 1) * 512 KB
                uint cSize = GetBits(_csd, 69, 48);
                _capacity = ((long)cSize + 1) * 512L * 1024L;
            }
            else
            {
                // CSD v1: capacity = (C_SIZE + 1) * 2^(C_SIZE_MULT + 2) * 2^READ_BL_LEN
                uint readBlLen = GetBits(_csd, 83, 80);
                uint cSize = GetBits(_csd, 73, 62);
                uint cSizeMult = GetBits(_csd, 49, 47);
                _capacity = ((long)cSize + 1) * (1L << (int)(cSizeMult + 2)) * (1L << (int)readBlLen);
            }

            return true;
        }

        private static uint GetBits(byte[] data, int high, int low)
        {
            uint value = 0;
            for (int bit = high; bit >= low; bit--)
            {
                int b = (data[15 - (bit / 8)] >> (bit % 8)) & 1;
                value = (value << 1) | (uint)b;
            }

            return value;
        }

        // ---------------------------------------------------------------- SPI level

        private void OpenSpi(int clockFrequency)
        {
            if (_spi != null)
            {
                _spi.Dispose();
                _spi = null;
            }

            // -1 = no hardware chip select: CS (GP17) is driven by this class
            SpiConnectionSettings settings = new SpiConnectionSettings(SpiBusId, -1);
            settings.ClockFrequency = clockFrequency;
            settings.Mode = SpiMode.Mode0;
            settings.DataBitLength = 8;
            _spi = SpiDevice.Create(settings);
        }

        // Exchange one byte (the card needs MOSI high while it talks, hence full duplex with 0xFF)
        private byte Xfer(byte value)
        {
            _tx1[0] = value;
            _spi.TransferFullDuplex(new SpanByte(_tx1), new SpanByte(_rx1));
            return _rx1[0];
        }

        private void Select()
        {
            _cs.Write(PinValue.Low);
        }

        // Release the card: CS high, then 8 extra clocks so the card frees MISO
        private void Deselect()
        {
            _cs.Write(PinValue.High);
            Xfer(0xFF);
        }

        // Sends a command and returns R1. CS stays LOW: the caller reads the rest of the
        // answer / the data and then calls Deselect().
        private byte SendCommand(byte index, uint argument, byte crc)
        {
            Select();

            // Wait until the card is ready (MISO high), bounded
            for (int i = 0; i < 50; i++)
            {
                if (Xfer(0xFF) == 0xFF)
                {
                    break;
                }
            }

            _cmd[0] = (byte)(0x40 | index);
            _cmd[1] = (byte)(argument >> 24);
            _cmd[2] = (byte)(argument >> 16);
            _cmd[3] = (byte)(argument >> 8);
            _cmd[4] = (byte)argument;
            _cmd[5] = crc;
            _spi.Write(new SpanByte(_cmd));

            // R1 arrives within 8 bytes; its top bit is 0
            for (int i = 0; i < 16; i++)
            {
                byte r = Xfer(0xFF);
                if ((r & 0x80) == 0)
                {
                    return r;
                }
            }

            return 0xFF;
        }

        // Command with a one-byte answer, then release the card
        private byte Command(byte index, uint argument, byte crc)
        {
            byte r = SendCommand(index, argument, crc);
            Deselect();
            return r;
        }

        // Waits for the 0xFE data token, then reads count bytes and the 2 CRC bytes.
        // CS must already be low.
        private bool ReadDataBlock(byte[] buffer, int count)
        {
            bool token = false;
            for (int i = 0; i < 1000; i++)
            {
                byte t = Xfer(0xFF);
                if (t == 0xFE)
                {
                    token = true;
                    break;
                }

                if (t != 0xFF)
                {
                    _error = "Read: error token " + Hex(t);
                    return false;
                }
            }

            if (!token)
            {
                _error = "Read: no data token (timeout)";
                return false;
            }

            _spi.TransferFullDuplex(new SpanByte(_ff, 0, count), new SpanByte(buffer, 0, count));
            Xfer(0xFF);   // CRC, ignored
            Xfer(0xFF);
            return true;
        }

        private static string Hex(byte value)
        {
            string digits = "0123456789ABCDEF";
            return "0x" + digits[value >> 4] + digits[value & 0x0F];
        }
    }
}
