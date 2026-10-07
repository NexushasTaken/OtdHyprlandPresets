# OtdHyprlandPresets

An [OpenTabletDriver](https://github.com/OpenTabletDriver/OpenTabletDriver) **v0.6.7** plugin that switches
OTD presets automatically depending on which Hyprland window has focus. With Hyprland's
`input:follow_mouse = 1`, that is the window under the pen, so moving the pen into osu! applies your
osu preset and moving it to Krita applies your drawing preset.

## How it works

```
Hyprland ──(.socket2.sock events)──► plugin (inside otd-daemon) ──► IDriverDaemon.SetSettings(preset)
         ◄─(.socket.sock j/activewindow)─┘
```

- Hyprland's IPC is two Unix sockets in
  `$XDG_RUNTIME_DIR/hypr/$HYPRLAND_INSTANCE_SIGNATURE/`. `.socket.sock` is the request/reply socket that
  `hyprctl` uses. `.socket2.sock` streams events such as `activewindowv2>>ADDRESS`.
  (`otd` talks to `otd-daemon` through a .NET named pipe, which is also a Unix socket. This plugin
  doesn't need it because it runs *inside* the daemon.)
- The plugin is an OTD **Tool** (`ITool`). The daemon injects its `IDriverDaemon`, and presets are
  applied the same way OTD's built-in *Preset Binding* applies them
  (`PresetManager.FindPreset` → `SetSettings` → `ForceResynchronize`).
- When focus changes, the plugin waits `debounceMs`, asks Hyprland for the active window's
  class and title, runs your rules, and applies the resulting preset if it differs from the
  current one. Title changes in the focused window, such as switching browser tabs, are also handled.
- If Hyprland restarts, the plugin reconnects automatically.

## Build

Requires the .NET 8 SDK.

```sh
dotnet build -c Release           # plugin
dotnet test                       # unit tests (one also queries a live Hyprland if running)
```

`refs/` contains `OpenTabletDriver{,.Plugin,.Desktop}.dll` built from the v0.6.7 tag. The plugin
compiles against them, but they're **not** shipped because the daemon provides them at runtime.
For a different OTD version, rebuild them from that tag:
`dotnet build OpenTabletDriver.Desktop -c Release -o <dir>`, then copy the three DLLs into `refs/`.

## Install

```sh
./install.sh          # copies the DLL to ~/.config/OpenTabletDriver/Plugins/OtdHyprlandPresets/
                      # and offers to restart opentabletdriver.service
./install.sh --zip    # or build dist/OtdHyprlandPresets.zip for the UI's "Install plugin..."
```

Then enable it once:

1. Open the OTD UI and go to the **Tools** tab. Tick **Hyprland Preset Switcher**.
2. **Save** the settings so it starts together with the daemon.

   (`otd enabletools OtdHyprlandPresets.HyprlandPresetTool` also works, but it doesn't save to disk.)

The tool doesn't have to be enabled inside every preset. When the plugin applies a preset, the daemon
disposes the tool, but the listener keeps running until the daemon exits. It only has to be enabled in
the settings the daemon *starts* with.

## Configure

On first start the plugin creates `~/.config/OpenTabletDriver/HyprlandPresets.json`. Edit it at any
time: it's re-read on the next focus change, so no restart is needed.

```jsonc
{
  // Used when no rule matches. null = leave the current preset alone.
  "defaultPreset": "Default",
  "debounceMs": 100,
  "logWindows": false,   // true = log every focused window's class/title to the OTD log
  "rules": [
    { "class": "^osu!?$", "preset": "osu" },
    { "initialClass": "^steam_app_\\d+$", "preset": "Games" },
    { "class": "^(krita|org\\.kde\\.krita)$", "preset": "Drawing" },
    { "class": "firefox", "title": "YouTube", "preset": "Browsing" }
  ]
}
```

- `preset` is a preset name, meaning a file in `~/.config/OpenTabletDriver/Presets/` without the `.json`.
  Create presets in the OTD UI with *Presets > Save as preset*. The name is case-sensitive.
- `class`, `initialClass`, `title`, `initialTitle` are case-insensitive .NET regexes. They're unanchored,
  so use `^…$` for an exact match. Every field you set must match. The first matching rule wins.
- To find a window's class, run `hyprctl activewindow` while it's focused, or set `"logWindows": true`
  and watch the OTD log (the UI's Console tab, or `journalctl --user -fu opentabletdriver`).

## Notes and limitations

- Applied presets are in memory only, like OTD's Preset Binding. `settings.json` is untouched,
  so a daemon restart starts from your saved settings. Pressing **Save** in the UI while a switched
  preset is active saves *that* preset as your settings.
- If you press **Apply** in the UI, your changes stay active until the next window switch that maps to a
  different preset.
- Every preset switch rebuilds the output mode, filters and bindings. That takes milliseconds, but
  `debounceMs` keeps quick pen sweeps across several windows from triggering repeated switches.
- Log messages appear under the group **Hyprland Presets**.

## License

MIT, see [LICENSE](LICENSE).
