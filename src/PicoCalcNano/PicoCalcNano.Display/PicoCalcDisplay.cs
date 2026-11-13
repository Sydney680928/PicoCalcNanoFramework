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

// PicoCalc display driver for .NET nanoFramework
//
// Hardware (PicoCalc carrier board, Pico in the socket):
//   SPI1 (nanoFramework bus id 2): SCK = GP10, MOSI = GP11
//   CS    = GP13 (driven manually here)
//   DC    = GP14 (low = command, high = data)
//   RST   = GP15 (active low)
// Panel: 4" IPS, 320x320. The controller is sold as ILI9488 but some units use a
// "99% compatible" chip (ST7365P). This driver therefore only uses standard
// MIPI DCS commands (reset, sleep out, pixel format, MADCTL, window, memory write,
// display on), no vendor-specific gamma/power tuning.
//
// Pixel format over SPI: 16 bits (COLMOD 0x55, RGB565, 2 bytes per pixel) by default,
// or 18 bits (COLMOD 0x66, 3 bytes per pixel) with Use16Bit = false.
// (The 3-bit format, COLMOD 0x61, was tried and does not work on this panel.)
//
// The backlight is NOT controlled here: it is driven by the keyboard MCU.
// If the screen stays black, raise it with Alt + '.' on the keyboard.
//
// Transport:
//   - Hardware SPI (default): nanoFramework SPI bus ids are 1-BASED on RP2040/RP2350.
//     Id 1 = SPI0 (GP16..GP19, the SD card pins), id 2 = SPI1 (GP10/GP11, the display).
//     So the display is on bus 2 (PicoCalcDisplay.SpiBusId). Chip select is driven by
//     this class (CS = -1 in the SPI settings).
//     The firmware caps the clock: any value >= 62.5 MHz gives the same speed
//     (~75 ms for 5 KB, ~13 ms for a text cell). Full screen clear: ~0.15 s.
//   - Software SPI (bit-banging on GP10/GP11): ~200 bytes/s, kept only as a fallback.
//
// NuGet packages needed: nanoFramework.System.Device.Spi, nanoFramework.System.Device.Gpio
//
// Usage:
//     var display = new PicoCalcDisplay(62500000);   // hardware SPI
//     display.Init();
//     display.FillScreen(PicoCalcColors.Red);

using System;
using System.Device.Gpio;
using System.Device.Spi;
using System.Threading;

namespace PicoCalcNano
{
    /// <summary>Colors as 0xRRGGBB.</summary>
    public static class PicoCalcColors
    {
        public const int Black = 0x000000;
        public const int White = 0xFFFFFF;
        public const int Red = 0xFF0000;
        public const int Green = 0x00FF00;
        public const int Blue = 0x0000FF;
        public const int Yellow = 0xFFFF00;
        public const int Cyan = 0x00FFFF;
        public const int Magenta = 0xFF00FF;
    }

    public class PicoCalcDisplay : IDisposable
    {
        public const int Width = 320;
        public const int Height = 320;
        private const int ControllerHeight = 480;   // lines of display memory in the controller

        /// <summary>nanoFramework SPI bus id for hardware mode. Ids are 1-based on RP2040/RP2350: 1 = SPI0 (SD card pins), 2 = SPI1 (display pins GP10/GP11). Set BEFORE creating the display.</summary>
        public static int SpiBusId = 2;
        private const int PinSck = 10;
        private const int PinMosi = 11;
        private const int PinCs = 13;
        private const int PinDc = 14;
        private const int PinRst = 15;

        // Standard MIPI DCS commands
        private const byte CmdSwReset = 0x01;
        private const byte CmdSleepOut = 0x11;
        private const byte CmdInvertOff = 0x20;
        private const byte CmdInvertOn = 0x21;
        private const byte CmdDisplayOn = 0x29;
        private const byte CmdColumnAddress = 0x2A;
        private const byte CmdPageAddress = 0x2B;
        private const byte CmdMemoryWrite = 0x2C;
        private const byte CmdMemoryAccess = 0x36;
        private const byte CmdPixelFormat = 0x3A;

