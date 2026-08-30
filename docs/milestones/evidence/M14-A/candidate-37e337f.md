# M14-A finalization — committed-source technical candidate

## Identity

| Field | Value |
|---|---|
| Exact committed source | `37e337f6793ae68889b964b0232637cc076aec2b` |
| Source state before build | clean `m14-a-finalization` worktree |
| Build entry point | `scripts/build-installer.ps1` |
| Deployment | framework-dependent, unpackaged WinUI 3 |
| Installer | `Quail-0.2.0-Setup.exe` |
| Installer size | 9,947,284 B |
| Installer SHA-256 | `efd6bf4846d9d82fb4e4b432522ce73b0a47a581f213d784406c1f31aef6be49` |
| Payload | 56 files, 43,926,881 B |
| Payload tree SHA-256 | `53e528b6291a34804c85a403d2f446509f86f35b785c0882564778927d7f7b9a` |
| `Quail.exe` SHA-256 | `8b2c49da52ac611a71544aaf4ad6a060350ccd047f43617903e6d1219b173021` |
| `Quail.Cli.exe` SHA-256 | `1decfb0aed48d7b3dea83d271cfe3e1e62f2176155fa9eae7b60951d2a29cb5b` |

The payload tree hashes UTF-8 records of normalized `/` relative path, byte
length, and lowercase SHA-256, in ordinal path order, separated by tabs and
terminated with a newline.

## Required gates

| Gate | Result |
|---|---|
| Release tests | PASS — 176/176 |
| Release App build | PASS — 0 warnings / 0 errors |
| Release CLI build | PASS — 0 warnings / 0 errors |
| Windows App Runtime detector | PASS — 6 cases |
| Fixed-location installer guard | PASS |
| M14-P provenance/privacy guard | PASS — 56 payload files, 3 forbidden-root classes |
| Installer privacy scan | PASS — 1 installer, 3 forbidden-root classes |
| Current SourceLink | PASS — all three Quail PDBs name the exact staging source commit and no physical checkout path was found |
| Prerequisite/version/payload checks | PASS — canonical builder used the three committed Microsoft pins and validated the framework-dependent payload |
| Canonical installer build | PASS |
| `git diff --check` | PASS |

The first test/build attempt needed ordinary NuGet restore because this host's
local cache lacked part of the declared graph. After restoring only declared
dependencies, all recorded build/test gates ran without a restore. The source
worktree remained clean; generated outputs are ignored.

## Scope boundaries

This is a technical candidate only. Its three Quail PDBs truthfully map to
`calorypher/Quail-public-staging` at the exact commit above. It does not
supersede the required M14-B post-rename candidate: final SourceLink must name
`calorypher/Quail`, not the staging repository. M14-A intentionally did not
repeat M13-D performance tests, search benchmarks, 500-cycle lifecycle
validation, M14-T T1-T10, or the full history/privacy audit.
