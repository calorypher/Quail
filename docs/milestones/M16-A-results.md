# M16-A Results — Benchmark Procedure and Harness

## Status

Implementation handoff for independent M16-A QA. M16-B has not started.

## Delivered procedure

- `benchmarks/m16/scenarios.example.json` defines the eight-scenario representative set.
- `scripts/run-m16-benchmark.ps1` is the canonical one-command Release harness. It writes ignored JSONL traces, per-run driver inputs, `results.json`, and `summary.txt` under `artifacts/m16/<timestamp>/`.
- `docs/milestones/M16-A-procedure.md` defines the environment assumptions, exact invocation, output format, repetition budget, and manual Everything comparison.
- The existing `SearchPerformanceTrace` remains the timing source. The only production addition is an opt-in private scenario input that drives the existing QueryBox path and labels measured trace events with a non-sensitive scenario id. Warmup trace events intentionally remain unlabelled and cannot enter measured aggregation. Normal launch, search, ranking, storage, source composition, and UI behavior are unchanged when that option is absent.

## Scenario coverage

The set has eight stable ids: `ordinary-name`, `strong-prefix`, `broad-result`, `one-character`, `two-character`, `warm-repeated`, `fresh-process-first-search`, and `rapid-typing`. It covers the required normal, strong, broad, short-query, warm, fresh-process, and typing-burst classes without an exhaustive matrix.

The committed file contains only replaceable example query strings. M16-B must use an ignored local copy with stable, non-sensitive queries known to match the physical host's intended real index. Every `warm-same-session` scenario declares a small warmup query; `fresh-process-first-search` declares none. Results and traces intentionally retain only scenario id, query length, timing metadata, and index scale.

## Validation evidence

| Check | Result |
| --- | --- |
| Focused scenario, trace, and render-waiter tests | PASS — 13 tests, Release configuration |
| Release `Quail.App` build | PASS — 0 warnings, 0 errors |
| PowerShell harness parameter parsing | PASS — `scripts/run-m16-benchmark.ps1 -?` |
| Physical-host local-interactive harness validation (`broad-result`, `rapid-typing`, one repetition) | PASS — both scenarios completed and produced expected artifacts |

The earlier Codex desktop sample used only the small validation subset but was blocked before managed App startup: the newly launched `Quail.exe` remained alive for the 45-second scenario timeout without creating its diagnostics log or trace. That remains useful environment context, but is no longer a blocker because the physical-host local-interactive validation passed. No alternative desktop automation path was added.

The Windows PowerShell `PropertyNotFoundStrict` compatibility blocker reported by independent QA was fixed in `scripts/run-m16-benchmark.ps1`: `Get-TraceStage` is now explicitly single-or-null, and its consumers use null checks and scalar properties. A second StrictMode failure was found during user-owned validation: conditional warmup assignments could unwrap a one-item array into a scalar. Both validation and driver-preparation paths now initialize `$warmupQueries` as an array before assigning `@(...)`. The narrow audit found no additional unwrapped scalar collection checks in the affected path; explicitly array-wrapped collections remain unchanged.

User-owned local validation then exposed a scenario-driver synchronization failure. The warmup search completed and emitted `first-text-results-rendered` for its actual UI generation, but the scenario timed out before `scenario-start`. The driver had captured `_queryGeneration` immediately after assigning `QueryBox.Text`, before the ordinary `TextChanged`/`ApplySearch` flow had necessarily assigned the generation for that input. `SearchPerformanceRenderWaiter` now prepares a completion task for the expected non-empty query before submission, binds it only when `ApplySearch` processes that query and assigns the actual UI generation, and completes only when the corresponding first-text render is recorded. Empty clearing input cannot bind the waiter. Rapid typing prepares the waiter immediately before the final query setter, so it binds only to the final processed query generation.

The physical-host validation was run from a local Windows session with the committed example scenario and one repetition. The harness performed its Release build and exited with `PASS`. `results.json` reported `sourceDirty=false`, HEAD `cdf4fb3bc8c10b7ab252b261a0f63173176be9d0`, .NET SDK `10.0.400`, Windows `Microsoft Windows NT 10.0.26200.0`, and exactly two samples. Both `broad-result` and `rapid-typing` completed with `resultCount=50`; both had `inputToFirstTextMilliseconds`, while only `rapid-typing` had `typingBurstToFirstTextMilliseconds` (`broad-result` was `null`). The corresponding `summary.txt` and JSONL trace artifacts were produced. These are harness/runtime validation samples only, not an M16 performance baseline or M17 target; M16-B remains responsible for the controlled three-repetition baseline and Everything comparison. No further M16-A runtime validation is required.

| Scenario | Input-to-first-text (ms) | Typing-burst-to-first-text (ms) | Queue wait (ms) | Core search (ms) |
| --- | ---: | ---: | ---: | ---: |
| `broad-result` | 549.702 | `null` | 427.337 | 115.4165 |
| `rapid-typing` | 42.309 | 522.057 | 0.053 | 32.7364 |

Both samples used index scale `indexCount=1`, `recordCount=911883`, and `databaseBytes=269438976`.

The completed physical-host validation used:

```powershell
.\scripts\run-m16-benchmark.ps1 -ScenarioPath .\benchmarks\m16\scenarios.example.json -ScenarioId broad-result,rapid-typing -Repetitions 1
```

It confirmed that `results.json` contains one sample for each requested id and corresponding JSONL traces; every sample has `inputToFirstTextMilliseconds`, while `typingBurstToFirstTextMilliseconds` is populated only for `rapid-typing`. This is harness validation only, not M16-B.

## Everything comparison ownership

Quail trace capture and aggregation are automated. Everything focus, input, and first-visible-result timing are user-owned manual steps on the same physical PC, as specified in `M16-A-procedure.md`. No WinApp, SendInput, VMConnect, or custom desktop automation was added.

## M16-B cost and boundary

After QA approval, M16-B should use the local scenario copy, run the eight scenarios once with the agreed small repetition count (initially three), collect the user-owned Everything observations only where semantically comparable, and perform only narrowly justified follow-ups. Expected Quail execution is 24 isolated samples plus the short manual comparison; no 50/100/500-iteration campaign, Quail-Lab, package, installer, M15 protected-index, M17, or M18 work is authorized.

## Limitations

- A real host index and local query strings are intentionally not committed.
- The fresh-process scenario measures first-search behavior after the new process becomes visible; app startup remains a separate metric.
- First text-result availability is primary; shell-icon completion is supporting trace data rather than the completion condition.
- The scenario-driver synchronization remediation passed focused automated verification and a Release `Quail.App` build; it does not authorize another Codex desktop validation attempt.
- Focused helper check: PASS under `Set-StrictMode -Version Latest`; parser/parameter check: PASS via `scripts/run-m16-benchmark.ps1 -?`. No Release build was required for those PowerShell-only remediations; the later C# synchronization remediation is covered by the Release build above.
- The Codex desktop execution environment could not complete its earlier live validation run; the physical-host local-interactive validation subsequently passed, so this is no longer an M16-A blocker.
