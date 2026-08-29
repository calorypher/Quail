# Host automation context

The Codex terminal desktop accepted the M10 self-contained process and reported real post-render `visible-ready` events. The final M10 lifecycle harness was later able to inject real `SendInput` into the normal physical desktop session and completed 100 hotkey, 50 keyboard, and 500 lifecycle cycles; see `production-lifecycle.md`.

The same context did permit direct process validation: the self-contained Quail process created `Quail Quick Search`, logged `Visible-ready`, and a second launch exited as a secondary instance after requesting activation of the primary.

The final Quail startup did not report a `Shell_NotifyIcon(NIM_ADD)` failure. Tray action verification was recorded separately by focused physical-host retest: LMB, RMB menu, Show, Settings, and Exit passed.
