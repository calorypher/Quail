# M13-A physical-host measurement summary

## Method

The Release `win-x64` application built from `42731f5cab080e291c9ebfdcf9fb28a6a481f8fe` was driven in the unlocked physical Windows desktop with Microsoft WinApp CLI 0.6.1 UI Automation. The bounded feasibility check found `QueryBox`, set focus, completed both `set-value` and real `send-keys --via send-input` input, and read the result value back.

The production catalog contained one complete index with 888,708 records and a 256,999,424-byte database. The host description is intentionally non-identifying. Trace output was written to ignored local `artifacts/m13-a/` files; this committed summary contains only privacy-safe aggregates.

`fresh-process first search` means a new Quail process with no cache control. It is not described as a cold-cache test. Each warm group has one excluded warm-up followed by five measured runs. The broad operational input had length 4; the selective operational input had length 14. Neither input text is retained in this evidence.

## Aggregates

All values are milliseconds; each row has five valid measured final-query runs.

| Condition | Input to first text render, median (max) | Queue, median (max) | Core median | Post-Core to text render median | First icon-load median |
| --- | ---: | ---: | ---: | ---: | ---: |
| Fresh, broad, full value | 133.709 (147.180) | 1.061 (1.204) | 73.193 | 45.155 | 50.014 |
| Fresh, broad, typed | 3,129.231 (3,859.374) | 3,036.082 (3,766.187) | 52.363 | 44.395 | 51.141 |
| Fresh, selective, full value | 86.952 (110.728) | 0.980 (1.053) | 25.164 | 45.626 | 64.396 |
| Fresh, selective, typed | 18.191 (20.565) | 0.039 (0.044) | 8.993 | 8.371 | 12.074 |
| Warm, broad, full value | 65.219 (72.562) | 0.054 (0.094) | 53.531 | 9.289 | 11.976 |
| Warm, broad, typed | 3,059.582 (3,088.284) | 2,998.061 (3,024.385) | 52.251 | 10.314 | 12.917 |
| Warm, selective, full value | 25.408 (28.668) | 0.051 (0.087) | 9.614 | 12.907 | 16.693 |
| Warm, selective, typed | 19.549 (21.672) | 0.035 (0.038) | 8.850 | 9.818 | 0.427 |

The fresh broad full-value process had a median 167.0 MiB working set at first textual render; the corresponding typed process had 180.2 MiB. Warm-session working-set snapshots were 207.9 MiB and 232.7 MiB respectively, but include earlier warm-up activity and therefore are context rather than per-query memory deltas.

The new full-value follow-up traces again recorded icon completion after the textual render boundary. In the warm selective typed traces, some cached icon applications completed before that recorded boundary; their icon-load durations were at most 13.822 ms and thus remain immaterial to the multi-second broad typed latency.

## Representative slow timeline

The slowest fresh broad typed final generation took 3,859.374 ms from input observation to first textual render. Its privacy-safe decomposition was:

| Boundary | Duration |
| --- | ---: |
| Input to request enqueue | 0.129 ms |
| Queue wait before final Core start | 3,766.187 ms |
| Final Core search | 52.363 ms |
| Completion dispatch to UI action | 0.153 ms |
| Result mapping | 0.765 ms |
| Collection apply | 2.465 ms |
| Selection and scroll | 18.841 ms |
| Freshness/status | 2.982 ms |
| Remaining scheduled presentation to first textual render | 15.490 ms |

The trace has no unexplained interval over 100 ms. The first icon was applied 51.850 ms after its icon request, but only after `first-text-results-rendered`; icons therefore do not explain the textual-result latency.

The final generation's Core duration was only 52.363 ms. Its queue wait was caused by an earlier query-length-1 fallback that had already entered the single synchronous coordinator worker and ran for 4,631.038 ms. The final request arrived while that request was in flight, so coalescing replaced only pending work and could not preempt the running fallback.

## Synthetic supporting control

The existing 850,000-entry fixture was run as a supporting control, not as a production substitute. Its Core medians were 188.473 ms for broad length 1, 162.454 ms for length 2, 80.591 ms for broad length 3, and 6.079 ms for selective FTS. Its existing rapid-final control measured 298.014 ms when a length-1 fallback was already running and 92.963 ms when the one/two-character defer prevented it from starting.

## Measurement limitations

- UI Automation's per-character commands include command-launch/transport overhead. The paced broad typed procedure crossed the 150 ms short-query defer boundary and therefore intentionally exposed the one-character-fallback queueing failure mode. It is evidence of the production path, not a claim that every human typing cadence reproduces the exact 3.0-second magnitude.
- The physical-host matrix is complete for fresh and warm broad and selective full/typed runs. The warm selective follow-up used the actual WinApp-discovered Quail window PID rather than the `Start-Process` secondary launcher PID. Each new condition had one excluded warm-up and five valid final-query measurements; no bridge or broader automation framework was added.
- The measurements describe the exact Release build and current complete index only. They do not control Windows filesystem/page cache, background host activity, or manual typing cadence.

## Follow-up implication for M13-B

The representative real physical-host query-length-1 fallback took 4,631.038 ms, whereas the synthetic 850,000-entry length-1 Core median was 188.473 ms. M13-B should consider both the stale in-flight coordinator behavior and the real production performance of the one- and two-character fallback. This evidence does not select or implement a remediation.
