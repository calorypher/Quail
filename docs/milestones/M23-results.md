# M23 Results — Quick Search & UI Polish

## Status

**ACTIVE — Full Search liveness restored; final keyboard/interaction smoke and
user-owned acceptance remain pending. M23 is not complete.**

## Preparation and references

- `origin/main` and local `main` were safely fast-forwarded to the merged M22
  commit `94e9154f4286e7392f555795dcfa3aaad506cf97`.
- `scripts/prepare-milestone.ps1` verified the clean host and Quail-Lab state,
  created checkpoint `M23-clean`, and created `codex/m23-ui-polish`.
- The two current PNG references were copied from the locally mounted Google
  Drive into `docs/assets/`; their Google Docs specifications were read through
  the Google Drive connector and preserved as clean Markdown in `docs/`.
- The PNG references were visually inspected before implementation.

## Implementation

### Shared visual language

`App.xaml` provides a small practical family of deep blue-tinted surfaces,
subtle borders, accent/selection colors, compact icon and secondary action
buttons, search/input controls, table headers, and Settings cards. The same
resources preserve a readable equivalent hierarchy for Light and System.

### Quick Search

- The transient frameless header keeps the approved feather, integrates the
  query field with its native clear affordance, and restores the M22 icon-only
  Fluent Expand action plus Settings. There is no custom clear or Quick close
  button.
- Result rows retain M22 ranking, keyboard behavior, icons, metadata, and
  two-line name/context hierarchy.
- The context menu is attached to actual result containers only, so a blank
  ResultsList background cannot expose actions for stale selection. It retains
  source-supported Open, Reveal, and Copy path actions plus Open in Full Search.
- `Alt+Enter` reuses the same Quick-to-Full transition. Ordinary fast searches
  do not display a transient busy label. A quiet `Searching…` becomes eligible
  only after 225 ms for the current pending request and is cancelled on
  supersedure, completion, cancellation, invalid input, or empty input.
- A completed, current, non-empty query with zero results now replaces the
  list region with a restrained centered document/search state: `No results
  found` and `Try a different search term.` The footer remains available for
  operational notices and never duplicates that primary state. An empty query
  still collapses Quick to its compact search field.
- Completed result sets now use the same footer when no transient action,
  error, delayed busy, or source notice has priority: `1 result.`, normal
  plural counts through 49, and `Showing top 50 results.` at the existing Core
  request limit. Zero results still rely solely on the centered state.

### Full Search

- The application-owned title row now extends into the native caption area:
  Quail identity, Settings, and Collapse share the top line with native caption
  controls while preserving Windows resize, Snap, minimize, maximize, and close
  behaviour.
- The dominant search field is followed by a single compact default row: a
  `Type` label plus `Any` / `Files` / `Folders`, Extension, Modified, Add
  filter, and Clear all. This prevents per-option `Type:` repetition
  and the former Folders clipping. Modified opens an integrated, bounded date
  range panel; its existing date semantics remain unchanged. Add filter still
  toggles the existing Size and attribute controls; no M22 criterion was removed
  or changed.
- Name, Path, Size, and Modified headers cycle on repeated clicks through
  ascending, descending, and Relevance/default; switching fields starts the new
  field at ascending. Relevance/default has no active header arrow, Clear all
  restores that state, and Kind remains display-only.
- The normal Full shell remains present for all key states. A usable source plus
  an empty query replaces only the table region with `Start typing to search`
  and a concise search prompt. A real `NoSource` condition takes precedence
  over empty-query and filter presentation, replacing the table region with
  `Index unavailable`, recovery text, and the existing Settings entry point.
  Maintenance health remains outside this condition: CatchingUp, Retrying,
  service Unavailable, missing health, and expected health-read failure still
  leave a complete compatible readable index searchable.
- Quick-to-Full schedules a one-shot focus request on the existing Dispatcher
  queue after activation. The request carries the latest activation token and
  places an unselected caret at the end of the transferred query; stale,
  hidden, or closed-window requests are ignored. This applies uniformly to a
  first Full window, an existing hidden window, and a restored minimized window
  without polling or arbitrary delay.

### Final keyboard, lifecycle, and theme correction

