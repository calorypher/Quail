# M23 Results — Quick Search & UI Polish

## Status

**ACTIVE — ready for independent QA. M23 is not complete and remains subject to
user-owned visual/interaction acceptance.**

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

All captures below are real physical-host WinApp evidence under the ignored
`artifacts/m23/visual/` directory; they are intentionally not committed.

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

- Focused Quick/Full/scheduling coverage and delayed-busy policy tests:
  `dotnet test tests/Quail.Core.Tests/Quail.Core.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~M22FullSearchTests|FullyQualifiedName~M23DelayedBusyStateTests|FullyQualifiedName~M11QuickSearchOverlayLayoutTests|FullyQualifiedName~M13BSearchSchedulingTests"` — **46/46 PASS**.
- Final Release App build after the correction pass:
  `dotnet build src/Quail.App/Quail.App.csproj -c Release -r win-x64 --no-restore` — **PASS, 0 warnings, 0 errors**.
- Final full Release suite: `dotnet test Quail.sln -c Release --no-restore` —
  **311/311 Core tests and 13/13 Maintenance Service tests PASS**.
- `git diff --check` — **PASS**.
- `dotnet list src/Quail.Core/Quail.Core.csproj reference` — **no project references**, preserving the absence of a Core-to-FileSystem dependency; focused M22 coverage also asserts the compiled Core assembly has no FileSystem reference.

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
therefore not warranted.

Changed C# and XAML were manually inspected for readable, ordinary multiline
structure and the unchanged App-to-Core-to-FileSystem dependency direction.

## Design deviations

- The mockup Location and Tray behavior controls are deliberately absent because
  they would require unsupported new product semantics.
- Native Full Search and Settings caption controls remain unchanged to preserve
  Windows resize, Snap, minimize, maximize, and close behavior.

## Remaining acceptance

Correction-pass implementation is committed on `codex/m23-ui-polish`; the PR
head identifies the exact revision.
Pull request: #26.

User-owned final M23 visual/interaction acceptance: pending.
