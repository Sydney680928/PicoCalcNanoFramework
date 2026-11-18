# Serial port (`System.IO.Ports`)

The USB-C port of the PicoCalc is a serial link to a PC. **No PicoCalcNano package is needed**: the firmware already
provides the UART, and the standard nanoFramework package `nanoFramework.System.IO.Ports` is enough.

- NuGet package: `nanoFramework.System.IO.Ports`
- Port to use: **`COM1`**
- Firmware: tested on CLR 1.18.0.96 (`PICO2_RP2350_W`)

## The two serial ports of the firmware

`SerialPort.GetPortNames()` returns `COM1` and `COM2` on the PicoCalc.

| Port | What it is | Use it? |
|---|---|---|
| `COM1` | A UART of the Pico, wired to the USB-C port of the PicoCalc through a USB-serial chip (CH340). On the PC it appears as an ordinary COM port, named "USB-SERIAL CH340" | **Yes** |
| `COM2` | The channel of the nanoFramework debugger (the USB port of the Pico itself) | **No** |

`COM2` only opens while Visual Studio and the Device Explorer are connected. Whatever you write to it goes into the
debugger's protocol stream, and the debugger answers. Do not open it in an application.

## Quick start

On the PicoCalc:

```csharp
using System.IO.Ports;

using (var port = new SerialPort("COM1", 115200))
{
    port.NewLine = "\r\n";
    port.Open();
    port.WriteLine("HELLO from the PicoCalc");
}
```

On the PC, plug the USB-C cable of the PicoCalc and open the new "USB-SERIAL CH340" port in a terminal (PuTTY,
Tera Term...) with the same speed (here 115200), 8 data bits, no parity, 1 stop bit, no flow control. The message appears
in the terminal. The other direction works too: what you type in the terminal can be read on the PicoCalc with
`port.BytesToRead` and `port.Read(...)`.

The full example is the F6 entry of the demo in `samples` (an echo between the PicoCalc and the terminal).

## Tips

- **Dispose the port** when you are done (`using`, or `Close()` then `Dispose()`). The nanoFramework documentation says that the
  `SerialPort` constructor throws an `ArgumentException` when the port is already opened, so an undisposed port can
  break the next `new SerialPort("COM1", ...)`.
- The USB-C port is separate from the debugger: you can keep Visual Studio connected on the Pico USB port and use a
  terminal on the USB-C serial port at the same time.
- Tested at **9600** and **115200 baud**, in both directions, with PuTTY on the PC.
