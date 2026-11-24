# Display (`PicoCalcDisplay`, text screen, canvas, font)

Driver for the PicoCalc 4" IPS display (320 × 320 pixels). It has three layers that you can use separately:

1. **`PicoCalcDisplay`**: the panel itself (init, fills, rectangles, lines, text, raw pixels, hardware scroll)
2. **`PicoCalcTextScreen`**: a 40 × 20 character grid with hardware scrolling, only changed cells are redrawn
3. **`PicoCalcCanvas`**: an in-memory RGB565 surface for lines, circles, triangles, sprites, sent to the screen in one transfer

The font (`PicoCalcFont`) is an 8 × 16 table (GNU Unifont, U+0020 to U+00FF, accents included).

- Source files: `PicoCalcDisplay.cs` (also contains `PicoCalcColors`), `PicoCalcTextScreen.cs` (also contains `PicoCalcPalette`), `PicoCalcCanvas.cs`, `PicoCalcFont.cs`, `PicoCalcSymbols.cs`
- NuGet packages: `nanoFramework.System.Device.Gpio`, `nanoFramework.System.Device.Spi`
- Colors are `int` values `0xRRGGBB` for `PicoCalcDisplay` and `PicoCalcCanvas` (see `PicoCalcColors`: Black, White, Red, Green, Blue, Yellow, Cyan, Magenta). The **text screen** works differently: it uses a palette of 16 colors, see [Colors](#colors-of-the-text-screen)

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

All drawing is clipped to the screen. Each call costs several milliseconds (see Performance): draw groups of things
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
| `DrawGlyphs(x, y, chars, start, count, fg, bg)` | A run of characters in **one** transfer (several times faster than one by one) |
| `WriteRaw(x, y, w, h, data)` | Sends raw pixel bytes into a window, in the current pixel format |
| `SetWindow(x0, y0, x1, y1)` | Sets the drawing window (low level). With `WindowCache`, a column or row range that did not change is not sent again |

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
| `HoldChipSelect` | Keeps CS low permanently instead of toggling it around each transfer (two slow GPIO writes saved per transfer, about 10 %). **On by default** since 0.3. Only safe when nothing else shares this bus, which is true on the PicoCalc; set it to `false` to toggle CS around every transfer as in 0.2 |
| `WindowCache` | Does not send again the column range or the row range of the window when it has not changed since the previous drawing call (two SPI transfers saved per range). **On by default** since 0.3. Set it to `false` only if something else changes the controller's window behind the driver's back |

## `PicoCalcTextScreen`: a 40 × 20 text layer

Characters are written into an in-memory grid; every cell has its own text color and background color. `Flush()` redraws only the cells that changed. A newline at the
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
| `Foreground`, `Background` | Text color and background color of what is written from now on: an index in the palette (0 to 15, see `PicoCalcPalette`). Can be changed at any time, see [Colors](#colors-of-the-text-screen) |
| `GetPaletteColor(index)`, `SetPaletteColor(index, rgb)` | Reads or changes the `0xRRGGBB` value of a palette entry |
| `NearestColorIndex(rgb)` | Index of the palette entry nearest to an RGB color |
| `PaletteSize` | 16 (constant) |
| `CursorVisible`, `CursorX`, `CursorY` | The cursor is drawn as an inverted cell, unless `CursorGlyph` is set |
| `CursorGlyph` | Shape of the cursor: a `byte[]` of at least 16 bytes (16 rows of 8 pixels, the most significant bit is the leftmost pixel), drawn in the text colors instead of the inverted cell. `null` (default) = the inverted cell. The cursor keeps blinking with `CursorVisible`. `PicoCalcConsole.Busy` uses it to show an hourglass |
| `Write(char)`, `Write(string)`, `WriteLine(string)`, `NewLine()` | Text output. Handles `\n`, `\r`, `\b` and tab (every 4 columns); other control characters are ignored |
| `Backspace()` | Moves left and erases the character |
| `SetCursor(x, y)` | Moves the cursor (clamped to the grid) |
| `Clear()` | Clears the grid with the current background color and homes the cursor (drawn on the next `Flush()`) |
| `ClearPanel()` | Paints the whole panel with the current background color in one fast fill and marks every cell as drawn. Use it once at the start |
| `Flush()` | Draws the changed cells |
| `Invalidate()` | Forces the next `Flush()` to look at the grid again (after toggling `CursorVisible`, for instance) |
| `ScrollCount` | Number of scrolls since the start |
| `PrepareDirectDrawing()`, `RestoreAfterDirectDrawing()` | For drawing graphics in the middle of a text session, see below |

**`PicoCalcTextScreen` is not thread-safe.** If several threads use it, put a lock around every call, `Flush()`
included. `PicoCalcConsole` does this for you.

### Colors of the text screen

Every cell of the grid keeps **its own** text color and background color, taken from a palette of 16 colors. The
palette has the same names and the same numbers as the .NET `ConsoleColor` enumeration (`PicoCalcPalette`), with the
default colors of the Windows console:

| Index | Name | `0xRRGGBB` | Index | Name | `0xRRGGBB` |
|---|---|---|---|---|---|
| 0 | `Black` | `000000` | 8 | `DarkGray` | `767676` |
| 1 | `DarkBlue` | `0037DA` | 9 | `Blue` | `3B78FF` |
| 2 | `DarkGreen` | `13A10E` | 10 | `Green` | `16C60C` |
| 3 | `DarkCyan` | `3A96DD` | 11 | `Cyan` | `61D6D6` |
| 4 | `DarkRed` | `C50F1F` | 12 | `Red` | `E74856` |
| 5 | `DarkMagenta` | `881798` | 13 | `Magenta` | `B4009E` |
| 6 | `DarkYellow` | `C19C00` | 14 | `Yellow` | `F9F1A5` |
| 7 | `Gray` | `CCCCCC` | 15 | `White` | `F2F2F2` |

The default text color is `White` and the default background is `Black`.

```csharp
screen.Foreground = PicoCalcPalette.Red;
screen.Write("error ");
screen.Foreground = PicoCalcPalette.White;      // changes whenever you want
screen.WriteLine("(see above)");

screen.Background = PicoCalcPalette.DarkBlue;   // text written from now on is on dark blue
screen.WriteLine(" Title ");
```

- `Foreground` and `Background` apply to what is written **afterwards**: the text that is already on the screen
  keeps its colors (and keeps them when it scrolls).
- A blank cell (a space) only has a background color.
- `Clear()` and `ClearPanel()` erase with the **current** background color: set `Background`, then `Clear()` to
  get a screen of that color.
- The cursor (an inverted cell) uses the colors of the cell under it; on a blank cell it takes the current text color.
- `SetPaletteColor(index, rgb)` changes the color of a palette entry: what is displayed with that entry is redrawn
  with the new color at the next `Flush()`. For instance `screen.SetPaletteColor(PicoCalcPalette.Green, 0x00FF00)` for a
  purer green. The palette is only used by the text screen; `PicoCalcDisplay` and `PicoCalcCanvas` keep taking RGB colors.
- To use an RGB color with the text screen, get its index with `NearestColorIndex(rgb)` (the exact entry if there is
  one, else the nearest), or put it into the palette with `SetPaletteColor`.

### User-defined symbols

The codes U+007F and U+0080 to U+009F (33 codes) are blank in the font: `PicoCalcSymbols` lets you give them a shape
of your own (a heart, a battery gauge, a game sprite, a box-drawing character...). A symbol is then written like any
character: `screen.Write((char)0x80)`, `"Life: \u0080\u0080\u0080"`, or `display.DrawText(...)`.

```csharp
PicoCalcSymbols.Define((char)0x80, new string[]
{
    "........",
    ".##..##.",
    "########",
    "########",
    "########",
    ".######.",
    "..####..",
    "...##..."
});
```

| Member | Description |
|---|---|
| `First`, `Last`, `Count` | `(char)0x7F`, `(char)0x9F`, 33 |
| `IsSymbol(char)` | `true` if the code can be redefined |
| `Define(char code, byte[] rows)` | 8 bytes (8 rows of 8 pixels; each row is doubled to fill the 16 rows of the glyph) or 16 bytes. The most significant bit is the leftmost pixel |
| `Define(char code, string[] pattern)` | 8 or 16 strings of 8 characters, `#` = pixel on, any other character = off |
| `Clear(char code)`, `ClearAll()` | Back to blank |
| `Version` | Incremented at every change |

- Redefining a symbol is visible at once on the **text screen**: the cells that show it are redrawn at the next
  `Flush()`. What `DrawText` or `DrawGlyph` already drew on the display is not changed.
- A wrong code or a wrong size throws `ArgumentOutOfRangeException` / `ArgumentException`.
- The symbols are shared by the whole program (they are static) and are blank again at every start.

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

Two things cost time: the **number of SPI calls** (each `SpiDevice.Write` has a fixed overhead of about 1.5 to 2 ms, a window needs several of them) and the **managed code** that prepares the pixels (an `Array.Copy` costs about 0.09 ms on the interpreter). The bytes themselves are almost free: the bus carries several MB/s.

Measured on an RP2040 (Pico W), hardware SPI at 62.5 MHz, 16-bit mode, debugger attached. The first column is version 0.2 (chip select toggled, window always sent), the second is 0.3 (defaults: `HoldChipSelect` and `WindowCache` on):

| Operation | 0.2 | 0.3 |
|---|---|---|
| One 8 × 16 character, drawn alone | 12.5 ms | 6.0 ms |
| Strip 8 × 320 pixels (`WriteRaw`) | 11.7 ms | 5.4 ms |
| `FillRect` 8 × 16 | 13.4 ms | 6.9 ms |
| 10 characters together (`DrawGlyphs`) | 25.5 ms | 16.6 ms |
| A line of 39 characters of text (`DrawGlyphs`) | 64 ms | 37 ms |
| Full-screen clear (`FillScreen`) | about 0.15 s | about 0.15 s |
| Redraw of a whole text screen (780 cells, every cell changed) | 1.38 s | 1.24 s |
| Scrolling | instantaneous (hardware), then the new line is drawn | |

What to remember:

- A character drawn **alone** costs the same as a strip of 40 pixels: group things (`DrawGlyphs` for text, a canvas for shapes).
- A run of characters costs about 1.5 ms per character, mostly the managed code that builds the pixel block, not the transfer. In a text line, spaces and the empty rows of the glyphs are skipped.
- An ordinary console line is drawn in less than 45 ms; the whole text screen is only redrawn when leaving graphics mode (`EndGraphics()`), in about 1.2 s.

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