- Quick and Full now have one explicit global-activation decision: an existing
  visible or minimized Full Search is restored/foregrounded and receives a new
  query-focus request; otherwise Quick is summoned. The registered hotkey,
  single-instance activation, and tray summon path all use that decision. A
  collapsed or closed Full window therefore permits Quick, while the two search
  surfaces are never intentionally visible together.
- Full query focus is now a latest-token lifecycle rather than a fire-and-forget
  `Focus()` call. A request remains pending until `FocusManager` confirms that
  `QueryBox` owns keyboard focus. After activation and foregrounding it receives
  one dispatcher attempt and, only if needed, one bounded next-dispatch retry;
  stale, hidden, closed, and superseded requests are rejected. A new global
  activation always creates a fresh request, including for an already-active
  Full window.
- Quick and Full retain `Enter` for Open, add `Ctrl+Enter` for supported Reveal,
  and add `Ctrl+Shift+C` for supported Copy path. `Alt+Enter` switches modes in
  both directions without recreating Full. Query-box `Ctrl+C` remains native
  text copy, and Full keeps its existing result-list `Ctrl+C` compatibility
  alias. The existing context menus expose the corresponding accelerator text.
- The Full Settings glyph explicitly uses the restrained secondary header brush,
  so its Light and Dark resting color no longer inherits the blue icon brush.
- The attempted Full/Settings `AppWindow.TitleBar` caption-foreground override
  regressed runtime behavior and has been removed in the subsequent correction
  below. Both windows retain their previously stable DWM immersive-dark call.

### Native titlebar runtime regression correction

Interactive user QA on both the physical host and Quail-Lab found that the
`7142f577eafc8c7f0c3939c3e5bf21efd8cc9d58` product build showed a black,
unrendered Full window after Quick → Full and terminated after about four seconds.
Settings rendered but its native titlebar became pure black. This **invalidates
the earlier automated-only readiness claim** for that build.

The VM AppLog ended at `Hide: expand-full-search` with no managed Full success
marker. Quail-Lab Application Error event 1000 at 2026-09-12 22:02:20 +02:00
recorded `CoreMessagingXP.dll` with exception code `0xc000027b`; WER event 1001
at 22:02:25 recorded `combase.dll` and `0x80070057` (`E_INVALIDARG`). There was
no corresponding `.NET Runtime` event or recoverable managed stack in the
queried events. Earlier VM events also recorded a `Microsoft.UI.Input.dll`
`0xc0000602` failure. The exact native call site is not proven by these logs.
The initial hypothesis implicated new native caption-color setters. Removing
them in `abbb8c764e3bc821d0bc8de34cf6ad4617fa2ae3` did **not** stop the
same black-window/crash behavior on either host; those setters were **not** the
root cause of the Full crash.
Diagnostic logs: `.quail-tooling/m23-crash-events-20260912-221154153.log` and
`.quail-tooling/m23-crash-applog-20260912-221214119.log` (ignored local files).

Candidate `abbb8c764e3bc821d0bc8de34cf6ad4617fa2ae3` removes those new
individual caption-color setters and `ActualThemeChanged` callbacks from Full
and Settings, restores their pre-regression `ApplyTheme`/DWM path, and removes
the now-unused caption-color policy/test. Settings' Quail icon, content theme,
layout, and behavior remain unchanged. The later active/inactive-window screenshot
comparison does not establish a remaining Settings code regression; Settings
native titlebar appearance will be reassessed only after Full stability. The Full Settings gear brush, mode
exclusivity, focus-token lifecycle, and keyboard shortcuts remain unchanged.
`AppWindow.TitleBar.PreferredTheme` was not added: without an established safe
runtime path after this native crash, the mixed Windows/Quail-theme caption
contrast edge case is deferred to M24 stabilization. Native caption buttons are
not replaced.

### Full Search XamlRoot crash and transferred-query correction

Diagnostic build `423d405412ed4a9bf79fde3c83c851edfa802881` was reproduced
once in interactive Quail-Lab. It completed Full construction, `ApplyTheme`,
`Activate()`, and `SetForegroundWindow`. The first deferred focus callback then
recorded `QueryBox.IsLoaded=False`, `QueryBox.XamlRoot=null`, and
`QueryBox.Focus(...)` returning `False`. Its **last successful marker** was
`before FocusManager.GetFocusedElement`; the next VM Application Error/WER
reported the same `CoreMessagingXP.dll` `0xc000027b` crash and `combase.dll`
`0x80070057` (`E_INVALIDARG`). This identifies the call to
`FocusManager.GetFocusedElement` with an unavailable XamlRoot as the confirmed
crash path. Diagnostic log:
`.quail-tooling/m23-focus-diagnostic-read-20260912-223154368.log` (ignored).

