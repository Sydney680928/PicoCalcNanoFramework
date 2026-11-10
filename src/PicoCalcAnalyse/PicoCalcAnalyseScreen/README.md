# PicoCalc drivers for .NET nanoFramework

C# drivers to use a Clockwork **PicoCalc** (Raspberry Pi Pico in the socket) with
[.NET nanoFramework](https://nanoframework.net/): keyboard, display and a text screen.
Validated on a Raspberry Pi Pico (RP2040, `PICO_RP2040` firmware).

| File | Role |
|---|---|
| `PicoCalcKeyboard.cs` | Keyboard (STM32 co-processor over I2C): events with key code, character, modifiers |
| `PicoCalcDisplay.cs` | 320×320 display: init, fill, rectangles, 8×16 glyphs |
| `PicoCalcTextScreen.cs` | 40×20 character grid, only changed cells are redrawn |
| `PicoCalcFont.cs` | 8×16 font (GNU Unifont, U+0020–U+00FF, accents included) |
| `Program.cs` | Demo: type on the keyboard, text appears on the screen |

NuGet packages: `nanoFramework.System.Device.Gpio`, `nanoFramework.System.Device.Spi`,
`nanoFramework.System.Device.I2c`, `nanoFramework.System.Diagnostics.Stopwatch` (demo timings only).

## Minimal use

```csharp
var display = new PicoCalcDisplay(62500000);     // hardware SPI
display.Init();

var screen = new PicoCalcTextScreen(display);
screen.ClearPanel();                             // first run only: the panel keeps its content
screen.WriteLine("Hello PicoCalc");
screen.Flush();                                  // draws the cells that changed

var keyboard = new PicoCalcKeyboard();
keyboard.KeyDown += (s, e) => { if (e.HasChar) { screen.Write(e.Char); } };
keyboard.Start();
```

Call `Flush()` regularly (the demo does it every 20 ms) and lock around the screen
if you write from the keyboard event, as the demo does.

## Hardware facts

- **Keyboard**: I2C1 (SDA GP6, SCL GP7), address `0x1F`. Write register `0x09`, read 2 bytes `[state, code]`
  (1 = pressed/auto-repeat, 2 = hold, 3 = released, `[0,0]` = empty). The STM32 applies Shift and Caps Lock;
  Ctrl is handled by the driver. Key codes are in `PicoCalcKeys`.
- **Display**: SPI1 (SCK GP10, MOSI GP11), CS GP13, DC GP14, RST GP15. Standard MIPI DCS commands,
  `INVON` is required (otherwise colors are inverted), `MADCTL = 0x48`.
  The backlight is driven by the keyboard MCU: `Alt + ,` / `Alt + .` change the brightness.
- **SD card** (not tested yet): SPI0 (SCK GP18, MOSI GP19, MISO GP16, CS GP17), i.e. nanoFramework bus id **1**.

## Pitfalls found on the way

1. **SPI bus ids are 1-based on RP2040/RP2350**: id 1 = SPI0, id 2 = SPI1. With id 1 the display stays black
   (data goes out on the SD pins) even though `Write` returns without error. Id 0 throws `ArgumentException`.
2. **The firmware ignores clock values above ~62.5 MHz** (they all give the same speed). Below that, speed is
   proportional to the clock but ~300× slower than expected (1 MHz asked ≈ 5 KB in 12 s), so use 62.5 MHz.
3. **Software SPI works** (GPIO bit-banging) but only reaches ~200 bytes/s: a full-screen clear takes ~17 minutes.
4. **Pixel formats**: 16-bit (RGB565) and 18-bit work; the 3-bit format does not.
5. **The panel keeps its memory** between program runs while powered, so `ClearPanel()` is only needed once.
6. Use `PicoCalcDisplay.Dispose()` carefully: releasing the pins makes the panel reset.
7. **Development**: do not install batteries while the Pico is powered from USB (back-feeding risk).
   Stop the debugger before deploying; if deployment hangs at the erase step, unplug/replug the Pico USB cable.

## Measured speed (RP2040, hardware SPI, 16-bit)

Full-screen clear ≈ 0.15 s, one text cell ≈ 13–20 ms, first draw of ~60 characters ≈ 1.3 s,
one typed character ≈ 85 ms.

## Font license

The glyph table comes from GNU Unifont, dual-licensed under GPL v2+ with the Font Embedding Exception,
or the SIL Open Font License 1.1. Keep the notice at the top of `PicoCalcFont.cs`.
