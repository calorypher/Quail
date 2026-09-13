# M24-C Results — Final Security / Known Defects / RC Freeze

## Status

**BLOCKED — public 0.3.0 release readiness requires a project-owner decision
and external completion of a publicly trusted code-signing/SAC workflow.**

This document records M24-C evidence only. It reuses accepted M20–M24-B
evidence where the reviewed boundary has not changed. M24 remains active; this
does not authorize a merge, tag, GitHub Release, or release-asset publication.

## Preparation and candidate boundary

- Base `main`: `8bb8becccab5f5324649b9642bf1521ac4645a2a` (PR #28 merge).
- `scripts/prepare-milestone.ps1` safely fast-forwarded the clean Quail-Lab
  `main`, verified the host and VM at that same base, verified
  `QUAIL_LAB_DATA`, created checkpoint `M24-C-clean`, and created
  `codex/m24-c-final-security-known-defects-rc-freeze`.
- M24-A is **COMPLETE / MERGED** through PR #27 at
  `1a7849e1a8accc1d425b832e847505f568a7c74f`. M24-B is **COMPLETE / MERGED**
  through PR #28 at `8bb8becccab5f5324649b9642bf1521ac4645a2a`.
- M24-C changes only release documentation plus the bounded native
  caption-theme correction described below. It makes no service, IPC,
  protected-storage, search, ranking, installer, or package-pipeline change.

## Focused security review

The final source/configuration review covered the actual 0.3 privileged
boundaries. No new release-blocking defect was found. The applicable M20
runtime/adversarial evidence is reused because those implementation paths are
unchanged.

| Boundary | Current disposition |
| --- | --- |
| `QuailMaintenance` | One non-interactive LocalSystem service remains the sole protected index writer. `MaintenanceService` maps unexpected runtime completion to `Environment.FailFast`, allowing SCM recovery; normal stop/shutdown uses a bounded cancellation path. No ordinary Search IPC, listener, shell, updater, plugin, or network responsibility exists. |
| Named-pipe control | `Quail.Maintenance.v1` is a single local byte-mode pipe with `PIPE_REJECT_REMOTE_CLIENTS`, SYSTEM/Administrators-only DACL, fail-closed impersonated token authorization, 16 KiB framing, strict JSON fields/duplicates, ten-second deadline, five-minute freshness, and bounded replay memory. Its closed commands carry canonical volume identity or operation ID only, never a filesystem path, command, executable, or database path. |
| Protected machine state | `%PROGRAMDATA%\Quail` is opened without reparse traversal, owned by Administrators or SYSTEM, ACL-validated against dangerous non-admin rights, and used for deterministic index/state paths. Database sidecars, staging/previous files, locks, and maintenance target/health snapshots are reparse-checked. Target/health writes flush a unique temporary file and atomically replace or move it under that lease. |
| UAC / `AdminIndexWorker` | The elevated worker accepts only Build, Rebuild, or Unregister plus a GUID and canonical volume identity; it forwards a bounded request to the service and has no direct index-writing implementation. The normal App stays unelevated. |
| Search | Ordinary search remains direct read-only SQLite against complete, compatible protected snapshots. It uses no service control channel and removes only proven invalid states such as `RebuildRequired`, not transient service unavailability. |
| Installer / uninstaller | `packaging/Quail.iss` retains the fixed quoted Program Files service image path, LocalSystem automatic non-delayed startup, restrictive SCM DACL, bounded stop/replace/remove lifecycle, and reparse guards. It preserves ProgramData and LocalAppData state on uninstall. No permissive ACL was added. |

Focused automated evidence is recorded with the final build below. M20's
accepted installed service/control/ACL/reader-writer evidence remains the
representative runtime proof; M24-C did not justify repeating that campaign.

## Native caption-theme correction

M23 deferred mixed Windows-theme and forced-Quail-theme caption contrast after
an unrelated Full Search XamlRoot focus crash made further native-titlebar
experimentation unsafe. The root cause was subsequently isolated and corrected
without blaming the former caption setters.

M24-C uses the supported `AppWindow.TitleBar.PreferredTheme` API only when
`AppWindowTitleBar.IsCustomizationSupported()` returns true. `FullSearchWindow`
and `SettingsWindow` now set `TitleBarTheme.Dark` or `TitleBarTheme.Light` from
Quail's effective theme and retain the existing DWM immersive-dark fallback.
It does not add a custom title bar or individual caption-color policy.

Microsoft documents `AppWindow.TitleBar` for WinUI desktop title-bar
customization and states that `PreferredTheme` selects the title-bar theme:

- [Title bar customization](https://learn.microsoft.com/en-us/windows/apps/develop/title-bar?tabs=winui3)
- [AppWindowTitleBar.PreferredTheme](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindowtitlebar.preferredtheme?view=windows-app-sdk-1.8)

The exact Release build compiles this API with zero warnings/errors. The
available desktop-automation surface could not bind a native Quail window, so
the two visual combinations remain a short independent-QA/manual smoke:

1. light Windows with forced Dark Quail, Full Search and Settings;
2. dark Windows with forced Light Quail, Full Search and Settings.

**Disposition: FIXED IN M24-C; visual confirmation pending independent QA.**

## Known defects and limitations audit

| Item | Disposition | Rationale |
| --- | --- | --- |
| Public Smart App Control compatibility | **RELEASE BLOCKER — external prerequisite** | A single successful unsigned execution is not a public trust guarantee. The final technical RC cannot be published until it is signed and verified with a public-trust RSA identity. |
| Mixed Windows/forced-Quail native caption contrast | **FIXED IN M24-C** | The small supported theme API change compiles and preserves the established window architecture. The two visible combinations remain for independent QA. |
| Quail-Lab sleep/resume evidence | **ACCEPTED NON-BLOCKING LIMITATION** | The Gen-2 lab exposes no supported sleep/hibernate path. Existing restart/downtime, continuity, and fail-closed evidence applies; a physical sleep/resume observation is not fabricated from VM save/restore. |
| Cross-session silent uninstall while an interactive GUI remains open | **ACCEPTED NON-BLOCKING LIMITATION** | M24-A established clean uninstall after Quail is closed. A remote `/VERYSILENT` invocation cannot close a GUI in another interactive session; this does not alter the supported closed-app uninstall contract. |
| Other historical 0.2/deferred scope (content, cloud, network folders, updater, no-admin mode, history, plugins, Linux, AI) | **DEFERRED OUTSIDE 0.3** | These are explicit 0.3 non-goals, not late release defects. |

No unresolved privilege escalation, service replacement, protected-storage,
sole-writer, IPC authorization, continuity, search-trust, installer ACL, M16
performance, M18 relevance, or resource-leak blocker was found in current
evidence.

## Signing / Smart App Control decision gate

### Technical requirement

Windows Smart App Control accepts RSA signatures from trusted providers; ECC is
not currently supported for this check. It can block unsigned executable files,
including files not downloaded from the Internet. Microsoft recommends signing
all application code, including executables, DLLs, temporary installer files,
scripts, and uninstallers.

- [Sign your app for Smart App Control compliance](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control)
- [Smart App Control overview](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview)
- [SmartScreen reputation for Windows app developers](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)

For Quail's current Inno Setup delivery, public signing therefore needs to cover
the Setup EXE, its generated uninstaller, and every Quail-owned PE in the
payload: `Quail.exe`, `Quail.dll`, `Quail.Cli.exe`, `Quail.Cli.dll`,
`Quail.MaintenanceService.exe`, `Quail.MaintenanceService.dll`,
`Quail.Core.dll`, and `Quail.FileSystem.dll`. The final signed-payload audit
must enumerate actual staged PE files rather than assume this list is complete.

### Current pipeline finding

`scripts/build-installer.ps1` currently publishes App, CLI, and Maintenance
Service, merges them into `artifacts/installer-staging/win-x64/payload`, then
invokes Inno Setup. It has no signing input, signing step, signature
verification, or Inno Setup `SignTool` hook. A future smallest integration can
sign the staged Quail-owned payload PEs before `ISCC`, use Inno Setup's signing
hook for Setup/uninstaller generation, and verify signatures after packaging.
It requires no application-architecture change, but it does require the
selected provider, identity, credentials/secret handling, and owner approval.

### Real public-trust options

| Option | Current official facts | M24-C conclusion |
| --- | --- | --- |
| Azure Artifact Signing (formerly Trusted Signing), Public Trust | Microsoft's recommended non-Store path; about USD 9.99/month, identity validation, Azure subscription/account and certificate profile required. Organizations are eligible in the USA, Canada, EU, and UK; individuals only in the USA and Canada. Reputation still builds over time. | Preferred only if the owner confirms an eligible organization and accepts Azure account, cost, identity validation, and CI/credential setup. |
| Traditional RSA OV certificate from a Microsoft-trusted CA | Worldwide option; Microsoft lists typical USD 150–300/year, legal-identity validation, and HSM/token/cloud-HSM private-key handling. Reputation also builds over time. | Fallback when Azure individual/organization eligibility is unavailable or a chosen CA is required. It still needs purchase, validation, and secure signing-operation design. |
| Microsoft Store MSIX | Store re-signs an MSIX, but the current product is an Inno Setup EXE installer; Store EXE/MSI submissions still require publisher signing. | Outside 0.3 because it changes the approved deployment mode. |
| Self-signed or unsigned artifact | Not accepted for public SAC trust. | Rejected; dev/test only. |

Sources: [Microsoft's current option comparison](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) and [Artifact Signing overview](https://learn.microsoft.com/en-us/azure/artifact-signing/overview).

### Required owner decision

The owner must select a public-trust provider/path and authorize its external
cost, registration, identity validation, account/resource creation, and secure
credential/CI configuration. Only after that decision may Codex add the
minimal pipeline integration, configure secrets outside the repository, sign a
fresh artifact, verify every required signature, and perform a signed
installed-candidate smoke. No certificate, account, secret, policy change, or
signing integration was attempted in M24-C.

## Final RC build and verification

The exact clean source commit, technical unsigned installer manifest, signature
inventory, and final commands are appended after the final source commit and
canonical installer build.
