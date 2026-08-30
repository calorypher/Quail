# M14-T results — Installer simplification and fixed-location contract

## Disposition

**STOP — `remediation blocked`.**

The installer implementation and every completed focused lifecycle/security
test passed. The required T9 execution proof from a standard non-admin account
could not be completed in the current Quail-Lab without changing its user
execution/security-policy configuration. ACL inspection is supportive evidence
only and is not recorded as a substitute for that required runtime proof.

No publication-sensitive action occurred. No checkpoint was restored, deleted,
renamed, or overwritten.

## Exact candidate

| Field | Value |
|---|---|
| Source HEAD | `ea26295aa5a3666a2b5ca02a1dd0e862966de95a` |
| Installer | `Quail-0.2.0-Setup.exe` |
| Installer SHA-256 | `a7007973ce992a856b341598ecd66ae150bcacf1b12d054c6b7045c473f4247a` |
| Installer size | `9,947,148 B` |
| Payload | 56 files, `43,926,873 B` |
| Payload tree SHA-256 | `7f929193595233830719481cf2324aba697453788ddb38b3e32bf42a4bb0a3d8` |
| `Quail.exe` SHA-256 | `c1d4aa5625fb7d2ae8343177fd3e416b07242bd6a200fe807ee8e8b000f7f0d8` |
| `Quail.Cli.exe` SHA-256 | `377dc6d2d3c2ec5943cc70e4840bcf5e00aecaac107ff6a5c7e38a19a8553546` |

The candidate was built only by `scripts/build-installer.ps1` from the exact
staging source. It is a technical candidate and was not published.

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
| M14-P release provenance/privacy guard | PASS — 56 payload files, 2 forbidden-root classes scanned |
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
| T9 standard-user payload replacement | BLOCKED | Installed directory/file ACLs grant Users read/execute rather than write, and payload hash remained unchanged. However, both a password task and `Start-Process -Credential` failed to start a process for the fresh standard account in this lab (`SCHED_S_TASK_HAS_NOT_RUN` / `0xC0000142`). No lab policy or ACL was weakened to force the test. |
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

## Required follow-up

Run T9 in a Quail-Lab configuration that can launch a genuine standard-user
process without weakening security policy, then rerun the final fixed-location
installer candidate checks before changing the disposition to validated.
