# XBar - notes for Claude

Floating overlay taskbar for Windows 11: C# core + WPF control panel (`src/XBar.Core`, `src/XBar.App`)
and a C++ XAML-injection module (`src/XBar.Tap`) that paints the pills inside Explorer. See README.md / PLAN.md.

## Mandatory: run the app and look at it after every change

Unit tests are not enough for this project. **After any change that can affect what the user sees or how the
taskbar behaves - especially anything frontend or styling (pill layout/shape, padding, height, float, colours,
material, the control panel XAML/theme) - you must run the real app and verify the effect before reporting done.**

1. Rebuild and restart the real app: `.\tools\rerun.ps1` (add `-WithTap` if `src/XBar.Tap` changed).
   It stops the running XBar (the watchdog restores the taskbar), builds Release into `publish\XBar`, relaunches.
2. **Screenshot the taskbar area** and actually look at the image: `.\tools\screenshot.ps1 -Out <scratchpad>\bar.png -BottomStrip 110`.
   Zoom in on the area you changed (crop/scale with System.Drawing) - e.g. icons contained by the pill, the tray pill at the
   screen edge, no overlap between pills.
3. For control-panel changes, also capture the panel: `.\tools\screenshot.ps1 -Out <scratchpad>\panel.png -Panel`,
   then click through the affected nav pages (mouse_event) and screenshot each. Check text is readable (white on dark,
   no black-on-dark text) and sliders/ranges behave.
4. To test a setting live, edit `%LOCALAPPDATA%\XBar\settings.json` (applies live), screenshot, then **restore the
   original file** - it is the user's real config.
5. Report what you saw, honestly. If something could not be run or captured, say so - never claim a visual result from tests alone.

Save screenshots in the session scratchpad, not in the repo.

## Other rules

- Make small, frequent commits with simple messages.
- Taskbar auto-hide must be off. Never leave the user's taskbar broken: `XBar.exe --restore` or Ctrl+Win+Alt+R restores it.
- The taskbar window cannot grow, so the pill height is capped at (taskbar height - float gap).
- Build: `.\build.ps1` (debug) / `.\build.ps1 -Release`; tests: `dotnet test tests/XBar.Core.Tests`.
