# Display (`PicoCalcDisplay`, text screen, canvas, font)

Driver for the PicoCalc 4" IPS display (320 × 320 pixels). It has three layers that you can use separately:

1. **`PicoCalcDisplay`**: the panel itself (init, fills, rectangles, lines, text, raw pixels, hardware scroll)
2. **`PicoCalcTextScreen`**: a 40 × 20 character grid with hardware scrolling, only changed cells are redrawn
3. **`PicoCalcCanvas`**: an in-memory RGB565 surface for lines, circles, triangles, sprites, sent to the screen in one transfer

The font (`PicoCalcFont`) is an 8 × 16 table (GNU Unifont, U+0020 to U+00FF, accents included).

- Source files: `PicoCalcDisplay.cs` (also contains `PicoCalcColors`), `PicoCalcTextScreen.cs`, `PicoCalcCanvas.cs`, `PicoCalcFont.cs`
- NuGet packages: `nanoFramework.System.Device.Gpio`, `nanoFramework.System.Device.Spi`
- Colors are `int` values `0xRRGGBB` everywhere (see `PicoCalcColors`: Black, White, Red, Green, Blue, Yellow, Cyan, Magenta)

If you only want a text console with a keyboard, use [`PicoCalcConsole`](console.md) instead: it drives these classes for you.

> **One display at a time.** The display pins (GP10, GP11, GP13, GP14, GP15) can only be opened once. Do not create a
> `PicoCalcDisplay` in a program that also creates a `PicoCalcConsole` (the console has its own, available as
> `console.Display`): the second one fails with an `InvalidOperationException`. `Dispose()` releases the pins.

## Hardware

| Signal | Pin |
|---|---|
| SPI1 SCK / MOSI | GP10 / GP11 (nanoFramework SPI bus id **2**) |
| CS | GP13 (driven by the driver, not by the SPI peripheral) |
| DC (low = command, high = data) | GP14 |
| RST (active low) | GP15 |

The panel controller is sold as an ILI9488, but some units use a compatible chip (ST7365P). The driver only uses
standard MIPI DCS commands (reset, sleep out, pixel format, MADCTL, window, memory write, display on, vertical
scrolling): no vendor-specific gamma or power tuning. The pixel format is 16 bits (RGB565, `COLMOD 0x55`) by
default; 18 bits (`0x66`) also works.

Two requirements specific to this panel are built in: `INVON` is required (without it every color is
inverted) and `MADCTL = 0x48` gives the right orientation.

The **backlight is not controlled by this driver**: it is driven by the keyboard MCU. If the screen stays black,
raise the brightness with `Alt + .` on the keyboard.

## Quick start

```csharp
var display = new PicoCalcDisplay(62500000);   // hardware SPI, 62.5 MHz
display.Init();
display.FillScreen(PicoCalcColors.Black);
display.FillRect(10, 10, 60, 30, PicoCalcColors.Red);
display.DrawText(10, 60, "Hello", PicoCalcColors.White, PicoCalcColors.Black, 2);   // scale 2
```

## `PicoCalcDisplay`

### Construction and setup

| Member | Description |
|---|---|
| `PicoCalcDisplay()` | Hardware SPI, 10 MHz |
| `PicoCalcDisplay(int clockFrequency)` | Hardware SPI at the given clock. Use `62500000`: the firmware caps higher values |
| `PicoCalcDisplay(int clockFrequency, bool softwareSpi)` | `true` = GPIO bit-banging (about 200 bytes/s), kept only as a fallback |
| `static int SpiBusId` | nanoFramework SPI bus id, default `2` (SPI1). Set it **before** creating the display |
| `byte MemoryAccess` | Value sent with MADCTL, default `0x48`. Set before `Init()` if the orientation is wrong |
| `bool InvertColors` | Send "display inversion on", default `true`. Set before `Init()` |
| `bool Use16Bit` | 16 bits per pixel (default) or 18 bits. Set before `Init()` |
| `Init()` | Reset, sleep out, pixel format, orientation, inversion, scroll area, display on |
| `Dispose()` | Releases the pins. The panel resets when they are released |

### Drawing

All drawing is clipped to the screen. Each call costs a few milliseconds (see Performance): draw groups of things
in a canvas rather than many small things directly.

