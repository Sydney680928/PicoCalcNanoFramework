// PicoCalc display driver (step 1: init + solid fill) for .NET nanoFramework
//
// Hardware (PicoCalc carrier board, Pico in the socket):
//   SPI1  SCK = GP10, MOSI = GP11
//   CS    = GP13 (driven manually here)
//   DC    = GP14 (low = command, high = data)
//   RST   = GP15 (active low)
// Panel: 4" IPS, 320x320. The controller is sold as ILI9488 but some units use a
// "99% compatible" chip (ST7365P). This first step therefore only uses standard
// MIPI DCS commands (reset, sleep out, pixel format, MADCTL, window, memory write,
// display on), no vendor-specific gamma/power tuning.
//
// Pixel format over SPI: 18 bits (COLMOD 0x66) = 3 bytes per pixel, R, G, B,
// the 6 significant bits being the top 6 bits of each byte.
//
// The backlight is NOT controlled here: it is driven by the keyboard MCU.
// If the screen stays black, raise it with Alt + '.' on the keyboard.
//
// Two transport modes:
//   - hardware SPI1 (default, fast)
//   - software SPI (bit-banging on GP10/GP11), very slow but independent of the
//     SPI peripheral and its pin configuration. Use it only for diagnostics,
//     with FillRect on a small area.
//
// NuGet packages needed: nanoFramework.System.Device.Spi, nanoFramework.System.Device.Gpio
//
// Usage:
//     var display = new PicoCalcDisplay();           // hardware SPI
//     var display = new PicoCalcDisplay(10000000, true);  // software SPI (diagnostic)
//     display.Init();
//     display.FillScreen(255, 0, 0);

using System;
using System.Device.Gpio;
using System.Device.Spi;
using System.Threading;

namespace PicoCalc
{
    public class PicoCalcDisplay : IDisposable
    {
        public const int Width = 320;
        public const int Height = 320;

        private const int SpiBusId = 1;   // SPI1
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

        private readonly GpioController _gpio;
        private readonly GpioPin _cs;
        private readonly GpioPin _dc;
        private readonly GpioPin _rst;
        private readonly GpioPin _sck;     // software SPI only
        private readonly GpioPin _mosi;    // software SPI only
        private readonly SpiDevice _spi;   // hardware SPI only (null in software mode)
        private readonly byte[] _oneByte = new byte[1];

        /// <summary>Value sent with MADCTL (0x36). 0x48 = column mirror + BGR order.
        /// Change it before Init() if colors or orientation are wrong.</summary>
        public byte MemoryAccess = 0x48;

        /// <summary>Send "display inversion on" during Init(). IPS panels often need it:
        /// set it before Init() if colors look inverted (red shown as cyan).</summary>
        public bool InvertColors = false;

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
            }
            else
            {
                // -1 = no hardware chip select: CS is driven by this class
                SpiConnectionSettings settings = new SpiConnectionSettings(SpiBusId, 9);
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

            WriteCommand(CmdPixelFormat, new byte[] { 0x66 });          // 18 bits per pixel
            WriteCommand(CmdMemoryAccess, new byte[] { MemoryAccess });
            WriteCommand(InvertColors ? CmdInvertOn : CmdInvertOff);

            WriteCommand(CmdDisplayOn);
            Thread.Sleep(30);
        }

        /// <summary>Fill the whole screen with one color (8 bits per channel, the controller keeps 6).</summary>
        public void FillScreen(byte r, byte g, byte b)
        {
            FillRect(0, 0, Width, Height, r, g, b);
        }

        /// <summary>Fill a rectangle with one color.</summary>
        public void FillRect(int x, int y, int w, int h, byte r, byte g, byte b)
        {
            SetWindow(x, y, x + w - 1, y + h - 1);

            int blockRows = h < 8 ? h : 8;
            byte[] block = MakeBlock(w * blockRows, r, g, b);

            WriteCommand(CmdMemoryWrite);

            _dc.Write(PinValue.High);
            _cs.Write(PinValue.Low);

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

            _cs.Write(PinValue.High);
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

        private static byte[] MakeBlock(int pixels, byte r, byte g, byte b)
        {
            byte[] block = new byte[pixels * 3];
            for (int i = 0; i < block.Length; i += 3)
            {
                block[i] = r;
                block[i + 1] = g;
                block[i + 2] = b;
            }

            return block;
        }

        private void SendBytes(byte[] data)
        {
            if (_spi != null)
            {
                _spi.Write(data);
                return;
            }

            // Software SPI, mode 0, MSB first
            for (int i = 0; i < data.Length; i++)
            {
                byte value = data[i];
                for (int bit = 7; bit >= 0; bit--)
                {
                    _mosi.Write(((value >> bit) & 1) != 0 ? PinValue.High : PinValue.Low);
                    _sck.Write(PinValue.High);
                    _sck.Write(PinValue.Low);
                }
            }
        }

        private void WriteCommand(byte command)
        {
            _oneByte[0] = command;

            _dc.Write(PinValue.Low);
            _cs.Write(PinValue.Low);
            SendBytes(_oneByte);
            _cs.Write(PinValue.High);
        }

        private void WriteCommand(byte command, byte[] data)
        {
            WriteCommand(command);

            _dc.Write(PinValue.High);
            _cs.Write(PinValue.Low);
            SendBytes(data);
            _cs.Write(PinValue.High);
        }
    }
}
