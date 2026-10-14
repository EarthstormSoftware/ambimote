# AmbiMote

Ambient lighting for PC monitors, in the style of Philips Ambilight, using
[Pimoroni Mote](https://shop.pimoroni.com/products/mote) LED sticks. AmbiMote reads the colours
at the edges of each screen and lights the wall behind the monitors to match.

> **Status:** early development. The desktop app and the `ambimote` CLI light every mapped stick from the screen
> and work as a lamp. See the [roadmap](docs/roadmap.md).

## How it works

Mote hosts are USB devices, so AmbiMote runs on the PC that drives the monitors:

1. **Capture** each monitor with the Windows Desktop Duplication API, keeping frames on the GPU.
2. **Sample** the screen edge behind each stick, downscaled on the GPU.
3. **Process** the colours: smoothing, gamma, white balance, brightness and a power limit.
4. **Send** them to the Mote hosts over USB serial.

It is built to stay out of the way while gaming: the expensive work happens on the GPU and only
a few kilobytes per frame reach the CPU.

## Desktop app

`AmbiMote.exe` lives in the tray and keeps the lights running. The menu down the left of its
window picks the mode:

- **Light**: white light from candle (1900 K) to daylight (6500 K).
- **Colour**: one colour all round.
- **Effect**: rainbow, breathe, candle or chase, at an adjustable speed.
- **Ambient**: the colours at the edges of the screens, with smoothing, depth (how far into the screen the
  colours come from), saturation and gamma. The colours can come from each stick's own monitor, or from one
  monitor for every stick: the sticks on the monitors beside it then carry its picture on sideways, or stay dark.

Every mode page has the on/off switch and brightness. Turning the lights off frees the Mote hosts
for the `ambimote` command, and turning them on goes back to the last mode. The cog at the bottom
of the menu holds the settings: start with Windows, start minimised, minimise (or close) to the
tray, turning the lights off while the PC is locked or the screens are off, the power limit per
host, and the stick mapping file. **Identify** there lights each stick in turn for a few seconds,
white at pixel 0, and the status bar names its host and channel and where the mapping puts it.
The tray icon's menu switches mode and brightness, and lights up while the sticks are running;
exit from there.

Keyboard shortcuts work anywhere, even in games: **Ctrl+Alt+Shift+L** turns the lights on or off,
**Ctrl+Alt+Shift+M** goes to the next mode, and **Ctrl+Alt+Shift+Up** and **Down** change the brightness. Change
or turn them off in the settings; if another program already uses one, the settings say so.

**Automatic mode** switches the mode by what is in front: Ambient while a game or video fills the screen (the
default; choose another mode or none in the settings), or a mode of its own for a program you add, such as a
game. The mode goes back afterwards, unless you changed it meanwhile, and lights you turned off stay off. A
quick Alt+Tab or opening AmbiMote itself changes nothing. Full screen can also take its colours from one monitor,
such as the one you game on, and goes back to your own Ambient choice afterwards.

The app reopens a Mote host that is unplugged and plugged back in, and **Reconnect** reloads
`config.json` after you edit it. Its own settings are saved to `%APPDATA%\AmbiMote\settings.json`.
Only one copy runs at a time; starting it again shows the window. The app and the `ambimote`
command cannot use a host at the same time: control the app with `ambimote app` (below), or turn its lights off first.

## Command line

```powershell
ambimote list                       # Mote hosts plugged in, with serial numbers
ambimote identify                   # light each stick in turn to see which host and channel it is
ambimote fill ff8000 --brightness 0.2 # also names (warm, red, ...) and temperatures (3000k); stays lit
ambimote effect rainbow             # also breathe, candle and chase; runs until Ctrl+C
ambimote config                     # check where each stick is mounted (see Setup below)
ambimote off
ambimote monitors                   # monitors attached to the desktop
ambimote snapshot screen.bmp        # save the small image AmbiMote samples from, and print the edge colours
ambimote run --edge top --channel 1 # light one stick from the top edge of the primary monitor until Ctrl+C
ambimote ambient --brightness 0.4   # light every stick in the config from its monitor edge until Ctrl+C
```

While the desktop app is running it holds the Mote hosts, so the commands above cannot open them. Control the
app instead with `ambimote app`. It talks to the app on this PC only, through a pipe that only you can use, so
it also works from scripts, Task Scheduler, AutoHotkey or a Stream Deck button:

```powershell
ambimote app status                 # mode, brightness and what the app is doing
ambimote app toggle                 # also on and off
ambimote app light 2700             # light mode, optionally at a temperature (or candle, warm, neutral, daylight)
ambimote app colour red             # colour mode, optionally in a new colour
ambimote app effect candle          # effect mode, optionally with another effect
ambimote app ambient
ambimote app next                   # the next mode: light, colour, effect, ambient
ambimote app brightness 60          # or +10 and -10 to step up and down
ambimote app identify               # also reconnect, and show to open the window
```

Add `--json` for the reply as JSON.

Exit codes: `0` success, `1` error, `2` invalid arguments, `3` no Mote host found,
`4` config file missing or invalid, `5` the app is not running.

## Setup

Copy [config.example.json](config.example.json) to `%APPDATA%\AmbiMote\config.json` and fill in
where each stick is mounted: which host and channel, which monitor edge, and which way it runs
(`from` is where the first LED is, `to` where the last one is). `ambimote list` gives the host
instance IDs and `ambimote monitors` the monitor names; `ambimote effect chase` checks the result.

Mote hosts have no USB serial number, so AmbiMote tells them apart by the USB port they are
plugged into. Keep each host in the same port.

## Power

A Mote host with four sticks at full white would want several amps, far more than a USB port
supplies. AmbiMote estimates the current for every frame and scales it down to stay within a
budget: 500 mA per host by default, which is what a USB 2.0 port guarantees. Change it in the
app's settings or with `--max-current`, and use a powered USB hub if you raise it.

## Building

Requires Windows and the .NET 10 SDK. The icons are drawn by `dotnet run tools/make-icon.cs -- src/AmbiMote.App`.

```powershell
dotnet build AmbiMote.slnx
dotnet test AmbiMote.slnx
```

## Installing

There is no installer. Publish the app to a folder of its own, run `AmbiMote.exe` from there, and turn on
**Start with Windows** in its settings, which starts the copy you are running:

```powershell
dotnet publish src/AmbiMote.App -c Release -o "$env:LOCALAPPDATA\Programs\AmbiMote"
```

To update, exit AmbiMote from the tray and publish again. Running the app straight from `bin\Debug` works too,
but then the next build replaces it, and a build fails while it is running.

## Licence

MIT. See [LICENSE](LICENSE).
