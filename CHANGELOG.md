# Changelog

All notable changes to this repository are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). The packages are versioned together
for now; when they start to diverge, the entries will say which package they concern.

## [Unreleased]

### Added

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

[Unreleased]: https://github.com/sydney680928/PicoCalcNanoFramework/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/sydney680928/PicoCalcNanoFramework/releases/tag/v0.1.0
