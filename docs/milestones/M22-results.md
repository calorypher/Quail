# M22 Results — Full Search v1

## Status

**ACTIVE — implementation handoff is ready for independent QA. Do not merge until independent QA and user acceptance are complete.**

## Preparation

- Approved base: `cf2697d68d2d323004bd86fd627e6fa4927ba5ec`.
- `scripts/prepare-milestone.ps1` verified clean host and Quail-Lab repositories at the same HEAD, the `QUAIL_LAB_DATA` volume, and branch availability; it created checkpoint `M22-clean` and branch `codex/m22-full-search-v1`.

## Implementation

- Added one application-owned, native `FullSearchWindow` with standard minimize,
  maximize, close, resize, a 1180x760 logical initial size, and an 820x560
  logical minimum size. Its header retains normal system chrome and places the
  inward Collapse action at the right edge.
- Quick Search now exposes the outward `Open Full Search` action immediately
  beside Settings. Query transfer is exact in both directions; Collapse restores
  Quick Search and focus, while the native Full Search close does not summon it.
  The App owns the singleton window and propagates the current theme.
- Quick and Full Search share the same `SearchApplicationService`. Core gained
  only internal opaque request/result detail markers; filesystem criteria,
  structured fields, and Open/Reveal/Copy actions remain optional and source-owned.
  `Quail.App` composes those filesystem details in `FileSystemSearchComposition`;
  the Full Search window does not access `IndexStore` or rank results itself.
- Full Search renders a standard virtualized `ListView` with Name, Path, Kind,
  Size, and Modified fields and a global 1,000-result bound. It supports the
  approved type, extension, size, local-calendar Modified date, Hidden, System,
  and Read-only filters plus Open, Reveal/Open containing folder, and Copy path.
- Relevance retains the M18 comparator. Field sorts use Name, Path, Size, or
  Modified in either direction; nullable Size/Modified/Path values sort last in
  both directions. The per-index and global comparators use the same field,
  Name, and native-file-id tie order, with source identity only as the final
  global tie-breaker.
- Modified dates map each selected local day inclusively from its local start to
  the final tick before the next local day, including DST-sensitive boundaries.
  Created metadata, schema changes, service, installer, and M23 work remain
  absent.

## Verification

- Focused M22 tests: `dotnet test tests/Quail.Core.Tests/Quail.Core.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~M22FullSearchTests"` — **12/12 PASS**.
- Final small ranking/supersession regression guard after the field-sort
  tie-break correction: `dotnet test tests/Quail.Core.Tests/Quail.Core.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~M22FullSearchTests|FullyQualifiedName~FileSearchRankingTests|FullyQualifiedName~M18RelevanceTests|FullyQualifiedName~M11ShortQueryDeferrerTests|FullyQualifiedName~M13BSearchSchedulingTests"` — **50/50 PASS**.
- Final full Release suite: `dotnet test Quail.sln -c Release --no-restore` —
  **303/303 Core tests and 13/13 Maintenance Service tests PASS**. The final
  run used the authorized host context because the sandbox prevents the existing
  M21 HKCU startup-registration fixtures from creating their disposable keys.
- Final App build: `dotnet build src/Quail.App/Quail.App.csproj -c Release -r win-x64 --no-restore` — **PASS, 0 warnings, 0 errors**; Release XAML provenance check passed.
- `git diff --check` — **PASS**.
- Dependency inspection: `dotnet list src/Quail.Core/Quail.Core.csproj reference`
  reports no project references. A source scan finds only the intentional
  `InternalsVisibleTo("Quail.FileSystem")` declaration, not a Core-to-filesystem
  implementation dependency.
- Changed C# and XAML were inspected for the Core/FileSystem boundary, shared
  request path, optional source details, deterministic aggregate ordering,
  lifecycle suppression, actions, and absence of M23/custom-chrome creep.
- Bounded runtime smoke already passed on this candidate's unchanged startup
  path: the Release `Quail.exe --show-on-start --test-exit-after-visible-ready-count 1`
  process exited with code 0. No installer, service, reboot, or benchmark campaign
  was rerun because M22 does not change those boundaries.

## User-owned manual UI smoke

Pending. Follow the checklist in `M22.md` before user acceptance.
