# M18 Results — Ranking / Relevance v2

## Status

**READY FOR INDEPENDENT QA — deterministic relevance and physical performance acceptance passed.**

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
inside a store. MultiIndexSearch consequently retains the existing global merge.

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

## Initial physical campaign failure and focused correction

The first complete canonical M16 8x3 campaign ran at clean commit `7a4f8df`.
It contains all 24 input samples, three rapid-burst samples, 24 trace files and
24 diagnostics files. Every trace has one scenario start, no scenario failure,
and a rendered final result. Metadata records .NET 10.0.400, Windows
10.0.26200.0, two indexes, 873,327 reported records, 420,327,424 database
bytes, exact HEAD `7a4f8dffe1c3ff6a29acf13e2cffd61f4ec237d9`, and `sourceDirty=false`.

The campaign is valid FAIL evidence and was not rerun for a better result:

| Scenario | Samples ms | Median | Target | Worst | Guardrail | Result |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| ordinary-name | 2499.046, 2118.629, 2123.230 | 2123.230 | 50 | 2499.046 | 100 | FAIL |
| strong-prefix | 2123.095, 2130.778, 2122.669 | 2123.095 | 50 | 2130.778 | 100 | FAIL |
| broad-result | 2210.684, 2197.960, 2191.044 | 2197.960 | 150 | 2210.684 | 250 | FAIL |
| one-character | 2253.644, 2191.499, 2191.495 | 2191.499 | 150 | 2253.644 | 250 | FAIL |
| two-character | 2185.548, 2177.937, 2166.409 | 2177.937 | 150 | 2185.548 | 250 | FAIL |
| warm-repeated | 2185.505, 2177.842, 2185.508 | 2185.505 | 100 | 2185.508 | 150 | FAIL |
| fresh-process-first-search | 2187.319, 2187.676, 2191.594 | 2187.676 | 125 | 2191.594 | 150 | FAIL |
| rapid-typing final | 6067.848, 5980.826, 5961.052 | 5980.826 | 75 | 6067.848 | 125 | FAIL |
| rapid-typing burst | 6558.595, 6470.702, 6451.544 | 6470.702 | 600 | 6558.595 | 700 | FAIL |

Nearly all time was inside Core search and every query shape paid the same
roughly 2.1-second floor. The cause was not M18 candidate ranking. M17.6 made
`EnsureSearchReady` execute writable FTS5 `integrity-check rank=1`, while
`MultiIndexSearch` still invoked that full validation for every configured
index on every interactive request. With two indexes, each query performed two
full content-integrity scans before searching. The M17.6 builder already runs
this check before publication, and explicit `EnsureSearchReady` retains it for
diagnostics and lifecycle verification.

The bounded correction removes only the redundant per-query calls from
`MultiIndexSearch`. Each `IndexStore.Search` still opens a read transaction and
calls `EnsureSearchable`, which checks complete build state, schema and format
versions, current short-query generation, and checkpoint presence. Full FTS
content, trigger and coverage validation remains in build/publication and
explicit `EnsureSearchReady`; no schema, persistent format, mutation, ranking,
or recovery behavior changed.

Focused affected tests passed 98/98. Four one-repetition UI samples on the same
two-index corpus then passed their relevant target and guardrail: ordinary
12.263 ms, broad 91.082 ms, one-character 78.733 ms, rapid final 65.428 ms and
rapid burst 556.364 ms. The earlier intermediate correction that retained
per-query full-table coverage counts passed 100 tests but still measured
62.944, 274.060, 130.992, 82.626 and 574.388 ms respectively; it was rejected
before commit because ordinary, broad and rapid medians were not within target.
The final corrected candidate then passed the full `Quail.Core.Tests` Release
suite **237/237** and `dotnet build Quail.sln -c Release` with zero warnings or
errors.
Raw evidence remains under `artifacts/m18/final-7a4f8df/`,
`artifacts/m18/focused-preflight-fix/`, and
`artifacts/m18/focused-preflight-fix-final/`.

