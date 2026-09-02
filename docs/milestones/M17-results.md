# M17 Results — Search Engine Performance

## Status

**IMPLEMENTATION IN PROGRESS — physical-host acceptance evidence pending.**
PR #10 is not ready to merge. The M17 performance campaign below remains valid
only as a measurement of commit `fa66270461f8f4461fd574c490a82ad214dcad39`;
it is not acceptance evidence because that commit weakened short-query
candidate recall.

## Current production implementation evidence (not acceptance evidence)

The active branch now contains the amended M17 compact short-query path:

- `namespace_entries` remains authoritative; schema v4 adds filesystem-owned
  SQLite rank and posting chunk tables built inside staging before publication;
- postings are chunked at 1,024 labels and delta-varint encoded, so ordinary
  create/rename/delete changes rewrite only affected chunks rather than a
  complete term list;
- 64-bit sparse labels preserve static ordering; a separate chunked static-order
  map allocates insertion gaps without a corpus-sized numeric merge key;
- runtime current-user and system-location precedence is derived from indexed
  parent topology and the searching context. No builder-process user identity
  is persisted as ranking state;
- a matching derived generation is required for search and sync. A missing or
  mismatched structure is reported as rebuild-required rather than trusted;
- the one-second short-query defer is removed. The retained duplicate-query
  coalescing remains unchanged.

Focused automated evidence on the implementation branch:

- `FileSearchRankingTests`: permanent late exact `a` and `ks` guards, compact
  location/text/static ordering, and a 1,100-entry cross-chunk recall guard;
- `IncrementalIndexStoreTests`: generation mismatch, create/rename/delete,
  directory-rename descendant search, transactional checkpoint behavior, and
  the 1,024-posting chunk bound;
- the full `Quail.Core.Tests` Release suite and the affected `Quail.App` Release
  build pass locally.

No physical-host performance, footprint, load-cost, direct-build-cost, or
write-amplification numbers are recorded for this implementation yet. The
Codex session cannot open `C:` for the required NTFS build (`CreateFile(C:)`),
so it must not claim M17 timing acceptance or reuse the invalidated campaign.

### User-owned physical-host final run pending

After checking out the final clean production commit, rebuild the configured
indexes through the established privileged filesystem-index workflow. Schema v4
intentionally treats older development indexes as rebuild-required; do not
attempt an in-place upgrade. Then run exactly one canonical campaign:

```powershell
.\scripts\run-m16-benchmark.ps1 -ScenarioPath .\artifacts\m16\scenarios.local.json -Repetitions 3
```

The final owner must record the compact footprint/load/build/mutation evidence
from the rebuilt representative corpus, update `M17-baseline.json` and this
file with the actual final commit and samples, and verify every M17 target and
guardrail before changing this status or merging PR #10.

## Invalidated short-query candidate

For one- and two-character literal substring queries, `IndexStore` now reads a
bounded four-times-result-limit candidate set in stable SQLite `rowid` order and
applies the existing final ranking and result limit. This avoids repeated full
table scans and candidate-expansion sorts on the short-query path. The existing
trigram path and its candidate-expansion behavior remain unchanged for queries
of three or more characters. This strategy is invalid: a later exact or prefix
candidate can be excluded before the unchanged ranker sees it.

The focused regression uses `Limit=1` and inserts `ba`, `ca`, `da`, `ea`, then
`a` (and the analogous `bks` through `eks`, then `ks`). The invalid candidate
returns the first substring instead of the later exact match for both query
lengths. The test failed on `a70ff8fd86694609e35e5aaa2de0495d3a31fc5c` and is
retained as the correctness guard for a future replacement.

The unsafe short-query strategy and zero defer were reverted after QA. The
coordinator's duplicate identical-query coalescing remains, because it does not
change candidate recall or ranking behavior and independently removed measured
queue work.

## Invalidated physical-host campaign

