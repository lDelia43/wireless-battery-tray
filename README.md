# Wireless Battery Tray

Shows the battery level of your wireless peripherals right on the Windows taskbar, next to the system icons:

```
🎧 90%   🖱️ 72%   ^   ENG   📶 🔊   14:10
```

## Supported devices

| Source | Devices | Requirement |
|---|---|---|
| **Logitech G HUB** | Mice, headsets, keyboards (e.g. PRO X Superlight, A50 X, G915) | G HUB running |
| **Corsair iCUE** | Wireless headsets, mice, keyboards | iCUE running with *Enable SDK* turned on, plus the iCUE SDK DLL (see below) |
| **HyperX Cloud II Wireless** | Headset (read directly over USB, experimental) | — |
| **Xbox controllers** | Full / Medium / Low / Empty | Connected wirelessly |
| **Generic Bluetooth** | Any device whose battery shows up in *Settings → Bluetooth & devices* | — |

## Features

- Device-type icon (mouse, headset, keyboard, controller) and percentage, vertically centered on the taskbar, using the same text color as the taskbar clock (follows Windows light/dark mode).
- Turns red and shows a notification below 20%.
- Smooth animations: fades in/out, hover highlight, devices slide in/out when they connect or disconnect, numbers count to the new level, and a gentle pulse when a battery is low.
- Shows a ⚡ next to the level while a device is charging, and notifies you when it's fully charged.
- Hover a device to see its name; double-click it to open G HUB / iCUE (or Bluetooth settings).
- Moves out of the way of other taskbar widgets and tray changes automatically.
- Behaves like a taskbar button: it highlights on hover and a click opens the menu (*Refresh now*, *Settings…*, *Start with Windows*, *Exit*).
- Starts with Windows. Uses ~20 MB of RAM and ~0% CPU.

## Customization

Drag the readout along the taskbar to move it (*Lock position* / *Reset position* in the menu).

Click the battery readout → **Settings…** to change:

- **Display:** show/hide device icons and the `%` sign, red-when-low, text and icon size, spacing between devices, number animation.
- **Behavior:** low-battery threshold, refresh interval, notifications, start with Windows.
- **Devices:** hide any device you don't want to see.

To start over, use **Restore factory settings…** in the menu (or uninstall and reinstall).

Settings are saved to `%LOCALAPPDATA%\WirelessBatteryTray\settings.ini`, so you can also edit them by hand.

## Install

1. Download `WirelessBatteryTray-Setup.exe` from [Releases](https://github.com/lDelia43/wireless-battery-tray/releases/latest).
2. Run it. The installer isn't code-signed, so SmartScreen will warn you: **More info → Run anyway**.

No admin rights needed. Uninstall from *Settings → Apps*.

### Corsair support

Corsair support uses the official iCUE SDK, which is not redistributed here. To enable it, download `iCUESDK.x64_2019.dll` from [CorsairOfficial/cue-sdk](https://github.com/CorsairOfficial/cue-sdk/releases) (`redist/x64` folder) and copy it to `%LOCALAPPDATA%\WirelessBatteryTray\`.

## Build

Requires .NET Framework 4.5+ (included with Windows 10/11) and [NSIS](https://nsis.sourceforge.io/) for the installer.

```bat
csc /target:winexe /platform:x64 /out:installer\WirelessBatteryTray-1.0.0.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll src\*.cs
makensis /DVERSION=1.0.0 installer\setup.nsi
```

On Linux, `mcs -sdk:4.5` and `makensis` work the same way.

## Limitations

- Windows 10/11, 64-bit.
- Windows doesn't allow third-party items inside the taskbar, so this is a transparent always-on-top window positioned over it.
- HyperX support is experimental. If your headset doesn't show up, open an issue with the exact model.

## License

MIT