Because this correction changes a measured hot path and moves full content
integrity out of interactive dispatch, independent QA must specifically review
the validation boundary and rerun or inspect the existing corruption/lifecycle
tests. Another general implementation subagent review was not run after this
small correction.

## Final physical acceptance

After the focused correction, one final canonical M16 8x3 campaign ran on clean
commit `de615db03d064e0487ff6a62fb09c3eb39e9fdcf`. It used the same disposable
frozen pre-reinstall C final-v3 index as M17 plus the current D index: two
indexes, 873,327 reported records, 420,327,424 database bytes, .NET 10.0.400,
and Windows 10.0.26200.0. The evidence contains 24 input samples, three rapid
burst samples, 24 valid traces, and 24 diagnostic logs. `sourceDirty=false`.

| Scenario | Samples ms | Median | Target | Worst | Guardrail | M17 median | Delta | Result |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| ordinary-name | 15.481, 15.438, 13.706 | 15.438 | 50 | 15.481 | 100 | 16.675 | -1.237 | PASS |
| strong-prefix | 54.085, 12.162, 16.227 | 16.227 | 50 | 54.085 | 100 | 17.658 | -1.431 | PASS |
| broad-result | 84.387, 85.848, 88.985 | 85.848 | 150 | 88.985 | 250 | 124.152 | -38.304 | PASS |
| one-character | 86.683, 84.340, 86.349 | 86.349 | 150 | 86.683 | 250 | 97.766 | -11.417 | PASS |
| two-character | 87.372, 71.527, 73.587 | 73.587 | 150 | 87.372 | 250 | 91.568 | -17.981 | PASS |
| warm-repeated | 72.939, 71.430, 74.546 | 72.939 | 100 | 74.546 | 150 | 60.174 | +12.765 | PASS |
| fresh-process-first-search | 101.708, 81.795, 88.587 | 88.587 | 125 | 101.708 | 150 | 81.134 | +7.453 | PASS |
| rapid-typing final | 64.436, 65.811, 66.848 | 65.811 | 75 | 66.848 | 125 | 49.501 | +16.310 | PASS |
| rapid-typing burst | 548.262, 550.770, 549.978 | 549.978 | 600 | 550.770 | 700 | 550.434 | -0.456 | PASS |

All median targets and all 27 per-sample guardrails pass. Broad-result improved
by 38.304 ms against M17 and retains substantial target margin. Warm, fresh and
rapid final are slower than M17 but remain within their explicit targets and
guardrails; no further optimization is justified after acceptance. Raw evidence:
`artifacts/m18/final-de615db/`.

## Acceptance assessment and remaining QA

| Area | Result |
| --- | --- |
| Known same-text-tier late-candidate failure | PASS; reproduced in baseline and fixed for arbitrary hit counts |
| Deterministic relevance | PASS; top-1 55% to 100%, top-5 70% to 100%, MRR 0.6464285714 to 1.0 |
| Mandatory explicit orders | PASS; 20/20 including duplicates, near-duplicates, locations, depth, short queries, multi-index and final ties |
| Short-query policy | PASS; same visibility/text/location policy, unchanged compact-short-query-v3 |
| Multi-index global top N | PASS; complete local top N plus deterministic global merge |
| Focused/full tests and Release build | PASS; final 237/237 and full solution zero warnings/errors |
| Final M16 8x3 | PASS; every target and guardrail |
| Manual Quick Search smoke | Pending user/independent QA: representative visual order and Enter-to-open |
| Independent project QA | Pending |

The final campaign itself provides real Quick Search input-to-render evidence for
ordinary, broad, short, warm, fresh and rapid workflows. The repository
verification playbook assigns the remaining brief visual/Enter-to-open check to
the user or independent QA when native desktop control is unavailable. No new
desktop automation was built for this observation.

M18 is ready for independent QA. It is not complete, no merge has occurred,
and no merge, branch deletion, tag or release is authorized.
