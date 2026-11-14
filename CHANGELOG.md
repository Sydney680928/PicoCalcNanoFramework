# Changelog

All notable changes to this repository are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). The packages are versioned together
for now; when they start to diverge, the entries will say which package they concern.

## [Unreleased]

### Added

- `PicoCalcNano.Keyboard`: keyboard driver (key events with code, character and modifiers).
- `PicoCalcNano.Display`: 320x320 display driver, 40x20 text screen with hardware scrolling, in-memory canvas
  (RGB565) and an 8x16 font (GNU Unifont, U+0020 to U+00FF).
- `PicoCalcNano.Console`: standalone console with line editing, history, key input and a graphics mode.
- `PicoCalcNano.SdCard`: SD card driver over SPI, with a FAT16 / FAT32 file system (list, read, write, delete,
  rename, create and remove folders). Pure C#, no native firmware support needed.
- `PicoCalcNano.Sound`: beeps, notes and melodies on the left and right speakers.

### Known issues

- Sound: the pitch does not follow the requested frequency on the current firmware. A fix is being worked on in the
  nanoFramework firmware (PWM clock divider on the Pico targets).
- SD card: writes are slow (a few KB/s) and exFAT is not supported.

[Unreleased]: https://github.com/sydney680928/PicoCalcNanoFramework/compare/HEAD
