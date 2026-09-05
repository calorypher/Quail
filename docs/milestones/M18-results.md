# M18 Results — Ranking / Relevance v2

## Status

**ACTIVE — implementation and focused verification passed; physical acceptance pending.**

## Deterministic baseline

Production baseline: `4d73210bf063464f85fff33e756e0dea72e1a720`, identical
search/ranking code to base main `9b1f9fa22ec8cd3c1fdff5b8ed320db28a3342a6`.
Only status documentation and the synthetic fixture were dirty during capture.

`M18RelevanceTests` defines 20 named cases with explicit expected orders.
All names and X:/Y: paths are synthetic. Top-1 success is 11/20 (55%),
top-5 success is 14/20 (70%), and MRR is 0.6464285714285715. Nine mandatory
orders fail. Repeated searches and reversed store input order are deterministic.
Metrics use the first explicitly expected result as the intended result; an
omitted result has reciprocal rank zero. Every expected prefix is also checked,
independently of aggregate metrics.

The baseline reproduces the same-text-tier defect with `quartz`, `Limit=5`,
75 root-level `quartz-000` through `quartz-074` prefix hits, and the late
`X:\Users\Aster\Work\quartz-zzz`. The current comparator prefers the latter,
but candidate retrieval omits it. Separate cases reproduce a late shallow
result on another volume and per-index truncation hiding the global winner.
One-/two-character equivalents already preserve that recall.

The baseline also demonstrates current-user substring dominance over exact
matches in ordinary visible space: six current-user substrings push the intended
exact match to position seven, for one-, two-, and longer-character queries.

Capture command (before production changes): set `QUAIL_M18_BASELINE=1` and
`QUAIL_M18_RELEVANCE_OUTPUT=docs/milestones/M18-baseline.json`, then run
`dotnet test tests/Quail.Core.Tests/Quail.Core.Tests.csproj -c Release --filter FullyQualifiedName~M18RelevanceTests`.
The opt-in baseline mode records failures; normal test execution requires all
explicit expected orders to pass. The capture test passed.

## Final ranking and candidate completeness

The policy is lexicographic, with no scalar weights:

1. visible, internal, system-heavy band;
2. exact, prefix, token-prefix, substring;
3. existing location order within the band (current user, other user, other);
4. path depth, path length, SQLite NOCASE name, binary UTF-8 name, ordinal
   path, and native file ID; global merge finally compares source identity.

The visible band includes ordinary non-profile and other-volume locations.
Internal includes AppData and hidden/system attributes; Windows, Program Files,
ProgramData and the existing system roots retain the system-heavy band.
No folder-specific boost, recency, history, fuzzy matching, or personalization
was added. Identically matching current-user results retain their preference.

For long and filtered queries, SQL streams every matching filtered row as only
`rowid,name`. At most N+1 initial candidates establish whether the stream is
exhausted. If exhausted, sorting all of them is complete and reconstructs at
most N paths. Otherwise the existing v3 rank map and runtime location map are
loaded, with a transient rowid-to-rank lookup. A worst-first priority queue
retains exactly the best N policy/static-label keys while visiting every hit.
Only selected results are materialized and sorted; no all-hit path reconstruction
or all-hit sort occurs. The initial N+1 is an exhaustion test, not oversampling.

Completeness proof: after each visited candidate the queue contains the best N
visited keys. An excluded candidate has N keys ahead of it and therefore cannot
enter the final top N. Existing v3 labels preserve exactly the static suffix of
the full comparator. Exhausting the input proves completeness independently of
hit count or candidate order. Space is O(index rows) for transient existing rank
state plus the rowid lookup and O(N) for selected candidates; work is O(index
rows + hits log N). The index-sized state is not cached or persisted.

Short-query v3 posting buckets remain unchanged on disk. Their traversal now
uses the same policy tuple. Each location/text bucket retains its first N
static labels, which is sufficient because the bucket policy prefix is constant.
The runtime context now resolves the user-parent location independently when
the current profile is absent, keeping full and compact classification equal.
A read transaction covers readiness, selection and materialization in one SQLite
snapshot. Generation checks and mutation/build formats remain unchanged.

Each store supplies its correct local top N. A result outside that set already
has N results ahead of it from the same store under the global comparator;
therefore it cannot be globally top N. The source-identity tie-break is constant
inside a store. MultiIndexSearch consequently needs no production change.

## Focused results

The unchanged 20-case expectation set now has top-1 **20/20**, top-5 **20/20**,
MRR **1.0**, all mandatory orders PASS, and all repeated/reversed-store-order
checks PASS. Deltas: +45 percentage points top-1, +30 points top-5, and
+0.3535714285714285 MRR. See `M18-baseline.json` and `M18-final.json`.
An independent JSON comparison confirmed no case or expectation changed.

Release verification:

- Initial focused ranking/search/multi-index/relevance run: 32 PASS and one
  obsolete expected result FAIL. The old Limit=1 test encoded the alphabetical
  cutoff; it now expects the same prefix winner as unrestricted Search and
  explicitly checks this prefix property.
- Full `Quail.Core.Tests`: **236/236 PASS**, including relevant short-query,
  Core, metadata/filter, incremental, multi-index and ranking coverage.
- Additional focused sparse-rank rename/delete regression: **1/1 PASS**.
  Its first assertion used an unnormalized 8-byte fixture ID versus the stored
  16-byte ID; correcting the assertion did not require a production change.
- Permanent completeness tests compare limits 1, 5 and 50 with a complete
  comparator oracle across three query lengths, extension/hidden filters and
  current/other/absent-profile/explicit-system contexts.
- A 1,100-hit long-query fixture proves the late winner survives even with
  Limit=1,000. Existing short-query cross-chunk and multi-index ties remain green.
- `dotnet build Quail.sln -c Release`: PASS, zero warnings/errors, including
  FileSystem and App. The first attempted solution name was mistakenly `.slnx`;
  it failed before build and was corrected to the existing `.sln`.
- `git diff --check`: PASS.

The first baseline test invocation was sandbox-blocked at NuGet.Config; the
same test with the required host access passed. No package/runtime setting changed.

The existing M17 measurement helper gained only `search --mode full` to measure
production IndexStore.Search (and allocation bytes). Three fresh helper processes
used the existing M16 local ordinary/strong-prefix/broad query texts on the
unchanged 850,675-row frozen C v3 index. No query text is committed.

| Scenario | First call ms | Warm calls ms | Mean transient allocation |
| --- | ---: | ---: | ---: |
| ordinary-name | 55.691 | 1.804, 1.754 | < 0.1 MiB |
| strong-prefix | 55.294 | 2.314, 2.072 | < 0.1 MiB |
| broad-result | 383.498 | 67.040, 61.730 | 80.2 MiB |

First helper calls include cold/JIT costs; these are direct-store microchecks,
not end-to-end acceptance or application-start measurements. Raw evidence is
`artifacts/m18/micro-initial/`. No speculative optimization followed these samples.

Independent adversarial static review of the implementation found no concrete
blockers in selection completeness, comparator/label equivalence, snapshots,
short-query policy, context classification, filters or global top-N composition.
The reviewer did not rerun the implementer's tests. Final physical acceptance
remains a required gate; this review does not replace independent project QA.

## Pending acceptance

The canonical M16 8x3 campaign, short manual smoke, and independent project QA
remain pending. M18 is not complete and no merge is authorized.
