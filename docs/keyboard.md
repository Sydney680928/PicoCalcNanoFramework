# Keyboard (`PicoCalcKeyboard`)

Driver for the PicoCalc keyboard. The keys are scanned by an STM32 co-processor that exposes a small FIFO over
I2C; this driver polls that FIFO on a background thread and raises `KeyDown` / `KeyUp` events with the key
code, the character, and the modifier state.

- Source file: `PicoCalcKeyboard.cs` (also contains `PicoCalcKeys`, `PicoCalcKeyEventArgs`, the event delegate)
- NuGet packages: `nanoFramework.System.Device.I2c`
- Needs nothing else: this driver is independent of the display, the console and the other drivers
- Do not create a `PicoCalcKeyboard` in a program that also uses [`PicoCalcConsole`](console.md): the console has its
  own keyboard and the two would compete for the same I2C device. `Dispose()` releases it.

## Hardware

| | |
|---|---|
| Bus | Second I2C controller: SDA = GP6, SCL = GP7. nanoFramework bus id `1` or `2` depending on the firmware (see below) |
| Slave address | `0x1F` |
| Protocol | write register `0x09`, read 2 bytes `[state, code]` |
| `[0, 0]` | FIFO empty |
| `state` | `1` = pressed (repeated while the key is held: auto-repeat), `2` = hold (modifier keys), `3` = released |
| Battery | write register `0x0B`, read 2 bytes: the **second** one holds the level (bits 0-6, percent) and the charging flag (bit 7), see [Battery](#battery) |

The STM32 already applies **Shift** and **Caps Lock** to printable keys (`a` becomes `A`, `1` becomes `!`). It does
**not** handle **Ctrl**: the driver does it (see below). The layout is US QWERTY; the codes were captured on a real
PicoCalc.

The keyboard MCU also drives the screen backlight: `Alt + ,` and `Alt + .` change the brightness. This is
handled by the MCU, not by this driver.

## I2C bus id and firmware versions

The numbering of the I2C buses of the Pico targets changed in the nanoFramework firmware (nf-interpreter pull
request #3620): the same physical bus (GP6/GP7) is bus id `1` on older firmware and `2` on newer firmware.

The default constructor handles both: it tries bus id 1, then 2, and keeps the first one where the keyboard
answers. The id that was chosen is available in `BusId`. It was tested on both numberings. If you want to skip the
detection, pass the id yourself: `new PicoCalcKeyboard(2, 0x1F)`.

## Quick start

```csharp
using System.Threading;
using PicoCalcNano;

var keyboard = new PicoCalcKeyboard();      // detects the I2C bus, address 0x1F
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
| `PicoCalcKeyboard()` | Detects the I2C bus (see below), address `0x1F`. Throws `InvalidOperationException` if no keyboard answers |
| `PicoCalcKeyboard(int busId, int address)` | Forces the bus id and the address (pass `PicoCalcKeyboard.AutoDetect` as bus id to detect it) |
| `BusId` | I2C bus id actually used (1 or 2) |
| `event KeyDown` | Raised when a key is pressed, and again on every auto-repeat |
| `event KeyUp` | Raised when a key is released |
| `ShiftPressed`, `CtrlPressed`, `AltPressed` | Current state of the modifier keys |
| `ReadBattery(out int percent, out bool charging)` | Battery level (0 to 100) and charging state. Returns `false` (and `percent` = -1) if the value is not available |
| `BatteryPercent` | Battery level, 0 to 100, or -1 if it is not available |
| `IsCharging` | `true` while the battery is being charged |
| `ReadBatteryRaw()` | The raw register value (0 to 255; 0 = not available), or -1 on an I2C error |
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

## Battery

The keyboard MCU also measures the battery. The level and the charging state are in its register `0x0B`: bits 0-6 are
the percentage (0 to 100) and bit 7 is set while the battery is being charged.

```csharp
var keyboard = new PicoCalcKeyboard();
keyboard.Start();                        // optional: the battery can be read with or without the polling thread

int percent;
bool charging;
if (keyboard.ReadBattery(out percent, out charging))
{
    Debug.WriteLine("Battery " + percent + " %" + (charging ? " (charging)" : ""));
}
else
{
    Debug.WriteLine("Battery level not available");
}
```

- `ReadBattery`, `BatteryPercent`, `IsCharging` and `ReadBatteryRaw` can be called from any thread, also while the
  keyboard is started: the polling thread and these calls take turns on the I2C bus.
- Each call reads the MCU. The MCU refreshes its value **about every 20 seconds**, so reading more often shows nothing
  new.
- The value is not available (`false`, -1) when there is no battery, when the MCU has not measured it yet (a few seconds
  after power-on), when the keyboard firmware is older than **1.2** (the battery register was added in that version), or
  on an I2C error.
- For a correct level, power the PicoCalc on with the battery inserted, then connect the USB cable.
- The charging flag follows the charger: it is set while charging (for example raw `0xD5` = 85 % and charging), but near
  100 % the charger stops and `IsCharging` can be `false` even with the USB-C cable plugged (seen at 98 %).
- `Alt + B` on the keyboard makes the top-left LED blink to show the level (3 blinks: full or above 80 %, 2: about
  50 %, 1: low): handy to compare with what the driver reads.
- `PicoCalcConsole` owns its keyboard and does not expose the battery: read it with a `PicoCalcKeyboard` of your own
  in a program that does not use the console.

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
