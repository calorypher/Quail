# M22 Results — Full Search v1

## Status

**ACTIVE — bounded independent-QA corrections are verified. Do not merge until independent QA and user acceptance are complete.**

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
- Independent QA correction: the M18 Relevance candidate stream again projects
  exactly `rowid,name`; Name, Size, Modified, and Path field sorts have their
  own narrow candidate projections. This preserves the existing Relevance
  candidate-completeness/ranking path without a schema or ranking-policy change.
- Independent QA correction: Clear filters now restores the disabled direction
  control to `Default`. Attribute fixtures independently prove Hidden-only,
  System-only, and Read-only-only filter bits.

## Verification

- Focused M22 tests after the independent-QA correction: `dotnet test tests/Quail.Core.Tests/Quail.Core.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~M22FullSearchTests"` — **14/14 PASS**.
- Affected ranking/search coverage after the independent-QA correction:
  `dotnet test tests/Quail.Core.Tests/Quail.Core.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~M22FullSearchTests|FullyQualifiedName~FileSearchRankingTests|FullyQualifiedName~M18RelevanceTests|FullyQualifiedName~MultiIndexSearchTests"` — **44/44 PASS**.
- Final small ranking/supersession regression guard after the field-sort
  tie-break correction: `dotnet test tests/Quail.Core.Tests/Quail.Core.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~M22FullSearchTests|FullyQualifiedName~FileSearchRankingTests|FullyQualifiedName~M18RelevanceTests|FullyQualifiedName~M11ShortQueryDeferrerTests|FullyQualifiedName~M13BSearchSchedulingTests"` — **50/50 PASS**.
- Final full Release suite after the independent-QA correction: `dotnet test Quail.sln -c Release --no-restore` —
  **305/305 Core tests and 13/13 Maintenance Service tests PASS**. The final
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

### Independent-QA performance evidence

The existing M17 production-measure helper gained optional `--sort` and
`--limit` diagnostics only; it continues to call production `IndexStore.Search`.
No CLI product option, persistent format, ranking policy, or benchmark framework
was added. The local raw outputs remain ignored under `artifacts/m22/`.

The preserved complete frozen-C index has **850,688 records** and
**411,049,984 bytes**. A non-sensitive broad query class at Full Search's
1,000-result bound produced these direct-store samples on clean commit
`1ead33c3d0eb60a25c74406b6c6e81a1d16536ba`:

| Sort | Samples ms | Median ms | Result | Conclusion |
| --- | ---: | ---: | ---: | --- |
| Relevance | 217.536, 149.735, 167.471 | 167.471 | 1,000 | Expected bounded relevance work; no regression signal. |
| Name | 127.407, 98.785, 87.915 | 98.785 | 1,000 | Practical. |
| Path | 587.175, 531.856, 354.425 | 531.856 | 1,000 | Perceptible but subsecond; not a multi-second blocker. |

Path sorting reconstructs candidate paths before top-K selection, so it is not
as cheap as Name. The measured subsecond result on the representative corpus
does not justify a schema/indexing redesign; no further optimization was added.

The canonical M16 harness was then run only for the local non-sensitive
`ordinary-name` and `broad-result` scenarios, three repetitions each, against
the same index on clean `1ead33c3d0eb60a25c74406b6c6e81a1d16536ba`:

| Scenario | Samples input-to-first-text ms | Median ms | Target / guardrail | Result |
| --- | ---: | ---: | ---: | --- |
| ordinary-name | 15.524, 16.308, 14.401 | 15.524 | <= 50 / <= 100 | PASS |
| broad-result | 88.398, 86.773, 78.164 | 86.773 | <= 150 / <= 250 | PASS |

The harness reported zero queue wait for all six samples. This is a bounded
two-scenario regression check, not a repeated historical M16 8x3 campaign.

## User-owned manual UI smoke

Pending. Follow the checklist in `M22.md` before user acceptance.
