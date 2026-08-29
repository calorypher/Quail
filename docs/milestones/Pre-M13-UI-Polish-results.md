# Pre-M13 UI Polish results

## Status

**COMPLETE — automated, manual, and independent QA PASS.**

This is the final durable evidence record for the accepted Pre-M13 UI Polish milestone.

## Implementation source

The final implementation is recorded on `pre-m13-ui-polish`; the accepted implementation HEAD is maintained by the branch and pull request history.

## Changed surfaces

- Quick Search compact mode is a 700 × 56 single-surface launcher with transparent QueryBox styling, retaining focus/accessibility structure and feather, query, and Settings alignment.
- Quick Search result rows retain Shell icon, Name, Path, and Metadata with a clearer primary/secondary hierarchy, restrained selected and pointer-over surfaces, refined spacing, and matching light/dark semantic resources.
- The existing immediate compact/expanded resize remains intact. Expanded content receives a non-blocking 140 ms opacity plus 6 logical-pixel vertical reveal; query dispatch and result updates are not animated or awaited.
- Settings retains hotkey capture, Save/Cancel, validation, persistence, and Manage indexes flow while grouping the hotkey/default action, theme, index-management action, and error space more clearly.
- Index Manager retains its native title bar, operations, and action semantics while using heading/status hierarchy, semantic card surfaces, spacing, and the Quail theme current at opening/reopening.

## Automated verification

- Full Release test suite after the fourth polish round: 162 passed, 0 failed.
- Release `Quail.App` `win-x64` build: passed with 0 warnings and 0 errors.
- Release M10 harness build: passed with 0 warnings and 0 errors.
- Final diff review: passed with no whitespace errors. No project/dependency, package-lock, asset, font, or icon files were changed.

## Known limitations

- Theme changes made while an Index Manager window is already open are not propagated live. Closing and reopening it applies the current persisted theme.

## Second manual-QA observations

- Physical-host QA reproduced a USN continuity loss. The first Refresh correctly reported `saved-usn-before-readable-range` and moved the index to RebuildRequired. The GUI now exposes Rebuild as the recovery action and disables Refresh while that state is present, so a second Refresh cannot replace the diagnostic reason with the general `index-not-complete` reason. Rebuild returned the index to Ready with 888,708 records, and normal search remained functionally correct.
- On that complete physical-host index, functional search correctness passed. A video/manual measurement of `desk` placed full input at approximately 7.5–7.6 seconds and visible results at approximately 9.7–9.8 seconds: around 2.1–2.3 seconds input-to-visible delay. The UI remained responsive and showed `Searching...`. Performance investigation is explicitly deferred to M13: the root cause remains unresolved and may include in-flight intermediate-query queueing, Core ranking, result mapping, UI apply/render, icon loading, cold/warm cache behavior, or another layer. M13 must separately measure input-to-request, queue delay, search start/end and Core duration, result mapping, UI apply/render, icon loading, cold/warm behavior, and pasted full queries versus typed intermediate queries.
- A future user-triggered local trace may record only anonymized/non-content timing and resource metrics, such as query length, input/request/search/result-apply timings, queue delay, result count, index record count/database size, process CPU/working set, and cold/warm/session context. It must not record query text, paths, file names, usernames, machine identifiers, or send remote telemetry. M13 may first use bounded internal instrumentation; a permanent diagnostics mode is a post-0.2 decision.

## Fourth manual-QA follow-up

- Compact Quick Search has a deliberate 700 × 56 logical-pixel contract. The root window is the sole visible search surface; its 56-pixel header contains the feather, transparent QueryBox, and Settings button. Expanded Quick Search remains 700 × 370 and Settings remains 700 × 500.
- The compact Settings direction is accepted and remains unchanged in this follow-up. The current Index Manager cards, action hierarchy, title-bar theme, and state presentation are likewise frozen for 0.2 unless a later direct blocker appears.
- A global-hotkey regression was found during Settings deactivation: focus on the hotkey field could suspend the registered hotkey before the user entered a replacement. Capture now begins only on actual keyboard input. Losing hotkey-field focus or deactivating Settings while capture is active attempts the existing idempotent restore path; Save still retains the newly registered hotkey, while Cancel retains or restores the old one.

## Final optical-alignment microfix

- Manual QA passed the compact 700 × 56 direction, single-surface composition, Settings, hotkey lifecycle, expanded Quick Search, transition, result rows, and the frozen Index Manager presentation. The only remaining visual check was the QueryBox text/caret optical baseline.
- QueryBox keeps transparent TextControl resources, caret, selection, clear button, focus, and accessibility semantics. The accepted final rendered-content offset is `Padding="0,2.25,0,0"`; no dimensions or surrounding controls changed.

## Final manual visual gate

- Manual visual QA passed compact Quick Search at 700 × 56, including the accepted final QueryBox optical alignment with `Padding="0,2.25,0,0"`. The remaining native WinUI caret appearance is consciously accepted.
- Manual QA also passed the global hotkey lifecycle through Settings deactivation, Settings presentation, expanded Quick Search and its transition, and result presentation. Index Manager presentation is accepted and frozen for 0.2.
- Search performance evidence remains deferred to M13; no search or ranking changes were made here.

## Theme-surface resolution

- A prior `ContentDialog.RequestedTheme` attempt was automated-pass but manual-fail because Settings remained dark. The accepted solution uses a local `SettingsSurface` hosted in the existing Quick Search themed tree instead of a runtime ContentDialog. Index Manager uses an outer themed Grid with `QuailSurfaceBrush`; ScrollViewer is content-only. The final Light/Dark/System manual gate passed for Quick Search, Settings, and Index Manager.

## Final cleanup

- Settings navigation now uses the same card surface geometry as the Quick Search and Appearance sections while retaining a full-row clickable affordance and right-aligned chevron.
- Index Manager retains `VerticalScrollBarVisibility=Auto`; content spacing and padding were reduced so the typical single-index view fits without an unnecessary scrollbar while larger catalogs can still scroll.
- `SettingsDialog.cs` was renamed to `SettingsSurface.cs`, and both changed C# files were expanded to normal multiline idiomatic formatting without semantic changes.
- Settings and Index Manager functional QA passed, including Save, Cancel, Escape, Manage Indexes, hotkey lifecycle, and Light/Dark/System coherence. The native WinUI caret appearance remains consciously accepted.
- Search performance evidence (approximately 2.2 seconds input-to-visible results on an 888,708-record complete index) remains deferred to M13.

## Final QA evidence

- Compact and expanded Quick Search, Settings, Index Manager, Save/Cancel/Escape, Manage Indexes, and hotkey lifecycle passed manual and independent QA.
- The native WinUI caret appearance is consciously accepted.
