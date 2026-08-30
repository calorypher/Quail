# M14-T results — Installer simplification and fixed-location contract

## Disposition

`fixed-location installer simplified and validated`

The installer implementation and focused lifecycle/security matrix passed.
T9 is accepted as a fixed-location ACL/security validation: M14-T neither
creates nor changes its own DACL, and the canonical Program Files destination
retains standard Windows access control. A separate standard-user executable
smoke was unavailable in the current Quail-Lab and is not required for this
disposition.

No publication-sensitive action occurred. No checkpoint was restored, deleted,
renamed, or overwritten.

## M14-T validation artifact

| Field | Value |
|---|---|
| Worktree basis | M14-T test worktree based on `ea26295aa5a3666a2b5ca02a1dd0e862966de95a` |
| Implementation snapshot | Subsequently committed as `186f73930580472da79cf275cc582bce393eb066` |
| Installer | `Quail-0.2.0-Setup.exe` |
| Installer SHA-256 | `a7007973ce992a856b341598ecd66ae150bcacf1b12d054c6b7045c473f4247a` |
| Installer size | `9,947,148 B` |
| Payload | 56 files, `43,926,873 B` |
| Payload tree SHA-256 | `7f929193595233830719481cf2324aba697453788ddb38b3e32bf42a4bb0a3d8` |
| `Quail.exe` SHA-256 | `c1d4aa5625fb7d2ae8343177fd3e416b07242bd6a200fe807ee8e8b000f7f0d8` |
| `Quail.Cli.exe` SHA-256 | `377dc6d2d3c2ec5943cc70e4840bcf5e00aecaac107ff6a5c7e38a19a8553546` |

This installer was built by `scripts/build-installer.ps1` from the tested
M14-T worktree while its checked-out basis was `ea26295aa5a3666a2b5ca02a1dd0e862966de95a`,
before the production/packaging implementation snapshot was committed as
`186f73930580472da79cf275cc582bce393eb066`. It is an M14-T validation
artifact used exclusively as evidence for T1–T10. It is not a candidate whose
installer SHA-256 is claimed reproducible directly from `ea26295...`, and it
is not publishable.

The next canonical source-derived technical candidate will be built after M14-T
is merged, during M14-A finalization.

## Implemented contract

- `DefaultDirName` remains `{autopf}\Quail`; `DisableDirPage=yes` and
  `UsePreviousAppDir=no` remove the normal selection and remembered-directory
  paths.
- `ValidateFixedInstallationContract` normalizes and compares both
  `WizardDirValue` and effective `{app}` with the expanded canonical directory
  before prerequisite bootstrap. Any mismatch aborts with the fixed-location
  message.
- The AppId uninstall registration is the installed-version signal. A
  registered custom location or different `DisplayVersion` aborts with an
  uninstall-first message. A non-empty canonical directory without a
  same-version canonical registration aborts without overwrite.
- The only machine PATH entry Quail adds or removes is the canonical directory;
  it is de-duplicated before addition.
- `LegacySelfContained0_1.issinc`, its 187-file cleanup list, the include, and
  custom-directory ownership recognition were removed. There is no
  `[InstallDelete]` section, wildcard cleanup, recursive deletion, or automatic
  legacy uninstaller execution.

## Build and regression verification

| Gate | Result |
|---|---|
| Release tests | PASS — 176/176 |
| Release App build | PASS — 0 warnings / 0 errors |
| Release CLI build | PASS — 0 warnings / 0 errors |
| Windows App Runtime detector | PASS — 6 cases |
| Installer fixed-location safety guard | PASS |
| M14-P release provenance/privacy guard | PASS — 56 payload files, 3 forbidden-root classes scanned |
| Canonical installer, payload, and prerequisite guards | PASS |
| Privacy scan of assembled payload | PASS |
| `git diff --check` | PASS |

## Quail-Lab focused matrix

The candidate transfer SHA-256 matched the host candidate. The retained 0.1
test input was read only and hash-verified as
`b39894a0ad807391af6abc1874ae02ff6e3e0c4133eab51e4bcd5f4460ab66a4`.

| Test | Result | Evidence |
|---|---|---|
| T1 fresh canonical install | PASS | Canonical payload hashes, 56 payload files, one AppId registration, one canonical PATH entry, and correct Start Menu target. |
| T2 `/DIR=` nonexistent foreign path | PASS | `/VERYSILENT` exited 7; destination remained absent; no payload, PATH, shortcut, or registration. |
| T3 `/DIR=` existing empty foreign path | PASS | `/VERYSILENT` exited 7; empty directory stayed empty; no global state. |
| T4 `/LOADINF` custom destination | PASS | `/VERYSILENT` exited 7; custom destination was not created and no global state appeared. |
| T5 same-version reinstall | PASS | Candidate reinstalled at the canonical location; payload hashes, one PATH/registration/shortcut state, and a nested foreign sentinel all remained correct. |
| T6 different development version | PASS | Real hash-verified 0.1 installed; candidate exited 7 without modifying its CLI-only payload or `0.1.0` registration; manual old uninstall then candidate installation succeeded and LocalAppData/ProgramData sentinels survived. |
| T7 old/custom-location development registration | PASS | Controlled AppId metadata outside the canonical directory caused exit 7 with no canonical payload, PATH, or shortcut mutation. |
| T8 uninstall | PASS | Payload, shortcut, canonical PATH entry, and registration were removed; foreign sentinel and external LocalAppData/ProgramData sentinels survived; shared .NET Desktop Runtime, Windows App Runtime, and VC++ runtime remained. |
| T9 focused ACL/security validation | PASS | T1 installed exactly under `C:\Program Files\Quail` (`{autopf}\Quail`). M14-T contains no DACL/permission mutation. Captured effective directory/file ACLs grant Users read/execute only, contain no write/modify/full-control grant for Users, Authenticated Users, or Everyone, and retain full control for Administrators and SYSTEM. The attempted standard-user write did not change the `Quail.exe` SHA-256. The executable smoke itself was unavailable in Quail-Lab (`SCHED_S_TASK_HAS_NOT_RUN` / `0xC0000142`) and was not forced by weakening VM policy or ACLs. T2–T4 independently passed for `/DIR=` and `/LOADINF` alternate destinations. |
| T10 reparse sanity | PASS | A disposable junction at the canonical location caused exit 7; its user-writable target stayed empty and no global state appeared. |

## Evidence applicability

M14-I's 187-entry compatibility mechanism is intentionally superseded and
removed. Its retained security invariant is narrower: Quail performs no
recursive or unowned destination deletion.

M14-P's source-derived release provenance/privacy contract remains applicable
and passed for this candidate. M13-D runtime/performance evidence remains
applicable because this milestone changes no application source, package graph,
runtime configuration, or payload semantics; its artifact identities are
superseded by the candidate identities above.

## T9 ACL evidence

The captured installation-directory DACL grants the built-in Users group
read/execute (`0x1200a9`) and does not grant it write, modify, or full control.
The installed executable has the same read/execute-only Users grant. Neither
Authenticated Users nor Everyone receives a write-capable grant. Administrators
and SYSTEM retain full control. The installer source contains no `icacls`, ACL,
security-descriptor, or permission-setting operation; it relies on the normal
inheritance of the fixed `{autopf}\Quail` directory.

The candidate's `Quail.exe` hash remained
`c1d4aa5625fb7d2ae8343177fd3e416b07242bd6a200fe807ee8e8b000f7f0d8`
after the attempted standard-user modification. The unavailable executable
smoke was a Quail-Lab session limitation, not a product or installer failure.
