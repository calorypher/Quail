# M08 shared prototype contract

Both prototypes load `mock-results.json` unchanged. They must use the file as the only result source and must not reference Quail.Core, SQLite, or the real filesystem index.

The test-only command-line interface is deliberately small:

```text
--m08-pipe <name>       Named-pipe server supplied by the harness.
--m08-theme <system|light|dark>
--m08-show-on-start     Show the overlay once after startup.
--m08-test-exit-after-visible-ready-count <n>
                         Test-only: invoke the prototype's normal Exit path
                         after the nth `visible-ready` event.
```

When a pipe name is supplied, each prototype writes one UTF-8 JSON object per line. Required events are:

```json
{"event":"visible-ready","framework":"winui|avalonia","hwnd":123,"focusHwnd":456,"queryHasKeyboardFocus":true,"windowDpi":144,"windowLeft":3440,"windowTop":120,"windowWidth":1020,"windowHeight":540}
{"event":"startup-hidden"}
{"event":"query-changed","query":"quail","resultCount":3}
{"event":"selection-changed","index":1,"name":"..."}
{"event":"selection-scroll-requested","index":12}
{"event":"confirmed","name":"..."}
{"event":"hidden"}
```

`visible-ready` is emitted only after the overlay is visible and foreground, its query textbox has framework-level keyboard focus, and the first rendered presentation point has been reached. Each prototype queues the signal after its framework render phase and calls `DwmFlush()` on Windows before emitting it. `focusHwnd` is diagnostic only: the required focus assertion is `queryHasKeyboardFocus` because framework controls do not own individual HWNDs. The event also records actual window DPI, top-left physical desktop coordinates, and physical `windowWidth`/`windowHeight` for DPI/monitor validation. The harness requires a 680 x 360 logical overlay to report physical sizes within 4 pixels of the DPI-derived size. The normal process starts hidden; `--m08-show-on-start` is test-only. The hotkey remains `Ctrl+Alt+Space`, is exercised through `SendInput`, and toggles hidden -> visible -> hidden.

The test harness owns the pipe and all measurement statistics. Framework projects own their UI, direct Windows interop, and emission of these events.
