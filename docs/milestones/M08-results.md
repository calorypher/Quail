# M08 results — WinUI 3 vs Avalonia

## Decision

**Recommend WinUI 3 for Quail 0.2.** This recommendation uses only the fresh final matrix committed in [M08 evidence](evidence/M08/), after the cross-DPI and lifecycle QA fixes.

## Source and environment

- Branch: `m08-ui-framework-spike`; this report is committed with the QA-fix source and does not claim a pre-fix SHA as its own final identity.
- Host: Windows 11 Pro build 26200, physical unelevated desktop, Balanced power plan, AMD Ryzen 7 7800X3D (8 cores / 16 logical processors), 64 GiB installed RAM, NVIDIA GeForce RTX 4080 SUPER driver 32.0.16.1088.
- Toolchain: .NET SDK 10.0.400, .NET runtime 10.0.11, MSBuild 18.9.6, Release x64.
- Display topology: extended physical desktop. DISPLAY1 is 3440 x 1440 at 100% / 96 DPI; DISPLAY2 is 3840 x 2160 at 150% / 144 DPI. The durable [environment.json](evidence/M08/environment.json) records the measurement APIs, current signal modes, physical desktop bounds, scaling, and DPI.
- Packages: Microsoft.WindowsAppSDK 2.4.0; Avalonia 12.1.1.
- Both candidates are unpackaged, framework-dependent `win-x64` Release applications launched directly from build output. No self-contained, MSIX, installer, or M09 deployment comparison was performed.

The display probe ran in a fresh `PER_MONITOR_AWARE_V2` process using `EnumDisplayMonitors`, `EnumDisplaySettings`, and `GetDpiForMonitor`. The prior 2560 x 1440 / 96-DPI TV observation was DPI virtualization and is not used.

## Equivalent protocol and QA corrections

`visible-ready` means that the overlay is visible and foreground, its query textbox has framework-level keyboard focus, and the first rendered presentation point has been reached. Both candidates queue the event after their render phase and call `DwmFlush()` before emission. It contains the framework focus flag, foreground HWND diagnostics, actual DPI, physical position, and physical window size. The harness verifies framework focus plus foreground HWND and requires a 680 x 360 logical overlay to be within 4 physical pixels of the DPI-derived dimensions.

Shell type icons are deferred until after `visible-ready` for both candidates. Resident warm-up performs summon, render, icon initialization, and hide before the first resource snapshot. The harness waits a bounded 100 ms after each confirmed hide before injecting the next real hotkey; this avoids queuing the next input before Avalonia's asynchronous hide transition completes and does not alter cold-start or hotkey-to-visible timing boundaries.

The WinUI manual defect was caused by manually scaling `AppWindow.Resize` before moving across a per-monitor-DPI transition. The native DPI transition then applied conflicting scaling on the first summon. WinUI now lets the PMv2 transition determine the actual HWND size, centers that real HWND after the render/DWM presentation point, and emits the event afterward. The physical-size check prevents regression.

Avalonia now starts resident and hidden unless the explicit test-only `--m08-show-on-start` option is used. Its global hotkey has toggle semantics, and keyboard selection calls `BringIntoView` and emits a lightweight scroll-follow observation for the harness. The earlier explicit-Exit hang was an implementation defect: `Window.Close()` re-entered `Deactivated -> HideOverlay`. The common Exit path now marks closing, closes and detaches the window, then invokes explicit lifetime shutdown.

## Fresh final comparable measurements

All raw final values are durable: [WinUI CSV](evidence/M08/winui-metrics.csv) and [Avalonia CSV](evidence/M08/avalonia-metrics.csv). Each series passed 5 cold-start warmups, 30 measured cold starts, resident warm-up, 100 real `SendInput` hotkeys, 50 keyboard flows, 500 summon/Escape cycles, 120 hidden-idle samples, and DISPLAY1 -> DISPLAY2 -> DISPLAY1 placement.

