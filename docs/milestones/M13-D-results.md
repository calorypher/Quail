# M13-D results — Technical release-candidate validation

## Disposition

**COMPLETE — technical RC ready for M14.**

M13-D validated one exact framework-dependent Quail 0.2 installer as a complete installed product. The initial candidate was superseded after a supported Windows App SDK component composition removed unused AI/ML payload; all runtime measurements below are for the replacement candidate only.

## Branch and exact candidate

- Branch: `m13-d-technical-rc-validation`.
- Candidate implementation source: `e6089749b5d4c6614c833f37aaf631f90b84ac1d`.
- Preserved installer: `artifacts/rc/0.2.0/e608974/Quail-0.2.0-Setup.exe`.
- Installer: 9,946,816 bytes; SHA-256 `ea2df3ed91fc2a79a87416e044e0e4e3dea0501de9256699b5ee2e10f3009adf`.
- Staged payload: 56 files, 43,927,601 bytes; tree SHA-256 `23d0206bfcf65bc2c319f75eb00908daeb88fa263319f1962334e7f46db1e312`.
- `Quail.exe` SHA-256: `770aed13e5a433e0d8b2061f5dcf1248fb304b21f7a958554caa893731ac24cc`.
- `Quail.Cli.exe` SHA-256: `bd86f7f60aa6c74fa96f2b1c129d9c8b585360cfe97ff1625646d8a9c65d56a7`.
- Prerequisite-pin identity: `36834e841e2d0b0d24a372ed106800bc01f099197d8b0336b6b5bc5e21085a88`.

The installer SHA-256 was checked before each environment and after the Quail-Lab transfer. Both installed executables matched the frozen hashes and the installed CLI reported `quail 0.2.0`.

Final normal installation on Quail-Lab contained 58 files and 48,449,602 bytes:
the 56-file / 43,927,601-byte application payload plus `unins000.exe` and
`unins000.dat` (4,522,001 bytes). This is 15 files and 42,689,044 bytes below
the M13-C 73-file / 91,138,646-byte installed footprint.

## Automated freeze gates

| Gate | Result |
|---|---|
| Windows App Runtime detector fixtures | PASS, 6 fixtures |
| Full Release regression suite | PASS, 176 tests; unchanged from M13-B/M13-C |
| Release `Quail.App` build | PASS, 0 warnings / 0 errors |
| Release `Quail.Cli` build | PASS, 0 warnings / 0 errors |
| Canonical installer build | PASS, Inno Setup 7.1.0 |
| Payload and version guards | PASS; 0.2.0, no private .NET runtime, no audited AI/ML payload |
| Prerequisite pins | PASS; committed pins consumed without refresh |

## Installed-product verification

Quail-Lab passed normal installed start, Quick Search, Escape/hide, Settings Save/Cancel, Index Manager open/close/reopen, catalog enablement, tray Show/Exit, restart, and clean Exit. A controlled file on the existing healthy `D:` index was created and refreshed through the installed Index Manager with a normal same-account UAC prompt. The protected worker exited, status returned to Ready/Complete, the unelevated application remained resident, and the controlled result was found through the protected ProgramData database.

The physical-host exact installation passed resident start, hotkey/focus, compact/expanded behavior, indexed and zero-result searches, keyboard result navigation and successful Enter/open, Settings Save/Cancel, Index Manager reopen, tray Show/Exit, restart, and no orphan process after Exit. An earlier source-identical error-state observation remains safety evidence only; it is not presented as a replacement-candidate measurement.

## Deployment revalidation for the final payload contract

The lean candidate changed the package graph and payload contract, so M13-C's
deployment matrix was repeated on Quail-Lab. With all required runtimes present,
the final installer succeeded while its executable was outbound-firewall
blocked: the setup log contained no download branch and prerequisite transfer
was 0 bytes. Final executable identities, CLI version, one machine PATH entry,
and the Start Menu link were correct.

The final uninstaller removed all 58 Quail-owned files, the uninstall entry,
PATH entry, and Start Menu link, while retaining the empty `{app}` directory,
all three controlled data sentinels, VC++ 14.51.36247, and two registered
Windows App Runtime 2.4.0 packages. The released `v0.1.0` installer (SHA-256
`b39894a0ad807391af6abc1874ae02ff6e3e0c4133eab51e4bcd5f4460ab66a4`) then
installed and upgraded in place to the final candidate. The upgrade left one
0.2.0 uninstall entry, correct GUI/CLI hashes, no obsolete self-contained
runtime files, no superseded AI/ML files, one PATH entry, the Start Menu link,
and unchanged LocalAppData, ProgramData Indexes, and external-index sentinels.
The final same-version reinstall also succeeded with the same preservation and
one-entry invariants.