The focus correction (`f2595aa4751573dd3a8d085f05a99f4cc117360c`) keeps
the latest request pending until `QueryBox.IsLoaded` and a non-null XamlRoot
are both true. If not ready, it subscribes once to `QueryBox.Loaded` and queues
the focus attempt after that notification without consuming an attempt.
Collapse/close cancels the handoff, superseded tokens are ignored, and at most
two real focus attempts remain possible. `FocusManager` is called only after a
successful `QueryBox.Focus(...)` with a non-null XamlRoot. Interactive VM QA
confirmed that Full rendered and stayed alive beyond 15 seconds; AppLog showed
the Loaded handoff followed by actual focus confirmation. Log:
`.quail-tooling/m23-focus-ready-liveness-read-20260912-223551851.log` (ignored).

That smoke exposed one additional in-scope activation defect: Full initially
showed no results for a transferred Quick query until the text was edited.
`ActivateSearch` previously relied on a programmatic `TextChanged` event when
the query changed, which was not reliable before the new Full XAML tree loaded.
The narrow correction (`c8ea1368badc979c6ad4269b93612763d5948886`)
suppresses the setter-triggered input handler and explicitly calls the existing
`ApplySearch()` once after transfer/activation. Interactive VM QA confirmed
Full rendered, stayed alive beyond 15 seconds, and displayed the transferred
query's results immediately without another keystroke. No search semantics or
ranking changed. The final code commit `ee7377da63c59341b17d1d7ed97403099c1f1414`
only removes temporary step-by-step tracing, retaining low-noise focus
milestone logs.

### Settings

- Settings now opens centered on the cursor monitor at its existing initial
  size, and both titlebar icon sizes use the Quail feather identity.
- The left navigation is a fixed compact 228-pixel pane with no toggle button,
  returning visual priority to the General, Indexing, and About content.
- General now represents existing startup, hotkey, and theme functionality as
  compact feature cards with aligned controls and a transactional Save action.
- Indexing remains driven by the existing catalog, health, and action policies;
  it is presented through the shared card treatment.
- About remains concise and derives its version from the entry assembly, with
  existing repository and license links.

## Visual evidence

WinApp 0.6.1 on the physical host launched and navigated all captures below.
The ignored `artifacts/m23/visual/` directory intentionally keeps them local.
For the Full and Settings custom title bars, WinApp UIA reports only the native
non-client sink (a 48-pixel titlebar); the actual visual content was therefore
captured from the same foreground host window immediately after the WinApp
interaction. No browser or Computer Use fallback was used.

### Key-state correction pass

- `quick-no-results-key-state-final-dark.png` — WinApp capture of a non-empty
  zero-result query. It shows the centered No Results state and no duplicate
  footer message.
- `full-empty-key-state-final-dark.png` — physical-host capture after WinApp
  cleared Full Search. It preserves the header, search box, filters, and column
  region while showing the centered empty-search state.
- `quick-results-key-state-smoke-dark.png`
- `full-results-key-state-smoke-dark.png`
- `settings-general-key-state-smoke-dark.png`

The three smoke captures confirm normal Quick results, normal Full results, and
the accepted Settings General layout remain intact. A fast normal query showed
its results/count without a `Searching…` flash.

There is intentionally no host screenshot of `Index unavailable`: its only
valid trigger is a real `NoSource` runtime, and obtaining it from the physical
host would require altering user-owned active index configuration. The exact
precedence and presentation are covered by deterministic policy tests below;
the final physical-host visual smoke of this particular state remains
user-owned pending acceptance.

### Final focus/footer correction pass

No new physical-host capture was created for the focus and footer correction.
The physical Windows host intentionally keeps Smart App Control in enforcement
mode. Code Integrity Event ID 3077 reports that the unsigned development
`Quail.FileSystem.dll` did not meet the Enterprise signing-level requirements / Code
Integrity policy. The resulting `FileLoadException` (`0x800711C7`) prevents the
fresh `Quail.exe` from creating a window. This is an environment constraint, not an
M23 functional regression. No Defender exclusion, Smart App Control or Code
Integrity policy change, registry/policy bypass, `Unblock-File`, certificate work,
or other host security configuration change was attempted.