| Member | Description |
|---|---|
| `Width`, `Height` | 320, 320 (constants) |
| `FillScreen(int rgb)` | Fills the whole screen (about 0.15 s) |
| `FillRect(x, y, w, h, rgb)` | Filled rectangle, one transfer |
| `HLine(x, y, length, rgb)`, `VLine(x, y, length, rgb)` | Horizontal and vertical lines (one window each) |
| `DrawRect(x, y, w, h, rgb)` | Rectangle outline (four lines) |
| `DrawPixel(x, y, rgb)` | One pixel (slow, avoid) |
| `DrawText(x, y, text, fg, bg, scale)` | Text with a background color, `scale` = 1, 2, 3... (each font pixel becomes scale × scale pixels). A character that does not fit entirely on the screen is skipped |
| `MeasureText(text, scale)` | Width in pixels of a text (static) |
| `DrawGlyph(x, y, font, offset, fg, bg)` | One 8 × 16 glyph from a font table |
| `DrawGlyphs(x, y, chars, start, count, fg, bg)` | A run of characters in **one** transfer (about three times faster than one by one) |
| `WriteRaw(x, y, w, h, data)` | Sends raw pixel bytes into a window, in the current pixel format |
| `SetWindow(x0, y0, x1, y1)` | Sets the drawing window (low level) |

### Hardware scrolling

The controller has **480 lines of memory** while the panel shows 320. The scroll area has to be declared as
`VSCRDEF(0, 320, 160)`: the three values add up to **480**, not 320. `Init()` does this, and
`SetScrollStart(line)` then moves the first displayed line, which scrolls the whole screen instantly.

| Member | Description |
|---|---|
| `SetVerticalScrollArea(topFixed, scrollArea, bottomFixed)` | `VSCRDEF`. The three values must add up to the controller height (480) |
| `SetScrollStart(line)` | `VSCRSADD`: the panel shows memory line `line` at the top, then wraps around |

### Advanced

| Member | Description |
|---|---|
| `SetPixelFormat(byte colmod)` | `0x55` = 16 bits, `0x66` = 18 bits. `0x61` (3 bits) does **not** work on this panel |
| `HoldChipSelect` | Keeps CS low permanently instead of toggling it around each transfer. Gains about 2 ms per character (9 ms instead of 11 ms); off by default. Only safe when nothing else shares this bus, which is true on the PicoCalc |

## `PicoCalcTextScreen`: a 40 × 20 text layer

Characters are written into an in-memory grid; `Flush()` redraws only the cells that changed. A newline at the
bottom moves the hardware scroll start (plus one blank row to draw) instead of redrawing the screen. Neighbouring
changed cells of a line are sent together.

```csharp
var screen = new PicoCalcTextScreen(display);
screen.ClearPanel();             // first run only: the panel keeps its content between runs
screen.WriteLine("Hello");
screen.Flush();                  // draws what changed
```

| Member | Description |
|---|---|
| `Columns`, `Rows` | 40, 20 (constants) |
| `ForegroundColor`, `BackgroundColor` | Text colors. Set them **before** `ClearPanel()` |
| `CursorVisible`, `CursorX`, `CursorY` | The cursor is drawn as an inverted cell, unless `CursorGlyph` is set |
| `CursorGlyph` | Shape of the cursor: a `byte[]` of at least 16 bytes (16 rows of 8 pixels, the most significant bit is the leftmost pixel), drawn in the text colors instead of the inverted cell. `null` (default) = the inverted cell. The cursor keeps blinking with `CursorVisible`. `PicoCalcConsole.Busy` uses it to show an hourglass |
| `Write(char)`, `Write(string)`, `WriteLine(string)`, `NewLine()` | Text output. Handles `\n`, `\r`, `\b` and tab (every 4 columns); other control characters are ignored |
| `Backspace()` | Moves left and erases the character |
| `SetCursor(x, y)` | Moves the cursor (clamped to the grid) |
| `Clear()` | Clears the grid and homes the cursor (drawn on the next `Flush()`) |
| `ClearPanel()` | Paints the whole panel in one fast fill and marks every cell as drawn. Use it once at the start |
| `Flush()` | Draws the changed cells |
| `Invalidate()` | Forces the next `Flush()` to look at the grid again (after toggling `CursorVisible`, for instance) |
| `ScrollCount` | Number of scrolls since the start |
| `PrepareDirectDrawing()`, `RestoreAfterDirectDrawing()` | For drawing graphics in the middle of a text session, see below |

**`PicoCalcTextScreen` is not thread-safe.** If several threads use it, put a lock around every call, `Flush()`
included. `PicoCalcConsole` does this for you.

### Mixing text and graphics

To draw graphics over a text screen and then get the text back as it was:

```csharp
screen.PrepareDirectDrawing();            // scroll start back to 0: screen coordinates match the display
display.FillScreen(PicoCalcColors.Black);
// ... draw on the display ...
screen.RestoreAfterDirectDrawing();       // wipes the panel, redraws the whole text on the next Flush()
```

With `PicoCalcConsole` use `BeginGraphics()` / `EndGraphics()`, which also pause the console's refresh thread.

## `PicoCalcCanvas`: draw in memory, send at once

A canvas is an RGB565 buffer (`width × height × 2` bytes: 140 × 140 is 39 KB). It is the fast way to draw
anything made of several parts. Drawing is clipped to the canvas.

