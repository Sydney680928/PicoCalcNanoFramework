# Changelog

All notable changes to this repository are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). The packages are versioned together
for now; when they start to diverge, the entries will say which package they concern.

## [Unreleased]

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

[Unreleased]: https://github.com/sydney680928/PicoCalcNanoFramework/compare/HEAD
