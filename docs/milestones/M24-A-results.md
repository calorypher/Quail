# M24-A Results — RC Integration / Deployment / Lifecycle

## Status

**ACTIVE — implementation and verification are in progress.**

## Preparation

- Base `main`: `c1c0606440b2cca96eaff0068a4bbb8c04e7dec8` (PR #26 merge).
- `scripts/prepare-milestone.ps1` verified matching clean host and Quail-Lab
  repositories, created checkpoint `M24-A-clean`, and created branch
  `codex/m24-a-rc-integration-deployment-lifecycle`.
- The disposable Quail-Lab data volume reported by the canonical script is
  `QUAIL_LAB_DATA`.

## Candidate integration changes

- `Directory.Build.props` is the sole product-version source and now supplies
  `Version` `0.3.0`, `AssemblyVersion` `0.3.0.0`, and `FileVersion` `0.3.0.0`.
- The Inno Setup fixed-location guard retains its recognized-installation,
  canonical-path, and reparse-point checks while allowing only the representative
  released `0.2.0` predecessor. Other development-version transitions remain
  uninstall-first pending separate evidence.

## Tooling

- `dotnet`: `C:\Program Files\dotnet\dotnet.exe` (SDK `10.0.401`).
- PowerShell: bundled `pwsh` `7.6.5` and Windows PowerShell `5.1` are available.
- Inno Setup: `C:\Program Files\Inno Setup 7\ISCC.exe` (`7.1.0`), supplied to
  the canonical installer script through `-IsccPath`.
- WinApp: `C:\Users\gawry\AppData\Local\Microsoft\WindowsApps\winapp.exe`
  (`0.6.1`); it is not used as a replacement installer or signing tool.

## Automated and payload evidence

- `scripts/test-installer-cleanup-safety.ps1` — PASS after the targeted upgrade
  guard change.
- `dotnet test Quail.sln -c Release --no-restore` — PASS, `342/342`.
- `dotnet build Quail.sln -c Release --no-restore` — PASS, zero warnings and
  zero errors.
- `dotnet list src/Quail.Core/Quail.Core.csproj reference` reported no project
  references, preserving the Core-to-FileSystem dependency direction.
- `git diff --check` — PASS during the candidate baseline.
- The initial canonical installer build produced a framework-dependent payload
  of `66` files / `45,567,849` bytes and an installer of `10,243,817` bytes.
  The final committed-candidate artifact identity is recorded after the
  post-commit rebuild below.
- `Quail.exe`, `Quail.Cli.exe`, and `Quail.MaintenanceService.exe` each reported
  file version `0.3.0.0`; installed CLI reported `quail 0.3.0`. Settings About
  continues to read the entry assembly version rather than a separate UI value.

## Released 0.2.0 provenance and transition

- Public GitHub release `v0.2.0` supplied
  `Quail-0.2.0-Setup.exe`, `9,947,313` bytes, with published SHA-256
  `cd5e11b28178bdfa46713482d128ccf98940bb37421b8c85fc1160533dc5e848`.
  The downloaded local asset matched that hash exactly.
- Before establishing that historical baseline, the lab contained a later
  service-capable `0.2.0` payload. Its own uninstaller removed only the payload,
  service, registration, PATH/startup ownership and preserved ProgramData plus
  all checked LocalAppData settings, catalog, and M24-A sentinel hashes.
  This was necessary because the released 0.2.0 installer predates M20 and does
  not know how to stop the later service.
- The verified released installer then installed normally with exit `0`;
  `DisplayVersion` and Quail.exe were `0.2.0` / `0.2.0.0`, no service was
  present, and all preservation hashes remained unchanged.
- The 0.3 candidate installer installed over that exact release with exit `0`.
  It registered `0.3.0`, installed all production executables as `0.3.0.0`,
  restored `QuailMaintenance` to `Running`, and preserved ProgramData, settings,
  catalog, and sentinel hashes exactly. No automatic schema/data migration was
  added or claimed.

## Service and lifecycle evidence

- Installed `QuailMaintenance` is `LocalSystem`, `Auto`, non-delayed
  (`DelayedAutoStart=0`), and uses the exact quoted path
  `"C:\Program Files\Quail\Quail.MaintenanceService.exe"`.
- `sc qfailure` recorded the existing `86400`-second reset and bounded restart
  actions. `sc sdshow` returned the constrained expected service DACL; the
  executable and protected ProgramData ACLs remained protected.
- A representative service stop/start returned to `Running` and
  `Healthy`/trusted. This reused M20 security evidence rather than repeating its
  full adversarial campaign.

## Continuous-maintenance and restart evidence

All probes used the disposable indexed `D:` volume and the installed candidate's
read-only `Quail.Cli` search over the protected service-owned database. No
Refresh or Rebuild was issued.

- CREATE `m24a-create-001.txt` became searchable as one result.
- RENAME converged to `results=0` for the old name and one result for
  `m24a-renamed-002.txt` with the same file ID.
- MOVE updated the result path to the `Moved` subdirectory with the same file ID.
- DELETE converged to `results=0`.
- A separately created `m24a-restart-003.txt` remained searchable after a normal
  Windows restart; service state was `Running` and health `Healthy`/trusted.
- A second file, `m24a-downtime-004.txt`, was created while the service was
  stopped and then followed by a normal Windows restart. The automatic service
  start caught it up to a searchable result with `Healthy`/trusted health.

## Physical-host observation

With SAC intentionally unchanged, the fresh unsigned candidate was started from
the staged payload for a bounded five-second observation. It remained running
from the expected payload path and was then closed by its exact process ID. No
host installation, policy change, signing, exclusion, or certificate action was
performed.

## Pending user-owned UI evidence

The SSH service session has no interactive desktop. One short VMConnect smoke is
still needed to enable Launch at startup through Settings, reboot/sign in, and
confirm hidden/tray-first launch plus `Alt+Space` summoning Quick Search. This
does not block the automated service/index continuity evidence but remains a
required visible M24-A acceptance check.

## Evidence to be completed

After the user-owned startup smoke, complete the candidate uninstall check:
payload, service registration, and owned PATH entry must be removed while
ProgramData and LocalAppData state remain preserved. Record the final committed
installer manifest/hash and exact candidate commit after rebuilding from the
committed worktree. Unchanged M20 protected-service and M21 startup evidence is
referenced rather than copied unless M24-A validation changes the relevant
boundary.
