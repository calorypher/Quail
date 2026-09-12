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
  query field, shows a clear action only for non-empty queries, and provides
  familiar Full Search and Settings actions.
- Result rows retain M22 ranking, keyboard behavior, icons, metadata, and
  two-line name/context hierarchy.
- The context menu exposes only source-supported Open, Reveal, and Copy path
  actions, plus Open in Full Search which preserves the current query.
- `Alt+Enter` reuses the same Quick-to-Full transition. Ordinary fast searches
  do not display a transient busy label.

### Full Search

- The application-owned header beneath the native caption adds Quail identity,
  direct Settings access, and Collapse without changing native window behavior.
- The default row contains Type, Extension, Modified dates, Add filter,
  Relevance, and Clear all. Add filter toggles the existing Size and attribute
  controls; no M22 criterion was removed or changed.
- Name, Path, Size, and Modified headers sort ascending on first click and
  descending on repeated click. Relevance is restored explicitly by its button.
  Kind remains display-only.

### Settings

- General now represents existing startup, hotkey, and theme functionality as
  compact feature cards with aligned controls and a transactional Save action.
- Indexing remains driven by the existing catalog, health, and action policies;
  it is presented through the shared card treatment.
- About remains concise and derives its version from the entry assembly, with
  existing repository and license links.

## Visual evidence

All captures below are real physical-host WinApp evidence under the ignored
`artifacts/m23/visual/` directory; they are intentionally not committed.

### Checkpoint A — Quick Search

- `quick-empty-dark.png`
- `quick-populated-dark.png`
- `quick-context-menu-dark.png`

The compact empty state keeps the search field dominant. The populated state
has six visible, two-line rows with calm metadata and a clearly stronger blue
keyboard selection. The captured context menu contains supported Open, Open
file location, Copy path, and Open in Full Search actions. Full-screen capture
was required for the WinUI flyout.

### Checkpoint B — Full Search

- `full-populated-dark.png`
- `full-no-results-dark.png`
- `full-advanced-filters-dark.png`

The application header, dominant query field, compact common-filter row, and
information-dense result table follow the primary reference without replacing
the native caption. Advanced controls appear only after Add filter. No-results
status remains visible without decorative empty artwork.

### Checkpoint C — Settings

- `settings-general-dark.png`
- `settings-indexing-dark.png`
- `settings-about-dark.png`

General cards have the intended visual hierarchy and aligned controls. Indexing
uses existing health/state data in readable cards rather than a service console.
About is concise and shows the real assembly version, approved feather, and
existing links.

### Light, System, and DPI

- `settings-general-light.png`
- `full-populated-light.png`
- `quick-populated-light.png`

Light-mode text, selected rows, controls, borders, and focus treatment were
visually inspected and remain readable. The temporary saved Light selection was
restored to System after the smoke; the physical host was dark, so System was
also covered by the Dark captures. User-owned high-DPI visual smoke: pending;
host display scaling was not changed for this cosmetic milestone.

## Verification

- Focused M22/M23 Full Search, Quick layout, and scheduling regression guard:
  `dotnet test tests/Quail.Core.Tests/Quail.Core.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~M22FullSearchTests|FullyQualifiedName~M11QuickSearchOverlayLayoutTests|FullyQualifiedName~M13BSearchSchedulingTests"` — **42/42 PASS**.
- Release App build after the M23 sort-card changes:
  `dotnet build src/Quail.App/Quail.App.csproj -c Release -r win-x64 --no-restore` — **PASS, 0 warnings, 0 errors**.
- Final full Release suite: `dotnet test Quail.sln -c Release --no-restore` —
  **307/307 Core tests and 13/13 Maintenance Service tests PASS**.
- Final Release App build: `dotnet build src/Quail.App/Quail.App.csproj -c Release -r win-x64 --no-restore` — **PASS, 0 warnings, 0 errors**.
- `git diff --check` — **PASS**.
- `dotnet list src/Quail.Core/Quail.Core.csproj reference` — **no project references**, preserving the absence of a Core-to-FileSystem dependency; focused M22 coverage also asserts the compiled Core assembly has no FileSystem reference.

### Perceived latency guard

Canonical `scripts/run-m16-benchmark.ps1` ran the existing local scenario file
for `ordinary-name` and `broad-result`, three repetitions each, with no resident
Quail process and no rebuild:

- evidence: `artifacts/m16/20260912-150947/`;
- ordinary-name median: **20.618 ms**; worst sample: **26.178 ms**;
- broad-result median: **64.378 ms**; worst sample: **227.091 ms**.

These remain inside the established M18 targets and per-sample guardrails
(ordinary-name <= 50/100 ms; broad-result <= 150/250 ms). The final candidate
does not show a busy-label or spinner flash for ordinary fast searches, and the
captured selection is present with the returned rows.

Changed C# and XAML were manually inspected for readable, ordinary multiline
structure and the unchanged App-to-Core-to-FileSystem dependency direction.

## Design deviations

- The mockup Location and Tray behavior controls are deliberately absent because
  they would require unsupported new product semantics.
- Native Full Search and Settings caption controls remain unchanged to preserve
  Windows resize, Snap, minimize, maximize, and close behavior.

## Remaining acceptance

Branch: `codex/m23-ui-polish` at `a4916536d1e00130f01f80f00a7e99b12f6886fe`.
Pull request: #26.

User-owned final M23 visual/interaction acceptance: pending.
