# M16-B Search Performance Baseline and Comparison

## Scope and environment

This document records the completed M16-B Quail baseline and the user-owned Everything comparison. It does not implement M17.

The canonical harness ran the approved eight local scenario shapes three times each on the physical Windows host:

```powershell
.\scripts\run-m16-benchmark.ps1 -ScenarioPath .\artifacts\m16\scenarios.local.json -Repetitions 3
```

The measured application commit was `993b03b1c30cdda24e4e4057e39e2b5a3ea13552`. The harness recorded `sourceDirty=true` because the uncommitted change at measurement time was the PowerShell-only `ScenarioId` null guard; Quail production source was unchanged. Environment: .NET SDK `10.0.400`, Windows `10.0.26200.0`, two GUI-managed indexes, 858,879 records, and 273,358,848 database bytes. No `-IndexPath` was supplied, matching the intended daily-use C: and D: catalog.

`docs/milestones/M16-baseline.json` is the committed non-sensitive compact baseline. It contains scenario ids, query lengths, counts, timing samples, and environment/index scale, but no query text, paths, usernames, or trace filenames. The complete ignored run artifacts remain under `artifacts/m16/20260902-104820/`.

## Results

`input-to-first-text` is the primary metric. Each range is the minimum to maximum of the three samples; it indicates observed spread, not a statistical confidence interval.

| Scenario | Median input-to-first-text (ms) | Three-sample range (ms) | Median typing burst (ms) | Result count |
| --- | ---: | ---: | ---: | ---: |
| `ordinary-name` | 28.047 | 24.411-36.730 | n/a | 2 |
| `strong-prefix` | 26.581 | 25.047-41.791 | n/a | 2 |
| `broad-result` | 281.509 | 269.769-283.378 | n/a | 50 |
| `one-character` | 3,006.749 | 2,995.578-4,046.191 | n/a | 50 |
| `two-character` | 2,008.437 | 2,003.535-2,027.333 | n/a | 50 |
| `warm-repeated` | 112.191 | 109.050-118.305 | n/a | 50 |
| `fresh-process-first-search` | 90.089 | 89.478-93.692 | n/a | 48 |
| `rapid-typing` | 57.456 | 53.959-58.065 | 529.727 (522.906-530.566) | 50 |

The `rapid-typing` burst includes the prescribed five inputs at 120 ms cadence. Its roughly 530 ms burst figure is therefore not comparable to the final-input latency alone.

## Quail measured conclusions

- The one- and two-character paths are the dominant latency problem. Their medians are approximately 3.0 s and 2.0 s respectively. Core search accounts for about 1,992 ms and 1,001 ms; queue, mapping, apply, and source-status costs are not material there. The one-character samples range from about 3.0-4.0 s, so three samples establish the scale and bottleneck class, not a finer-grained cause.
- `broad-result` is the next clear bottleneck: median queue wait is 138.193 ms and Core search 129.729 ms, together explaining most of the 281.509 ms end-to-end value. Mapping (0.018 ms), apply (0.447 ms), and source status (2.139 ms) are not material.
- Ordinary and strong-prefix searches reach first text in 28.047 ms and 26.581 ms medians. Their Core searches are about 9 ms; the visible spread is principally in queue wait (about 9-20 ms), not mapping or apply.
- `warm-repeated` remains slower than the two narrow-name cases (112.191 ms median), split roughly between queue wait (51.603 ms) and Core search (48.416 ms).
- The fresh-process first search is 90.089 ms median after the process is visible. It is not an application-start measurement. Its Core median is 27.014 ms; the remaining interval is outside the individually timed mapping/apply/source-status stages and should not be over-interpreted from three samples.
- The final `rapid-typing` input is 57.456 ms median, with 0.054 ms queue wait and 43.166 ms Core search. Mapping, apply, and source status are again small.

No search, ranking, timing, or production-code behavior changed in M16-B.

## Everything comparison

Everything indexed the same C: and D: volumes on the same physical PC. The user manually observed no perceptible wait for result availability for the representative normal, broad, short-query, warm, fresh-process, and rapid-typing workflows. This is qualitative user-owned evidence; no unmeasured millisecond values are inferred for those scenarios.

