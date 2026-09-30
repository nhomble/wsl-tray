# WslTray

A tiny Windows tray app that shows your WSL2 distros and opens a terminal in any of them.

![WslTray window](docs/screenshot.png)

## Download

Grab `WslTray.exe` from the [Releases](../../releases) page and run it. The exe is unsigned, so Windows SmartScreen may warn on first launch (More info, then Run anyway).

## Build

Needs only the .NET Framework compiler that ships with Windows:

```
.\build.cmd
```

This produces `bin\WslTray.exe`.

## Use

Run `bin\WslTray.exe`; it sits in the tray (left-click opens the window). Double-click a distro to open a terminal.

- **Terminal:** pick from the installed terminals (or a custom command using `{name}`) in the tray menu or window. Saved in `HKCU\Software\WslTray`.
- **Flags:** `--show`, `--probe` (JSON state), `--selftest`, `--autostart on|off`.
