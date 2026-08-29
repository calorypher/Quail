# Automated test matrix

| Area | Result | Evidence |
| --- | --- | --- |
| Existing Core behavior | PASS | 53 existing tests remain included in the 75-test run. |
| Settings defaults | PASS | `M10SettingsStoreTests.LoadAsync_UsesDefaultsWhenConfigDoesNotExist` |
| Settings save, reload, normalization, replacement cleanup | PASS | `M10SettingsStoreTests.SaveAsync_ReloadsNormalizedSettingsAndReplacesPreviousFile` |
| Malformed settings JSON | PASS | `M10SettingsStoreTests.LoadAsync_FallsBackForMalformedJson` |
| Invalid stored hotkey | PASS | `M10SettingsStoreTests.LoadAsync_FallsBackForInvalidStoredHotkey` |
| VERSION_4 tray decode | PASS | `M10TrayCallbackTests.DecodeVersion4_*` |
| Legacy tray decode fallback | PASS | `M10TrayCallbackTests.DecodeLegacy_*` |
| Legacy-shaped callback defense | PASS | `M10TrayCallbackTests.IsUnambiguousLegacyShape_*` protects a real legacy-shaped callback even when VERSION_4 was requested. |
| `NOTIFYICONDATAW` P/Invoke/layout | PASS | `M10NotifyIconLayoutTests` asserts the x64 size/union offset and exact Unicode `Shell_NotifyIconW` import. |
| Hotkey capture canonicalization | PASS | `M10HotkeyCaptureTests` covers canonical output, modifier-only rejection, modifier requirement, and Space. |
| Hotkey capture lifecycle | PASS | `M10HotkeyCaptureSessionTests` covers begin, save, and cancel completion only after previous-hotkey restoration. |
| Release executable icon | PASS | `scripts/verify-m10-app-icon.ps1` found six embedded PNG frames with approved SHA-256 values. |
| Self-contained executable icon | PASS | `scripts/verify-m10-app-icon.ps1 -Executable artifacts\\m10\\publish\\self-contained\\Quail.exe` found the same six approved frames. |
| Window style and DWM border attribute | PASS | Controlled startup logged `WS_EX_TOOLWINDOW`, no `WS_EX_APPWINDOW`, `WS_DLGFRAME` removal with frame refresh, and `DWMWA_BORDER_COLOR = DWMWA_COLOR_NONE`; physical-host retest confirmed no visible white border. |
| Global hotkey/focus, keyboard input, and lifecycle | PASS | `spikes/m10/harness` exercised the final self-contained executable with real `SendInput`: 100/100 hotkey, 50/50 keyboard, and 500/500 lifecycle cycles. See `production-lifecycle.md`. |
| Tray actions | PASS | Physical-host retest passed LMB, RMB menu, Show Quick Search, Settings, and Exit after the exact `Shell_NotifyIconW` fix. |
