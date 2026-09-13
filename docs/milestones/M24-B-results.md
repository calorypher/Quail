# M24-B Results — Performance / Relevance / Resources

## Status

**ACTIVE — performance and relevance pass; resource investigation has a
release-candidate blocker.**

## Preparation

- Base `main`: `1a7849e1a8accc1d425b832e847505f568a7c74f` (PR #27 merge).
- `scripts/prepare-milestone.ps1` verified matching clean host and Quail-Lab
  repositories, created checkpoint `M24-B-clean`, and created branch
  `codex/m24-b-performance-relevance-resources`.
- The canonical script reported the disposable Quail-Lab data volume
  `QUAIL_LAB_DATA`.
- This evidence document is committed before final measurements so the M16
  harness can record `sourceDirty=false` for the candidate under test.

## Evidence scope

M24-B reuses the accepted M16/M17/M18 contracts and records only current
physical-host measurements, deltas, conclusions, and any evidence-driven
correction. It does not repeat M24-A deployment/lifecycle evidence or M24-C
security and release-freeze work.

## Candidate identity

- M16 campaign candidate: `2255810b8054dda831658a288263e0d7d1f8752d`.
- Resource candidate: `33603964b9998b8cb052f7866531457ef38b7501`.
- The difference from the M24-A merge contains only this M24-B document,
  `M24.md`, and the measurement helper. No production C# or packaging code
  changed, so both measurements exercise the M24-A integrated product code.

## Physical-host M16 campaign

The canonical `scripts/run-m16-benchmark.ps1` ran once with the existing
private `artifacts/m16/scenarios.local.json` set and `-Repetitions 3`. It built
the Release App without a SAC, Defender, Code Integrity, or policy change.
The first sandboxed invocation failed before any sample because it could not
read the existing per-user NuGet configuration; one unmodified rerun outside
that sandbox completed the campaign.

Raw private artifacts are under `artifacts/m16/20260913-152739/`. The harness
recorded `sourceDirty=false`, .NET `10.0.401`, Windows `10.0.26200.0`, two
indexes, 506,609 records, and 257,081,344 database bytes.

| Scenario | Samples (ms) | Median | Worst | Target | Guardrail | Result |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| `ordinary-name` | 29.690, 20.728, 12.614 | 20.728 | 29.690 | 50 | 100 | PASS |
| `strong-prefix` | 29.181, 13.137, 19.924 | 19.924 | 29.181 | 50 | 100 | PASS |
| `broad-result` | 244.479, 67.723, 65.059 | 67.723 | 244.479 | 150 | 250 | PASS |
| `one-character` | 99.565, 65.031, 67.496 | 67.496 | 99.565 | 150 | 250 | PASS |
| `two-character` | 75.004, 61.153, 55.454 | 61.153 | 75.004 | 150 | 250 | PASS |
| `warm-repeated` | 61.681, 57.308, 60.547 | 60.547 | 61.681 | 100 | 150 | PASS |
| `fresh-process-first-search` | 96.632, 87.934, 88.997 | 88.997 | 96.632 | 125 | 150 | PASS |
| `rapid-typing` final input | 32.510, 27.443, 24.641 | 27.443 | 32.510 | 75 | 125 | PASS |
| `rapid-typing` prescribed burst | 518.215, 518.588, 506.042 | 518.215 | 518.588 | 600 | 700 | PASS |

All eight median targets and all 27 per-sample guardrails pass. No performance
optimization is justified.

The corpus is materially smaller than the accepted M18 physical campaign
(873,327 records / 420,327,424 bytes) and differs from the historical M16
baseline (858,879 records / 273,358,848 bytes). The comparison is therefore
qualified by corpus scale, not presented as a like-for-like throughput claim.
It nevertheless shows a strong target margin: M16 baseline medians were 28.047
ms ordinary, 26.581 ms strong-prefix, 281.509 ms broad, 3,006.749 ms one-
character, 2,008.437 ms two-character, 112.191 ms warm, 90.089 ms fresh, and
57.456/529.727 ms rapid final/burst. The final M18 physical acceptance medians
were 15.438, 16.227, 85.848, 86.349, 73.587, 72.939, 88.587, and
65.811/549.978 ms respectively. Every current result remains inside the
unchanged product limits.

## M18 relevance regression

Normal Release execution of `M18RelevanceTests` passed with the existing 20
expected orders enforced. Its ignored current JSON output is
`artifacts/m24-b/relevance-2255810.json`.

| Metric | M18 final | M24-B | Result |
| --- | ---: | ---: | --- |
| Mandatory orders | 20/20 | 20/20 | PASS |
| Top-1 | 20/20 | 20/20 | PASS |
| Top-5 | 20/20 | 20/20 | PASS |
| MRR | 1.0 | 1.0 | PASS |
| Repeated and reversed-store determinism | PASS | PASS | PASS |

No case list, expectation, scoring, ranking, fixture, or production behavior
changed.

## Resource investigation

`scripts/measure-m24-resources.ps1` is a narrow M24-only helper. It derives a
private driver from the existing local M16 shapes, runs three bounded batches
(36 inputs, 1,500 ms separation) in one Release process, samples Windows
process counters at 500 ms, and can attach the already installed Visual Studio
18 .NET Counters agent. It adds no production dependency or telemetry.

The valid profiled run was clean at `3360396`, used the existing Visual Studio
Diagnostics `DotNetCountersBase.json`, and wrote ignored raw artifacts under
`artifacts/m24-b/20260913-153430/`. A no-profiler control run was also clean at
the same commit under `artifacts/m24-b/20260913-154208/`. The Visual Studio
collector itself increases the observed native footprint, so its OS process
numbers are not substituted for the no-profiler control.

| Gate | No-profiler Working Set MiB | No-profiler Private Bytes MiB | Handles | Threads |
| --- | ---: | ---: | ---: | ---: |
| A: process start | 14.99 | 4.86 | 157 | 8 |
| B: after batch 1 | 298.70 | 288.04 | 1,262 | 74 |
| C: after batch 2 | 311.08 | 300.52 | 1,259 | 72 |
| D: after batch 3 | 322.61 | 312.09 | 1,259 | 72 |

The profiled run confirms that the managed live state is not monotonically
retained across the same workload: GC heap was 50.62 MiB at the first counter
sample, peaked at 118.25 MiB, and ended at 22.56 MiB; LOH was 16.99 MiB,
peaked at 88.47 MiB, and ended at 29.68 MiB. The collector observed Gen0/1/2
activity. The helper's process samples also show handles and threads settling
near 1,259 and 72 after startup.

However, the no-profiler control's Private Bytes grow by 24.05 MiB from batch 1
to batch 3 while replaying the same scenario shapes. The process driver exits
immediately after its final render, so this environment cannot obtain the
required post-workload idle gate from the same process without adding a new
production test hook or native desktop automation. Neither is authorized by
M24-B, and the bounded `ShellIconCache` (128 entries) rules out the most obvious
unbounded managed icon-byte cache but does not identify the native-retention
owner.

**Disposition: BLOCKER.** The evidence does not support calling the original
Working Set observation a leak, because managed heap and LOH do not trend upward
and Windows Working Set alone is insufficient. It also does not support calling
it a stable plateau: Private Bytes rise across the bounded repeated workload and
the required same-process post-workload idle observation is unavailable. No
production cleanup, cache change, forced GC, working-set trim, or other
cosmetic optimization was made.

## Idle CPU

A fresh Release App was allowed to settle and then measured over a 30.008 s
no-change window on the physical host. On 16 logical processors it consumed
15.625 ms CPU time, or 0.00325%; Working Set changed from 151.266 to 150.820
MiB and Private Bytes from 156.879 to 156.477 MiB. Handles fell from 1,177 to
1,169 and threads from 71 to 67. This is practically zero App idle CPU with no
evidence of polling.

`QuailMaintenance` is not installed on this physical host, so current-host
service CPU could not be sampled. M24-B does not install a package solely for
this measurement. The applicable unchanged runtime evidence remains M20's
installed service no-change window: 30.00 s, 0 ms CPU delta, and 0% on four
logical processors. M24-A changed candidate version/installer evidence, not
service runtime behavior; this reuse does not replace a future installed-
candidate service measurement if M24-C requires one.

## Verification and boundary

- `dotnet test tests/Quail.Core.Tests/Quail.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~M18RelevanceTests` — PASS, 1/1.
- `scripts/run-m16-benchmark.ps1 -ScenarioPath artifacts/m16/scenarios.local.json -Repetitions 3` — PASS after the one sandbox-only pre-build failure; Release build zero warnings/errors and all M16 limits pass.
- `scripts/measure-m24-resources.ps1 -ScenarioPath artifacts/m16/scenarios.local.json -CollectVsDiagnostics` — valid profiled data captured.
- `scripts/measure-m24-resources.ps1 -ScenarioPath artifacts/m16/scenarios.local.json` — valid no-profiler control captured.

M24-A remains COMPLETE / MERGED (PR #27,
`1a7849e1a8accc1d425b832e847505f568a7c74f`). M24-B remains ACTIVE and is not
ready for independent QA until the Private Bytes trend is conclusively classified
or an evidence-backed, bounded fix is made. M24-C remains out of scope.
