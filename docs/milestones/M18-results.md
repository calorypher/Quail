# M18 Results — Ranking / Relevance v2

## Status

**ACTIVE — deterministic baseline captured; implementation and acceptance pending.**

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

## Pending acceptance

Final relevance, focused correctness/performance, Release builds, the canonical
M16 8x3 campaign, manual smoke, and independent QA remain pending.