```csharp
var canvas = new PicoCalcCanvas(140, 140);
canvas.Clear(0x000040);
canvas.DrawLine(0, 0, 139, 139, PicoCalcColors.Red);
canvas.DrawCircle(70, 70, 60, PicoCalcColors.Yellow);
canvas.FillCircle(70, 70, 25, PicoCalcColors.Magenta);
canvas.FillTriangle(20, 120, 50, 80, 80, 120, PicoCalcColors.Cyan);
canvas.DrawText(8, 6, "Canvas", PicoCalcColors.White, 1);

var sprite = new PicoCalcCanvas(32, 32);
sprite.Clear(PicoCalcColors.Magenta);                       // magenta = transparent
sprite.FillCircle(16, 16, 14, PicoCalcColors.Yellow);
canvas.DrawCanvas(sprite, 100, 95, PicoCalcColors.Magenta); // sprite with a transparent color

canvas.DrawTo(display, 90, 170);                            // one transfer to the screen
```

| Member | Description |
|---|---|
| `PicoCalcCanvas(width, height)` | Allocates the buffer |
| `Width`, `Height` | Size |
| `Clear(rgb)` | Fills the canvas |
| `SetPixel`, `GetPixel` | One pixel |
| `HLine`, `VLine`, `DrawLine`, `DrawRect`, `FillRect` | Lines and rectangles |
| `DrawCircle`, `FillCircle` | Circles |
| `DrawTriangle`, `FillTriangle` | Triangles |
| `DrawText(x, y, text, rgb, scale)` | Text with a transparent background |
| `DrawText(x, y, text, rgb, bgRgb, scale)` | Text with a background color |
| `DrawCanvas(source, x, y)` | Copies another canvas (opaque) |
| `DrawCanvas(source, x, y, transparentRgb)` | Copies another canvas, skipping pixels of the transparent color |
| `DrawTo(display, x, y)` | Sends the whole canvas to the screen at `(x, y)` |

**Memory:** a full-screen canvas (320 × 320 × 2 = 200 KB) does not fit in the RAM of most Picos. Use zones or
sprites. Colors are stored with 5/6/5 bits.

## Performance

The cost is the **number of SPI calls**, not the amount of data: each `SpiDevice.Write` has a fixed overhead of about
1.5 ms, while a 39 KB block goes out in 11 ms (about 3.5 MB/s). Measured on an RP2040, hardware SPI, 16-bit mode:

| Operation | Time |
|---|---|
| Full-screen clear | about 0.15 s |
| One 8 × 16 character, drawn alone | about 9 ms (6 SPI calls) |
| 40 characters of a line, drawn together (`DrawGlyphs`) | about 125 ms (364 ms one by one) |
| 14 direct shape or text calls | about 400 ms (25 ms per call) |
| Draw into a 140 × 140 canvas (lines, circles, text...) | about 133 ms |
| Send that canvas (39 KB) to the screen | about 11 ms |
| Scrolling | instantaneous (hardware), then new lines are drawn |

## Pitfalls found on the way

1. **SPI bus ids are 1-based in `SpiDevice.Create`** on RP2040 and RP2350: id 1 = SPI0 (the SD card pins), id 2 = SPI1
   (the display). With id 1 the screen stays black even though `Write` returns without error. Id 0 throws
   `ArgumentException`. **`SpiDevice.GetBusInfo` is 0-based** (ids 0 and 1 work, 2 and 3 throw): the two are inconsistent
   (reported to the nanoFramework team).
2. **The firmware caps the SPI clock at 62.5 MHz** (the bus maximum). Lower clocks are far slower than expected (1 MHz
   asked: 5 KB in about 12 s), so always use `62500000`.
3. **Software SPI** works but only reaches about 200 bytes/s: a full-screen clear takes about 17 minutes.
4. **Pixel formats:** 16-bit (RGB565) and 18-bit work; the 3-bit format does not.
5. **The panel keeps its memory** between program runs while it stays powered, so random pixels can show on startup:
   `ClearPanel()` (text screen) or `FillScreen()` once at the start.
6. **VSCRDEF must add up to 480**, the controller memory height, not 320. With 320, random pixels scroll in.
7. **`Dispose()` resets the panel**: releasing the pins resets the controller.
8. Drawing many small things directly is slow because of the fixed cost per SPI call: group them in a canvas, or use
   `DrawGlyphs` for text runs.

Development notes: do not install batteries in the PicoCalc while the Pico is powered from USB (back-feeding
risk). Stop the debugger before deploying; if deployment hangs at the erase step, unplug and replug the USB cable.

## Font license

The glyph table comes from GNU Unifont, dual-licensed under the GPL v2 or later with the Font Embedding Exception,
or the SIL Open Font License 1.1. Keep the notice at the top of `PicoCalcFont.cs`.
