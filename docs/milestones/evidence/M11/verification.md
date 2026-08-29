# M11 verification evidence

## Controlled physical-host index

No pre-existing complete host index was present in `%LOCALAPPDATA%\Quail\Indexes`. The repository's existing `IndexStore.BuildFromRecords` test seam was used explicitly to build an ignored, schema-current SQLite index under `artifacts/m11/host-fixture/` from three real host entries: two files and one folder. It was not hand-authored SQLite and did not build or modify a host volume.

The reopened CLI search returned all three `quail` entries with resolved paths. The production self-contained app searched the same database and opened the controlled `quail-alpha.txt` through `IndexedEntryOpener`; its app log recorded `Open succeeded` followed by `Hide: open-success`.

## Self-contained payload

The first M11 production harness exposed a missing transitive `Microsoft.Data.Sqlite` payload. M11 adds the explicit App package reference and the final publish contains `Microsoft.Data.Sqlite.dll`, `SQLitePCLRaw.*`, and `e_sqlite3.dll`. This was verified before the final runtime rerun.

## Short lifecycle, keyboard, and resource run

`spikes/m10/harness` was minimally extended to forward repeated `--index` values and to support `--short`; production behavior is unchanged. The final self-contained run passed 5/5 hotkey/focus cycles, 1/1 keyboard flow including rapid five-character input, Down/Up and Enter-to-open, and 20/20 summon/Escape cycles.

The five hotkey-to-visible samples were 23.281, 19.297, 23.987, 22.999, and 20.270 ms. The controlled `quail` search completed in 17.6 ms in the app log; four intermediate typed-query generations were coalesced/discarded. The short resident sample reported 0.00962% idle CPU and the harness resource assessment was `pass`. It is a short sanity check, not a replacement for M10's full 500-cycle baseline.

## QA-fix round

The final QA-fix production run used the updated self-contained output. It passed 5/5 hotkey/focus cycles, 1/1 keyboard flow including Enter-to-open, and 20/20 summon/Escape cycles. Its final `quail` request completed in 17.5 ms after five typed characters, and the short harness resource assessment remained `pass` with 0.00963% hidden-idle CPU.

The final short-query scheduling runtime run used `artifacts/m11/publish/self-contained-performance/`. It again passed 5/5 hotkey/focus cycles, 1/1 keyboard/Enter-to-open, and 20/20 summon/Escape cycles. The final controlled `quail` request completed in 17.7 ms. Its five hotkey samples were 18.755, 17.846, 22.446, 21.385, and 20.023 ms; hidden-idle CPU was 0.00969% and the resource assessment was `pass`. The rapid input log contained no 1- or 2-character Core search request.

### Final physical-QA interaction and empty-state fix

Physical QA found that a result-row mouse click only transferred native ListView focus: it had no primary action, showed the default white focus rectangle, and diverted Enter/arrow behavior away from the keyboard-first query workflow. Result rows now use the WinUI single-click interaction mode and route directly to the same `OpenSelectedResult` method used by Enter. The list has no native selection/focus ownership; the existing Quail selected-row visual is maintained explicitly for keyboard navigation. On click-open success the controlled item opens and the overlay hides. On failure the overlay stays visible, shows the existing concise error, and the app restores `QueryBox` keyboard focus before emitting the test event.

The final self-contained run at `artifacts/m11/publish/self-contained-physical-qa/` passed a 1/1 real single-click open, a 1/1 keyboard Enter-open, 5/5 hotkey/focus cycles, and 20/20 summon/Escape cycles. The controlled click log recorded `Result click: quail-alpha.txt.`, followed by `Open succeeded.` A separate current-schema controlled fixture deliberately contained a missing `quail-missing.txt`; its 1/1 click-open failure reported restored `QueryBox` focus, remained available until Escape, and passed the same short lifecycle/resource sanity run.

Empty QueryBox now uses compact search-only mode. The harness verified `Compact → Expanded → Compact` at actual HWND heights 80 → 370 → 80 after typing and clearing a query, and verified centering on the cursor monitor after each transition. The window resizes only through `AppWindow.Resize`, then recenters from the actual HWND rectangle; it introduces no manual DPI scaling. Results and the footer collapse in compact mode, so stale rows are not visible. Future empty-state content may include recent searches or opened items; M11 intentionally does not add it.

`ResultSelection` is a small neutral seam used by the window and tests: Up, Down, Home, and End are all no-ops for an empty result list, including no-index, searching, zero-result, and error states. The coordinator now retains at most one replaceable pending request; a regression test sends 199 pending requests during a blocked first search and observes only the first and newest execution.

### Short-query performance and scheduling decision

An ignored, current-schema fixture built through `IndexStore.BuildFromRecords` contained 850,000 file entries. It is deliberately synthetic and is not presented as a host filesystem benchmark. Seven post-warm-up `IndexStore` measurements produced these medians:

| Query | Path | Median |
| --- | --- | ---: |
| `a` | 1-character SQLite fallback, broad | 196.001 ms |
| `ab` | 2-character SQLite fallback, broad | 168.845 ms |
| `abc` | FTS5 trigram, broad | 79.602 ms |
| `selective-needle` | FTS5 trigram, selective | 7.045 ms |
| `qzq` | FTS5 trigram, zero result | 5.998 ms |

The app's actual single-index `MultiIndexSearch` path measured 211.047 ms for broad `a`, 184.018 ms for broad `ab`, and 90.654 ms for broad `abc`. These values are directionally consistent with the historical M04 physical-index direct-scan evidence of approximately 250–350 ms at 859,000 entries; this fixture is evidence for the current schema and scheduling decision, not a replacement physical-host benchmark.

