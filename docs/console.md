# Console (`PicoCalcConsole`)

A standalone text console for the PicoCalc: the keyboard in, the 40 × 20 text screen out, with line editing and
history. It knows nothing about any application: use it for a REPL, a shell, a game menu, logs. It was designed as a
separate piece so that other projects can use it as well.

- Source file: `PicoCalcConsole.cs`
- Needs `PicoCalcNano.Display` 0.3 or later when you use the packages (colors, `PicoCalcPalette`)
- Depends on the [display](display.md) (`PicoCalcDisplay`, `PicoCalcTextScreen`, `PicoCalcFont`) and the
  [keyboard](keyboard.md) (`PicoCalcKeyboard`) drivers: add their source files, or their packages
- NuGet packages: the ones of those two drivers (Gpio, Spi, I2c, plus Collections for the key queue)

> **The console owns the display and the keyboard.** `PicoCalcConsole` creates its own `PicoCalcDisplay` and
> `PicoCalcKeyboard`. Do **not** create either of them in the same program: the pins are already open and the console
> constructor throws an `InvalidOperationException` (`GpioController.InternalOpenPin`). To draw, use the console's
> `Display` property, and the `Keyboard` property for the battery. If you need a display or a keyboard on its own, `Dispose()` it before creating the console (and
> create a new one after `console.Dispose()`).

The screen is refreshed by an internal thread (every 20 ms; the cursor blinks every 500 ms): you do **not** call
`Flush()` and you do not need any lock. `Write` and `WriteLine` can be called from any thread.
`ReadLine` and `ReadKey` block the calling thread; another thread can end a waiting `ReadLine` with `CancelRead()`.

## Quick start

```csharp
var console = new PicoCalcConsole();     // hardware SPI, 62.5 MHz
console.Start();                         // init display, clear it, start keyboard + refresh thread

console.WriteLine("Hello PicoCalc");
while (true)
{
    string line = console.ReadLine("> ");   // null if Break (Shift+Esc) or Ctrl+C was pressed
    if (line == null) continue;
    console.WriteLine("You typed: " + line);
}
```

Give the debugger time to take over before starting: a `Thread.Sleep(3000)` at the start of `Main` avoids most
deployment problems.

## API

### Setup

| Member | Description |
|---|---|
| `PicoCalcConsole()` | Hardware SPI at 62.5 MHz |
| `PicoCalcConsole(int spiFrequency)` | Another SPI clock (the firmware caps anything above 62.5 MHz) |
| `Start()` | Initialises the display, clears it, starts the keyboard and the refresh thread |
| `Dispose()` | Stops everything and releases the display and the keyboard (this resets the panel) |
| `Columns`, `Rows` | 40, 20 |

### Output

