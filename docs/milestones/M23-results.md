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
  query field, shows its only clear action only for non-empty queries, and
  provides an explicitly labelled Expand action plus Settings. This removes the
  visually ambiguous second X while preserving the existing dismiss lifecycle
  and keyboard behaviour.
- Result rows retain M22 ranking, keyboard behavior, icons, metadata, and
  two-line name/context hierarchy.
- The context menu exposes only source-supported Open, Reveal, and Copy path
  actions, plus Open in Full Search which preserves the current query.
- `Alt+Enter` reuses the same Quick-to-Full transition. Ordinary fast searches
  do not display a transient busy label.

### Full Search

- The application-owned title row now extends into the native caption area:
  Quail identity, Settings, and Collapse share the top line with native caption
  controls while preserving Windows resize, Snap, minimize, maximize, and close
  behaviour.
- The dominant search field is followed by a single compact default row: a
  `Type` label plus `Any` / `Files` / `Folders`, Extension, Modified, Add
  filter, Relevance, and Clear all. This prevents per-option `Type:` repetition
  and the former Folders clipping. Modified opens an integrated, bounded date
  range panel; its existing date semantics remain unchanged. Add filter still
  toggles the existing Size and attribute controls; no M22 criterion was removed
  or changed.
- Name, Path, Size, and Modified headers sort ascending on first click and
  descending on repeated click. Relevance is restored explicitly by its button.
  Kind remains display-only.

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

- `quick-populated-correction-dark.png`

The populated capture verifies the feather, integrated query, single clear
affordance, visibly named Expand action, and Settings button in the compact
header. Existing M23 captures of the result hierarchy and context menu remain
applicable because those paths were not changed.

### Correction pass — Full Search

- `full-populated-correction-dark.png`
- `full-modified-filter-correction-dark.png`

The populated-query capture verifies the application-owned header in the native
caption line, dominant query field, and unclipped compact default filter row.
The second capture shows the bounded Modified date-range interaction. Existing
M23 captures of advanced filters, result-table behaviour, and no-results status
remain applicable because their search semantics and controls were not changed.

### Correction pass — Settings

- `settings-general-correction-dark.png`
- `settings-indexing-correction-dark.png`
- `settings-about-correction-dark.png`

The General capture demonstrates the corrected 228-pixel navigation proportion,
Quail feather titlebar icon, and main-content priority. Indexing and About
confirm the information architecture remains intact.

### Prior Light, System, and DPI evidence

- `settings-general-light.png`
- `full-populated-light.png`
- `quick-populated-light.png`

The correction pass was limited to the dark-host visual composition above.
Previously captured Light/System/DPI evidence remains applicable to unchanged
shared resource treatments; user-owned high-DPI visual smoke remains pending.

## Verification

- Focused Full Search test class, including the M23 Modified presentation guard:
  `dotnet test tests/Quail.Core.Tests/Quail.Core.Tests.csproj -c Release --filter FullyQualifiedName~M22FullSearchTests` — **16/16 PASS**.
- Final Release App build after the correction pass:
  `dotnet build src/Quail.App/Quail.App.csproj -c Release -r win-x64 --no-restore` — **PASS, 0 warnings, 0 errors**.
- Final full Release suite: `dotnet test Quail.sln -c Release --no-restore` —
  **308/308 Core tests and 13/13 Maintenance Service tests PASS**.
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
changes only XAML presentation, window chrome/positioning, and UI filter
presentation; it does not change the search request, ranking, source dispatch,
or result projection. A latency rerun is therefore not warranted.

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