Five rapid-typing trials deliberately allowed `a` to enter the synchronous Core path before sending `ab` and `abc`. The final `abc` completion median was 301.046 ms. After the scheduling change, five equivalent rapid trials scheduled `a` and `ab` within the short-query window and immediately submitted `abc`; no short query executed and final `abc` completion median was 93.136 ms.

M11 therefore uses a 150 ms trailing defer only for trimmed queries of length 1–2. The value is shorter than the measured 169–196 ms direct short-query work, which avoids starting that materially expensive work during normal rapid input while still executing a deliberate short query after a brief pause. Queries of length 3 or more bypass the defer completely. The timer owns one replaceable pending short query, then forwards it to the existing single-slot latest-request coordinator; it does not block the UI or create a search backlog. Unit coverage confirms paused `a` and `ab` execute, a newer short query replaces the previous one, and cancellation prevents an intermediate short query from running.

The final publish guard explicitly requires `Microsoft.Data.Sqlite.dll`, the three used `SQLitePCLRaw` managed assemblies, and `e_sqlite3.dll`. The final self-contained output passed this guard and runtime search.

The fallback glyph is now collapsed once a real `ResultItem.Icon` is assigned. Exact final visual icon rendering remains a physical visual-QA item.

### Final mixed-DPI sizing regression

M11 compact/expanded `AppWindow.Resize` initially treated effective layout dimensions as physical screen pixels, causing cross-DPI size regression. A 700 × 80 effective layout could therefore become approximately 467 × 53 physical pixels on a 150% monitor. The fix keeps `QuickSearchOverlayLayout` as the logical definition and converts it with `Round(logical × currentHwndDpi / 96)` only after the hidden/resident HWND has moved to the cursor monitor and native PMv2 DPI transition has settled.

Each summon now follows this sequence: resolve the cursor monitor; move the hidden HWND there using its current actual geometry; flush the native transition; read `GetDpiForWindow`; resize through `AppWindow.Resize` using the converted physical size; read the actual HWND rectangle; and center that actual rectangle on the target monitor. Same-monitor compact/expanded changes use the current HWND DPI and then recenter from the actual rectangle. No target-DPI resize is attempted before the HWND transition, and no DPI-specific dimensions are hard-coded.

The M10 production harness now records `GetDpiForWindow`, actual HWND geometry, placement, query focus, and uses a ±4-pixel physical-size tolerance. The final self-contained run exercised true summon-on-cursor-monitor behavior rather than drag movement: first summon on the 144-DPI monitor produced 1050 × 120 compact, 1050 × 555 expanded, then 1050 × 120 after clear; return summon on the 96-DPI monitor produced 700 × 80, 700 × 370, then 700 × 80; a second 144-DPI summon repeated 1050 × 120, 1050 × 555, then 1050 × 120. All placements were centered on the cursor monitor and all compact summons reported QueryBox focus.

The same final run also passed the controlled real-file single-click open and keyboard Enter-open smoke, compact → expanded → compact cycle, 5/5 hotkey/focus cycles, 20/20 summon/Escape lifecycle cycles, and the short resource sanity check (`pass`, 0% measured hidden-idle CPU). The self-contained output at `artifacts/m11/publish/self-contained-mixed-dpi/` passed the SQLite payload guard with 533 files and 239,991,714 bytes.

### Final Settings compact-host fix

This is a development-only M11 QA fix. Physical QA found that opening the in-host Settings dialog while an empty query kept the host at compact height, clipping the dialog. `ShowSettings` now marks the dialog active, expands through the existing DPI-aware `ApplyOverlayMode` path, awaits its resize/recenter completion, and only then shows `SettingsDialog`. The active-dialog state also prevents a delayed empty-query `TextChanged` event from collapsing the host during that transition.

After `ShowAsync` has actually completed, the active-dialog state is cleared and the same layout path is applied from the current trimmed query: empty returns to compact; non-empty remains expanded. QueryBox is focused before the `settings-closed` instrumentation event. The host never collapses while the dialog is open. Enter and Escape are intentionally not captured as hotkey candidates while the hotkey text box has focus, so normal ContentDialog Save and Cancel paths remain available; all other hotkey capture behavior is unchanged.

The final real-input self-contained harness exercised Settings at both available DPIs. At 96 DPI: compact → Settings produced 700 × 370, Save with an empty query restored 700 × 80, Settings from an expanded query stayed 700 × 370 through Cancel. At 144 DPI the corresponding physical sizes were 1050 × 555, 1050 × 120, and 1050 × 555. Both 96- and 144-DPI cycles restored QueryBox focus after Save and Cancel, and the log confirmed hotkey capture suspension/restoration. The same run passed 2/2 Settings Save cycles, 2/2 Settings Cancel cycles, 1/1 real click-open, 1/1 keyboard Enter-open, 5/5 hotkey/focus, 20/20 lifecycle, and resource sanity (`pass`, 0% measured hidden-idle CPU). The final guarded publish is `artifacts/m11/publish/self-contained-settings-qa/` with 533 files and 239,995,682 bytes.

## Known limitations

The physical host had no naturally maintained full-volume index, so this evidence uses the safe controlled compatible index rather than a host-volume build requiring elevation. M12 remains responsible for normal index selection and management. The M11 icon cache uses Shell-provided type icons rather than exact per-file Explorer icons; this keeps icon lookup off the search path and bounded.
