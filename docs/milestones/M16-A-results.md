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
| Focused configuration, scenario-parser, trace-label, and measured-input-selection tests | PASS — 15 tests, Release configuration |
| Release `Quail.App` build | PASS — 0 warnings, 0 errors |
| PowerShell harness parameter parsing | PASS — `scripts/run-m16-benchmark.ps1 -?` |
| Live host harness sample (`broad-result`, then `broad-result` + `rapid-typing`, one repetition) | BLOCKED by execution environment before managed App startup |

The live sample used only the small validation subset and did not produce a measurement result. In this Codex desktop execution environment, the newly launched `Quail.exe` remained alive for the 45-second scenario timeout but did not create its requested diagnostics log or trace; therefore it did not reach `Program startup` and this is not evidence about Quail search latency or ranking. The harness left the child process for inspection as designed; the implementation session then stopped only that exact child process. A second attempt with the harness's per-run diagnostics path had the same infrastructure-only outcome. No alternative desktop automation path was added.

The Windows PowerShell `PropertyNotFoundStrict` compatibility blocker reported by independent QA was fixed in `scripts/run-m16-benchmark.ps1`: `Get-TraceStage` is now explicitly single-or-null, and its consumers use null checks and scalar properties. A second StrictMode failure was found during user-owned validation: conditional warmup assignments could unwrap a one-item array into a scalar. Both validation and driver-preparation paths now initialize `$warmupQueries` as an array before assigning `@(...)`. The narrow audit found no additional unwrapped scalar collection checks in the affected path; explicitly array-wrapped collections remain unchanged.

Independent M16-A QA must perform one small local-interactive validation run from the physical desktop session, for example:

```powershell
.\scripts\run-m16-benchmark.ps1 -ScenarioPath .\benchmarks\m16\scenarios.example.json -ScenarioId broad-result,rapid-typing -NoBuild
```

It should confirm that `results.json` contains one sample for each requested id and a corresponding JSONL trace; every sample must have `inputToFirstTextMilliseconds`, while `typingBurstToFirstTextMilliseconds` is populated only for `rapid-typing`. This is a harness validation only, not M16-B.

## Everything comparison ownership

Quail trace capture and aggregation are automated. Everything focus, input, and first-visible-result timing are user-owned manual steps on the same physical PC, as specified in `M16-A-procedure.md`. No WinApp, SendInput, VMConnect, or custom desktop automation was added.

## M16-B cost and boundary

After QA approval, M16-B should use the local scenario copy, run the eight scenarios once with the agreed small repetition count (initially three), collect the user-owned Everything observations only where semantically comparable, and perform only narrowly justified follow-ups. Expected Quail execution is 24 isolated samples plus the short manual comparison; no 50/100/500-iteration campaign, Quail-Lab, package, installer, M15 protected-index, M17, or M18 work is authorized.

## Limitations

- A real host index and local query strings are intentionally not committed.
- The fresh-process scenario measures first-search behavior after the new process becomes visible; app startup remains a separate metric.
- First text-result availability is primary; shell-icon completion is supporting trace data rather than the completion condition.
- The warmup-boundary remediation requires fresh focused automated verification before independent QA; it does not authorize another Codex desktop validation attempt.
- Focused helper check: PASS under `Set-StrictMode -Version Latest`; parser/parameter check: PASS via `scripts/run-m16-benchmark.ps1 -?`. No Release build was run because this remediation changes only PowerShell.
- The Codex desktop execution environment could not complete the live validation run described above. This must be resolved by the one user/QA-owned local-interactive validation, not by further automation work in M16-A.
