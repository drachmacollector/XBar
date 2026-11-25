# Third-party notices

XBar is free software under the GNU General Public License v3.0 or later (see `LICENSE`).

## Projects XBar learned from

- **RoundedTB** by torchgm (https://github.com/torchgm/RoundedTB), GPL-3.0.
  Ideas used: shaping Explorer's taskbar with `SetWindowRgn`, the work-area-minus-gap
  approach of its auto-hide mode, split "dynamic" segments. No code copied.

- **TranslucentTB** by the TranslucentTB contributors (https://github.com/TranslucentTB/TranslucentTB), GPL-3.0.
  Ideas used: injecting a XAML Diagnostics TAP into Explorer, replacing the taskbar's
  `BackgroundFill` brush, restoring when the owner process dies, dynamic taskbar states.
  Code adapted (with headers noting it): `src/XBar.Tap/effects/*` (Gaussian blur, flood and
  composite effects and the blur brush); the injection and tree-watching flow in
  `src/XBar.Tap/dllmain.cpp` and `src/XBar.Tap/tap.cpp` follows ExplorerTAP's structure.

## Runtime components

- .NET 10 / WPF / Windows Forms (MIT), Microsoft.
- C++/WinRT headers from the Windows SDK, Microsoft.
- xUnit (Apache-2.0), tests only.
