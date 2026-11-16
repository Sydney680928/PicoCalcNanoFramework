# Sound (`PicoCalcSound`)

Beeps, notes and small melodies on the PicoCalc speakers, with a square wave produced by `PwmChannel`.

- Source file: `PicoCalcSound.cs`
- NuGet package: `nanoFramework.System.Device.Pwm`
- Independent of the other drivers
- Firmware: needs CLR **1.18.0.96 or later** (see [Firmware](#firmware))

## Hardware

| Pin | Use |
|---|---|
| GP26 | Left speaker (PWM slice 5, channel A) |
| GP27 | Right speaker (PWM slice 5, channel B) |
| GP28 | **Not** an audio output on the PicoCalc |

GP26 and GP27 belong to the **same PWM slice**, which means they share one frequency: playing "both" speakers with
two channels at the same frequency works, two different frequencies would not.

## Quick start

```csharp
var sound = new PicoCalcSound();
sound.Beep();                                      // short beep
sound.Tone(440, 500);                              // A4 for 500 ms (blocks)
sound.Play("C4:250 E4:250 G4:500 R:250 C5:500");   // melody (blocks)
sound.PlayAsync("C4:250 D4:250 E4:250");           // background; sound.Stop() cancels
```

## API

| Member | Description |
|---|---|
| `Volume` | 0.0 (silent) to 1.0. Default 0.6. Implemented as the PWM duty cycle (`Volume × 0.5`; a square wave is loudest at 50 %) |
| `Speaker` | `PicoCalcSpeaker.Left` (default), `Right` or `Both` |
| `NoteGap` | Silence after each note in ms (default 15), so that repeated notes stay distinct |
| `Beep()` | 1000 Hz for 120 ms |
| `Tone(int frequency, int milliseconds)` | One note, blocks until finished. A frequency of 0 or less is a rest |
| `Play(string melody)` | Plays a melody, blocks until it ends or `Stop()` is called |
| `PlayAsync(string melody)` | Plays in a background thread and returns immediately (stops any previous melody first) |
| `Stop()` | Stops the melody at the end of the current note and waits for the thread |
| `IsPlaying` | `true` while `PlayAsync` is playing |
| `static ParseNote(string name)` | Frequency in Hz of `"A4"`, `"C#5"`, `"Bb3"`; `0` for a rest; `-1` if unknown |
| `PinLeft`, `PinRight` | 26 and 27 |
| `Dispose()` | Stops any melody |

### Melody format

Space-separated tokens `NOTE:milliseconds`.

- `NOTE`: a letter `A` to `G`, an optional `#` (sharp) or `b` (flat), then the octave digit. `A4` is 440 Hz.
- `R:200` is a rest of 200 ms.
- Malformed tokens are silently skipped.

Example: `"F#3:300 Bb4:150 R:100"`.

## How it works, and what was learned on the hardware

- **One new `PwmChannel` per note.** Changing the `Frequency` of a running channel produces **no sound**, so each
  note creates a channel (`CreateFromPin(pin, frequency, duty)`), starts it, waits, stops it and disposes it.
- **Left, right and both speakers work.** With `Both`, two channels are created on GP26 and GP27 at the same
  frequency.
- **The speakers are weak and small:** very low notes are barely audible. Test with a melody in octaves 4 to 6.
- **Blocking:** `Tone` and `Play` block the calling thread. Use `PlayAsync` to play while the program does
  something else.

## Firmware

The PWM clock divider of the Pico targets was wrong in older nanoFramework firmware, so the pitch did not follow the
requested frequency (nf-interpreter pull request #3623). The fix is in **CLR 1.18.0.96 or later**; this driver
needs it.

Measured with a spectrum analyser on a Pico 2 W (`PICO2_RP2350_W`) with a firmware containing the fix: 100, 200,
400, 800, 1600, 3200, 6400 and 7500 Hz all come out at the requested frequency. 12 800 Hz is not audible on the
PicoCalc speaker, which is too small for it. Melodies are in tune, and the speakers are best in octaves 4 to 6.

To check your own board, the test program `ProgramSoundTest.cs` plays 100 Hz to 12 800 Hz, 4 s per note, so that the
pitch can be measured with an analyser.