        private const int GlyphWidth = 8;
        private const int GlyphHeight = 16;

        private readonly GpioController _gpio;
        private readonly GpioPin _cs;
        private readonly GpioPin _dc;
        private readonly GpioPin _rst;
        private readonly GpioPin _sck;     // software SPI only
        private readonly GpioPin _mosi;    // software SPI only
        private readonly SpiDevice _spi;   // hardware SPI only (null in software mode)
        private readonly byte[] _oneByte = new byte[1];
        private byte[] _glyphBuffer;

        private bool _mosiLevel;           // last level written on MOSI (software SPI)

        /// <summary>Value sent with MADCTL (0x36). 0x48 = column mirror + BGR order.
        /// Set it before Init() if orientation is wrong.</summary>
        public byte MemoryAccess = 0x48;

        /// <summary>Send "display inversion on" during Init(). This panel needs it
        /// (without it, colors come out as their complement). Set before Init().</summary>
        public bool InvertColors = true;

        /// <summary>Use 16 bits per pixel (COLMOD 0x55, RGB565, 2 bytes) instead of 18 bits
        /// (3 bytes). Less data to send = faster with software SPI. Set before Init().</summary>
        public bool Use16Bit = true;

        private int BytesPerPixel { get { return Use16Bit ? 2 : 3; } }

        public PicoCalcDisplay() : this(10000000, false)
        {
        }

        public PicoCalcDisplay(int clockFrequency) : this(clockFrequency, false)
        {
        }

        public PicoCalcDisplay(int clockFrequency, bool softwareSpi)
        {
            _gpio = new GpioController();

            _cs = _gpio.OpenPin(PinCs, PinMode.Output);
            _dc = _gpio.OpenPin(PinDc, PinMode.Output);
            _rst = _gpio.OpenPin(PinRst, PinMode.Output);
            _cs.Write(PinValue.High);
            _dc.Write(PinValue.High);
            _rst.Write(PinValue.High);

            if (softwareSpi)
            {
                _sck = _gpio.OpenPin(PinSck, PinMode.Output);
                _mosi = _gpio.OpenPin(PinMosi, PinMode.Output);
                _sck.Write(PinValue.Low);     // SPI mode 0: clock idles low
                _mosi.Write(PinValue.Low);
                _mosiLevel = false;
            }
            else
            {
                // -1 = no hardware chip select: CS is driven by this class
                SpiConnectionSettings settings = new SpiConnectionSettings(SpiBusId, -1);
                settings.ClockFrequency = clockFrequency;
                settings.Mode = SpiMode.Mode0;
                settings.DataBitLength = 8;
                _spi = SpiDevice.Create(settings);
            }
        }

        public void Init()
        {
            // Hardware reset
            _rst.Write(PinValue.High);
            Thread.Sleep(10);
            _rst.Write(PinValue.Low);
            Thread.Sleep(20);
            _rst.Write(PinValue.High);
            Thread.Sleep(150);

            WriteCommand(CmdSwReset);
            Thread.Sleep(150);

            WriteCommand(CmdSleepOut);
            Thread.Sleep(150);

            _glyphBuffer = new byte[GlyphWidth * GlyphHeight * BytesPerPixel];
            WriteCommand(CmdPixelFormat, new byte[] { Use16Bit ? (byte)0x55 : (byte)0x66 });
            WriteCommand(CmdMemoryAccess, new byte[] { MemoryAccess });
            WriteCommand(InvertColors ? CmdInvertOn : CmdInvertOff);

            // Hardware scrolling: the controller memory is 480 lines tall (the panel shows 320),
            // and top + scroll area + bottom must add up to 480. Scroll area = the visible 320 lines.
            SetVerticalScrollArea(0, Height, ControllerHeight - Height);
            SetScrollStart(0);

            WriteCommand(CmdDisplayOn);
            Thread.Sleep(30);
        }

        /// <summary>Fill the whole screen with one color (8 bits per channel, the controller keeps 6).</summary>
        public void FillScreen(byte r, byte g, byte b)
        {
            FillRect(0, 0, Width, Height, r, g, b);
        }

