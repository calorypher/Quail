# M24-A Results — RC Integration / Deployment / Lifecycle

## Status

**COMPLETE — implementation and verification are ready for independent QA.**

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
  public-release `0.2.0` to candidate `0.3.0` transition. It permits neither
  `0.2.0` to a future target nor any other differing recognized-version
  transition; those remain uninstall-first pending separate evidence.

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
- The final canonical build from `ffbd72a49dbce4f25b7769cb836c3d055ce7b395`
  produced `Quail-0.3.0-Setup.exe`: framework-dependent payload `66` files /
  `45,567,865` bytes; installer `10,243,997` bytes; SHA-256
  `7c1b82e29a729a0f7a8dc445f12ea6b1daa5097c966fd18904bde4b8f220d01d`.
  The existing installer script's release-build provenance guard passed.
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
- The final committed artifact was then installed through the supported
  same-version replacement path with exit `0`; both Quail.exe and the service
  reported product version `0.3.0+ffbd72a49dbce4f25b7769cb836c3d055ce7b395`.

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

## Startup and visible UI evidence

- A user-owned VMConnect smoke enabled Launch at startup through Settings,
  rebooted and signed in normally, and confirmed tray-first launch with no
  foreground Quick Search plus successful `Alt+Space` summon.
- The owned HKCU Run value was then independently read as the exact quoted
  `"C:\Program Files\Quail\Quail.exe"` command while Quail was resident.

## Uninstall evidence

- With Quail already closed, a fresh candidate reinstall and its generated
  uninstaller both exited `0`. The payload and installation directory,
  uninstall registration, `QuailMaintenance`, owned HKCU Run value, and owned
  machine PATH entry were all removed.
- ProgramData, LocalAppData, settings, catalog, and the M24-A user-state
  sentinel remained present with their pre-uninstall hashes unchanged.
- An initial `/VERYSILENT` uninstall invoked through SSH while the manually
  launched GUI remained open in a separate interactive session removed the
  service, registration, Run value, and PATH but could not delete mapped GUI
  libraries. The generated uninstaller then removed itself. This is recorded as
  a noninteractive cross-session test limitation, not a clean-exit uninstall
  PASS or evidence that the installer closes an interactive GUI in that mode.
  The residual payload was moved intact to
  `C:\QuailLab\M24-A\residual-after-cross-session-uninstall` before the clean
  closed-app validation; no ProgramData or LocalAppData was moved or deleted.

## Independent-QA correction

- Independent QA identified that the initial `0.2.0` exception was not coupled
  to the candidate version, which could have allowed an unverified future
  `0.2.0` to `0.4.0` or `1.0.0` transition. The guard now permits either an
  installed version equal to the candidate version, or exactly candidate
  `0.3.0` with installed `0.2.0`.
- `scripts/test-installer-cleanup-safety.ps1` now asserts both sides of that
  bounded pair and its combined condition. It passed after the correction.
- The canonical installer build using Inno Setup `7.1.0` passed after the
  correction and produced `Quail-0.3.0-Setup.exe`, reporting version `0.3.0`.
  No VM campaign was repeated because the corrected guard has identical
  behavior for the already verified public-release `0.2.0` to candidate
  `0.3.0` transition.

## Completion boundary

M24-A changes and evidence are complete. The normal boundary is this branch and
its pull request ready for independent QA. M24 remains active: M24-B
performance/relevance/resources and M24-C security, signing/SAC disposition,
known-defect freeze, tags, release, and publication work remain explicitly
deferred.