Quail-Lab was used for the final automated verification at
`C:\Temp\Quail-M23-verify` on the exact M23 head. Its SSH transport can start the
application, but cannot expose the active interactive desktop to its WinApp
instance: `winapp ui list-windows -a Quail` returned `Found 0 windows`. A bounded
temporary interactive task started Quail in the active `quailadmin` session, but a
same-session temporary WinApp probe did not produce its output file. Both temporary
tasks and the test process were removed. No custom UI harness, session workaround,
or VM display reconfiguration was introduced. Consequently, the fresh manual
append-after-Expand, Collapse→Quick→Expand, footer-count, busy-flash, placement,
and DPI smoke remain pending on an accessible interactive host/VM session.

The existing evidence above remains applicable to the otherwise unchanged
surfaces. The focused policy coverage and the current Release build provide
automated coverage for the final focus and footer behavior, but do not substitute
for that pending manual runtime smoke.

### Correction pass — Quick Search

- `quick-populated-final-dark.png`
- `quick-context-result-final-dark.png`

The populated capture verifies one native clear X, the restored icon-only M22
Expand glyph, and Settings. The context capture verifies actions on a real row;
the flyout is now container-scoped, so the blank list background has no flyout.

### Correction pass — Full Search

- `full-name-ascending-final-dark.png`
- `full-name-descending-final-dark.png`
- `full-relevance-default-final-dark.png`

The three captures preserve the accepted title/header and compact filter row,
show the restored Fluent Collapse glyph, and prove Name ascending, descending,
and returned Relevance/default with neither a separate Relevance button nor an
active sort arrow. Earlier Modified date interaction evidence remains applicable.

### Correction pass — Settings

- `settings-general-final-dark.png`

The General smoke confirms the accepted 228-pixel navigation proportion, Quail
feather titlebar icon, and main-content priority remain unchanged.

### Prior Light, System, and DPI evidence

- `settings-general-light.png`
- `full-populated-light.png`
- `quick-populated-light.png`

The correction pass was limited to the dark-host visual composition above.
Previously captured Light/System/DPI evidence remains applicable to unchanged
shared resource treatments; user-owned high-DPI visual smoke remains pending.

## Verification

### Final keyboard/lifecycle/theme candidate

Implementation candidate: `7142f577eafc8c7f0c3939c3e5bf21efd8cc9d58`.
Quail-Lab checkout: `C:\Temp\Quail-M23-verify`, detached at that exact commit.

- Focused Release coverage for M23 keyboard lifecycle, footer, key state, M22
  Full Search, and delayed busy policy — **37/37 PASS**.
- Maintenance Service Release suite — first execution had an unrelated transient
  `ShutdownFollowedByStopIsIdempotent` result (**12/13**); one permitted retry
  passed **13/13**. No service code or test changed in this pass.
- Core Release suite — **328/329 PASS**. The sole failure remains the unchanged
  M20 `Native_pipe_acl_rejects_a_non_elevated_client_before_framing` test: the
  SSH runner is a local administrator, so its expected non-administrator
  `UnauthorizedAccessException` cannot occur. It is not an M23 regression and
  the test was not weakened.
- Release App `win-x64` build — **PASS, 0 warnings, 0 errors**.
- Host `git diff --check` — **PASS**. Host dependency-direction check confirms
  that `Quail.Core` has no project references and therefore no FileSystem
  dependency.

The final interactive candidate is:

`C:\Temp\Quail-M23-verify\src\Quail.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\Quail.exe`

The physical host Smart App Control constraint remains unchanged. Quail-Lab
SSH cannot provide reliable WinApp access to the interactive VM desktop, so the
following user-owned VMConnect smoke remains pending: ten Quick → Full → Quick
focus cycles with immediate typing; hotkey Full/Quick exclusivity including
minimized and closed Full; Quick and Full result shortcuts; query TextBox copy;
Light/Dark gear rest/hover; and the effective-theme native caption matrix.
No host security policy or VM display/session configuration was changed.

