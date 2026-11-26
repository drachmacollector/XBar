# XBar

A floating, rounded, translucent **overlay** taskbar for Windows 11.

- **True overlay:** maximised and snapped windows use the whole screen; the taskbar
  floats on top of them (apps pill + tray pill), and clicks outside the pills reach the app below.
- **Shape:** split pills (apps + tray) or one floating bar; radius, margins, padding, float height.
- **Style:** **liquid glass (default)** / normal / opaque / clear / blur / acrylic, tint colour with alpha, outline, glow,
  shadow, gradient, accent-colour sync, separate light-theme look.
- **Dynamic looks:** desktop, window open, window maximised, Start, Search, Task View.
- **Behaviour:** hides over fullscreen games and video, peek-through hotkey, idle dim,
  tray-on-hover, per-app rules (reserve space / always show / hide / custom look), profiles.
- **Safety:** every change is temporary and undone on exit, by a watchdog if XBar crashes,
  and at the next launch if needed.

## Using it

Run `XBar.exe`. Left-click the tray icon for the control panel; right-click for the menu.

| Default hotkey | Action |
|---|---|
| Ctrl+Win+Alt+R | **Panic:** restore the taskbar now and pause |
| Ctrl+Win+Alt+P | Pause / resume |
| Ctrl+Win+Alt+O | Overlay ↔ classic reserved strip |
| Ctrl+Win+Alt+Space (hold) | Peek through the bar |
| Ctrl+Win+Alt+N | Next profile |

Taskbar **auto-hide must be off** (Settings › Personalization › Taskbar).
Quit TranslucentTB / RoundedTB first; they fight over the same taskbar.

## If something goes wrong

1. Press **Ctrl+Win+Alt+R**, or tray › *Restore taskbar now*.
2. Start menu › XBar › **XBar - Restore taskbar** (runs `XBar.exe --restore`).
3. `powershell -ExecutionPolicy Bypass -File restore.ps1` (no XBar needed; add `-Explorer` to restart Explorer).
4. Last resort: sign out, or `taskkill /f /im explorer.exe` then `start explorer.exe`.

Start XBar holding **Shift** (or `--safe`) to open it without applying anything.
Logs: `%LOCALAPPDATA%\XBar\logs`. Settings: `%LOCALAPPDATA%\XBar\settings.json` (hand edits apply live).

## How the overlay works

Explorer reserves the taskbar strip by setting the monitor *work area*. XBar sets the work area to the
full monitor **without** broadcasting `WM_SETTINGCHANGE` (Explorer recomputes and undoes it whenever it
sees that broadcast), then notifies other apps directly and re-fits already-maximised windows. The taskbar
window is clipped with `SetWindowRgn` to the pill shapes so the rest of the strip is click-through.
Styling is done by `XBar.Tap.dll`, loaded into Explorer through XAML Diagnostics, which replaces the
taskbar's background with anti-aliased pills. See `PLAN.md` for the full design.

## Building

Requires the .NET 10 SDK and Visual Studio 2022 Build Tools (Desktop C++, Windows 11 SDK).

```powershell
.\build.ps1            # debug build
.\build.ps1 -Test      # + unit tests
.\build.ps1 -Release   # portable build in .\publish\XBar
```

## Licence

GPL-3.0-or-later. Credits: see `THIRD_PARTY_NOTICES.md`.