        /// <summary>Fill the whole screen with one 0xRRGGBB color.</summary>
        public void FillScreen(int rgb)
        {
            FillRect(0, 0, Width, Height, rgb);
        }

        /// <summary>Fill a rectangle with one 0xRRGGBB color.</summary>
        public void FillRect(int x, int y, int w, int h, int rgb)
        {
            FillRect(x, y, w, h, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
        }

        /// <summary>Fill a rectangle with one color.</summary>
        public void FillRect(int x, int y, int w, int h, byte r, byte g, byte b)
        {
            // Clip to the screen
            if (x < 0) { w += x; x = 0; }
            if (y < 0) { h += y; y = 0; }
            if (x + w > Width) { w = Width - x; }
            if (y + h > Height) { h = Height - y; }
            if (w <= 0 || h <= 0)
            {
                return;
            }

            SetWindow(x, y, x + w - 1, y + h - 1);

            int blockRows = h < 8 ? h : 8;
            byte[] block = MakeBlock(w * blockRows, r, g, b);

            WriteCommand(CmdMemoryWrite);

            SetDc(true);
            Select();

            int remaining = h;
            while (remaining >= blockRows)
            {
                SendBytes(block);
                remaining -= blockRows;
            }

            if (remaining > 0)
            {
                SendBytes(MakeBlock(w * remaining, r, g, b));
            }

            Deselect();
        }

        /// <summary>
        /// Draw one 8x16 monochrome glyph at pixel position (x, y).
        /// The glyph is 16 bytes in 'font' starting at 'offset': one byte per row,
        /// bit 7 = leftmost pixel (see PicoCalcFont). Set bits use fgRgb, others bgRgb.
        /// </summary>
        public void DrawGlyph(int x, int y, byte[] font, int offset, int fgRgb, int bgRgb)
        {
            PrepareExpand(fgRgb, bgRgb);
            int rowBytes = GlyphWidth * BytesPerPixel;

            for (int row = 0; row < GlyphHeight; row++)
            {
                Array.Copy(_expandTable, font[offset + row] * rowBytes, _glyphBuffer, row * rowBytes, rowBytes);
            }

            SetWindow(x, y, x + GlyphWidth - 1, y + GlyphHeight - 1);
            WriteCommand(CmdMemoryWrite);

            SetDc(true);
            Select();
            SendBytes(_glyphBuffer);
            Deselect();
        }

        /// <summary>
        /// Draw 'count' characters of 'chars' (starting at 'start') side by side on one text line,
        /// at pixel position (x, y), with ONE window and ONE data transfer. Much faster than
        /// calling DrawGlyph for each character, because every SPI call has a fixed cost.
        /// </summary>
        public void DrawGlyphs(int x, int y, char[] chars, int start, int count, int fgRgb, int bgRgb)
        {
            if (count <= 0)
            {
                return;
            }

            if (count == 1)
            {
                DrawGlyph(x, y, PicoCalcFont.Data, PicoCalcFont.GlyphOffset(chars[start]), fgRgb, bgRgb);
                return;
            }

            PrepareExpand(fgRgb, bgRgb);
            int rowBytes = GlyphWidth * BytesPerPixel;
            int lineBytes = count * rowBytes;
            int total = lineBytes * GlyphHeight;

            if (_runBuffer == null || _runBuffer.Length < total)
            {
                _runBuffer = new byte[total];
            }

            for (int i = 0; i < count; i++)
            {
                int offset = PicoCalcFont.GlyphOffset(chars[start + i]);
                for (int row = 0; row < GlyphHeight; row++)
                {
                    Array.Copy(_expandTable, PicoCalcFont.Data[offset + row] * rowBytes, _runBuffer, row * lineBytes + i * rowBytes, rowBytes);
                }
            }

            SetWindow(x, y, x + count * GlyphWidth - 1, y + GlyphHeight - 1);
            WriteCommand(CmdMemoryWrite);

            SetDc(true);
            Select();
            SendBytes(_runBuffer, total);
            Deselect();
        }

        // For each of the 256 possible font rows, the 8 pixels already converted to the display
        // format with the current colors: building a glyph row is then one Array.Copy.
        private byte[] _expandTable;
        private byte[] _runBuffer;
        private int _expandFg = -1;
        private int _expandBg = -1;
        private bool _expandIs16;

        private void PrepareExpand(int fgRgb, int bgRgb)
        {
            if (_expandTable != null && _expandFg == fgRgb && _expandBg == bgRgb && _expandIs16 == Use16Bit)
            {
                return;
            }

            int bpp = BytesPerPixel;
            int rowBytes = GlyphWidth * bpp;
            if (_expandTable == null || _expandTable.Length != 256 * rowBytes)
            {
                _expandTable = new byte[256 * rowBytes];
            }

            byte[] fgPx = MakeBlock(1, (byte)((fgRgb >> 16) & 0xFF), (byte)((fgRgb >> 8) & 0xFF), (byte)(fgRgb & 0xFF));
            byte[] bgPx = MakeBlock(1, (byte)((bgRgb >> 16) & 0xFF), (byte)((bgRgb >> 8) & 0xFF), (byte)(bgRgb & 0xFF));

            for (int pattern = 0; pattern < 256; pattern++)
            {
                int p = pattern * rowBytes;
                for (int col = 0; col < GlyphWidth; col++)
                {
                    byte[] px = (pattern & (0x80 >> col)) != 0 ? fgPx : bgPx;
                    for (int k = 0; k < bpp; k++)
                    {
                        _expandTable[p++] = px[k];
                    }
                }
            }

            _expandFg = fgRgb;
            _expandBg = bgRgb;
            _expandIs16 = Use16Bit;
        }

        // ---------------------------------------------------------------------------------
        // Drawing directly on the screen. Only shapes the controller can fill in ONE window
        // are offered here (each call costs a few milliseconds). For lines, circles, triangles...
        // draw into a PicoCalcCanvas (in memory) and send it with canvas.DrawTo(display, x, y).
        // ---------------------------------------------------------------------------------

        /// <summary>Horizontal line (clipped).</summary>
        public void HLine(int x, int y, int length, int rgb)
        {
            FillRect(x, y, length, 1, rgb);
        }

        /// <summary>Vertical line (clipped).</summary>
        public void VLine(int x, int y, int length, int rgb)
        {
            FillRect(x, y, 1, length, rgb);
        }

        /// <summary>Rectangle outline, 1 pixel wide (clipped).</summary>
        public void DrawRect(int x, int y, int w, int h, int rgb)
        {
            if (w <= 0 || h <= 0)
            {
                return;
            }

            HLine(x, y, w, rgb);
            if (h > 1)
            {
                HLine(x, y + h - 1, w, rgb);
            }

            if (h > 2)
            {
                VLine(x, y + 1, h - 2, rgb);
                if (w > 1)
                {
                    VLine(x + w - 1, y + 1, h - 2, rgb);
                }
            }
        }

        /// <summary>One pixel. Slow (a whole window per pixel): prefer a PicoCalcCanvas.</summary>
        public void DrawPixel(int x, int y, int rgb)
        {
            FillRect(x, y, 1, 1, rgb);
        }

        /// <summary>Width in pixels of a text drawn with DrawText.</summary>
        public static int MeasureText(string text, int scale)
        {
            return text.Length * GlyphWidth * scale;
        }

        /// <summary>
        /// Draw text at pixel position (x, y) with a background color. scale = 1, 2, 3...
        /// (each font pixel becomes scale x scale pixels). A character that does not fit
        /// entirely on the screen is skipped.
        /// </summary>
        public void DrawText(int x, int y, string text, int fgRgb, int bgRgb, int scale)
        {
            if (scale < 1)
            {
                scale = 1;
            }

            int cellW = GlyphWidth * scale;
            int cellH = GlyphHeight * scale;

            for (int i = 0; i < text.Length; i++)
            {
                int cx = x + i * cellW;
                if (cx < 0 || y < 0 || cx + cellW > Width || y + cellH > Height)
                {
                    continue;
                }

                DrawGlyphScaled(cx, y, PicoCalcFont.Data, PicoCalcFont.GlyphOffset(text[i]), fgRgb, bgRgb, scale);
            }
        }

        private byte[] _scaledBuffer;
        private byte[] _scaledRow;

        private void DrawGlyphScaled(int x, int y, byte[] font, int offset, int fgRgb, int bgRgb, int scale)
        {
            if (scale == 1)
            {
                DrawGlyph(x, y, font, offset, fgRgb, bgRgb);
                return;
            }

            int bpp = BytesPerPixel;
            int w = GlyphWidth * scale;
            int h = GlyphHeight * scale;
            int rowBytes = w * bpp;

            if (_scaledBuffer == null || _scaledBuffer.Length != rowBytes * h)
            {
                _scaledBuffer = new byte[rowBytes * h];
                _scaledRow = new byte[rowBytes];
            }

            byte[] fgPx = MakeBlock(1, (byte)((fgRgb >> 16) & 0xFF), (byte)((fgRgb >> 8) & 0xFF), (byte)(fgRgb & 0xFF));
            byte[] bgPx = MakeBlock(1, (byte)((bgRgb >> 16) & 0xFF), (byte)((bgRgb >> 8) & 0xFF), (byte)(bgRgb & 0xFF));

            int dst = 0;
            for (int row = 0; row < GlyphHeight; row++)
            {
                int bits = font[offset + row];

                // One font row, stretched horizontally
                int p = 0;
                for (int col = 0; col < GlyphWidth; col++)
                {
                    byte[] px = (bits & (0x80 >> col)) != 0 ? fgPx : bgPx;
                    for (int k = 0; k < scale; k++)
                    {
                        for (int b = 0; b < bpp; b++)
                        {
                            _scaledRow[p++] = px[b];
                        }
                    }
                }

                // ... then repeated vertically
                for (int k = 0; k < scale; k++)
                {
                    Array.Copy(_scaledRow, 0, _scaledBuffer, dst, rowBytes);
                    dst += rowBytes;
                }
            }

            WriteRaw(x, y, w, h, _scaledBuffer);
        }

        /// <summary>Change the pixel format (COLMOD): 0x66 = 18 bits, 0x55 = 16 bits, 0x61 = 3 bits.</summary>
        public void SetPixelFormat(byte colmod)
        {
            WriteCommand(CmdPixelFormat, new byte[] { colmod });
        }

        /// <summary>Send raw pixel bytes into a window, in whatever pixel format is currently set.</summary>
        public void WriteRaw(int x, int y, int w, int h, byte[] data)
        {
            SetWindow(x, y, x + w - 1, y + h - 1);
            WriteCommand(CmdMemoryWrite);

            SetDc(true);
            Select();
            SendBytes(data);
            Deselect();
        }

        /// <summary>
        /// Define the hardware vertical scroll areas (DCS VSCRDEF, 0x33):
        /// topFixed + scrollArea + bottomFixed must equal 480 (the controller memory height).
        /// </summary>
        public void SetVerticalScrollArea(int topFixed, int scrollArea, int bottomFixed)
        {
            WriteCommand(0x33, new byte[]
            {
                (byte)(topFixed >> 8), (byte)(topFixed & 0xFF),
                (byte)(scrollArea >> 8), (byte)(scrollArea & 0xFF),
                (byte)(bottomFixed >> 8), (byte)(bottomFixed & 0xFF)
            });
        }

        /// <summary>
        /// Set the first displayed line inside the scroll area (DCS VSCRSADD, 0x37).
        /// The panel shows memory line 'line' at the top, then wraps around.
        /// </summary>
        public void SetScrollStart(int line)
        {
            WriteCommand(0x37, new byte[] { (byte)(line >> 8), (byte)(line & 0xFF) });
        }

        // ---- chip select / data-command pins ---------------------------------------------
        // Every GPIO write costs time in managed code, so avoid the useless ones: DC is only
        // written when it changes, and CS can be held low permanently (nothing else is on this bus).

        private bool _holdCs;
        private bool _dcLevel = true;   // matches the initial write in the constructor

        /// <summary>
        /// Keep the display chip select low all the time instead of toggling it around every
        /// transfer. Saves many slow GPIO writes (a text cell gets several times faster).
        /// Safe as long as nothing else shares the display's SPI bus (true on the PicoCalc).
        /// </summary>
        public bool HoldChipSelect
        {
            get { return _holdCs; }
            set
            {
                _holdCs = value;
                _cs.Write(value ? PinValue.Low : PinValue.High);
            }
        }

        private void Select()
        {
            if (!_holdCs)
            {
                _cs.Write(PinValue.Low);
            }
        }

        private void Deselect()
        {
            if (!_holdCs)
            {
                _cs.Write(PinValue.High);
            }
        }

        private void SetDc(bool high)
        {
            if (_dcLevel != high)
            {
                _dc.Write(high ? PinValue.High : PinValue.Low);
                _dcLevel = high;
            }
        }

        public void SetWindow(int x0, int y0, int x1, int y1)
        {
            WriteCommand(CmdColumnAddress, new byte[] { (byte)(x0 >> 8), (byte)(x0 & 0xFF), (byte)(x1 >> 8), (byte)(x1 & 0xFF) });
            WriteCommand(CmdPageAddress, new byte[] { (byte)(y0 >> 8), (byte)(y0 & 0xFF), (byte)(y1 >> 8), (byte)(y1 & 0xFF) });
        }

        public void Dispose()
        {
            if (_spi != null)
            {
                _spi.Dispose();
            }

            _cs.Dispose();
            _dc.Dispose();
            _rst.Dispose();

            if (_sck != null)
            {
                _sck.Dispose();
                _mosi.Dispose();
            }

            _gpio.Dispose();
        }

        private byte[] MakeBlock(int pixels, byte r, byte g, byte b)
        {
            int bpp = BytesPerPixel;
            byte[] block = new byte[pixels * bpp];
            if (Use16Bit)
            {
                byte hi = (byte)((r & 0xF8) | (g >> 5));
                byte lo = (byte)(((g & 0x1C) << 3) | (b >> 3));
                for (int i = 0; i < block.Length; i += 2)
                {
                    block[i] = hi;
                    block[i + 1] = lo;
                }
            }
            else
            {
                for (int i = 0; i < block.Length; i += 3)
                {
                    block[i] = r;
                    block[i + 1] = g;
                    block[i + 2] = b;
                }
            }

            return block;
        }

        private void SendBytes(byte[] data)
        {
            SendBytes(data, data.Length);
        }

        // Send the first 'count' bytes of 'data'
        private void SendBytes(byte[] data, int count)
        {
            if (_spi != null)
            {
                if (count == data.Length)
                {
                    _spi.Write(data);
                }
                else
                {
                    _spi.Write(new SpanByte(data, 0, count));
                }

                return;
            }

            // Software SPI, mode 0, MSB first. MOSI is only written when the bit
            // changes, so runs of identical bits (0x00 / 0xFF bytes) cost two pin
            // writes per bit instead of three.
            bool level = _mosiLevel;
            for (int i = 0; i < count; i++)
            {
                byte value = data[i];
                for (int bit = 7; bit >= 0; bit--)
                {
                    bool wanted = ((value >> bit) & 1) != 0;
                    if (wanted != level)
                    {
                        _mosi.Write(wanted ? PinValue.High : PinValue.Low);
                        level = wanted;
                    }

                    _sck.Write(PinValue.High);
                    _sck.Write(PinValue.Low);
                }
            }

            _mosiLevel = level;
        }

        private void WriteCommand(byte command)
        {
            _oneByte[0] = command;

            SetDc(false);
            Select();
            SendBytes(_oneByte);
            Deselect();
        }

        private void WriteCommand(byte command, byte[] data)
        {
            WriteCommand(command);

            SetDc(true);
            Select();
            SendBytes(data);
            Deselect();
        }
    }
}
