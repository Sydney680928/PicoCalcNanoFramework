# SD card (`PicoCalcSdCard` and `PicoCalcFat`)

Two layers, in two files:

1. **`PicoCalcSdCard`** talks to the card over SPI: initialisation, card type and capacity, read and write of
   512-byte blocks.
2. **`PicoCalcFat`** is a FAT16 / FAT32 file system on top of it: list folders, read, write, delete and rename
   files, create and delete folders.

- Source files: `PicoCalcSdCard.cs`, `PicoCalcFat.cs`
- NuGet packages: `nanoFramework.System.Device.Gpio`, `nanoFramework.System.Device.Spi`
- Independent of the display, keyboard and console
- Validated on a 1 GB SD v1 card (FAT16) and on a 64 GB SDXC card formatted as FAT32, with `chkdsk` clean after reads,
  writes, deletions, folder creation and renaming

## Why this driver?

nanoFramework ships `nanoFramework.System.IO.FileSystem`, but on the firmware tested here (`PICO2_RP2350_W`,
CLR 1.18.0.84) it could not mount an SD card over SPI on the PicoCalc slot. This driver is written in pure C#,
needs no native support from the firmware (only the SPI and GPIO drivers), and gives you a FAT16 / FAT32 file system
on the SD slot. It was not tested on the `PICO_RP2040` firmware with the built-in file system, so the statement above
only covers the Pico 2 W.

## Hardware

The PicoCalc has a **full-size SD** slot on SPI0:

| Signal | Pin |
|---|---|
| MISO | GP16 |
| CS | GP17 (driven by the driver) |
| SCK | GP18 |
| MOSI | GP19 |
| nanoFramework SPI bus id | **1** (ids are 1-based on RP2040 / RP2350; id 2 is the display) |

## Supported cards and formats

| Card | Notes |
|---|---|
| SD v1, standard capacity (up to 2 GB) | Byte addressing, usually FAT16. Detected as `SdV1` |
| SD v2 standard capacity | `SdV2` |
| SDHC (4 to 32 GB) | Block addressing, usually FAT32. `SdHc` |
| SDXC (more than 32 GB) | Works at the protocol level (reported as `SdHc`) **if formatted FAT32**. Cards of this size come formatted as exFAT, which is not supported: reformat them as FAT32 |

The partition table must be a classic MBR (or the card has no partition table at all). FAT12 (cards under about 8 MB)
and exFAT are not supported. To format a card larger than 32 GB as FAT32, Windows needs a third-party tool (for
example guiformat or the SD Association formatter); macOS offers "MS-DOS (FAT)" with the "Master Boot Record"
scheme; on Linux use `mkfs.vfat -F 32`.

## Quick start

```csharp
var sd = new PicoCalcSdCard();
if (!sd.Init())
{
    Debug.WriteLine("Card error: " + sd.ErrorMessage);
    return;
}
Debug.WriteLine(sd.CardType + ", " + (sd.CapacityBytes / (1024 * 1024)) + " MB");

var fat = new PicoCalcFat(sd);
if (!fat.Mount())
{
    Debug.WriteLine("File system error: " + fat.ErrorMessage);
    return;
}

foreach (PicoCalcFatEntry entry in fat.ListDirectory("/"))
{
    Debug.WriteLine((entry.IsDirectory ? "[DIR] " : "") + entry.Name + "  " + entry.Size);
}

string text = fat.ReadAllText("HELLO.TXT");
fat.WriteAllText("NOTE.TXT", "Written by the PicoCalc\r\n");
```

Every operation returns `false` or `null` on failure and sets `ErrorMessage`: no exception is thrown for card or
file system errors.

## `PicoCalcSdCard`

| Member | Description |
|---|---|
| `PicoCalcSdCard()` | Data clock 10 MHz |
| `PicoCalcSdCard(int dataClockFrequency)` | Other data clock (25 MHz is the limit for old cards) |
| `static int SpiBusId` | nanoFramework SPI bus id, default `1` |
| `Init()` | Initialises the card (CMD0, CMD8, ACMD41, OCR, CSD), then switches to the data clock. Returns `false` on failure |
| `CardType` | `SdV1`, `SdV2`, `SdHc` or `Unknown` |
| `CapacityBytes`, `BlockCount` | Read from the CSD register |
| `ReadBlock(uint block, byte[] buffer)` | Reads one 512-byte block (the buffer must be at least 512 bytes) |
| `WriteBlock(uint block, byte[] buffer)` | Writes one 512-byte block. **Overwrites data on the card** |
| `ErrorMessage` | Text of the last error |
| `Dispose()` | Releases the SPI bus and the CS pin |

If `Init()` fails, the message lists what happened at each clock tried (250 kHz, then 100 kHz, then 500 kHz).

## `PicoCalcFat`

### Mounting

