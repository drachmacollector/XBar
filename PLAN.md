# PLAN — Overlay taskbar for Windows 11 — **XBar**

Status: **approved 2026-11-26 — implementation in progress.**
Date: 2026-11-26. Target: Windows 11 only, personal use.

## Decisions (answers to §12)

1. "System panel" = the **tray area** of the taskbar.
2. **Apps pill + separate tray pill**: visually separate, but driven by one shared mechanism (same geometry pass, same state, same appearance updates applied together).
3. **Hide over fullscreen games and video.**
4. Covering the bottom of maximised apps is OK; peek hotkey and reserve rules included.
5. Single monitor only (multi-monitor kept working but not a priority).
6. TranslucentTB will be retired.
7. Explorer DLL injection approved.
8. Toolchain install approved.
9. Name: **XBar**.
10. Left-aligned taskbar: later.
11. Licence: **GPL-3.0-or-later**.
12. **All §8.2 extras**, plus TranslucentTB-style styling, all controlled from one clean, sleek control panel.

Working style: frequent small commits with simple messages.

---

## 0. TL;DR

- **Build on Explorer's own taskbar, don't replace it.** Shape it the way RoundedTB does (window region), paint it the way TranslucentTB does (a small native DLL injected into Explorer that swaps the XAML background brush).
- **Overlay = take the taskbar's strip out of the monitor's *work area*** while keeping the taskbar topmost. Windows then maximize/snap to the full screen and the bar floats over them. Area outside the pill(s) is clipped out of the window region, so clicks there go straight through to the app underneath.
- Explorer re-computes the work area on its own at various times. We handle that in two stages: **(1) an out-of-process "guardian"** that re-applies the work area whenever it changes (documented APIs only, proven by RoundedTB's auto-hide mode). If the spike shows Explorer fights back too often, **(2) intercept `SystemParametersInfoW(SPI_SETWORKAREA)` inside Explorer** from our already-injected DLL so Explorer's own resets are rewritten. That hook targets a public export rather than private Explorer internals.
- **Stack:** C# / .NET 10 + WPF (WPF-UI Fluent controls) for the app, engine and settings UI, plus one C++20 / C++/WinRT DLL that runs inside Explorer. **Not installed yet:** the .NET SDK (only runtimes are present), Visual Studio / MSVC Build Tools and the Windows SDK.
- **No admin rights needed at runtime.** (Installing the build tools needs one UAC prompt.)
- **Licences:** both upstream projects are **GPL-3.0**. The LICENSE files in your local `reference/` copies have been overwritten with a one-line permissive statement, and that edit does not change the real licence. See §4.
- **Milestone 1 is the overlay spike.** Nothing else is built until it passes its exit criteria.

---

## M1 spike results (2026-11-26, build 26200, 1920×1080 @125 %)

| Question | Result |
|---|---|
| Does Explorer fight `SPI_SETWORKAREA` with `SPIF_SENDCHANGE`? | **Yes**: reset within ~300 ms every time. The loop guard tripped after 31 corrections and paused XBar safely. |
| Why? | Explorer recomputes the work area **when it receives the `WM_SETTINGCHANGE` broadcast**. Set *without* the broadcast, the value held for the whole test (no resets). |
| Do maximised windows follow a silent change? | New maximises: yes (full 0–1080). Existing ones: no. Posting `WM_SETTINGCHANGE` to them does not refit them. **`SetWindowPos` to the new work area (keeping the invisible-border insets) refits them and they stay maximised.** |
| Does sending the message to File Explorer / Progman windows trigger the reset? | Yes, so XBar notifies only windows **outside** Explorer's process. |
| Is a maximised window with a 0 px gap treated as fullscreen (bar dropped below it)? | **No**: taskbar stayed topmost over a maximised Notepad. Default gap = 0. |
| Fullscreen game detection | A borderless game (Spider-Man 2, `GameNxApp`, not zoomed, covers the monitor) was correctly detected and the bar hidden. |
| Watchdog after `taskkill /f` of XBar | Restored work area and region in ~0.3 s; journal marked clean. |
| Recovery on next launch (both processes killed) | Restored before starting the engine. |
| Legacy `ReBarWindow32`/`MSTaskSwWClass` rects | Stale on 25H2. UI Automation gives exact app/tray bounds, so it is used instead. |

**Decision:** approach A with the "silent set + notify non-Explorer windows + refit maximised" refinement.
A+ (detour inside Explorer) is not needed. Restore uses the normal broadcast so Explorer takes ownership back.

### Build status (2026-11-26)

Verified live on this machine:
- Overlay with no fighting; maximised apps run to the bottom edge.
- Split apps + tray pills with anti-aliased corners and an outline, driven by the TAP.
- Click-through outside the pills.
- Hidden over a fullscreen game.
- Materials: blur, clear + glow, acrylic + gradient, all applied live from settings.
- Float height.
- Peek hotkey, panic hotkey, pause/resume.
- Safety: watchdog restore after `taskkill /f`, next-launch recovery, TAP self-restore when XBar dies, TAP version handover.
- Control panel pages render (Home, Shape, Style). 16 unit tests pass.

Not yet verified:
- Explorer restart (TAP re-inject).
- Sleep/resume, display/DPI change.
- Run at login.
- Idle dim and tray-on-hover with a real mouse.
- Start/Search/Task View states.
- Rules and profiles end to end.
- Light-theme variant and accent sync.
- Multi-monitor.

---

## 1. How the two reference apps work

### 1.1 RoundedTB (`reference/RoundedTB`)

| Aspect | What it does | Files |
|---|---|---|
| Language / UI | C# **.NET 6 WPF** (`net6.0-windows10.0.19041`), WPF-UI (Mica/Fluent), Hardcodet.NotifyIcon for the tray, Newtonsoft.Json. Optional MSIX packaging project. | `RoundedTB/RoundedTB.csproj`, `MainWindow.xaml(.cs)`, `PackagingProject/` |
| Finding the taskbar | `FindWindowEx` for `Shell_TrayWnd` and `Shell_SecondaryTrayWnd`. Children: `TrayNotifyWnd` (tray), `ReBarWindow32 → MSTaskSwWClass` (app list). On secondary Win11 taskbars, the second `Windows.UI.Composition.DesktopWindowContentBridge` child. | `Taskbar.cs:429-520` (`GenerateTaskbarInfo`) |
| Rounding / floating | **`CreateRoundRectRgn` + `SetWindowRgn` on Explorer's taskbar window.** "Simple" mode uses one inset rounded rect (margins + radius). "Dynamic" mode builds separate rounded rects for app list / tray / widgets, combined with `CombineRgn` (RGN_OR). Centred layout comes from registry `TaskbarAl`. Margins are scaled by `GetDpiForWindow/96`. | `Taskbar.cs:166-327`, `CheckIfCentred` `Taskbar.cs:26-57` |
| Also sets | `WS_EX_LAYERED` + `SetLayeredWindowAttributes` (used for its fade "auto-hide"). | `Taskbar.cs:454-459`, `Background.cs:166-248` |
| Update loop | A `BackgroundWorker` **polls every 100 ms**: re-reads rects, re-applies the region when anything moved, handles "fill on maximise" (resets the region when a maximised window is on that monitor) and hover show/hide. No event hooks. | `Background.cs:25-294` |
| Work area / overlay precedent | Its **AutoHide** option calls `SystemParametersInfo(SPI_SETWORKAREA)` with *monitor rect minus 2 px* for every display and forces the taskbar `HWND_TOPMOST` + `ABM_SETSTATE AlwaysOnTop`. On disable it puts `monitor − taskbarHeight` back and toggles `ABM_SETSTATE` to make Explorer recompute. **This is the same mechanism our overlay needs.** | `MainWindow.xaml.cs:374-424`, `Interaction.cs:108-118`, `AppBars.cs` |
| Win10 vs Win11 | Build ≥ 21996 ⇒ Win11 (registry `CurrentBuild`). Win11 has different default radius/margins, no Win10 grab-handle offset, and different secondary-tray lookup. | `MainWindow.xaml.cs:69-82`, `Interaction.cs:37-45`, `Taskbar.cs:249-253` |
| Multi-monitor | Enumerates secondary taskbars, re-generates when the count or main handle changes; `EnumDisplayMonitors` for work areas. | `Taskbar.cs:335-371`, `MonitorStuff.cs` |
| DPI | PerMonitorV2 manifest; per-taskbar `ScaleFactor`. | `app.manifest:79`, `Taskbar.cs:450` |
| Explorer restart | Handled only implicitly: the poll notices a changed `Shell_TrayWnd` handle and rebuilds. No `TaskbarCreated` message. | `Background.cs:98-113` |
| Exit / restore | `OnClosing` resets the region (`SetWindowRgn(NULL)`), removes the layered style and restores the work area. **Nothing for crashes or next launch.** | `MainWindow.xaml.cs:523-571`, `Taskbar.cs:139-158` |
| Startup | A `.lnk` in the user Startup folder (unpackaged) or `StartupTask` (MSIX). | `MainWindow.xaml.cs:606-660` |
| Settings | `%LOCALAPPDATA%\rtb.json` (packaged: RoamingFolder), Newtonsoft. | `MainWindow.xaml.cs:39`, `Interaction.cs:30-51`, `Types.cs:29-47` |
| Single instance | Process-name check, plus renaming a window title to signal the running instance (hacky). | `MainWindow.xaml.cs:88-114` |
| TTB interop | Sends `TTB_ForceRefreshTaskbar` after changing the region. | `Interaction.cs:126-143` |

### 1.2 TranslucentTB (`reference/TranslucentTB`)

| Aspect | What it does | Files |
|---|---|---|
| Language / UI | **C++20**, Win32 + **C++/WinRT**, WIL, spdlog, rapidjson, Detours (vcpkg). UI is **UWP XAML Islands**: a tray flyout/context menu, colour picker and welcome page. No classic settings window; advanced settings mean editing `settings.json`. | `TranslucentTB/`, `Xaml/`, `TranslucentTB.slnx`, `settings.schema.json` |
| Process layout | Main exe + **ExplorerHooks.dll** (Win10 / early Win11: Detours hook of `SetWindowCompositionAttribute` inside Explorer so Explorer can't overwrite the accent) + **ExplorerTAP.dll** (Win11 22621+ XAML taskbar). | `ExplorerHooks/swcadetour.cpp`, `ExplorerTAP/` |
| Taskbar type detection | Checks whether Explorer has `Taskbar.dll` loaded and counts XAML islands, giving `Classic` / `Mixed` / `XAML`. | `taskbar/taskbarattributeworker.hpp:35-40`, `.cpp:1177+` |
| Appearance, old path | `SetWindowCompositionAttribute(WCA_ACCENT_POLICY)` on `Shell_TrayWnd`. | `taskbarattributeworker.cpp:588-627` |
| Appearance, Win11 XAML path (yours) | **Injection:** `SetWindowsHookEx(WH_CALLWNDPROC)` on Explorer's UI thread loads ExplorerTAP.dll. A Detours payload marks "we're in Explorer". `DllMain` starts a thread that calls **`InitializeXamlDiagnosticsEx`** on Explorer itself (this pins the DLL). The TAP watches the visual tree for `Taskbar.TaskbarFrame` and its `BackgroundFill` / `BackgroundStroke` `Shape`s, then replaces `.Fill` with a solid, acrylic or custom blur brush (Composition backdrop + Gaussian blur + flood). It exposes `ITaskbarAppearanceService` through the COM ROT (`GetActiveObject`). | `ExplorerTAP/api.cpp:18-100`, `dllmain.cpp`, `tapsite.cpp`, `visualtreewatcher.cpp:43-82`, `taskbarappearanceservice.cpp`, `XamlBlurBrush.cpp` |
| Crash safety | **The TAP, inside Explorer, waits on TTB's process handle and restores the original brushes when TTB dies** (`RestoreAllTaskbarsToDefaultWhenProcessDies`). On normal exit it calls `ReturnToStock()`. | `taskbarappearanceservice.cpp:161-190`, `taskbarattributeworker.cpp:1013-1042, 1488-1495` |
| Dynamic states | WinEvent hooks (foreground, minimize, move/size, show/hide, cloak, create/destroy, z-order, Aero Peek), `IAppVisibility` (Start), Search/TaskView coordinators. Per-monitor sets of maximised and visible windows choose an appearance: desktop / visible / maximised / start / search / task view, with per-app rules (class / title / process). | `taskbarattributeworker.hpp:98-128`, `Common/config/*.hpp` |
| Explorer restart | Listens for `RegisterWindowMessage("TaskbarCreated")` and `WM_DISPLAYCHANGE`, then `ResetState()` (re-find taskbars, re-inject). **Crash-loop guard:** a second Explorer restart within 30 s shows a warning and TTB exits. | `taskbarattributeworker.cpp:334-363, 1440-1500` |
| Power | Battery/energy-saver notifications (acrylic is disabled in power saver). | `taskbarattributeworker.cpp:299, 1285` |
| DPI | PerMonitorV2 manifest. | `resources/TranslucentTB.manifest:31` |
| Startup | MSIX `StartupTask` (portable build: Startup-folder shortcut). | `managers/startupmanager.cpp` |
| Settings | `settings.json` (packaged: `RoamingState`; portable: next to the exe), file-watched for live edits, JSON schema provided. | `managers/configmanager.cpp:27-48`, `settings.schema.json` |
| Single instance | Named mutex `344635E9-9AE4-4E60-B128-D53E25AB70A7`. | `Common/constants.hpp:11` |

**Neither app touches the reserved strip in a way that achieves your overlay.** RoundedTB's auto-hide mode comes closest, but it fades the bar out.

---

## 2. Achieving the true overlay

### 2.1 What has to be true

1. Maximised and snapped windows extend to the bottom of the monitor. That means the monitor's **work area** must not exclude the taskbar strip.
2. The bar is drawn **above** those windows, so the taskbar window stays topmost.
3. Outside the visible pill(s), the strip must **not** swallow clicks or block content. It must be clipped from the window region.
4. Explorer, display changes, sleep/resume and so on must not quietly undo 1–3, and everything must be undone on exit or crash.

### 2.2 Approaches compared

| # | Approach | Reliability | Side effects | Win11 25H2 fit | Verdict |
|---|---|---|---|---|---|
| **A** | **Keep Explorer's taskbar; set work area = full monitor (−ε) with `SPI_SETWORKAREA`; keep the taskbar `HWND_TOPMOST`; clip with `SetWindowRgn`; a guardian re-applies the work area after Explorer resets it** | Good if Explorer resets rarely; resets are detectable (`WM_SETTINGCHANGE`/`SPI_SETWORKAREA`, `WM_DISPLAYCHANGE`) | Maximised windows may "jump" for a frame when Explorer resets and we re-apply. Each set broadcasts `WM_SETTINGCHANGE` to every top-level window, so it must be debounced. Toasts/flyouts that position by work area may sit lower (to verify). | Documented APIs; RoundedTB ships this pattern (`MainWindow.xaml.cs:386-401`) | **Primary** |
| **A+** | A, plus a **Detours hook of `SystemParametersInfoW` inside Explorer** (in our TAP DLL) that rewrites Explorer's own `SPI_SETWORKAREA` calls | Best: no fight and no flicker, because Explorer's resets never land | Requires the in-Explorer DLL, which we need for blur anyway. Must verify that Explorer actually sets the work area through this export. | Hooks a **public** export, not private symbols, so it should survive updates. TTB already detours a user32 export in Explorer (`swcadetour.cpp`). | **Escalation if the spike shows A flickers or fights** |
| B | Turn on native **auto-hide** (Explorer then gives up the strip by itself) and stop the bar from sliding away | Work area is stable (Explorer owns it). Keeping the bar shown means fighting Explorer's hide animation (re-positioning, or faking "unhide locks"), which is undocumented and flickers. | Changes a persisted user setting (`StuckRects3`), so a crash leaves auto-hide on. Chromium-based apps inset maximised windows by 1 px for auto-hide bars. | Fragile | Tested in the spike as a data point only |
| C | `ABM_REMOVE` / un-register the taskbar as an appbar | The tray is special-cased inside Explorer, not a normal appbar, so results are unpredictable | Could desync Explorer's layout | Unknown | Quick experiment in the spike, not planned |
| D | **Own overlay window**: hide Explorer's taskbar and draw our own bar (RetroBar / Cairo-style) | Full control of z-order and work area | Must re-implement task buttons, grouping, jump lists, previews, **tray icon hosting** (we would have to become the `Shell_NotifyIcon` host), clock, Start / Quick Settings / Notification Center entry points. Loses the native Win11 look in your screenshots. Months of work. | OK | **Rejected** |
| E | Hook Explorer's private layout functions (Windhawk-mod style, needs PDB symbols) | Breaks on Windows updates | Symbol download at runtime | Fragile | Rejected (A+ covers the need with a public export) |

### 2.3 Recommendation: A, with a pre-planned path to A+

Per monitor that has a taskbar:

1. **Journal first** (see §6): write the original `rcWork` for each monitor to disk *before* changing anything.
2. `SPI_SETWORKAREA` = `rcMonitor` with the bottom inset by **ε** (0–2 px, configurable). The spike determines ε. RoundedTB uses 2 px, probably so that maximised windows aren't classified as "fullscreen/rude", which would make Explorer drop the taskbar below them.
3. `SetWindowPos(taskbar, HWND_TOPMOST, …NOMOVE|NOSIZE|NOACTIVATE)`, re-asserted on `EVENT_SYSTEM_FOREGROUND` (subject to the fullscreen policy, see Q3).
4. `SetWindowRgn(taskbar, pills)` so that only the pill(s) exist for hit-testing. Everything else passes clicks to the window below.
5. **Guardian** (hidden message-only window + top-level listener):
   - `WM_SETTINGCHANGE (wParam = SPI_SETWORKAREA)`: compare the current `rcWork` with the desired value. Re-apply only if it differs. Debounce 100 ms. Loop guard: >N resets/minute ⇒ back off and log.
   - `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, monitor add/remove ⇒ re-enumerate, then re-apply.
   - `TaskbarCreated` (Explorer restart) ⇒ new HWNDs, re-inject TAP, re-apply. Crash-loop guard as in TTB (2 restarts / 30 s ⇒ restore and pause).
   - `PBT_APMRESUMEAUTOMATIC`, `WTS_SESSION_UNLOCK`, `WTS_CONSOLE_CONNECT` (RDP) ⇒ verify after ~1–2 s and re-apply.
   - A cheap 2 s verification timer as a backstop (no 100 ms polling).
6. If the spike shows frequent resets or visible jumping, implement **A+** in the TAP DLL: hook `SystemParametersInfoW`; when Explorer calls `SPI_SETWORKAREA` for a monitor we manage, substitute our rect. The hook is removed and Explorer is told to recompute when our process dies.

### 2.4 What could break, and the mitigation

| Scenario | Expected behaviour / risk | Mitigation |
|---|---|---|
| **Maximised windows** | Fill the screen; the bar covers their bottom ~48 px (status bars, horizontal scrollbars, terminal last line). | That is the requested trade-off. Add a **"peek" hotkey** (bar goes faint and click-through while held) and **per-app "reserve space" rules** (§3.2). |
| Already-maximised windows when we start/stop | Should re-size when the work area changes (to verify in the spike). | If not, nudge them with `SetWindowPos` / re-maximise on our side. |
| **Fullscreen games / video** (borderless or exclusive) | Explorer lowers the taskbar for "rude" fullscreen windows. Exclusive fullscreen bypasses DWM entirely. | Default: **respect fullscreen** (bar hides). Optional "always on top, even over fullscreen" for borderless only. See Q3. |
| Explorer flags our maximised windows as fullscreen | The bar drops below maximised apps. | The ε inset; the spike tests 0/1/2 px. |
| **Snap layouts / Snap Assist / FancyZones** | Use the work area, so snapped windows reach the bottom. | Verify. FancyZones has its own zone rects. |
| Toasts, Quick Settings, Notification Center, Start, Widgets | They are flyouts and already overlay. Some position from the work area and might land lower or overlap the pill. | Verify each in the spike. |
| Desktop icons | Desktop uses the work area, so icons may sit under the bar. | Cosmetic. Document. |
| Other appbars (docks, Teams "share tray") | Their registration can trigger Explorer to recompute the work area. | Guardian handles it; log for analysis. |
| **Multi-monitor** | Each monitor's work area is set separately. Secondary taskbars (`Shell_SecondaryTrayWnd`) have no tray, only a clock. | Per-monitor state; enable or disable per monitor. |
| Taskbar "show on all displays" off | Monitors without a taskbar are left untouched. | Monitors are only managed when a taskbar is present. |
| Mixed DPI | Geometry is in physical px. | PerMonitorV2; margins/radii stored in DIPs, scaled by `GetDpiForWindow(taskbar)`. |
| **Explorer restart / crash** | New taskbar HWND; region and brushes are gone (the new window is stock). Explorer recomputes the work area. | `TaskbarCreated` ⇒ full re-apply; crash-loop guard. |
| **Sleep / resume, lock / unlock, RDP** | Displays may re-enumerate; Explorer may reset the work area. | Event-driven re-verify after delay. |
| Display change (resolution, scaling, plug/unplug, projector) | Work area reset; taskbar resized. | Re-enumerate; re-apply; region recomputed. |
| Taskbar settings change (alignment, size, icons added) | Pill geometry changes. | Geometry provider re-reads (event-driven via TAP; WinEvent location change as fallback). |
| Elevated (admin) windows | Unaffected: region, work area and z-order are global. | — |
| Windows Update changes taskbar internals | The TAP's visual-tree names (`TaskbarFrame`, `BackgroundFill`) could change. | TAP fails soft: the overlay still works, the appearance falls back to stock. Version check + logs. |
| **TranslucentTB is currently running on this PC** (Store build 2026.2.0.0) | Two TAPs fighting over the same brush. | Detect the TTB mutex / RoundedTB process ⇒ refuse to start the appearance module and tell you. You'll need to quit TTB and disable its startup entry. |

### 2.5 How we verify it works

- **Built-in diagnostics** (`XBar.exe --diag` and a settings tab). It dumps, per monitor: `rcMonitor`, `rcWork` (actual vs desired), taskbar HWND/rect/region, topmost flag, z-order rank vs the foreground window, TAP status, and a **reset log** (every time the work area changed, by whom, and how fast we re-applied).
- **Manual matrix** (§8). Each row is pass/fail, with screenshots for visual rows.
- **Soak:** run for a full working day and through a sleep/resume. Exit criterion: no stuck states, fewer than X visible re-layouts per hour (agree X after the spike), and zero cases of the bar disappearing behind a non-fullscreen window.

---

## 3. Tech stack

### 3.1 Recommendation

| Part | Choice | Why |
|---|---|---|
| App: engine, guardian, tray, settings UI | **C# / .NET 10 (LTS), WPF + WPF-UI** (Fluent/Mica), **CsWin32** (source-generated, type-safe P/Invoke), H.NotifyIcon (or Hardcodet), System.Text.Json | Fastest way to a polished Win11-style settings window and a maintainable engine. All the overlay APIs (`SystemParametersInfo`, `SetWindowRgn`, `SetWindowPos`, WinEvent hooks, `SHAppBarMessage`) are plain Win32 and easy to call from C#. Unpackaged; no Windows App Runtime deployment issues (WinUI 3 unpackaged needs the bootstrapper and a matching runtime). |
| In-Explorer DLL (appearance, geometry, optional A+ hook, restore-on-death) | **C++20, C++/WinRT, WIL, Detours** (vcpkg or NuGet), MSVC | Must be native: it runs inside `explorer.exe` and talks to UWP XAML. Same proven pattern as TTB's ExplorerTAP. |
| Watchdog | Tiny native C++ exe (or the same .NET exe in `--watchdog` mode) | Must survive the main process being killed. |
| IPC app ↔ TAP | COM object registered in the ROT (as TTB does) or a named pipe | ROT/COM is proven by TTB. A pipe is simpler to version. Decide in M4. |
| Build | One `.sln`: C# projects via `dotnet`/MSBuild, C++ via MSBuild. Plus `build.ps1`. | |

Alternative considered: **all C++** (like TTB). One language and smaller binaries, but the settings UI in C++ (XAML Islands or raw Win32) costs far more effort for a personal tool. Not recommended.

### 3.2 What's installed on this machine

| Item | Status |
|---|---|
| OS | Build **26200.9974** = Windows 11 **25H2**. (Registry `ProductName` says "Windows 10 Home Single Language", which is a known registry quirk; the build number is authoritative.) XAML taskbar ⇒ TTB "XAML" path applies. |
| Display | 1 monitor, 1920×1080 at **125 %** (taskbar 60 physical px), taskbar **centred** (`TaskbarAl=1`), auto-hide **off**. |
| Developer Mode | **On** (`AllowDevelopmentWithoutDevLicense=1`). |
| .NET | Runtimes 6.0.36 / 8.0.34 / 10.0.15 (+ WindowsDesktop 8/10). **No .NET SDK. ❌ Missing** |
| Visual Studio / MSVC (`cl`, `msbuild`, vswhere) | **❌ Missing** |
| Windows 10/11 SDK (`Windows Kits\10`) | **❌ Missing** |
| CMake, vcpkg, nuget.exe | ❌ Missing (vcpkg/nuget not strictly needed if we use NuGet via MSBuild) |
| MinGW GCC 6.3 (`C:\MinGW`) | Present but **unsuitable** (32-bit, too old, no C++/WinRT) |
| git, winget, Python 3.11, Node, VS Code | ✅ Present |
| Windows App Runtime | 1.1 / 1.2 / 1.4 present (not needed with WPF) |
| TranslucentTB | **Store build 2026.2.0.0 is running**. It must be quit and its startup disabled before testing. |

**To install (needs one admin/UAC prompt for the installers; the app itself never needs admin):**
```
winget install Microsoft.DotNet.SDK.10
winget install Microsoft.VisualStudio.2022.BuildTools --override "--quiet --wait --add Microsoft.VisualStudio.Workload.VCTools --add Microsoft.VisualStudio.Component.Windows11SDK.26100 --includeRecommended"
```
(Or Visual Studio Community 2022/2026 with the ".NET desktop development" and "Desktop development with C++" workloads if you want the IDE.)

### 3.3 Admin rights: not required

`SystemParametersInfo(SPI_SETWORKAREA)`, `SetWindowRgn` / `SetWindowPos` on Explorer's windows, WinEvent hooks, `SetWindowsHookEx` into Explorer, `OpenProcess` on Explorer and the COM ROT all work at medium integrity against same-user processes. HKCU Run for startup needs no admin. The only admin step is installing the build tools.

---

## 4. Licences: what you can reuse

- **Upstream licence of both projects is GPL-3.0.** Confirmed from git history: `git show HEAD:LICENSE` in `reference/RoundedTB` and `HEAD:LICENSE.md` in `reference/TranslucentTB`, and the TTB README ("free software under the GPLv3").
- ⚠️ **The working-copy LICENSE files in both `reference/` folders have been modified** to a 70-byte line: *"All inspired software is allowed to take ideas and concepts and code."* Only the copyright holders can relicense, so this local edit has no legal effect. Treat both as GPL-3.0. (I did not touch `reference/`. `git -C reference/RoundedTB checkout -- LICENSE`, and the same in TranslucentTB, would restore the originals if you want.)
- **Ideas, techniques and API usage patterns are free to use** (copyright doesn't cover them). For example: "region the taskbar", "inject a TAP and swap `BackgroundFill`", "watch the parent's PID and restore", "work area − 2 px".
- **Copied or closely adapted code** (for example, porting ExplorerTAP's visual-tree watcher, `XamlBlurBrush`/effects, or RoundedTB's region math line-for-line) makes our app a derivative work under GPL-3.0.
- **Personal use only:** the GPL puts *no* obligations on private use and modification. Obligations (source availability, same licence) apply only if you *distribute* (convey) the binary.
- **Recommendation: license XBar under GPL-3.0-or-later from day one.** Then borrowing code from either project is unconditionally fine, and nothing needs to be re-licensed if you ever share it. Keep a `THIRD_PARTY_NOTICES.md` that credits RoundedTB (torchgm) and TranslucentTB (TranslucentTB contributors) and lists the MIT-licensed dependencies (WIL, Detours, CsWin32, WPF-UI, etc.).
- Working policy: write code fresh and use the references for *how it's done*. Copy only where it's clearly cheaper (most likely the TAP plumbing), with a source comment and attribution.

---

## 5. Architecture

```
┌──────────────────────── XBar.exe (.NET 10, PerMonitorV2, medium IL) ───────────────────────┐
│ App shell: single-instance mutex · tray icon · settings window (WPF-UI) · hotkeys              │
│                                                                                                │
│ Core                                                                                           │
│  ├─ SafetyJournal      write-ahead state file; recovery on launch; --restore                   │
│  ├─ TaskbarLocator     Shell_TrayWnd / Shell_SecondaryTrayWnd ↔ HMONITOR, TaskbarCreated       │
│  ├─ MonitorModel       rcMonitor/rcWork/DPI per monitor, change notifications                  │
│  ├─ OverlayController  work-area set/verify/restore, topmost policy, fullscreen policy         │
│  ├─ Guardian           WM_SETTINGCHANGE / DISPLAYCHANGE / power / session → debounced verify   │
│  ├─ GeometryProvider   pill rects: TAP (XAML bounds) → UIA → Win32 child rects (fallback)      │
│  ├─ ShapeController    SetWindowRgn per taskbar (single / split pills), click-through          │
│  ├─ StateEngine        WinEvent hooks → desktop/visible/maximised/start/search/taskview/rules  │
│  ├─ AppearanceClient   talks to TAP: brush, tint, blur, border, corner radius, shadow          │
│  ├─ Settings           JSON + schema version + file watcher + atomic write                     │
│  └─ Diagnostics/Log    rolling log, --diag dump, reset log                                     │
└──────────────┬──────────────────────────────────────────────┬──────────────────────────────────┘
               │ spawns + passes PID                           │ COM (ROT) or named pipe
┌──────────────▼──────────────┐              ┌─────────────────▼──────────── explorer.exe ─────────┐
│ XBar.Watchdog.exe        │              │ XBar.Tap.dll (C++/WinRT, injected via XAML Diag) │
│ waits on main PID; on death │              │  ├─ VisualTreeWatcher (TaskbarFrame, BackgroundFill,│
│ → run restore sequence      │              │  │   task list & tray bounds)                       │
└─────────────────────────────┘              │  ├─ AppearanceService (brushes, AA rounded border)  │
                                             │  ├─ (A+) SystemParametersInfoW detour               │
                                             │  └─ OwnerWatch: on XBar death → restore all      │
                                             └─────────────────────────────────────────────────────┘
```

Proposed repository layout:
```
/src/XBar.App          WPF app, tray, settings UI
/src/XBar.Core         engine (no UI), unit-testable
/src/XBar.Native       CsWin32 NativeMethods.txt + thin wrappers
/src/XBar.Watchdog     tiny watchdog
/src/XBar.Tap          C++ DLL injected into Explorer
/tests/XBar.Core.Tests geometry math, settings migration, journal
/tools/restore.ps1        zero-dependency emergency restore
/docs                     test matrix results, screenshots
```

---

## 6. Safety design (hard requirement)

**Key property:** everything we change is **volatile**. Work area, window region, z-order and XAML brushes all live only in the current Explorer/session. We **never** write persistent taskbar settings (no `StuckRects3`, no `TaskbarAl`, etc.). Worst case is therefore always fixed by restarting Explorer or signing out.

Layers, in order of preference:

1. **Graceful exit** (tray → Exit, `WM_ENDSESSION`, Ctrl+C in debug): TAP `RestoreAll` → `SetWindowRgn(taskbar, NULL)` → restore journalled `rcWork` per monitor. Then ask Explorer to recompute (`ABM_SETSTATE` toggle, as RoundedTB does at `MainWindow.xaml.cs:402-422`) → verify → mark the journal clean.
2. **In-process crash:** `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException` / native `SetUnhandledExceptionFilter` run the same restore sequence (best-effort, time-boxed).
3. **Hard kill / hang** (`taskkill /f`, debugger stop):
   - **TAP OwnerWatch inside Explorer:** waits on our process handle, restores brushes, removes the A+ hook and the region, and recomputes the work area.
   - **Watchdog process** does the same from outside, covering the case where the TAP isn't loaded.
   - Hang detection: the watchdog pings the main window. No reply for 10 s ⇒ restore and offer to kill.
4. **Next launch:** if `state.json` says *dirty* and the recorded PID is not alive, run the restore **before** anything else, log it and show a toast.
5. **Panic paths you can always reach:**
   - Global hotkey (default **Ctrl+Win+Alt+R**, configurable): restore everything and pause.
   - Tray menu: **"Restore taskbar now"** / **"Pause overlay"**.
   - Start-menu shortcut **"XBar – Restore taskbar"** → `XBar.exe --restore` (works even when another instance is wedged).
   - `tools\restore.ps1`: plain PowerShell that resets the region, recomputes the work area and optionally restarts Explorer. It does not depend on our binaries.
   - Last resort, documented in the README: `taskkill /f /im explorer.exe & start explorer.exe`.
6. **Crash-loop guards:** Explorer restarts twice in 30 s, or the guardian sees more than N work-area resets/minute ⇒ restore, pause, and notify.
7. **Safe mode:** start with Shift held or `--safe` ⇒ no changes applied, settings window only.

`state.json` (in `%LOCALAPPDATA%\XBar\`) records: schema version, PID + process start time, dirty flag, per-monitor `{deviceName, rcMonitor, originalRcWork}`, taskbar HWNDs, which modules were active (region / work area / TAP / A+ hook). It is written atomically **before** each mutation.

---

## 7. Milestones (build order)

Each milestone ends with a demo and a check-in with you.

**M0 — Toolchain & skeleton (½ day)**
Install the .NET 10 SDK and MSVC Build Tools + Windows SDK. Solution skeleton, logging, `state.json` journal, `--diag`, `--restore`, `tools/restore.ps1`. GPL-3.0 LICENSE + THIRD_PARTY_NOTICES. Quit/disable TranslucentTB.

**M1 — ⚠️ Overlay risk spike (first real feature, 2–3 days)**
A minimal tray app with safety layers 1, 3 (watchdog), 4 and 5 already in place:
- Set the work area to full −ε, keep the taskbar topmost, guardian loop, full restore.
- Rectangular `SetWindowRgn` (just an inset rect) to prove click-through outside the pill.
- Experiments, all logged:
  - ε = 0/1/2 px vs Explorer's fullscreen detection
  - Explorer reset frequency over a day
  - whether already-maximised windows re-size
  - toasts / Quick Settings / Start / Widgets placement
  - snap layouts
  - sleep/resume, Explorer restart, display change
  - one quick look each at approach B (auto-hide) and C (`ABM_REMOVE`) for comparison
- Does `SetWindowRgn` on the 25H2 XAML taskbar still behave (hit-testing, no visual corruption)?
- **Exit gate:** §8 overlay rows pass, or we agree to move to A+ (pulled forward from M4) before going further. If neither works acceptably, we stop and re-plan with you.

**M2 — Shape: rounded & floating (2 days)**
- Region-based pills: *single bar* (full width, margins, radius) and *split* (apps pill + tray pill [+ widgets]), centred/left aware.
- Geometry provider v1: Win32 child rects + UI Automation fallback, because RoundedTB's `TrayNotifyWnd` / `MSTaskSwWClass` rects are unreliable on recent Win11 builds.
- Event-driven updates via WinEvent `EVENT_OBJECT_LOCATIONCHANGE` + debounce (no 100 ms polling). DPI scaling.

**M3 — Explorer TAP: appearance (4–6 days, highest technical risk after M1)**
- C++ DLL: injection (`WH_CALLWNDPROC` + `InitializeXamlDiagnosticsEx`), visual-tree watcher, OwnerWatch restore-on-death, versioned IPC.
- Modes: normal / opaque / clear / acrylic / blur (radius) with RGBA tint.
- **Anti-aliased rounded background drawn in XAML** (rounded `Border`/`Rectangle` sized to the pill). The window region becomes a slightly larger hit-test mask only, which fixes the jagged corners of pure `SetWindowRgn`.
- Geometry provider v2: exact task-list/tray bounds from the XAML tree.
- **(A+) `SystemParametersInfoW` detour** if M1 showed it's needed.

**M4 — Dynamic states (2 days)**
WinEvent hooks + `IAppVisibility` + Search/TaskView detection, per-monitor maximised/visible tracking, priority resolution, per-app rules. (Concepts from TTB; fresh implementation.)

**M5 — Settings UI, tray, startup (3 days)**
See §9.

**M6 — Extras (pick from §8.2 after you review; 1–4 days)**

**M7 — Hardening & soak (2 days)**
Full manual matrix, multi-monitor (if you have one, see Q5), mixed DPI, a week of daily use, log review, a portable zip build.

---

## 8. Features

### 8.1 Merged features (parity)

From RoundedTB:
- Rounded corners with configurable radius; per-side margins (floating gap).
- Simple mode (one bar) and split/dynamic mode (apps / tray / widgets as separate pills, each with its own radius and margins), aware of centred vs left alignment.
- Show tray / widgets toggles; "show segment on hover".
- Optional fill-to-edge when a window is maximised. In overlay mode this becomes "solid edge-to-edge bar when maximised".
- Multi-monitor and per-monitor DPI.

From TranslucentTB:
- Appearance modes: normal, opaque, clear, blur (with radius), acrylic. Full RGBA colour picker with live preview.
- Dynamic states, each with its own appearance: desktop, visible window, maximised window, Start open, Search open, Task View open.
- Per-app rules (window class / title / process) that force an appearance.
- Show/hide the Win11 taskbar top border line.
- Power-saver awareness (fall back from acrylic).
- Explorer-restart recovery with crash-loop guard; restore-on-death.

New (core requirement):
- **True overlay:** windows use the full screen and the bar (and tray area) floats on top, with no reserved strip.

### 8.2 Proposed additions (my suggestions — tell me which to keep)

1. **Peek-through hotkey:** hold a key (e.g. Ctrl+Win). The bar fades to ~10 % and becomes click-through so you can reach content underneath (status bars, scrollbars). This is the main answer to "the overlay covers things".
2. **Per-app "reserve space" rules:** for chosen apps (e.g. Excel, terminal), the work area is restored while that app is maximised in the foreground.
3. **Idle dim:** the bar sits at X % opacity while the mouse is away and goes to full on hover. It stays visible, just quieter.
4. **Vertical float offset:** lift the pill N px above the screen edge (a true floating dock look).
5. **Border/outline:** colour, thickness and opacity; optional glow in the accent colour.
6. **Drop shadow** under the pill (XAML/Composition shadow in the TAP).
7. **Gradient tint** (two-colour, angle) and **accent/wallpaper colour sync**.
8. **Light/dark auto-switch:** different appearance per system theme.
9. **Profiles/presets** (e.g. "Work", "Gaming", "Minimal") with a hotkey or tray switch; import/export as JSON.
10. **Per-monitor profiles** (different shape/appearance per display).
11. **Fullscreen policy per app:** always hide over games, always show over e.g. a video player in borderless.
12. **Overlay ↔ reserve toggle hotkey:** instantly switch between floating overlay and classic reserved strip.
13. **Animated transitions** between states (opacity / colour fade inside XAML, not layered-window hacks).
14. **Diagnostics tab:** the live `--diag` view, reset log and "copy debug bundle".

---

## 9. Settings UI, tray and startup

- **Tray icon** (theme-aware). Left-click opens Settings. Right-click menu:
  - Pause / Resume overlay
  - Overlay ↔ Reserve
  - Profiles ▸
  - **Restore taskbar now**
  - Settings…
  - Open log folder
  - Exit (with restore)
- **Settings window:** WPF-UI, Mica, NavigationView. Pages:
  - **Overlay** (on/off, ε, fullscreen policy, peek hotkey, reserve-space rules)
  - **Shape** (single/split, radius, margins per segment, float offset; a live diagram like RoundedTB's stand-in preview)
  - **Appearance** (per-state cards: mode, colour picker, blur radius, border, shadow)
  - **Rules** (per-app)
  - **Monitors**
  - **Profiles**
  - **Startup & Safety** (run at login, panic hotkey, restore now, safe mode)
  - **Diagnostics**
  - **About** (licences/credits)
- **Live apply:** changes apply to the real taskbar immediately (debounced ~150 ms). A "Revert changes" button covers the session.
- **Startup:** HKCU `…\CurrentVersion\Run` value → `XBar.exe --startup` (no admin, no packaging). On `--startup`, wait until `Shell_TrayWnd` exists and Explorer is idle (+ ~3 s) before applying. Run recovery first if the journal is dirty.
- **Single instance:** named mutex. A second launch signals the first (named pipe) to open Settings, or with `--restore` to restore and pause.
- **Settings storage:** `%LOCALAPPDATA%\XBar\settings.json` (schema-versioned, migrations, atomic write, file watcher so hand edits apply live). Logs: `%LOCALAPPDATA%\XBar\logs\` (rolling, 7 days).

---

## 10. Testing plan

### 10.1 Automated (where practical)
- Unit tests (XBar.Core): region/pill geometry at 100/125/150/200 % DPI, centred/left; work-area math per monitor; settings migration; journal write/recover state machine; debounce and loop-guard logic.
- `--diag` assertions script: after apply, `rcWork == desired`, taskbar topmost, region present. After `--restore`, all three are back to the journal values.

### 10.2 Manual checklist (run per milestone; M1 runs the Overlay and Safety sections)

**Overlay**
- [ ] Maximise Edge, VS Code, Explorer: the window reaches the bottom edge; the bar is drawn over the content; clicking outside the pill hits the app.
- [ ] Windows that were maximised before XBar started grow to full height; after exit they shrink back.
- [ ] Snap layouts (Win+Z, drag to top, Win+←/→/↑/↓): snapped windows reach the bottom.
- [ ] The tray area (icons, clock, input indicator, volume/network/battery) overlays content and all of it stays clickable.
- [ ] Quick Settings, Notification Center, calendar flyout, Start, Search, Widgets, Task View, Alt+Tab open and position correctly.
- [ ] Toast notifications position correctly (not hidden behind the pill).
- [ ] Borderless-fullscreen video (YouTube F11, VLC) and a fullscreen game: follow the chosen fullscreen policy; bar returns after exit.
- [ ] Exclusive-fullscreen game: no flicker or stutter; bar returns after alt-tab out.
- [ ] Other topmost windows (PiP video, Task Manager "always on top"): the bar stays visible or behaves as agreed.
- [ ] Desktop with no windows: looks like the reference screenshots.

**Robustness**
- [ ] Explorer restart (`taskkill /f /im explorer.exe`, then start): overlay and appearance come back within ~5 s.
- [ ] Restart Explorer 3× quickly: crash-loop guard triggers, taskbar restored, notification shown.
- [ ] Sleep → resume; hibernate → resume; lock → unlock.
- [ ] Change resolution; change scaling 125 → 150 → 125 %; change taskbar alignment centre ↔ left.
- [ ] (If you have one) connect/disconnect an external monitor; mixed DPI; taskbar on all displays on/off.
- [ ] RDP into the machine and back (if you use it).
- [ ] Pin/unpin apps and open many windows: pill resizes correctly.
- [ ] Day-long soak: review the reset log against the threshold agreed after M1.

**Safety**
- [ ] Exit from tray ⇒ work area, region and appearance are all stock (`--diag` matches the journal).
- [ ] `taskkill /f` XBar ⇒ watchdog/TAP restore within 2 s.
- [ ] Kill XBar **and** the watchdog ⇒ the TAP still restores appearance; next launch repairs the work area (journal dirty).
- [ ] Panic hotkey, tray "Restore now", Start-menu "Restore taskbar", `restore.ps1`: each works on its own.
- [ ] Sign out / reboot while running ⇒ clean desktop next session, nothing persisted.
- [ ] Start with Shift (safe mode) ⇒ no changes applied.
- [ ] Launch with TranslucentTB running ⇒ clear warning; no fighting.

---

## 11. Risks

| Risk | Likelihood | Impact | Plan |
|---|---|---|---|
| Explorer resets the work area often → visible jumps | Medium | Medium | Measure in M1; A+ detour removes it. |
| Maximised windows count as fullscreen → bar falls behind | Medium | High | ε inset; verify in M1. |
| `SetWindowRgn` on the 25H2 XAML taskbar misbehaves (hit-test, redraw) | Low-Med | High | Tested in M1 before any styling work. Fallback: XAML-only shaping plus a transparent but hit-testable strip, which loses click-through (bad). |
| TAP breaks after a Windows update (element names change) | Medium (over time) | Medium | Fail soft to stock appearance; overlay keeps working; log a version mismatch. |
| DLL injection into Explorer flagged by antivirus / Defender | Low-Med | Medium | Personal build; add a Defender exclusion for the build folder if needed. TTB does the same thing. |
| Bugs in our TAP crash Explorer | Medium during dev | Medium | Explorer auto-restarts. Crash-loop guard. All TAP code wrapped in try/catch with no-throw boundaries. |
| Bar covers useful app content | Certain (by design) | Low-Med | Peek hotkey, reserve-space rules, idle dim. |
| Coexistence with TTB / RoundedTB / ExplorerPatcher / Windhawk mods | Medium | Medium | Detect and refuse to fight; documented. |
| Toasts/flyouts mis-positioned with a full work area | Unknown | Low | Verify in M1; may accept or adjust ε. |

---

## 12. Open questions for you

1. **"Side system panel":** do you mean (a) the **tray area of the taskbar** (hidden-icons chevron, tray icons, input indicator, network/volume/battery, clock), (b) the **Quick Settings / Notification Center flyouts** that pop up from it, or (c) the **Widgets** board? My reading is (a). (b) and (c) are flyouts that already draw over windows.
2. **Pill layout:** your RoundedTB screenshot shows one centred pill around the apps **with the tray hidden**. Do you want (a) one full-width floating bar, (b) **apps pill + separate tray pill** (RoundedTB "dynamic + show tray"), or (c) both as selectable modes? I recommend (c), defaulting to (b).
3. **Fullscreen:** "visible at all times" over **true fullscreen games and video** too, or should the bar hide there (normal Windows behaviour)? I recommend hiding over fullscreen by default, with a per-app override.
4. **Covered content:** are you OK with the bar covering the bottom ~48 px of maximised apps? Do you want the peek hotkey and per-app reserve rules (§8.2 #1, #2)?
5. **Monitors:** do you ever use an external monitor or projector? (Only one display is connected now.) Should the overlay apply to all of them?
6. **TranslucentTB:** OK to quit it and disable its startup while XBar runs? XBar replaces it.
7. **Explorer injection:** are you comfortable with XBar injecting a DLL into `explorer.exe` (required for blur/acrylic/clear on your build; it's what TranslucentTB does today)? Without it we get shape + overlay but only Explorer's default background.
8. **Toolchain install:** OK to install the .NET 10 SDK and VS 2022 Build Tools (C++ workload + Windows 11 SDK) via winget? (One UAC prompt.) Or would you rather have the full Visual Studio IDE?
9. **Name:** "XBar" is a placeholder. Any preferred name?
10. **Taskbar alignment:** you use centred. Must left-aligned also be supported from day one, or later?
11. **Licence:** OK with GPL-3.0-or-later for XBar (recommended, see §4)?
12. **Extras:** which of §8.2 #1–14 do you want, and in what priority?
