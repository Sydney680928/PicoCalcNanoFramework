# Sound (`PicoCalcSound`)

Beeps, notes, small melodies and sound effects (lasers, explosions, sirens...) on the PicoCalc speakers, with a square
wave produced by `PwmChannel`.

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
sound.PlayEffect(PicoCalcEffect.Laser);            // ready-made sound effect (0.2)
sound.Sweep(2400, 300, 130);                       // glide from 2400 Hz to 300 Hz in 130 ms
sound.Noise(60, 600, 900, true);                   // noise, fading out: an explosion
```

## API

| Member | Description |
|---|---|
| `Volume` | 0.0 (silent) to 1.0. Default 0.6. Implemented as the PWM duty cycle (`Volume × 0.5`; a square wave is loudest at 50 %) |
| `Speaker` | `PicoCalcSpeaker.Left` (default), `Right` or `Both` |
| `NoteGap` | Silence after each note in ms (default 15), so that repeated notes stay distinct |
| `StepMs` | Duration of one step of `Sweep` and `Noise` in ms (default 5, minimum 2) |
| `Beep()` | 1000 Hz for 120 ms |
| `Tone(int frequency, int milliseconds)` | One note, blocks until finished. A frequency of 0 or less is a rest |
| `Sweep(int fromHz, int toHz, int milliseconds [, bool fadeOut])` | Glide between two frequencies; with `fadeOut` the volume also goes down to 0. Blocks |
| `Noise(int minHz, int maxHz, int milliseconds [, bool fadeOut])` | Pseudo-noise: random frequencies between `minHz` and `maxHz`. Blocks |
| `PlayEffect(PicoCalcEffect effect)` | Plays a ready-made effect, blocks |
| `PlayEffectAsync(PicoCalcEffect effect)` | Plays a ready-made effect in the background |
| `static EffectMelody(PicoCalcEffect effect)` | The melody string of an effect, to mix with notes |
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

Since 0.2, **effects** can be mixed with the notes (a token with three parts separated by `:` is an effect):

| Token | Effect |
|---|---|
| `S:2400>300:130` | Sweep: glide from 2400 Hz to 300 Hz in 130 ms |
| `SF:1500>100:600` | Sweep whose volume also fades out |
| `N:100-3000:400` | Noise: random frequencies between 100 and 3000 Hz for 400 ms |
| `NF:60-600:900` | Noise whose volume fades out |

Example: `"G4:120 C5:120 E5:120 S:600>2400:250 R:100 NF:100-3000:500"`.

## Sound effects (0.2)

`PicoCalcEffect` has eight ready-made effects, each one a melody string (see `EffectMelody`):

| Effect | Melody string |
|---|---|
| `Laser` | `S:2400>300:130` |
| `Jump` | `S:300>1200:130` |
| `Coin` | `B5:70 E6:250` |
| `Siren` | `S:500>1200:140 S:1200>500:140` three times |
| `Explosion` | `NF:60-600:900` |
| `Wind` | `NF:800-4000:1000` |
| `Fall` | `SF:1500>100:600` |
| `Tick` | `A6:8 R:90` six times |

A sweep or a noise is a succession of very short notes (`StepMs`, 5 ms by default). The frequency follows the **real
elapsed time**, so the requested duration is respected whatever the cost of a step. The effects are pure square
waves: they sound like an 8-bit console, not like samples.

## How it works, and what was learned on the hardware

- **One new `PwmChannel` per note.** Changing the `Frequency` of a running channel produces **no sound**, so each
  note creates a channel (`CreateFromPin(pin, frequency, duty)`), starts it, waits, stops it and disposes it.
- **Left, right and both speakers work.** With `Both`, two channels are created on GP26 and GP27 at the same
  frequency.
- **A step costs about 1.7 ms** (measured with `Tone(f, 1)`: creating, starting and disposing the channel). That is why
  `StepMs` defaults to 5 ms and cannot go below 2 ms.
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