The lab no longer had a natural missing-prerequisite state: .NET Desktop 10.0.11,
VC++, and Windows App Runtime 2.4.0 were already present. Recreating one would
require restoring a checkpoint or removing shared runtimes, neither of which
M13-D permits. M13-C's natural bootstrap evidence therefore remains the
applicable missing-state proof; the final candidate retains the unchanged 2.4
Runtime deployment contract and passed the final present/offline matrix.

## Physical measurements

The startup boundary is process launch to the existing `visible-ready` event with QueryBox focus. After five conditioning runs, 30 measured installed starts were p50 457.228 ms, p95 468.197 ms, and maximum 471.904 ms. This meets the 500/750 ms targets and is appropriately higher than the smaller M08 WinUI baseline of approximately 387–400 ms.

The existing direct lifecycle harness recorded 100/100 valid hotkey summons and 500/500 summon/Escape cycles with no focus failures, duplicate process, or ghost tray regression. Hotkey-to-visible-ready was p50 29.743 ms, p95 32.603 ms, and maximum 38.999 ms, within the 50/100 ms targets and comparable to the historical approximately 18–30 ms production baselines.

After hidden-idle settle, CPU averaged 0.0024184479518087063%. The final working set/private bytes were 174,448,640 / 173,957,120 bytes (166.4 / 165.9 MiB), with 1,218 handles, 46 USER objects, and 77 GDI objects. Across baseline, 50, 100, 250, 500, and final-settle checkpoints, resources plateaued rather than growing monotonically. This meets the approved Quail 0.2 physical release criterion of approximately <=200 MiB with practically zero idle CPU and no leak trend. It does **not** meet the older approximately 100 MiB lightweight aspiration, which remains long-term work. The earlier 167.6 MiB / 165.1 MiB result and the M08 minimal WinUI reference of approximately 174.5 MiB / 170.8 MiB support that disposition.

Physical trace measurements used the installed product, real foreground input, and the privacy-safe M13 trace:

| Scenario | Runs | Input-to-first-text p50 / p95 / max | Maximum interactive queue wait | Result |
|---|---:|---:|---:|---|
| Broad typed >=4 | 10 | 52.458 / 74.145 / 74.145 ms | 34.580 ms | PASS |
| Selective >=3 control | 5 | 13.821 / 15.706 / 15.706 ms | 0.027 ms | PASS |
| Forced in-flight 1–2 character Core then >=3 | 5 | 92.903 / 99.657 / 99.657 ms | 53.106 ms | PASS |

The forced short-Core durations were 1,297.633–1,309.541 ms, but all five interactive queries used the Interactive lane and no stale short completion rendered over the newer results. Broad short Core did not start. This is within the RC gates and shows no material regression from the M13-B 64.246 ms warm broad median.

## AI/ML payload decision

The direct `Microsoft.WindowsAppSDK` 2.4.0 metapackage had brought optional AI/ML components into the framework-dependent payload, including named AI/ML-related files totalling at least 41,995,768 bytes before associated projections. No retained durable module sample exists for that superseded candidate, so M13-D makes no claim about its runtime load state.

The supported replacement uses explicit `Microsoft.WindowsAppSDK.WinUI`, `Microsoft.WindowsAppSDK.Runtime`, and `Microsoft.WindowsAppSDK.InteractiveExperiences` component packages. The replacement contains and loads none of the audited modules, removed the payload without manual pruning, preserved the framework-dependent runtime contract, reduced the staged payload from 71 files / 86,631,373 bytes to 56 files / 43,927,601 bytes, and passed the full final runtime and deployment matrix. This establishes that the audited modules are not required by the verified Quail 0.2 feature set. See `evidence/M13-D/payload-dependency-audit.md` for provenance and research.

## Limitations and follow-up

- No mixed-DPI topology was available for the replacement run. Earlier M08/M11 physical coverage remains applicable because no geometry/DPI production path changed.
- Run-at-login/autostart is absent in 0.2 and is not applicable.
- Approximately 100 MiB remains a long-term lightweight objective; no memory optimization or lifecycle redesign was justified by this stable result.
- M13-D intentionally does not perform the M14 public-release audit, signing, license/notice audit, tag, or release publication.

Independent QA accepted the exact-candidate runtime campaign. This completed
deployment revalidation leaves the disposition **technical RC ready for M14**.
M14 may start after PR #23's final evidence is independently reviewed and the
branch is merged; M13-D itself does not merge it.
