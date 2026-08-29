# M11 results

## Status

Ready for independent QA.

## Delivered

- Production `Quail.exe` now references `Quail.Core` directly and accepts repeated temporary `--index` arguments.
- Dummy M10 search results were removed. Non-empty input uses `MultiIndexSearch` with the existing literal name-match semantics, fail-closed source handling, Core ordering, and global 50-result limit.
- `LatestFileSearchCoordinator` keeps Core work off the UI thread, coalesces queued input, and prevents stale completion/error updates from replacing a newer query or a hidden overlay.
- One- and two-character queries defer for 150 ms before entering Core. Queries of three or more characters still start immediately, so rapid typing skips avoidable SQLite fallback scans without adding a global debounce.
- `ResultItem` is now a file-specific presentation model containing source identity, native file ID, name/path, kind, compact metadata, and asynchronous icon state.
- `IndexedEntryOpener` is used through the selected result's original source identity. Success hides the overlay; failure leaves it visible with a concise error.
- A result row is a single-click primary action. It uses the same open path as Enter, returns keyboard focus to the query surface, and preserves the keyboard selected-row visual without leaving native ListView focus on a row.
- Empty Quick Search is a compact 700 × 80 search-only surface. Non-empty input expands it to 700 × 370; clearing input collapses it again. Future empty-state content may include recent searches or opened items, but M11 intentionally keeps this state search-only.
- Opening Settings from compact mode first expands the host through the existing DPI-aware layout path, then shows the in-host dialog. After Save or Cancel, an empty query returns to compact mode; a non-empty query remains expanded, and QueryBox focus is restored.
- Overlay layout dimensions remain logical/effective pixels. On every summon and compact/expanded resize, Quail obtains the current HWND DPI after the native monitor transition and passes the corresponding physical size to `AppWindow.Resize`.
- Windows Shell type icons use an asynchronous 128-entry LRU cache with glyph fallback and explicit `HICON` release.
- The self-contained publish now explicitly carries the SQLite dependency chain required by the new Core runtime path.
- Empty-result Up/Down/Home/End navigation is a safe no-op. The pending-query coordinator uses one replaceable latest slot rather than an unbounded queue, and startup parsing rejects `--index` followed by another option.

## Verification

- `dotnet test tests\Quail.Core.Tests\Quail.Core.Tests.csproj -c Release --no-restore`: 98 passed, 0 failed.
- `dotnet build src\Quail.App\Quail.App.csproj -c Release -r win-x64 --no-restore`: passed, 0 warnings/errors.
- `scripts\publish-m10.ps1 -Output artifacts\m11\publish\self-contained-settings-qa`: passed; final output has 533 files and 239,995,682 bytes, including the SQLite payload guard artifacts.
- A controlled current persistent index built by Core from real physical-host fixture files passed reopened CLI search and production self-contained GUI search/open.
- Short M10 harness regression passed: compact → expanded → compact sizing (80 → 370 → 80), 1/1 single-click open, 1/1 keyboard/Enter-open, 5/5 hotkey/focus, and 20/20 lifecycle; resource assessment passed. Details are in [M11 evidence](evidence/M11/verification.md).
- The mixed-DPI first-summon regression passed on the available 96/144-DPI monitors: 144 DPI produced 1050 × 120 compact and 1050 × 555 expanded; the return to 96 DPI produced 700 × 80 compact and 700 × 370 expanded; the second 144-DPI summon repeated the correct sizes.
- Settings runtime verification passed at both available DPIs. At 96 DPI, opening from compact produced 700 × 370 and Save returned to 700 × 80; at 144 DPI, the equivalent sizes were 1050 × 555 and 1050 × 120. Settings opened from an already expanded query remained expanded after Cancel. QueryBox focus and hotkey-capture Save/Cancel restoration passed.
- An ignored 850,000-entry current-schema synthetic fixture measured the 1/2-character fallback at realistic scale. It justified a short-query-only 150 ms defer; it is not a physical-host benchmark.

## Deferred

M12 owns a persistent index catalog and GUI management/build/sync flows. M13 owns installer integration and a full release-quality desktop performance/resource campaign. M11 does not introduce a service, IPC, ranking, fuzzy/path/content search, or other providers.
