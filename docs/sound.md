# Sound (`PicoCalcSound`)

Beeps, notes and small melodies on the PicoCalc speakers, with a square wave produced by `PwmChannel`.

> **Status: work in progress.** The API works and the speakers sound, but the **pitch does not follow the requested
> frequency** (see [Known issue](#known-issue-the-pitch-does-not-follow-the-frequency)). An addendum will be added
> once this is understood.

- Source file: `PicoCalcSound.cs`
- NuGet package: `nanoFramework.System.Device.Pwm`
- Independent of the other drivers

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

## Known issue: the pitch does not follow the frequency

Notes are produced and are audible, but most of them sound at **about the same pitch**. Measurements made with a
spectrum analyser on a phone, 4 s per note:

| Requested | Measured |
|---|---|
| 100 Hz, 200 Hz, 400 Hz | about 2000 Hz |
| 800 Hz | about 1800 Hz |
| 1600 Hz, 3200 Hz | about 6000 Hz |
| 6400 Hz and above | very high, with a pop |

What has been ruled out: a gap between notes, the `Frequency` setter, and the use of two channels. The current
hypothesis is a limit in how the firmware computes the PWM period on the RP2040/RP2350 (a 16-bit counter wrap, which
would give a lowest usable frequency of about 1.9 kHz when no clock divider is applied), but it is **not
confirmed**. The same idea written in MOGWAI NANO for ESP32 (`pwm.open` with a frequency, then `pwm.start`) works
there, so the wiring and the principle are right.

A question has been posted to the nanoFramework team on Discord. This document will get an addendum with the answer,
and the driver will be adapted accordingly.

### Practical consequence for now

- Use sound for **signals** (beep, error, end of task), not for melodies that must be in tune.
- If you need to experiment, the test program `ProgramSoundTest.cs` plays 100 Hz to 12 800 Hz, 4 s per note, so
  that the pitch can be measured with an analyser.
