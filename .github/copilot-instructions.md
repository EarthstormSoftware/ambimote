# Project Guidelines

## Purpose
AmbiMote drives Pimoroni Mote LED sticks mounted behind PC monitors to light the wall with the
colours at the edges of the screen (like Philips Ambilight). It runs on the PC itself: the Mote
hosts plug in over USB, and no Raspberry Pi is involved.

## Architecture
- `src/AmbiMote.Core`: all logic. Mote serial protocol and host discovery, power limiting,
  screen capture, edge sampling, stick mapping, colour processing, the lighting engine and the
  local control pipe the app listens on. No UI code.
- `src/AmbiMote.App`: the WinUI 3 desktop app (tray icon, settings window, hotkeys, automatic mode).
- `src/AmbiMote.Cli`: the `ambimote` command line, for testing hardware, running headless and
  controlling the running app (`ambimote app`).
  Stable exit codes (`ExitCodes.cs`); never renumber them.
- `tests/`: xUnit tests. Core logic changes need tests. Hardware access sits behind
  interfaces (`IMoteTransport`) so it can be faked.
- Roadmap and design decisions: `docs/roadmap.md`.

## Build And Test
- `dotnet build AmbiMote.slnx` and `dotnet test AmbiMote.slnx`.
- Warnings are errors (`Directory.Build.props`). Fix them rather than suppressing them.
- Windows only (`net10.0-windows`).

## Performance
AmbiMote runs while games are running, so it must stay cheap:
- Keep captured frames on the GPU and downscale there; only copy a tiny texture back to the CPU.
- Never copy full-resolution frames to the CPU in the main path.
- Cap the update rate (around 30 fps), and skip work when the screen has not changed.

## Public Repository Rules
- This repository is public. Never commit personal setup details: USB serial numbers,
  monitor device IDs, hostnames, IP addresses, usernames or credentials.
- Real settings live in a per-user config outside the repo (under `%APPDATA%\AmbiMote`).
  Only `*.example.json` files are committed.
- Commit messages use Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:`, `test:`).

## Conventions
- Hardware safety: everything sent to a Mote host goes through `MoteHost.Show`, which applies
  brightness and the power limit. Do not bypass it.
- Keep the Mote protocol byte-compatible with Pimoroni's Python library.
