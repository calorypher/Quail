# M16-A Benchmark Procedure

## Purpose and boundary

This is the canonical M16 procedure for measuring Quick Search perceived result availability on the developer's physical Windows PC. It is designed for the M16-B baseline campaign and later M17/M18 regression checks. M16-A validates only the harness and its output; it does not establish a performance baseline or target.

The primary Quail metric is `inputToFirstTextMilliseconds`: the monotonic interval from the final non-empty measured QueryBox input event to the first DWM-flushed text-result render for that input generation. For `rapid-typing` only, the harness additionally writes `typingBurstToFirstTextMilliseconds`, from the first measured input in the burst to the final result render. It is `null` for single-query scenarios. Existing trace breakdowns cover queue wait, Core search, result mapping, result application, source status, and shell-icon activity. Query text, paths, names, usernames, and machine identifiers are not written to results or traces.

## Scenario set

`benchmarks/m16/scenarios.example.json` defines eight scenario shapes:

| Id | Coverage |
| --- | --- |
| `ordinary-name` | ordinary daily filename query |
| `strong-prefix` | strong exact/prefix-like daily query |
| `broad-result` | broad-result query |
| `one-character` | delayed one-character query |
| `two-character` | delayed two-character query |
| `warm-repeated` | repeat of a warmed query in one process |
| `fresh-process-first-search` | first query after a fresh Quail process starts |
| `rapid-typing` | five query changes at a fixed 120 ms cadence |

Before M16-B, copy this file under ignored `artifacts/m16/` and replace only its example query strings with stable, non-sensitive queries that are known to return representative local results. Keep the ids, session kinds, and scenario shapes unchanged unless an approved milestone changes the regression set. Do not commit the local copy or an absolute index path.

Each harness sample starts a new Quail process to prevent a resident shell from contaminating the trace. Every `warm-same-session` scenario must declare at least one warmup query; those queries execute before the measured scenario begins and have no measured `scenarioId`. Thus `warm-same-session` means a measured query after a controlled in-process warmup, not merely a label on a first-process query. `fresh-process-first-search` must declare no warmup query. `warm-repeated` warms and then measures the same query; `rapid-typing` warms first and then changes QueryBox through the normal input handler without external desktop-input automation.

## Quail invocation

1. Build from the intended branch/commit and exit any resident Quail process. The harness refuses to take over a running desktop session.
2. Place the local scenario copy under ignored `artifacts/m16/`.
3. Run the single canonical command from the repository root:

```powershell
.\scripts\run-m16-benchmark.ps1 -ScenarioPath .\artifacts\m16\scenarios.local.json -IndexPath 'C:\path\to\representative-index.db' -Repetitions 3
```

`-IndexPath` is optional only when the GUI-managed catalog already selects the exact intended representative index. It is otherwise preferred because it pins the same Quail corpus for every process. The script builds the Release App unless `-NoBuild` is explicitly supplied, launches one process per sample, and writes ignored raw artifacts under `artifacts/m16/<timestamp>/`:

- one JSONL trace and private per-run driver input for each sample;
- `results.json`, machine-readable samples and environment context;
- `summary.txt`, a compact median summary.

The machine-readable output records commit, dirty-state flag, .NET version, OS version, trace index scale, scenario id, session kind, query length, result count, and timing metadata. It deliberately does not contain the query text or an index path.

M16-B should run the final scenario set once with the agreed small repetition count (initially three), not increase the count unless observed noise creates a concrete decision problem. Validation uses `-Repetitions 1` and only a small scenario subset.

## Everything comparison

Everything is a practical reference only. Its desktop UI does not provide a comparably trustworthy end-to-end result-availability event through an existing repository harness, so M16-B uses this short user-owned step instead of WinApp, SendInput, VMConnect, or a custom desktop driver:

1. On the same physical PC and ordinary system state, confirm Everything indexes the same relevant local volumes as the Quail scenario corpus.
2. For each comparison-meaningful scenario, open or focus Everything, clear its search box, enter the configured query using the same mode as the Quail scenario (paste a complete query, or use the documented rapid-typing cadence), and observe the first stable visible results.
3. Record the manually observed input-to-visible-result time, whether the scenario was fresh-process or warm, and any semantic mismatch that makes comparison invalid. Do not treat different result counts or ranking as a latency failure.
4. Store the manual notes beside the ignored M16-B result artifact and summarize only non-sensitive scenario ids and timings in `M16-results.md`.

Everything launch/focus and visual observation are user-owned. Quail timing, trace capture, aggregation, and result artifacts are automated by this harness. No Quail-Lab, packaging, installer, hotkey, lifecycle, or M15 protected-index campaign belongs to this procedure.

## Assumptions and limitations

- The physical developer PC and real indexed corpus are the benchmark environment; the VM is intentionally excluded.
- The trace begins at input observation. Fresh-process scenarios describe first-search cache behavior after a newly launched process becomes visible; application-start latency remains a separate startup metric.
- A zero-result scenario is technically measurable but should be replaced before M16-B with a known representative matching query.
- The harness times Quail text-result availability, not completed shell-icon enrichment or a database microbenchmark.
- The app must be the only running Quail instance. On a trace/startup failure, inspect the generated trace once; do not create a second automation path merely to repeat the same evidence.