| Member | Description |
|---|---|
| `Write(string)`, `Write(char)`, `WriteLine(string)`, `WriteLine()` | Text output at the cursor. `\n`, `\r`, `\b` and tab are handled |
| `Clear()` | Clears the screen (with the current background color) and homes the cursor |
| `SetCursor(x, y)` | Moves the cursor (column, row), clamped to the 40 × 20 grid |
| `CursorVisible` | Shows or hides the cursor |
| `Foreground`, `Background` | Text color and background color of what is written from now on: an index in the palette of 16 colors (0 to 15, see `PicoCalcPalette` and [Colors](#colors)). Can be changed at any time, also before `Start()` |
| `GetPaletteColor(index)`, `SetPaletteColor(index, rgb)`, `NearestColorIndex(rgb)`, `PaletteSize` | The palette: read or change the `0xRRGGBB` value of an entry, find the entry nearest to an RGB color. Need `Start()` to have been called |
| `Busy` | `true` shows a blinking **hourglass** as the cursor instead of the block (a program is running); `false` (default) the usual block. Can be set from any thread, also before `Start()` |

### Input

| Member | Description |
|---|---|
| `ReadLine(string prompt)` | Writes the prompt, then calls `ReadLine()` |
| `ReadLine()` | Reads a line with editing, see below. Returns `null` if Break or Ctrl+C is pressed (the line is abandoned), or if another thread called `CancelRead()` |
| `CancelRead()`, `CancelRead(int timeoutMs)` | Called from **another thread**: ends the `ReadLine` that is waiting, see [Ending a ReadLine from another thread](#ending-a-readline-from-another-thread). Waits up to 1 s (or `timeoutMs`) until `ReadLine` has really ended; returns `false` only if that wait timed out |
| `ReadCancelled` | `true` if the last `ReadLine` returned `null` because of `CancelRead()` (`false` for Enter or Break) |
| `ReadKey()` | Waits for the next key press (auto-repeat included) and returns a `PicoCalcKeyEventArgs`. Modifier keys alone are not reported |
| `KeyAvailable` | `true` if a key event is waiting for `ReadKey()` |
| `ClearKeys()` | Forgets the key presses waiting for `ReadKey()` |
| `event BreakPressed` | Raised (on the keyboard thread) when Break (Shift+Esc) or Ctrl+C is pressed, **whether or not** `ReadLine` is waiting. Typical use: stop a running script |

**Line editing in `ReadLine`:** Left and Right move the cursor, Home and End jump to the ends of the line,
Backspace and Delete erase, Up and Down browse the history (the last 20 lines), Esc clears the line, Enter validates.
A line is limited to 200 characters. Characters typed in the middle of the line are inserted.

Key codes (`PicoCalcKeys`) and what a `PicoCalcKeyEventArgs` contains are described in the [keyboard doc](keyboard.md).

### Ending a ReadLine from another thread

`ReadLine` normally ends with Enter or Break. When something else decides that the input must stop (for example an
order received from a PC while the console is waiting for a command), call `CancelRead()` **from another thread**:

- `ReadLine` returns `null` and `ReadCancelled` is `true`;
- the text typed so far stays on the screen and the cursor goes to the next line, so that the output of the program
  you start starts on a clean line (without this, the input line keeps redrawing itself where it started, over the
  output);
- the line is not added to the history;
- `CancelRead()` returns once `ReadLine` has really ended, so you can write to the console right after;
- if no `ReadLine` is waiting, it does nothing.

```csharp
// Thread that receives the external order
console.CancelRead();           // the console leaves the input mode
RunProgram();                   // its output goes to the console normally
console.ClearKeys();            // forget the keys typed meanwhile

// Main loop
string line = console.ReadLine("> ");
if (line == null && console.ReadCancelled)
{
    // wait until the external program has finished, then loop: ReadLine gives the input back
}
```

Do not call `CancelRead()` from the thread that is inside `ReadLine`. If the user presses Enter at the very moment of
the call, `ReadLine` returns that line normally and `ReadCancelled` stays `false`.

**Watch out for the timing.** If the order arrives while your main loop is not inside `ReadLine` (it is processing a
command), `CancelRead()` finds nothing to cancel, and the loop may call `ReadLine` again while the external program
runs. Keep a flag in your application ("external run in progress"), set it **before** calling `CancelRead()` and test
it in the main loop before every `ReadLine`.

To run a blocking action from an event handler without blocking the event, post it to a worker thread (a queue that
one thread empties) instead of running it in the handler.

### Colors

Every character of the screen has its own text color and background color, chosen among the 16 entries of a palette
(`PicoCalcPalette`: the names and numbers of the .NET `ConsoleColor` enumeration, with the default colors of the
Windows console; the table is in the [display doc](display.md#colors-of-the-text-screen)). Change `Foreground` and
`Background` whenever you want: they apply to what `Write` and `WriteLine` output afterwards.

```csharp
console.Foreground = PicoCalcPalette.Red;
console.WriteLine("Error: file not found");
console.Foreground = PicoCalcPalette.White;
console.WriteLine("Back to white");

console.Background = PicoCalcPalette.DarkBlue;
console.Clear();                       // the whole screen is dark blue
```

- The text that is already on the screen keeps its colors, also when it scrolls.
- `Clear()` erases with the current background color.
- The line being typed in `ReadLine` is drawn with the colors current **while it is typed**: set the colors of the
  input before calling `ReadLine`, and set them back afterwards.
- An index outside 0 to 15 throws `ArgumentOutOfRangeException`.
- `SetPaletteColor(index, rgb)` changes the color of a palette entry (all the text displayed with it changes at once);
  `NearestColorIndex(rgb)` gives the entry nearest to an RGB color.

### Busy and idle cursor

The cursor tells the user what the console is doing: the blinking block means "waiting for input", the blinking
hourglass means "busy". Switch between the two with `Busy`:

```csharp
console.Busy = true;     // a program is running
RunProgram();
console.Busy = false;    // back to the input prompt
```

`Busy` only changes the shape of the cursor: it does not stop `ReadLine`. The hourglass follows the cursor as the
program writes, and blinks like the block. It is drawn with the `CursorGlyph` of the text screen, see the
[display doc](display.md#picocalctextscreen-a-40--20-text-layer).

### Graphics mode

The console can hand the screen to your own drawing code and give it back:

| Member | Description |
|---|---|
| `Display` | The `PicoCalcDisplay`, for direct drawing |
| `Keyboard` | The `PicoCalcKeyboard` owned by the console, for the features that do not go through the console (battery, see below) |
| `BeginGraphics()` | Pauses the text refresh and puts the scroll start back to 0: screen coordinates 0..319 match the display |
| `EndGraphics()` | Wipes the panel and redraws the text screen exactly as it was (prompt included) |

```csharp
console.BeginGraphics();
console.Display.FillScreen(PicoCalcColors.Black);
console.Display.FillRect(50, 50, 100, 60, PicoCalcColors.Red);
console.Display.DrawText(10, 302, "Press a key", PicoCalcColors.White, PicoCalcColors.Black, 1);

console.ClearKeys();      // forget keys typed before (like the Enter that started the command)
console.ReadKey();        // the picture stays until a key is pressed
console.EndGraphics();
```

## Example: a small command interpreter

```csharp
var console = new PicoCalcConsole();
console.Start();
console.WriteLine("PicoCalc console. Type help.");

while (true)
{
    string line = console.ReadLine("> ");
    if (line == null) continue;
    line = line.Trim();

    if (line == "help") console.WriteLine("help  clear  echo <text>");
    else if (line == "clear") console.Clear();
    else if (line.StartsWith("echo ")) console.WriteLine(line.Substring(5));
    else if (line.Length > 0) console.WriteLine("Unknown command: " + line);
}
```

The demo in `Program.cs` goes further: `calc`, `lines <n>` (scrolling test) and `graph` (a screen of drawing tests,
shown until a key is pressed).

## Notes and limits

- **Break while a program runs:** subscribe to `BreakPressed` to stop long work. The event comes from the keyboard
  thread, so just set a flag in the handler.
- **Several threads:** `Write`, `WriteLine`, `Clear`, `SetCursor`, `Foreground` and `Background` are safe from any thread. Only one thread should
  call `ReadLine` or `ReadKey` at a time. `CancelRead()` is the one input method meant to be called from another
  thread, and it only affects `ReadLine`, not `ReadKey`.
- **Memory:** the text grid and the font use only a few KB. A `PicoCalcCanvas` used in graphics mode adds its own
  `width × height × 2` bytes.
- **Font:** characters from U+0020 to U+00FF are drawn (accents included). Other characters are not available. The
  codes U+007F to U+009F are blank until you define them with `PicoCalcSymbols`, see the
  [display doc](display.md#user-defined-symbols).
- **Battery:** `console.Keyboard.ReadBattery(out percent, out charging)` (see the [keyboard doc](keyboard.md#battery)).
  Only use the reading members (`ReadBattery`, `BatteryPercent`, `IsCharging`, `ReadBatteryRaw`): do not call `Start()`,
  `Stop()` or `Dispose()` on this keyboard and do not subscribe to its `KeyDown` event, the console relies on them.
- **One owner per pin:** the console and a separate `PicoCalcDisplay` or `PicoCalcKeyboard` cannot live together in
  one program, see the note at the top. The same goes for two consoles.
- **Screen between runs:** the panel keeps its content while powered; `Start()` clears it.
