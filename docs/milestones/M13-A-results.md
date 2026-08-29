# M13-A search performance diagnosis results

## Status

**COMPLETE — remediation required.**

The measured bottleneck is `LatestFileSearchCoordinator` queueing behind a running synchronous short-query Core search. No performance remediation was implemented in M13-A.

## Implementation source

- Measurement source: `42731f5cab080e291c9ebfdcf9fb28a6a481f8fe` on `m13-a-search-performance-diagnosis`.
- Base source: `184f16b2efa96e2ae87fa1cc9dae848f93f38700`.
- The final documentation commit follows the measured source and does not change production search behavior.

## Delivered diagnostic plumbing

The application has an internal opt-in `SearchPerformanceTrace` that uses a single monotonic `Stopwatch` time basis. It records only privacy-safe generation/run IDs, query length, fixed stage timings, result count, index scale, and bounded process CPU/working-set snapshots. It is disabled by default and is not connected to the existing content-bearing QA event pipe.

The traced production path separates:

- TextChanged/input and one/two-character defer;
- request, worker dequeue, queue wait, and Core start/end;
- completion and UI dispatch;
- result mapping, collection apply, selection/scroll, freshness/status, and first textual render;
- asynchronous icon start/completion timing measured independently from the textual-render boundary.

See [trace contract](evidence/M13-A/trace-contract.md) and [physical-host measurement summary](evidence/M13-A/measurement-summary.md).

## Diagnosis

The previous approximately 2.1–2.3 second typed-query observation is reproducible in kind: typed broad input is orders of magnitude slower than the same full value supplied at once, while final Core and post-Core/UI costs remain small.

The physical-host broad medians were 3,129.231 ms fresh typed and 3,059.582 ms warm typed, compared with 133.709 ms and 65.219 ms for full-value input. In both typed groups, about 3 seconds was queue wait; final Core was about 52 ms and the warm post-Core textual-render path was about 10 ms. The completed warm selective follow-up was fast for both input forms (25.408 ms full value and 19.549 ms typed), with sub-0.1 ms queue medians, confirming that the broad typed failure is not a general warm-session UI or render cost.

The intermediate-query queueing hypothesis is therefore confirmed, with an important refinement: the representative slow trace was not waiting behind a three-character FTS search. It waited behind an already running query-length-1 fallback. The 150 ms short-query defer protects fast typing only when the next input arrives before the timer fires. Once the fallback starts, `LatestFileSearchCoordinator` has a single synchronous worker and can only coalesce pending requests; it cannot cancel the running search.

Mapping, collection update, selection/scroll, status/freshness, composition, and icon retrieval are not material causes of the observed multi-second latency. The representative slow trace rendered textual results before its first icon completion. Some cached warm selective icon applications completed before the recorded render boundary, but all were sub-14 ms and cannot explain the multi-second delay. Full-value and selective runs further rule out a general UI/render bottleneck.

## Verification

- Focused privacy/timing/coordinator tests: 13 passed.
- Full Release test suite: 165 passed, 0 failed.
- Release `Quail.App` `win-x64` build: passed, 0 warnings and 0 errors.
- Official Microsoft WinApp CLI 0.6.1 physical-host feasibility: passed for process/window discovery, `QueryBox` UIA discovery, focus, full-value input, and real `send-input` character typing.
- Physical-host privacy-safe matrix: 40 valid final-query measurements retained in local ignored trace artifacts and summarized in committed evidence.
- Synthetic 850,000-entry supporting control: passed; it reproduces the same already-running-short-query coordinator effect without replacing physical-host evidence.

## Limitations and next decision

The matrix does not control OS cache state and the automation cadence is not a substitute for every human cadence. The bounded warm selective follow-up was completed with the actual Quail window PID resolved through WinApp, rather than the secondary launcher PID; no unsafe bridge or general automation framework was introduced.

The retained plumbing is appropriate technical groundwork for a future explicit user-triggered diagnostics feature, subject to a separate product/privacy design. M13-A does not add such a feature.

M13-B requires a separately scoped remediation decision for the coordinator/short-query interaction, including cancellation or another safe way to prevent a stale in-flight fallback from delaying the final request. It must also evaluate real production performance of the one- and two-character fallback: the representative physical-host length-1 fallback took about 4.6 s, while the synthetic 850,000-entry length-1 median was 188.473 ms. It must preserve the accepted ranking and query semantics unless its own specification authorizes a change.

## Final disposition

remediation required
