# M13-B physical-host measurement summary

## Method

The Release `win-x64` application with production code commit `5c581cd0bcba8b3476925a4dfb1567b6811bfc07` was driven on the unlocked physical host through the existing Microsoft WinApp CLI/UIA route. The one complete managed index contained 888,708 records. The exact query values are intentionally not retained. The operational broad input had length four.

One excluded warm-up preceded the measurements. Every group below contains five valid warm runs. The opt-in JSONL trace records only privacy-safe timings, generations, query lengths, result counts, and execution-lane labels. It contains no query text, names, paths, source identities, usernames, machine identifiers, or remote telemetry.

## Aggregates

All values are milliseconds.

| Condition | n | Input to first text render, median (max) | Final queue median (max) | Final Core median | Post-Core median | Short Core median | Stale short renders |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Normal fast typed broad length 4 | 5 | 64.246 (72.795) | 0.037 (0.060) | 53.133 | 9.978 | — | — |
| Forced in-flight length 1 to length 4 | 5 | 79.950 (81.512) | 0.037 (0.043) | 67.889 | 11.403 | 3,969.722 | 0/5 |
| Forced in-flight length 2 to length 4 | 5 | 78.497 (80.076) | 0.036 (0.050) | 67.617 | 9.214 | 1,722.666 | 0/5 |
| Standalone length 1 | 5 | 5,090.994 (5,134.223) | 0.026 (0.033) | 4,074.816 | 9.976 | 4,074.816 | — |
| Standalone length 2 | 5 | 2,670.826 (2,729.495) | 0.029 (0.036) | 1,658.396 | 11.162 | 1,658.396 | — |

The normal fast typed group started no short-lane Core search in any run. Its final searches were all in the interactive lane. In both forced groups, the trace recorded the short-lane Core start before the appended length-three/four input, then recorded an interactive-lane final Core start while the short work remained in flight. Each final request had less than 0.05 ms queue wait attributable to its own lane; no trace shows it waiting for the short lane. Each short completion was recorded after the newer final request and produced no stale textual render.

## Interpretation

The approved 1,000 ms trailing defer is intentional. It is separate from the standalone Core cost: the observed standalone totals contain approximately the defer period plus the Core and presentation durations. The real short fallback remains expensive, especially at length one. M13-B deliberately preserves that Core behavior and isolates it from interactive length-three-or-more input.

Compared with the M13-A warm broad typed median of 3,059.582 ms (including 2,998.061 ms queue wait behind a stale length-one fallback), the M13-B normal typed median is 64.246 ms. This is a 47.6x reduction in observed input-to-text-render time for the representative normal path, while its final Core time remains of the same order as M13-A.

The committed document retains aggregates only. Raw local traces and runner logs remain ignored under `artifacts/m13-b/`.
