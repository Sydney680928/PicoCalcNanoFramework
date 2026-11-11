# PicoCalc drivers for .NET nanoFramework

C# drivers to use a Clockwork **PicoCalc** (Raspberry Pi Pico in the socket) with
[.NET nanoFramework](https://nanoframework.net/): keyboard, display, text screen and a standalone console.
Validated on a Raspberry Pi Pico (RP2040, `PICO_RP2040` firmware) and a Raspberry Pi Pico 2 W (RP2350, `PICO2_RP2350_W` firmware): same code, same SPI bus ids (2 for the display).

| File | Role |
|---|---|
| `PicoCalcKeyboard.cs` | Keyboard (STM32 co-processor over I2C): events with key code, character, modifiers |
| `PicoCalcDisplay.cs` | 320×320 display: init, fill, rectangles, 8×16 glyphs |
| `PicoCalcTextScreen.cs` | 40×20 character grid, only changed cells are redrawn, hardware scrolling |
| `PicoCalcConsole.cs` | Standalone console: `Write`, `WriteLine`, `ReadLine` (editing + history), `ReadKey`, `BreakPressed` |
| `PicoCalcCanvas.cs` | In-memory drawing surface (RGB565): pixels, lines, circles, triangles, text, sprites; sent to the screen in one transfer |
| `PicoCalcFont.cs` | 8×16 font (GNU Unifont, U+0020–U+00FF, accents included) |
| `Program.cs` | Demo: a small REPL (`help`, `echo`, `calc`, `lines`, `clear`) |

NuGet packages: `nanoFramework.System.Device.Gpio`, `nanoFramework.System.Device.Spi`,
`nanoFramework.System.Device.I2c`, `nanoFramework.System.Diagnostics.Stopwatch` (demo timings only).

## Minimal use (console)

```csharp
var console = new PicoCalcConsole();     // hardware SPI, 62.5 MHz
console.Start();                         // init display, clear it, start keyboard + refresh thread

console.WriteLine("Hello PicoCalc");
string line = console.ReadLine("> ");    // null if Break (Shift+Esc) or Ctrl+C was pressed
console.WriteLine("You typed: " + line);
```

The screen is refreshed by an internal thread: no `Flush()` and no lock needed.
`Write`/`WriteLine` can be called from any thread. `ReadLine` supports Left/Right, Home/End, Backspace,
Delete, Up/Down (history), Esc (clear the line) and Enter. The `BreakPressed` event is raised on
Break / Ctrl+C even when nobody is reading (typical use: stop a running script).

## Lower-level use (display + text screen)

```csharp
var display = new PicoCalcDisplay(62500000);
display.Init();
var screen = new PicoCalcTextScreen(display);
screen.ClearPanel();             // first run only: the panel keeps its content
screen.WriteLine("Hello");
screen.Flush();                  // draws the cells that changed (not thread-safe: lock around it)
```

## Drawing

Directly on the screen (`PicoCalcDisplay`): `FillRect`, `FillScreen`, `HLine`, `VLine`, `DrawRect`, `DrawPixel`
(slow), `DrawText(x, y, text, fg, bg, scale)`, `DrawGlyph`/`DrawGlyphs`, all clipped to the screen. Each call
costs a few milliseconds, so only use it for shapes the controller fills in one window.

For anything else (lines, circles, triangles, sprites, scenes) draw into a `PicoCalcCanvas` and send it at once:

```csharp
var canvas = new PicoCalcCanvas(140, 140);       // 140*140*2 bytes = 39 KB of RAM
canvas.Clear(0x000040);
canvas.DrawLine(0, 0, 139, 139, PicoCalcColors.Red);
canvas.FillCircle(70, 70, 25, PicoCalcColors.Yellow);
canvas.FillTriangle(20, 120, 50, 80, 80, 120, PicoCalcColors.Cyan);
canvas.DrawText(8, 6, "Canvas", PicoCalcColors.White, 1);
canvas.DrawCanvas(sprite, 100, 95, PicoCalcColors.Magenta);   // sprite, magenta = transparent
canvas.DrawTo(display, 90, 170);                              // one transfer
```

Colors are `0xRRGGBB` (see `PicoCalcColors`); the display and canvas keep 5/6/5 bits per pixel (16-bit mode).
A full-screen canvas (200 KB) is too big for the RAM of most Picos: use zones or sprites.

## Hardware facts

- **Keyboard**: I2C1 (SDA GP6, SCL GP7), address `0x1F`. Write register `0x09`, read 2 bytes `[state, code]`
  (1 = pressed/auto-repeat, 2 = hold, 3 = released, `[0,0]` = empty). The STM32 applies Shift and Caps Lock;
  Ctrl is handled by the driver. Key codes are in `PicoCalcKeys`.
- **Display**: SPI1 (SCK GP10, MOSI GP11), CS GP13, DC GP14, RST GP15. Standard MIPI DCS commands,
  `INVON` is required (otherwise colors are inverted), `MADCTL = 0x48`.
  The controller memory is **480 lines** tall while the panel shows 320: the hardware scroll area must be
  declared as `VSCRDEF(0, 320, 160)` (the three values add up to 480), then `VSCRSADD` moves the start line.
  The backlight is driven by the keyboard MCU: `Alt + ,` / `Alt + .` change the brightness.
- **SD card** (not tested yet): SPI0 (SCK GP18, MOSI GP19, MISO GP16, CS GP17), i.e. nanoFramework bus id **1**.

## Pitfalls found on the way

1. **SPI bus ids are 1-based in `SpiDevice.Create`** on RP2040/RP2350: id 1 = SPI0, id 2 = SPI1. With id 1 the
   display stays black (data goes out on the SD pins) even though `Write` returns without error. Id 0 throws
   `ArgumentException`. **But `SpiDevice.GetBusInfo` is 0-based** (ids 0 and 1 work, 2 and 3 throw): the two
   are inconsistent (reported to the nanoFramework team). `GetBusInfo` reports 1922 Hz .. 62.5 MHz.
2. **The firmware caps the clock at 62.5 MHz** (the bus maximum; higher values give the same speed). Below that, speed is
   proportional to the clock but ~300× slower than expected (1 MHz asked ≈ 5 KB in 12 s), so use 62.5 MHz.
3. **Software SPI works** (GPIO bit-banging) but only reaches ~200 bytes/s: a full-screen clear takes ~17 minutes.
4. **Pixel formats**: 16-bit (RGB565) and 18-bit work; the 3-bit format does not.
5. **The panel keeps its memory** between program runs while powered, so `ClearPanel()` is only needed once.
6. Drawing many small things directly on the display is slow (fixed cost per SPI call): group them in a canvas.
7. Use `PicoCalcDisplay.Dispose()` carefully: releasing the pins makes the panel reset.
8. **Development**: do not install batteries while the Pico is powered from USB (back-feeding risk).
   Stop the debugger before deploying; if deployment hangs at the erase step, unplug/replug the Pico USB cable.

## Measured speed (RP2040, hardware SPI, 16-bit)

The cost is the NUMBER of SPI calls, not the amount of data: each `SpiDevice.Write` has a fixed overhead of
about 1.5 ms, while a 39 KB block goes out in 11 ms (≈ 3.5 MB/s).

| Operation | Time |
|---|---|
| Full-screen clear | ≈ 0.15 s |
| One 8×16 character, drawn alone | ≈ 9 ms (6 SPI calls: window + memory write) |
| 40 characters of a line, drawn together (`DrawGlyphs`) | ≈ 125 ms (against 364 ms one by one) |
| 14 direct shapes/text calls | ≈ 400 ms (≈ 25 ms per call) |
| Draw into a 140×140 canvas (lines, circles, text...) | ≈ 133 ms |
| Send that canvas (39 KB) to the screen | ≈ 11 ms |
| Scrolling | instantaneous (hardware), then new lines are drawn |

`PicoCalcTextScreen` sends neighbouring changed cells of a line in one transfer. Keeping CS low
(`HoldChipSelect = true`) only gains ~2 ms per character (9 ms instead of 11 ms), it is off by default.

## Font license

The glyph table comes from GNU Unifont, dual-licensed under GPL v2+ with the Font Embedding Exception,
or the SIL Open Font License 1.1. Keep the notice at the top of `PicoCalcFont.cs`.
