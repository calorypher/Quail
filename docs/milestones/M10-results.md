# M10 results

Status: complete; physical-host retests and final production lifecycle/focus/resource verification are recorded.

## XAML publish regression

The initial self-contained publish failed while constructing `QuickSearchWindow` with `Microsoft.UI.Xaml.Markup.XamlParseException` (`0x802B000A`). The Release build output contained `App.xbf`, `QuickSearchWindow.xbf`, and `Quail.pri`; the publish output contained none of them. This was a packaging payload defect, not XAML syntax, resource resolution, binding, converter, custom template, or asset-URI failure.

`scripts/publish-m10.ps1` now copies and verifies those three WinUI artifacts plus all approved runtime branding assets. After the change, self-contained startup emits `visible-ready` with a real HWND and focused query box.

## PASS

- Release build is clean;
- the existing 53 Core tests and 22 M10 non-interactive tests pass (75 total);
- self-contained publish is complete and payload-guarded;
- self-contained startup creates a 700×370 real HWND and reaches `visible-ready` with query focus;
- single-instance activation forwarding is verified by a second process exiting after activating the primary;
- settings default, normalization, save/reload, malformed JSON fallback, invalid-hotkey fallback, and temporary-file cleanup are covered by automated tests;
- tray VERSION_4, legacy, legacy-shaped fallback, and managed `NOTIFYICONDATAW` layout assertions are covered by automated tests;
- physical-host retest passed taskbar exclusion, Alt-Tab exclusion, global hotkey, query focus, Escape/deactivation hide, keyboard result flow, System/Light/Dark themes, settings persistence, mixed DPI, monitor centering, tray LMB/RMB menu actions, the capture-only hotkey recorder (including the active combination and `Use default`), and outer white-border removal.
- final production self-contained verification passed 100/100 real global-hotkey/focus cycles, 50/50 keyboard-flow cycles, and 500/500 summon/Escape lifecycle cycles; it recorded p50/p95/max/average hotkey-to-ready latency of 20.770/26.712/32.536/21.457 ms, stable USER/GDI/handle checkpoints, and 0.000805% hidden-idle CPU. See `docs/milestones/evidence/M10/production-lifecycle.md`.

## Tray callback root cause and remediation

The actual Windows 11 tray callback was legacy-shaped despite an apparently successful `NIM_SETVERSION` call: `wParam=1`, `lParam=0x0200`, `HIWORD(lParam)=0`. The controller therefore decoded a VERSION_4 callback with icon ID zero and ignored it. The confirmed code-level cause was an implicit P/Invoke import of `Shell_NotifyIcon`: C# default charset selection bound the call to the ANSI entry point while the managed structure was `NOTIFYICONDATAW`. The ANSI native interpretation reads the `uVersion` union at a different offset, so a true return from `NIM_SETVERSION` did not prove that version 4 was selected.

All `NIM_ADD`, `NIM_SETVERSION`, and `NIM_DELETE` operations now call an exact `Shell_NotifyIconW` import with `CharSet.Unicode`, `ExactSpelling=true`, and the matching `NOTIFYICONDATAW` layout. The application logs and tests verify the x64 managed size (976 bytes), `uTimeout/uVersion` offset (816), and supplied version 4. A normal VERSION_4 callback for icon ID 1 is now expected to encode the event in `LOWORD(lParam)` and ID 1 in `HIWORD(lParam)` (for example, `0x00010205` for `WM_RBUTTONUP`); `wParam` is its anchor coordinates.

As a defensive per-callback fallback, an unambiguous legacy shape (`wParam == 1`, a known whole `lParam` mouse/notification message, and zero VERSION_4 icon ID) is decoded as legacy with one bounded diagnostic. That fallback is not the root-cause fix. The native popup lifecycle retains `SetForegroundWindow`, `TrackPopupMenuEx`, and `PostMessage(WM_NULL)`; the missing `WM_NULL` was a valid lifecycle defect, but was not the root cause of the observed absence of all tray actions.

## Focused UI corrections

The Settings recorder is read-only/capture-only: focusing it temporarily unregisters the active global hotkey so the currently active combination can be captured, a physical combination replaces the canonical display text directly, and `Use default` explicitly restores `Ctrl+Alt+Space`. Save registers the new value through the existing rollback path. Cancel closes only after the previous hotkey is registered again; a restore failure keeps the dialog open and displays an error rather than leaving Quail without an active hotkey. The focused physical-host retest passed this UX.

The search TextBox keeps its surface geometry and uses accepted final content padding `4,3,0,0`: 4 DIP separates the text/caret from the feather column, while the additional 1 DIP top correction moves placeholder, text, and caret optically downward.

`DWMWA_BORDER_COLOR = DWMWA_COLOR_NONE` remains applied but was insufficient: the residual WinUI white edge came from the non-client `WS_DLGFRAME` retained by the frameless `OverlappedPresenter` configuration. The local WinUI non-resizable-white-border workaround removes only `WS_DLGFRAME` from the existing HWND and issues `SWP_FRAMECHANGED`; the existing `WS_EX_TOOLWINDOW`, no-app-window, rounded-corner, DWM, and DPI behavior remain in place. The focused physical-host retest passed this removal.

## Scope boundary

No additional visual polish was performed after `QueryBox.Padding = 4,3,0,0` was accepted. Task Manager Processes icon remains the non-blocking M13 packaging/polish follow-up described below.

## Build-time application icon

`Assets/quail-app-icon.ico` deterministically packages the approved 16 px small-icon plus unchanged 32 px, 48 px, 64 px, 128 px, and 256 px app-icon PNG frames. `Quail.App.csproj` embeds it in `Quail.exe` with `ApplicationIcon`; programmatic PE-resource verification confirms the six embedded frame hashes in both Release and self-contained publish outputs. The ICO is build-time only and does not add a runtime dependency or require publish payload copying. Runtime `WM_SETICON` continues to use the native 32 px and 48 px PNGs, and the tray continues to use the separate approved 16 px PNG.

Explorer application icon, embedded PE resources, and runtime branding passed physical-host checks. Task Manager Processes still renders a generic icon despite the approved 16 px ICO frame. This is a non-blocking known limitation for packaging/polish follow-up (naturally M13); M10 makes no further branding or asset changes for it.