The user also supplied a 30 FPS Everything screen recording that was analyzed frame by frame independently of Codex. One frame is 33.333 ms. For representative one-character and `ks` two-character queries, the changed query and corresponding results were visible no later than the next captured frame. During `q -> qu -> qua -> quai -> quail` at 120 ms cadence, the list updated during typing and final-query results were present in the first captured frame for the final state. This establishes a video-observed GUI upper bound of approximately 33.3 ms for those short-query and rapid-typing cases. It is not an exact Everything latency: the actual change can have occurred anywhere between captured frames and may be substantially faster.

The attempted Codex-side automatic GUI comparison is closed as an environment limitation, not a product finding: the visible Everything window was isolated from Codex's desktop session, so the standard Win32 `EVERYTHING` window/control probe could not obtain a valid handle. No alternate desktop automation was added.

Everything is a latency reference, not a ranking reference. Its default Name-then-Path ordering is materially different from Quail relevance/ranking behavior. Different result order or counts do not constitute a performance failure and must not motivate weakening Quail ranking. This distinction is an explicit input to M18.

## Comparison implications

- Quail's one-character median of 3,006.749 ms is at least about 90 times the 33.3 ms video upper bound; the two-character median of 2,008.437 ms is at least about 60 times that bound. The actual gap may be larger.
- The 28.047 ms `ordinary-name` and 26.581 ms `strong-prefix` medians are already within the same sub-frame perceptual class as Everything at 30 FPS. This does not establish exact parity.
- `broad-result` remains a visibly slow Quail-specific path at 281.509 ms. Everything was qualitatively immediate, but no precise Everything figure was measured for that scenario.
- The Quail baseline has only three samples per scenario. It is sufficient to distinguish the seconds-scale short-query and perceptible broad-result costs from normal noise, but not to attribute a single outlying value to an architectural cause.

## M17 targets and regression guardrails

All targets use the existing M16 physical-host harness and its `inputToFirstTextMilliseconds` boundary. They are product-level medians, not database microbenchmarks, and deliberately do not require exact Everything parity.

| Scenario class | M17 target | Per-sample guardrail | Rationale |
| --- | ---: | ---: | --- |
| `ordinary-name`, `strong-prefix` | median <= 50 ms | no sample > 100 ms | Preserve effectively immediate normal filename search. |
| `one-character`, `two-character` | median <= 150 ms | no sample > 250 ms | Remove seconds of visible blocking without requiring sub-frame parity. |
| `broad-result` | median <= 150 ms | no sample > 250 ms | Prevent a clearly perceptible broad-result wait. |
| `warm-repeated` | median <= 100 ms | no sample > 150 ms | Keep a controlled in-process repeat close to immediate. |
| `fresh-process-first-search` | median <= 125 ms | no sample > 150 ms | Preserve acceptable first-search behavior after the process is visible; this is not an app-start target. |
| `rapid-typing` final input | median <= 75 ms | no sample > 125 ms | Keep final result availability responsive during the prescribed burst. |
| `rapid-typing` prescribed burst | median <= 600 ms | no sample > 700 ms | Preserve the existing 120 ms-cadence interaction budget; this includes the fixed typing time. |

The eight existing scenarios remain the compact M17 regression set. M18 ranking/relevance work must remain within these same latency targets and guardrails; it must not sacrifice relevance or candidate recall merely to imitate Everything ordering or latency.

## Bounded M17 recommendation and architecture conclusion

M17 should investigate and change only the measured hot paths:

1. the one- and two-character deferred/short-query path, including its Core-search cost;
2. broad-result queue wait and Core candidate retrieval/search cost;
3. queue behavior affecting `warm-repeated`, while preserving normal-query and rapid-typing responsiveness.

Use the smallest measured optimization that satisfies the targets. Preserve the M15 source-neutral boundary, Quail ranking/relevance semantics, timing boundary, scenario set, and raw-result methodology. Do not include index rebuild performance, indexing/build code, new benchmark infrastructure, ranking changes, or broader architecture work.

The evidence does not indicate that a fundamental search-architecture replacement or roadmap redesign is required. Normal name searches already meet the intended perceptual class, while the identified slow paths are localized in short-query/Core and broad-result queue/Core behavior. Bounded M17 optimization is therefore a plausible path to the targets; this is a feasibility conclusion, not proof that a particular optimization will succeed.
