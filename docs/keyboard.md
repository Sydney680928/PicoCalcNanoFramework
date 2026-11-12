# Keyboard (`PicoCalcKeyboard`)

Driver for the PicoCalc keyboard. The keys are scanned by an STM32 co-processor that exposes a small FIFO over
I2C; this driver polls that FIFO on a background thread and raises `KeyDown` / `KeyUp` events with the key
code, the character, and the modifier state.

- Source file: `PicoCalcKeyboard.cs` (also contains `PicoCalcKeys`, `PicoCalcKeyEventArgs`, the event delegate)
- NuGet packages: `nanoFramework.System.Device.I2c`
- Needs nothing else: this driver is independent of the display, the console and the other drivers

## Hardware

| | |
|---|---|
| Bus | I2C1: SDA = GP6, SCL = GP7, nanoFramework bus id `1` |
| Slave address | `0x1F` |
| Protocol | write register `0x09`, read 2 bytes `[state, code]` |
| `[0, 0]` | FIFO empty |
| `state` | `1` = pressed (repeated while the key is held: auto-repeat), `2` = hold (modifier keys), `3` = released |

The STM32 already applies **Shift** and **Caps Lock** to printable keys (`a` becomes `A`, `1` becomes `!`). It does
**not** handle **Ctrl**: the driver does it (see below). The layout is US QWERTY; the codes were captured on a real
PicoCalc.

The keyboard MCU also drives the screen backlight: `Alt + ,` and `Alt + .` change the brightness. This is
handled by the MCU, not by this driver.

## Quick start

```csharp
using System.Threading;
using PicoCalc;

var keyboard = new PicoCalcKeyboard();      // I2C bus 1, address 0x1F
keyboard.KeyDown += (sender, e) =>
{
    if (e.HasChar)
    {
        Debug.WriteLine("char: " + e.Char);
    }
    else
    {
        Debug.WriteLine("key code: 0x" + e.Code.ToString("X2"));
    }
};
keyboard.Start();                           // starts the polling thread

Thread.Sleep(Timeout.Infinite);
```

## API

### `PicoCalcKeyboard`

| Member | Description |
|---|---|
| `PicoCalcKeyboard()` | Bus id 1, address `0x1F` (the PicoCalc values) |
| `PicoCalcKeyboard(int busId, int address)` | Other values, if your board differs |
| `event KeyDown` | Raised when a key is pressed, and again on every auto-repeat |
| `event KeyUp` | Raised when a key is released |
| `ShiftPressed`, `CtrlPressed`, `AltPressed` | Current state of the modifier keys |
| `Start()` | Starts the polling thread (every 10 ms, up to 16 events drained per cycle) |
| `Stop()` | Stops the thread |
| `Dispose()` | Stops and releases the I2C device |

Events are raised **on the polling thread**: keep handlers short, and never block in them. To hand keys to
another thread, queue them (this is what `PicoCalcConsole` does).

Modifier keys (Shift, Ctrl, Alt) are **tracked by the driver but never raised** as events; their state is reported
in the `Shift`, `Ctrl` and `Alt` properties of the event of the next key.

### `PicoCalcKeyEventArgs`

| Property | Description |
|---|---|
| `Code` | Raw key code (`byte`), see `PicoCalcKeys` |
| `Char` | The character for printable keys, or `Backspace` (`\b`), `Tab`, `Enter` (`\n`); `'\0'` otherwise |
| `HasChar` | `true` when `Char` is not `'\0'` |
| `Shift`, `Ctrl`, `Alt` | Modifier state when the event was raised |
| `IsRepeat` | `true` for auto-repeat events (key still held) |

With **Ctrl** held, the letters `a`..`z` give the control characters 1..26 in `Char` (Ctrl+C gives `(char)3`).
This is how `PicoCalcConsole` detects Ctrl+C.

### `PicoCalcKeys` (key codes)

| Key | Constant | Code |
|---|---|---|
| Backspace, Tab, Enter | `Backspace`, `Tab`, `Enter` | `0x08`, `0x09`, `0x0A` |
| Esc | `Esc` | `0xB1` |
| Arrows | `Left`, `Up`, `Down`, `Right` | `0xB4`, `0xB5`, `0xB6`, `0xB7` |
| Break (Shift + Esc) | `Break` | `0xD0` |
| Insert (Alt + I) | `Insert` | `0xD1` |
| Home (Shift + Tab) | `Home` | `0xD2` |
| Delete | `Delete` | `0xD4` |
| End (Shift + Del) | `End` | `0xD5` |
| Caps Lock | `CapsLock` | `0xC1` |
| F1 to F5 | `F1`..`F5` | `0x81`..`0x85` |
| F6 to F10 (Shift + F1..F5) | `F6`..`F10` | `0x86`..`0x89`, `0x90` |
| Modifiers (not raised) | `Alt`, `ShiftLeft`, `ShiftRight`, `Ctrl` | `0xA1`, `0xA2`, `0xA3`, `0xA5` |

Several keys of a normal keyboard are reached with a modifier on this one: Break is **Shift + Esc**, Home is
**Shift + Tab**, End is **Shift + Del**, Insert is **Alt + I**.

## Example: reading keys with a queue

```csharp
var keys = new Queue();
var arrived = new AutoResetEvent(false);

var keyboard = new PicoCalcKeyboard();
keyboard.KeyDown += (s, e) => { lock (keys) { keys.Enqueue(e); } arrived.Set(); };
keyboard.Start();

while (true)
{
    arrived.WaitOne();
    lock (keys)
    {
        while (keys.Count > 0)
        {
            var e = (PicoCalcKeyEventArgs)keys.Dequeue();
            if (e.Code == PicoCalcKeys.Up) { /* ... */ }
            else if (e.HasChar && !e.Ctrl) { /* typed character e.Char */ }
        }
    }
}
```

## Known limits and notes

- `KeyUp` and `KeyDown` pair up by key code. If you let go of Shift before the letter, the release can carry the
  other letter case: the driver clears both cases internally so a key is never stuck.
- Only the US layout is supported.
- A failed I2C transfer is ignored: the driver just tries again 10 ms later.
