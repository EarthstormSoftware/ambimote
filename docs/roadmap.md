# Roadmap

## Steps

1. **Mote output** *(done)*: serial protocol, host discovery by USB ID, power limit,
   and the `ambimote list`, `identify`, `fill` and `off` commands.
2. **Capture one monitor** *(done)*: Desktop Duplication through Vortice.Windows, downscale on the GPU,
   read back a small texture, sample one edge and drive one stick (`ambimote run`, `snapshot`, `monitors`).
3. **Stick mapping and lamp mode** *(done)*: record where each stick is mounted and which way round, then
   use the sticks as ordinary lights, like Pimoroni Plasma: any colour, plus an effect or two
   (a chase around the monitors in order, which also checks the mapping, and a rainbow).
4. **All monitors and sticks**: per-monitor capture driven by the stick mapping, smoothing,
   saturation and gamma (`ambimote ambient`, done); still to do: white balance.
5. **Measure while gaming**: CPU use and frame times with AmbiMote on and off (PresentMon or the
   GPU vendor's overlay).
6. **Desktop app**: tray icon and menu, mode and settings window, start with Windows, start minimised,
   minimise to tray, and reconnecting when a Mote host is unplugged and plugged back in (done); still to do:
   identify mode, and turning off when the PC locks or the monitors sleep.
7. **Optional**: MQTT control (on/off, brightness, mode) for Home Assistant.

## Stick mapping (step 3)

Each stick records which host and channel it is on, which monitor edge it sits behind, and
where along that edge it sits, as a fraction of the edge length. `from` greater than `to`
means the stick runs the other way. Monitors are matched by device name, not index, and
hosts by USB serial number, because port numbers and indexes can change.

Example for three monitors with eight sticks across two hosts:

```text
 [ L-top-a ][ L-top-b ]   [ C-top-a ][ C-top-b ]   [ R-top-a ][ R-top-b ]
[L                      ] [                      ] [                      R]
[  Left monitor         ] [   Centre monitor     ] [   Right monitor       ]
```

```json
{
  "hosts": [ { "id": "mote-a", "serialNumber": "..." }, { "id": "mote-b", "serialNumber": "..." } ],
  "sticks": [
    { "host": "mote-a", "channel": 1, "monitor": "...", "edge": "left", "from": 0.95, "to": 0.15 },
    { "host": "mote-a", "channel": 2, "monitor": "...", "edge": "top",  "from": 0.05, "to": 0.45 }
  ]
}
```

## Decisions

- **Run on the PC, not a Raspberry Pi.** The capture has to happen on the PC either way, and
  that is the expensive part. A Pi would only take over sending a few hundred bytes per frame.
  HDMI capture on a Pi would take the PC out entirely, but splitters limit refresh rate, break
  FreeSync/G-Sync and run into HDCP, which is worse for gaming.
- **Windows capture APIs, not GPU vendor SDKs.** Desktop Duplication (or
  Windows.Graphics.Capture) already keeps frames on the GPU. AMD AMF and similar SDKs mainly
  help with video encoding, which AmbiMote does not need, and they tie the app to one vendor.
  Capture sits behind an interface so another backend can be added if a game needs it.
- **C# from the start, no Python prototype.** Pimoroni's Python library was only needed as
  the reference for the serial protocol.
- **Colour processing in software.** The Mote host can apply gamma itself, but doing it in
  AmbiMote keeps it testable and adjustable. The host's gamma flag is left off.

## Open questions

- Mote hosts do not report a USB serial number, so Windows gives them an instance ID based on
  the USB port they are plugged into. Plan: match hosts by that ID (stable while each host stays
  in the same port), and fall back to flashing the hosts and asking which is which, saving the answer.
- The real current draw of a Mote host, to set a better default power limit.
- Protected video (DRM) captures as black; decide whether to hold the last colours or fade out.
- HDR: Desktop Duplication (`DuplicateOutput`) hands over 8-bit frames that Windows has already
  converted to SDR, so the desktop looks right, but HDR highlights in games are clipped. If that
  matters, `DuplicateOutput1` can capture FP16 scRGB to tone-map ourselves.
