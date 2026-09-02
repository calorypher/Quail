# M16-B Automated Quail Baseline

## Scope and environment

This is the automated Quail portion of M16-B. The manual Everything comparison remains pending.

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

## Initial measured conclusions

- The one- and two-character paths are the dominant latency problem. Their medians are approximately 3.0 s and 2.0 s respectively. Core search accounts for about 1,992 ms and 1,001 ms; queue, mapping, apply, and source-status costs are not material there. The one-character samples range from about 3.0-4.0 s, so three samples establish the scale and bottleneck class, not a finer-grained cause.
- `broad-result` is the next clear bottleneck: median queue wait is 138.193 ms and Core search 129.729 ms, together explaining most of the 281.509 ms end-to-end value. Mapping (0.018 ms), apply (0.447 ms), and source status (2.139 ms) are not material.
- Ordinary and strong-prefix searches reach first text in 28.047 ms and 26.581 ms medians. Their Core searches are about 9 ms; the visible spread is principally in queue wait (about 9-20 ms), not mapping or apply.
- `warm-repeated` remains slower than the two narrow-name cases (112.191 ms median), split roughly between queue wait (51.603 ms) and Core search (48.416 ms).
- The fresh-process first search is 90.089 ms median after the process is visible. It is not an application-start measurement. Its Core median is 27.014 ms; the remaining interval is outside the individually timed mapping/apply/source-status stages and should not be over-interpreted from three samples.
- The final `rapid-typing` input is 57.456 ms median, with 0.054 ms queue wait and 43.166 ms Core search. Mapping, apply, and source status are again small.

These are observations, not M17 targets or an optimization decision. No search, ranking, timing, or production-code behavior changed in M16-B.

## Limitations and pending comparison

The baseline uses three samples per scenario. It is sufficient to distinguish the large short-query and broad-result costs from normal noise, but not to attribute a single outlying value to an architectural cause.

Everything comparison is still pending and must remain user-owned. On the same physical PC and ordinary system state, compare these ids using the same session condition and input mode: `ordinary-name`, `strong-prefix`, `broad-result`, `one-character`, `two-character`, `warm-repeated`, `fresh-process-first-search`, and `rapid-typing`. For each, record input-to-first-stable-visible-result time and any semantic mismatch. For `rapid-typing`, use the same 120 ms cadence and record both final-result availability and, if practical, burst-to-final-result availability. For `fresh-process-first-search`, start Everything fresh and measure only from query input after it is visible; startup is a separate measure. Different result counts or ranking do not by themselves invalidate a latency observation.

Do not define final M17 targets until the comparison is available, because its magnitude and any semantic mismatch materially affect the appropriate target and bounded implementation scope.
