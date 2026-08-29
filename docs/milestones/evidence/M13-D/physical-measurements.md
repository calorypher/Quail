# M13-D physical-host measurements

All measurements used the exact installed candidate `e6089749b5d4c6614c833f37aaf631f90b84ac1d`.
The privacy-safe search trace records timings, generations, lanes, and counts;
it does not retain query text, paths, identities, or telemetry.

## Startup

Boundary: `Start-Process` of installed `C:\Program Files\Quail\Quail.exe`
to the application's `visible-ready` test event. Five conditioning runs
preceded 30 measured runs. Each measured resident process was then ended by
the exact-PID harness cleanup so the next measurement started from a clean
process state; clean tray Exit/restart was verified separately.

| Runs | p50 | p95 | max | QueryBox focus |
|---:|---:|---:|---:|---|
| 30 | 457.228 ms | 468.197 ms | 471.904 ms | 30/30 |

The result meets the 500 ms p50 and 750 ms p95 targets. It is reasonably near
the earlier approximately 387--400 ms minimal WinUI baseline despite the
complete product surface.

## Hotkey, lifecycle, and hidden idle

The existing M10 harness used its canonical `visible-ready` boundary and real
SendInput for 100 hotkey cycles, 500 summon/Escape cycles, and 120 hidden-idle
samples. Every hotkey confirmed foreground and QueryBox keyboard focus.

| Metric | Result |
|---|---|
| Hotkey valid summons | 100/100 |
| Hotkey p50 / p95 / max | 29.743 / 32.603 / 38.999 ms |
| Summon/Escape lifecycle | 500/500, no stuck state or duplicate/orphan process |
| Hidden-idle CPU | 0.0024184% average |
| Resource assessment | pass; no monotonic handle, USER, GDI, private-byte, or working-set leak |

| Checkpoint | Working set | Private bytes | Handles | USER | GDI |
|---|---:|---:|---:|---:|---:|
| Warm baseline | 155.1 MiB | 160.1 MiB | 1242 | 48 | 77 |
| Cycle 50 | 167.6 MiB | 167.3 MiB | 1250 | 50 | 77 |
| Cycle 100 | 167.5 MiB | 167.3 MiB | 1252 | 50 | 77 |
| Cycle 250 | 168.0 MiB | 167.6 MiB | 1245 | 50 | 77 |
| Cycle 500 | 166.9 MiB | 166.4 MiB | 1241 | 48 | 77 |
| Final settle | 166.4 MiB | 165.9 MiB | 1218 | 46 | 77 |

The initial cache/framework plateau is bounded and settles rather than growing
monotonically. The CPU result is within the <=0.05% idle target. The memory
result meets the approved 0.2 approximately <=200 MiB physical release
criterion, but not the long-term approximately 100 MiB aspiration.

## Search responsiveness

The current complete physical index was used without rebuilding it. Input was
real foreground keyboard input; measurement used the M13 privacy-safe trace.

| Condition | n | Input-to-first-text p50 / p95 / max | Max final queue wait | Result |
|---|---:|---:|---:|---|
| Fast typed broad >=4 | 10 | 52.458 / 74.145 / 74.145 ms | 34.580 ms | PASS; 0 short Core starts |
| Selective >=3 control | 5 | 13.821 / 15.706 / 15.706 ms | 0.027 ms | PASS; five-result control |
| Forced in-flight one-char to broad | 5 | 92.903 / 99.657 / 99.657 ms | 53.106 ms | PASS; 0 stale renders |

Forced one-character Core work remained intentionally expensive (p50 1302.608
ms), but all final searches ran in the Interactive lane and every short
completion was discarded. The broad result is below the RC 250/500 ms target
and remains materially better than M13-A's 3059.582 ms broad median. It is in
the same practical range as M13-B's 64.246 ms warm broad median without
requiring that historical cache state as a hard target.

The official WinApp UIA enumeration did not expose Quail's custom window in
this desktop session. The existing product test pipe, M10 real SendInput, and
the M13 trace were used instead; no new production automation framework was
introduced.