- Final Quail-Lab workspace: `C:\Temp\Quail-M23-verify`, detached at
  `a76802b90853110f59dffe526d03c76340288588`. The interrupted restore/test command
  did not produce reusable final output; its restore created the required assets,
  after which only the missing verification was run again.
- Final Quail-Lab focused Release coverage:
  `dotnet test tests/Quail.Core.Tests/Quail.Core.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~M23QuickSearchFooterPresentationTests|FullyQualifiedName~M23SearchKeyStatePresentationTests|FullyQualifiedName~M22FullSearchTests|FullyQualifiedName~M23DelayedBusyStateTests"`
  — **30/30 PASS**.
- Final Quail-Lab Core Release suite:
  `dotnet test tests/Quail.Core.Tests/Quail.Core.Tests.csproj -c Release --no-restore`
  — **321/322 PASS**. The sole failure is the unchanged M20 native-pipe ACL test
  `Native_pipe_acl_rejects_a_non_elevated_client_before_framing`: the remote
  `quailadmin` runner is a local administrator, so the expected
  `UnauthorizedAccessException` cannot occur. The test was neither weakened nor
  changed; this is a VM runner-identity limitation unrelated to M23.
- Final Quail-Lab Maintenance Service Release suite:
  `dotnet test tests/Quail.MaintenanceService.Tests/Quail.MaintenanceService.Tests.csproj -c Release --no-restore`
  — **13/13 PASS**.
- Final Quail-Lab Release solution suite:
  `dotnet test Quail.sln -c Release --no-restore` — Maintenance Service
  **13/13 PASS** and Core **321/322 PASS**; the process exits 1 solely because
  of the administrator-context M20 ACL assertion documented above.
- Final Quail-Lab Release App build:
  `dotnet build src/Quail.App/Quail.App.csproj -c Release -r win-x64 --no-restore`
  — **PASS, 0 warnings, 0 errors**.
- Focused Full, delayed-busy, and deterministic key-state policy coverage:
  `dotnet test tests/Quail.Core.Tests/Quail.Core.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~M23SearchKeyStatePresentationTests|FullyQualifiedName~M22FullSearchTests|FullyQualifiedName~M23DelayedBusyStateTests"` — **25/25 PASS**.
- Release App build after the key-state correction:
  `dotnet build src/Quail.App/Quail.App.csproj -c Release -r win-x64 --no-restore` — **PASS, 0 warnings, 0 errors**.
- Earlier pre-final host results remain historical evidence only: an earlier
  M23 candidate passed **317/317 Core tests** before the final focus/footer
  additions. They do not replace the final Quail-Lab outcome recorded above.
- Final focus/footer correction Release build:
  `dotnet build src/Quail.App/Quail.App.csproj -c Release -r win-x64 --no-restore`
  — **PASS, 0 warnings, 0 errors**.
- Focused and full Core Release runners were attempted after the final build,
  but both were blocked before discovery because host application control denied
  `tests/Quail.Core.Tests/bin/Release/net10.0-windows/Quail.FileSystem.dll`
  (`FileLoadException`, `0x800711C7`). The exact same policy also prevents the
  freshly built `Quail.exe` from starting, so Maintenance Service tests were not
  retried; their code and dependencies remain untouched and prior **13/13 PASS**
  evidence is retained.
- `git diff --check` — **PASS**.
- `dotnet list src/Quail.Core/Quail.Core.csproj reference` — **no project references**, preserving the absence of a Core-to-FileSystem dependency; focused M22 coverage also asserts the compiled Core assembly has no FileSystem reference.
- Final host `git diff --check` — **PASS**. Final host dependency-direction check:
  `dotnet list src/Quail.Core/Quail.Core.csproj reference` — **no project
  references**, so Core still has no FileSystem dependency.

### Native titlebar regression correction candidate

The earlier `7142f577...` automated PASS did **not** catch the interactive
black-window/crash regression. This **failed** candidate was synced to the
existing Quail-Lab checkout as exact commit
`abbb8c764e3bc821d0bc8de34cf6ad4617fa2ae3` using the repository's
Quail-Lab SSH/SCP module and a small Git bundle; the VM checkout was clean.

- Focused M22/M23 lifecycle, Quick footer, key-state, Full Search, and delayed
  busy tests: **35/35 PASS**.