| Metric | WinUI 3 | Avalonia |
|---|---:|---:|
| Cold start p50 / p95 / max / avg (30) | 387.373 / 400.212 / 404.531 / 387.805 ms | 969.572 / 1006.655 / 1035.299 / 974.373 ms |
| Hotkey-to-ready p50 / p95 / max / avg (100) | 23.490 / 27.876 / 31.652 / 23.704 ms | 12.498 / 15.127 / 16.287 / 12.408 ms |
| Keyboard flow | 50/50 pass | 50/50 pass |
| Summon/Escape lifecycle | 500/500 pass | 500/500 pass |
| Mixed-DPI placement and physical size | 680x360 @ 96, 1020x540 @ 144, 680x360 @ 96; pass | 680x360 @ 96, 1020x540 @ 144, 680x360 @ 96; pass |
| Hidden idle working set average | 182,928,418 B (174.5 MiB) | 214,637,807 B (204.7 MiB) |
| Hidden idle private bytes average | 179,051,383 B (170.8 MiB) | 224,614,434 B (214.2 MiB) |
| Hidden idle CPU | 0.002% | 0.002% |
| Hidden idle handles / USER / GDI average | 1,270 / 51 / 109 | 917 / 696 / 75 |

Both working sets remain above Quail's eventual approximately 100 MB idle target. These spikes are framework-selection evidence, not proof that the later product meets that target.

## Resource trend after resident warm-up

WinUI private bytes rose from 171,786,240 B at post-warm-up baseline to 181,227,520 B at cycle 50, then remained within a narrow range (178,823,168 B at cycle 100; 179,564,544 B at cycle 500) and settled at 179,015,680 B. Handles settled from 1,293 to 1,269, USER objects from 55 to 51, and GDI remained 109. This is a bounded initialization/cache plateau, not evidence of monotonic growth over 500 cycles.

Avalonia private bytes rose from 184,827,904 B to 225,775,616 B by cycle 50 and stayed near that level through the final settled 224,595,968 B; working set likewise plateaued near 205 MiB. Handles decreased from 941 to 915 and GDI remained 75. However, USER objects rose monotonically from 48 after warm-up to 247 / 297 / 447 / 697 at cycles 50 / 100 / 250 / 500 and remained 696 after the settle plus 120-second idle observation. Therefore Avalonia resource stability is **not** marked pass: this is a material persistent USER-object growth signal that needs focused follow-up before selecting Avalonia for a long-lived launcher process. No forced GC was used.

## Manual QA findings

WinUI's pre-fix first cross-DPI summons were visibly oversized on DISPLAY2 and undersized returning to DISPLAY1; this is fixed and covered by automated physical-size regression checks. Its tray context menu looks more native to Windows, but the current spike's light styling was manually assessed as provisional and weak.

Avalonia's cross-DPI geometry was correct. Manual QA found and this pass fixed its initial visible/wrong-placement startup, non-toggle hotkey behavior, and lack of keyboard scroll-follow. The current Avalonia prototype was manually assessed as visually stronger than the WinUI spike, especially in light mode, while its tray context menu looks less native on Windows. These qualitative observations are not a pixel-perfect score and do not override performance, residency, or Windows-integration evidence.

## M09 input and Linux assessment

WinUI's validated M08 form is unpackaged and framework-dependent with `WindowsPackageType=None`; M09 must independently decide how an installer satisfies Windows App SDK runtime/bootstrap requirements. This milestone does not choose an installer, MSIX, or self-contained deployment.

Avalonia is portable in principle, but this spike deliberately relies on Windows hotkeys, Shell icons, tray behavior, and monitor APIs. It does not establish a low-cost Linux path or justify cross-platform abstractions.

## Final manual QA

Final manual regression QA passed for both frameworks on the extended two-display host. WinUI passed resident start, DISPLAY1 100% summon, first DISPLAY2 150% summon, return to DISPLAY1 summon, click-outside deactivation, real tray Show, real tray Exit, and no ghost tray icon after Exit. Avalonia passed hidden resident startup, Ctrl+Alt+Space show -> hide -> show toggle, click-outside deactivation, real tray Show, real tray Exit, and no ghost tray icon after Exit. The previously completed automated keyboard scroll-follow coverage remains valid.

M08 verification is complete and ready for merge decision. No further repeatable benchmark or manual tray coverage is required for this milestone. The Avalonia USER-object growth remains a documented resource-stability finding, not a proven framework leak.

## Recommendation rationale

WinUI reaches equivalent `visible-ready` about 2.5 times faster at p50, uses about 30 MiB less hidden working set and 43 MiB less private memory, and showed a stable USER/GDI trend across 500 cycles. Avalonia wins hotkey latency and has the stronger current prototype visual styling, but both hotkey results are immediate. Its persistent USER-object growth is an additional material residency risk. For Windows-only Quail 0.2 and its lightweight-operation constraint, WinUI's startup, settled residency, resource trend, and more native tray behavior outweigh the additional Win32/XAML lifecycle and deployment friction.