| Member | Description |
|---|---|
| `PicoCalcFat(PicoCalcSdCard card)` | The card must be initialised |
| `Mount()` | Reads the partition table and the boot sector, finds the first FAT16 / FAT32 partition |
| `FatType` | `"FAT16"` or `"FAT32"` |
| `ClusterSizeBytes`, `TotalClusters` | Geometry |
| `IsMounted` | State |
| `ErrorMessage` | Text of the last error |

### Reading

| Member | Description |
|---|---|
| `ListDirectory(path)` | Returns a `PicoCalcFatEntry[]` (`Name`, `IsDirectory`, `Size`, `FirstCluster`), or `null`. `"/"` is the root |
| `GetEntry(path)` | Information on one file or folder, or `null` if it does not exist |
| `ReadFile(path)` | The whole file as `byte[]`, or `null`. Limited to `MaxReadSize` bytes (default 65 536) |
| `ReadAllText(path)` | The whole file decoded as UTF-8, or `null` |
| `MaxReadSize` | Largest file loaded in memory. Raise it if you have the RAM |

### Writing

| Member | Description |
|---|---|
| `WriteFile(path, byte[] data)` | Creates the file, or replaces it if it exists. The parent folder must exist |
| `WriteAllText(path, string text)` | Same, with UTF-8 text |
| `DeleteFile(path)` | Deletes a file and frees its clusters |
| `CreateDirectory(path)` | Creates a folder (with its `.` and `..` entries) |
| `DeleteDirectory(path)` | Deletes an **empty** folder (refused otherwise) |
| `Rename(path, newName)` | Renames a file or folder inside its folder. `newName` is a bare name, no path |
| `FileDate`, `FileTime`, `SetTimestamp(y, m, d, h, min, s)` | Date and time stored in the files that are written |

Paths use `/` or `\`, relative to the root: `"HELLO.TXT"`, `"TEST/NOTE.TXT"`. Matching is case-insensitive.

### Names

- To **create, rename or delete**, names must be valid **8.3 names**: up to 8 characters, a dot, up to 3 more, with
  letters (stored in upper case), digits and `_ - ~ $ ! # % & ( ) @ ^`. No spaces, no accents.
- Files and folders created by a computer with **long names** appear in a listing under their short alias (for
  example `HELLOW~1.TXT`): use that alias. Deleting or renaming such a file also removes its long-name entries, so
  nothing is left orphaned.

### Date and time

The Pico has no clock. Files get the date in `FileDate` / `FileTime` (default: 2026-11-11 00:00). Call
`SetTimestamp` to set it, for example from a clock you read over the network (the Pico W firmware includes SNTP).

## Behaviour and limits

- **Not thread-safe:** use one thread, or put a lock around every call.
- **Only the first FAT16 / FAT32 partition** is used. FAT12 and exFAT are not supported.
- **A full folder is not extended:** creating an entry in a folder that has no free entry left fails with an error
  (a 16 KB cluster holds 512 entries, so this is rare; the FAT16 root has a fixed size, usually 512 entries).
- **Long names cannot be created**, only short 8.3 names.
- **Writes are ordered for safety:** the data first, then the FAT (both copies), then the directory entry last.
  A power cut in the middle of a write can still leave an incomplete file; do not remove the card or the power
  during a write.
- **FAT32:** the first write marks the "free clusters" counters of the FSInfo sector as unknown, so a computer will
  recompute them. This is normal.
- **Cards swapped or removed:** after changing the card, call `Init()` and `Mount()` again.

### Performance (Pico 2 W, 10 MHz data clock)

Reads are fast, writes are slow because each block is written with several SPI calls and old cards need time per
block:

| Operation | Time |
|---|---|
| List a folder, read a small file | about 0.25 s |
| Write a small file | about 1 s |
| Delete a file | about 1 s |
| Create a folder (the whole 16 KB cluster is written) | about 5 s |
| Write a 34 KB text file and read it back | about 20 s |

Speeding up the writes is planned for a later version.

## Pitfalls found on the way

1. **Some SPI clocks read the card's answers shifted by one bit** on this firmware (150, 200, 300 and 400 kHz: CMD0
   answers `00` instead of `01`), while 100 kHz, 250 kHz and everything from 500 kHz up read correctly. The driver
   therefore initialises at 250 kHz and falls back to 100 kHz, then 500 kHz.
2. **Never open GP16 (MISO) as a GPIO** in the same program before using the SPI bus: the pin loses its SPI function
   and the card seems to answer only zeros.
3. **If the card answers CMD0 but nothing after it,** take the card out and put it back in firmly (a loose contact,
   or a card left in a odd state by a computer). Then run the program again.
4. **Large strings are very slow in nanoFramework:** strings are stored as UTF-8, so `text[i]` and `text.Length`
   walk the string from the start (the cost grows with the square of the length). To process a large text, convert it
   first with `ToCharArray()` (the driver does this when writing). Avoid building large texts with `+=` too.
5. **Memory:** `WriteAllText` of a 34 000-character text needs the string (twice its length in bytes) and its UTF-8 copy.
   For large files build a `byte[]` and call `WriteFile` instead.
