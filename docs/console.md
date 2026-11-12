# Console (`PicoCalcConsole`)

A standalone text console for the PicoCalc: the keyboard in, the 40 × 20 text screen out, with line editing and
history. It knows nothing about any application: use it for a REPL, a shell, a game menu, logs. It was designed as a
separate piece so that other projects can use it as well.

- Source file: `PicoCalcConsole.cs`
- Depends on the [display](display.md) (`PicoCalcDisplay`, `PicoCalcTextScreen`, `PicoCalcFont`) and the
  [keyboard](keyboard.md) (`PicoCalcKeyboard`) drivers: add their source files, or their packages
- NuGet packages: the ones of those two drivers (Gpio, Spi, I2c, Threading)

The screen is refreshed by an internal thread (every 20 ms; the cursor blinks every 500 ms): you do **not** call
`Flush()` and you do not need any lock. `Write` and `WriteLine` can be called from any thread.
`ReadLine` and `ReadKey` block the calling thread.

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
| `ForegroundColor`, `BackgroundColor` | Text colors (`0xRRGGBB`). Set them **before** `Start()` |
| `Start()` | Initialises the display, clears it, starts the keyboard and the refresh thread |
| `Dispose()` | Stops everything and releases the display and the keyboard (this resets the panel) |
| `Columns`, `Rows` | 40, 20 |

### Output

| Member | Description |
|---|---|
| `Write(string)`, `Write(char)`, `WriteLine(string)`, `WriteLine()` | Text output at the cursor. `\n`, `\r`, `\b` and tab are handled |
| `Clear()` | Clears the screen and homes the cursor |
| `SetCursor(x, y)` | Moves the cursor (column, row), clamped to the 40 × 20 grid |
| `CursorVisible` | Shows or hides the cursor |

### Input

| Member | Description |
|---|---|
| `ReadLine(string prompt)` | Writes the prompt, then calls `ReadLine()` |
| `ReadLine()` | Reads a line with editing, see below. Returns `null` if Break or Ctrl+C is pressed (the line is abandoned) |
| `ReadKey()` | Waits for the next key press (auto-repeat included) and returns a `PicoCalcKeyEventArgs`. Modifier keys alone are not reported |
| `KeyAvailable` | `true` if a key event is waiting for `ReadKey()` |
| `ClearKeys()` | Forgets the key presses waiting for `ReadKey()` |
| `event BreakPressed` | Raised (on the keyboard thread) when Break (Shift+Esc) or Ctrl+C is pressed, **whether or not** `ReadLine` is waiting. Typical use: stop a running script |

**Line editing in `ReadLine`:** Left and Right move the cursor, Home and End jump to the ends of the line,
Backspace and Delete erase, Up and Down browse the history (the last 20 lines), Esc clears the line, Enter validates.
A line is limited to 200 characters. Characters typed in the middle of the line are inserted.

Key codes (`PicoCalcKeys`) and what a `PicoCalcKeyEventArgs` contains are described in the [keyboard doc](keyboard.md).

### Graphics mode

The console can hand the screen to your own drawing code and give it back:

| Member | Description |
|---|---|
| `Display` | The `PicoCalcDisplay`, for direct drawing |
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
- **Several threads:** `Write`, `WriteLine`, `Clear` and `SetCursor` are safe from any thread. Only one thread should
  call `ReadLine` or `ReadKey` at a time.
- **Memory:** the text grid and the font use only a few KB. A `PicoCalcCanvas` used in graphics mode adds its own
  `width × height × 2` bytes.
- **Font:** characters from U+0020 to U+00FF are drawn (accents included). Other characters are not available.
- **Screen between runs:** the panel keeps its content while powered; `Start()` clears it.
