# AmbiMote

Ambient lighting for PC monitors, in the style of Philips Ambilight, using
[Pimoroni Mote](https://shop.pimoroni.com/products/mote) LED sticks. AmbiMote reads the colours
at the edges of each screen and lights the wall behind the monitors to match.

> **Status:** early development. The `ambimote` CLI can drive Mote sticks and light one stick
> from one edge of a monitor; mapping all sticks and a lamp mode are next. See the [roadmap](docs/roadmap.md).

## How it works

Mote hosts are USB devices, so AmbiMote runs on the PC that drives the monitors:

1. **Capture** each monitor with the Windows Desktop Duplication API, keeping frames on the GPU.
2. **Sample** the screen edge behind each stick, downscaled on the GPU.
3. **Process** the colours: smoothing, gamma, white balance, brightness and a power limit.
4. **Send** them to the Mote hosts over USB serial.

It is built to stay out of the way while gaming: the expensive work happens on the GPU and only
a few kilobytes per frame reach the CPU.

## Command line

```powershell
ambimote list                       # Mote hosts plugged in, with serial numbers
ambimote identify                   # light each stick in turn to see which host and channel it is
ambimote fill ff8000 --brightness 0.2
ambimote off
ambimote monitors                   # monitors attached to the desktop
ambimote snapshot screen.bmp        # save the small image AmbiMote samples from, and print the edge colours
ambimote run --edge top --channel 1 # light one stick from the top edge of the primary monitor until Ctrl+C
```

Exit codes: `0` success, `1` error, `2` invalid arguments, `3` no Mote host found.

## Power

A Mote host with four sticks at full white would want several amps, far more than a USB port
supplies. AmbiMote estimates the current for every frame and scales it down to stay within a
budget (400 mA per host by default; change it with `--max-current`). Use a powered USB hub
if you raise it.

## Building

Requires Windows and the .NET 10 SDK.

```powershell
dotnet build AmbiMote.slnx
dotnet test AmbiMote.slnx
```

## Licence

MIT. See [LICENSE](LICENSE).