- Core Release: **326/327 PASS**. The sole failure remains the unchanged M20
  `Native_pipe_acl_rejects_a_non_elevated_client_before_framing` under the
  administrator SSH runner; the test was not changed or weakened.
- Maintenance Service Release: **13/13 PASS**.
- Release Quail.App `win-x64`: **PASS, 0 warnings, 0 errors**. The same build
  also succeeded on the host, but physical-host Smart App Control prevents
  relying on unsigned host runtime execution.
- Host `git diff --check`: **PASS**. `Quail.Core` still has no project references
  and therefore no dependency on `Quail.FileSystem`.

VM verification log: `.quail-tooling/m23-titlebar-verification-20260912-221636763.log`
(ignored local file). Candidate executable:
`C:\Temp\Quail-M23-verify\src\Quail.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\Quail.exe`.
Interactive user QA subsequently confirmed that this candidate still crashed;
the automated results above are historical evidence, not liveness evidence.
No host security policy was changed.

### Final XamlRoot-safe focus candidate

Quail-Lab checkout and Release build: exact code commit
`ee7377da63c59341b17d1d7ed97403099c1f1414`, clean before verification.
Diagnostic and functional liveness gates passed interactively on preceding
commits `f2595aa...` and `c8ea136...`; the final commit changed logging only.

- Focused M22/M23 Full, keyboard lifecycle, Quick footer, key-state, and
  delayed-busy tests: **35/35 PASS**.
- Core Release: **326/327 PASS**; sole failure is the same unchanged M20
  non-administrator pipe ACL assertion under the administrator SSH runner.
- Maintenance Service Release: **13/13 PASS**.
- Quail.App Release `win-x64`: **PASS, 0 warnings, 0 errors**.
- Host `git diff --check`: **PASS**; `Quail.Core` has no project references and
  no compile-time dependency on `Quail.FileSystem`.

Final VM log: `.quail-tooling/m23-final-focus-verification-20260912-224159610.log`
(ignored). Executable:
`C:\Temp\Quail-M23-verify\src\Quail.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\Quail.exe`.
The final user-owned 10-cycle focus stress, global-hotkey mode routing, and
result shortcut smoke remain pending; no claim of final interaction PASS or
merge readiness is made.

### Perceived latency guard

Canonical `scripts/run-m16-benchmark.ps1` previously ran the existing local scenario file
for `ordinary-name` and `broad-result`, three repetitions each, with no resident
Quail process and no rebuild:

- evidence: `artifacts/m16/20260912-150947/`;
- ordinary-name median: **20.618 ms**; worst sample: **26.178 ms**;
- broad-result median: **64.378 ms**; worst sample: **227.091 ms**.

These remain inside the established M18 targets and per-sample guardrails
(ordinary-name <= 50/100 ms; broad-result <= 150/250 ms). The correction pass
changes only UI presentation/state policy and does not change the search request,
ranking, source dispatch, or result projection. The delayed busy timer is
non-blocking and starts after a request is already pending; a latency rerun is
therefore not warranted. The final focus/footer correction likewise changes
only deferred UI focus and local status-label selection, not the search request,
ranking, source dispatch, or result projection.

Changed C# and XAML were manually inspected for readable, ordinary multiline
structure and the unchanged App-to-Core-to-FileSystem dependency direction.

## Design deviations

- The mockup Location and Tray behavior controls are deliberately absent because
  they would require unsupported new product semantics.
- Native Full Search and Settings caption controls remain unchanged to preserve
  Windows resize, Snap, minimize, maximize, and close behavior.
- Trusted code signing / Smart App Control compatibility is a candidate M24 RC
  concern. It is explicitly not M23 implementation work.
- Mixed Windows/forced-Quail theme native Full caption contrast is deferred to
  M24 stabilization until a safe supported semantic titlebar-theme path can be
  verified without risking Full Search startup.

## Remaining acceptance

The current Full activation/focus implementation is on `codex/m23-ui-polish`
at product-code commit `ee7377da63c59341b17d1d7ed97403099c1f1414`.
Full liveness and immediate transferred-query results passed interactive
Quail-Lab smoke; final keyboard/interaction acceptance remains pending.
Pull request: #26.

User-owned final M23 visual/interaction acceptance: pending.
User-owned high-DPI visual smoke: pending.