The canonical M16 harness ran once with all eight scenarios and three
repetitions on commit `fa66270461f8f4461fd574c490a82ad214dcad39` with a clean
worktree. The host had .NET SDK `10.0.400`, Windows `10.0.26200.0`, two active
indexes, 858,879 records, and 273,358,848 database bytes. The compact,
non-sensitive samples are committed in `docs/milestones/M17-baseline.json`.
The ignored raw traces remain under `artifacts/m17/final-fa66270/`.

| Scenario | Samples (ms) | Median (ms) | Target / guardrail | Result |
| --- | ---: | ---: | ---: | --- |
| `ordinary-name` | 21.865, 16.439, 21.628 | 21.628 | <= 50 / <= 100 | PASS |
| `strong-prefix` | 18.427, 17.149, 15.426 | 17.149 | <= 50 / <= 100 | PASS |
| `broad-result` | 135.741, 136.219, 139.119 | 136.219 | <= 150 / <= 250 | PASS |
| `one-character` | 39.891, 41.092, 40.867 | 40.867 | <= 150 / <= 250 | PASS |
| `two-character` | 43.410, 41.082, 47.259 | 43.410 | <= 150 / <= 250 | PASS |
| `warm-repeated` | 60.807, 60.667, 60.177 | 60.667 | <= 100 / <= 150 | PASS |
| `fresh-process-first-search` | 89.961, 88.056, 87.328 | 88.056 | <= 125 / <= 150 | PASS |
| `rapid-typing` final | 56.422, 58.381, 55.676 | 56.422 | <= 75 / <= 125 | PASS |
| `rapid-typing` burst | 538.225, 538.309, 525.437 | 538.225 | <= 600 / <= 700 | PASS |

`broad-result` Core median was 126.547 ms with zero measured queue wait.
`warm-repeated` Core median was 47.755 ms with zero measured queue wait. The
short-query Core medians were 33.528 ms (`one-character`) and 33.573 ms
(`two-character`), with zero measured queue wait.

## Verification before the blocker

| Area | Result | Evidence |
| --- | --- | --- |
| Short-query correctness | INVALIDATED | Post-candidate QA showed the `rowid` window could omit later exact matches. The final performance candidate therefore does not satisfy M17 correctness constraints. |
| Ranking and multi-index ordering | PASS | Existing focused ranking and multi-index tests passed; final ranking and merge comparers were not changed. |
| Duplicate-query scheduling | PASS | The focused coordinator test proves one running identical Core search serves the latest UI generation, while the existing stale-result, invalidation, bounded-pending, lane, and disposal tests remain green. |
| Focused automated suite | PASS | `dotnet test tests\\Quail.Core.Tests\\Quail.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~FileSearchTests|FullyQualifiedName~FileSearchRankingTests|FullyQualifiedName~MultiIndexSearchTests|FullyQualifiedName~M13BSearchSchedulingTests|FullyQualifiedName~M11SearchCoordinatorTests|FullyQualifiedName~M12IndexCatalogTests|FullyQualifiedName~SearchPerformanceTraceTests|FullyQualifiedName~M11ShortQueryDeferrerTests"` — 57 passed. |
| Affected Release build | PASS | The final canonical harness rebuilt `Quail.App`, `Quail.Core`, and `Quail.FileSystem` in Release with zero warnings and zero errors before the 8x3 campaign. |

No bounded multi-index concurrency was added: the local candidate and duplicate
query changes met every target, so the measured evidence did not justify extra
parallelism or a scheduler framework.

## Blocker and remaining handoff

No schema/index redesign, rebuild work, ranking v2, maintenance work, package
work, or generic scheduler was introduced. The required small, correct short
candidate strategy remains unresolved. Existing trigram FTS has no 1–2-character
terms. On the physical C: index, a full exact-candidate scan with no exact match
measured approximately 1.46 seconds for a one-character query, far beyond the
150 ms M17 target; a correct per-class fallback therefore cannot preserve the
target on the existing read path. A durable short-query index or another material
search/storage design would need roadmap approval before resuming this part of
M17. Search actions and path resolution are unchanged. Do not merge PR #10;
installer, Quail-Lab, and historical architecture campaigns were correctly not
repeated.
