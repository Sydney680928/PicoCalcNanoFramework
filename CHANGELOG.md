# Changelog

All notable changes to this repository are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). The packages are versioned
independently; the repository tag is the highest package version, and the entries say which package they concern.

## [Unreleased]

### Added

- `PicoCalcNano.Display`: user-defined symbols. `PicoCalcSymbols.Define()` gives a shape to the codes U+007F and
  U+0080 to U+009F (33 codes, blank in the font): hearts, gauges, sprites, box-drawing characters. The text screen
  redraws the cells that show a symbol when it is redefined.
- `PicoCalcNano.Display`: colors per character in `PicoCalcTextScreen`. Every cell has its own text and background
  colors, taken from a palette of 16 colors with the names and numbers of the .NET `ConsoleColor` enumeration
  (`PicoCalcPalette`) and the default colors of the Windows console. `Foreground` and `Background` can be changed at
  any time and apply to the text written afterwards. `GetPaletteColor()`, `SetPaletteColor()` and
  `NearestColorIndex()` give access to the RGB value of the palette entries.
- `PicoCalcNano.Console`: `Foreground` and `Background` can be changed at any time (the text already displayed keeps
  its colors), and `GetPaletteColor()`, `SetPaletteColor()`, `NearestColorIndex()` and `PaletteSize`. `Clear()` erases
  with the current background color. `PicoCalcNano.Console` therefore needs `PicoCalcNano.Display` 0.3 or later.

### Changed

- **Breaking change** (`PicoCalcNano.Console`, `PicoCalcNano.Display`): `PicoCalcConsole.ForegroundColor` and
  `BackgroundColor` (`0xRRGGBB` fields, to set before `Start()`) are replaced by `Foreground` and `Background`, which
  are palette indexes (0 to 15, use `PicoCalcPalette`). Replace `console.ForegroundColor = PicoCalcColors.Red` by
  `console.Foreground = PicoCalcPalette.Red`, or use `NearestColorIndex(rgb)` for an RGB color. The same change applies
  to `PicoCalcTextScreen`. `PicoCalcDisplay` and `PicoCalcCanvas` still use `0xRRGGBB` colors.

## [0.2.0] - 2026-11-19

Packages in this release: `PicoCalcNano.Display` 0.2.0, `PicoCalcNano.Console` 0.2.0 and `PicoCalcNano.Sound` 0.2.0.
`PicoCalcNano.Keyboard` and `PicoCalcNano.SdCard` are unchanged and stay in 0.1.0.

### Added

- `PicoCalcNano.Sound`: sound effects. `Sweep(fromHz, toHz, ms [, fadeOut])` (glide between two frequencies) and
  `Noise(minHz, maxHz, ms [, fadeOut])` (pseudo-noise), with the `StepMs` property (step duration, default 5 ms).
  The effects follow the real elapsed time, so the requested duration is respected.
- `PicoCalcNano.Sound`: effects in the melody string, mixed with the notes: `S:2400>300:130` (sweep), `SF:` (sweep
  with fade-out), `N:100-3000:400` (noise) and `NF:` (noise with fade-out).
- `PicoCalcNano.Sound`: `PicoCalcEffect` (`Laser`, `Jump`, `Coin`, `Siren`, `Explosion`, `Wind`, `Fall`, `Tick`) with
  `PlayEffect()`, `PlayEffectAsync()` and `EffectMelody()`, which returns the melody string of an effect.
- Demo: sound effects (key 5) and a notes + effects jingle (key 6) in the sound demo.
- `PicoCalcNano.Console`: `Busy` property. When true, the cursor is a blinking hourglass instead of the block, to show
  that a program is running; when false, the usual block (waiting for input).
- `PicoCalcNano.Display`: `PicoCalcTextScreen.CursorGlyph`, a custom shape for the text cursor (16 bytes, 8x16). The
  console uses it for the hourglass. `PicoCalcNano.Console` therefore needs `PicoCalcNano.Display` 0.2 or later.
- `PicoCalcNano.Console`: `CancelRead()` and `ReadCancelled`. Another thread can now end a `ReadLine` that is waiting
  (for example when a program is started from a PC while the console waits for a command): `ReadLine` returns `null`,
  the typed text stays on the screen and the cursor goes to the next line, so that the output of the program does not
  collide with the input line.
- `docs/serial.md`: how to use the serial port of the PicoCalc USB-C connector (`COM1`, with the standard
  `nanoFramework.System.IO.Ports` package), and why `COM2` (the debugger channel) must not be used.
- Demo: new F6 entry, a serial echo between the PicoCalc and a terminal on the PC.

### Changed

- `PicoCalcNano.Sound`: `Tone` is now built on an internal method that does not touch `Volume` or `NoteGap`; its
  behaviour is unchanged.
- README: the native `SDCard` class of `nanoFramework.System.IO.FileSystem` is not implemented on the Pico targets,
  which is why the SD card driver is written in C#.

## [0.1.0] - 2026-11-17

First release. The five packages are published together on nuget.org in version 0.1.0.

### Added

- `PicoCalcNano.Keyboard`: keyboard driver (key events with code, character and modifiers). The I2C bus id is
  detected automatically, so the driver works with both the older and the newer I2C numbering of the nanoFramework
  firmware on the Pico targets (`BusId` tells which one was used).
- `PicoCalcNano.Display`: 320x320 display driver, 40x20 text screen with hardware scrolling, in-memory canvas
  (RGB565) and an 8x16 font (GNU Unifont, U+0020 to U+00FF).
- `PicoCalcNano.Console`: standalone console with line editing, history, key input and a graphics mode.
- `PicoCalcNano.SdCard`: SD card driver over SPI, with a FAT16 / FAT32 file system (list, read, write, delete,
  rename, create and remove folders). Pure C#, no native firmware support needed.
- `PicoCalcNano.Sound`: beeps, notes and melodies on the left and right speakers. Needs nanoFramework firmware
  CLR 1.18.0.96 or later (PWM clock divider fix, nf-interpreter #3623).

### Known issues

- SD card: writes are slow (a few KB/s) and exFAT is not supported.
- Console: `PicoCalcConsole` creates and owns its own display and keyboard. Do not create a `PicoCalcDisplay` or a
  `PicoCalcKeyboard` in the same program (the pins are already open); use `console.Display` instead.

[Unreleased]: https://github.com/sydney680928/PicoCalcNanoFramework/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/sydney680928/PicoCalcNanoFramework/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/sydney680928/PicoCalcNanoFramework/releases/tag/v0.1.0
