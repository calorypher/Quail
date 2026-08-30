# M14-A finalization — M14-T delta security review

## Scope

This is a source review of only the installer/build delta merged by M14-T.
It does not repeat the earlier M14-A history, privacy, asset, vulnerability,
or prerequisite audits, and it does not repeat Quail-Lab T1-T10.

## Committed source reviewed

- M14-A finalization baseline: `c3688ee80ee5f5942337fde782f91572773a990f`;
- M14-T implementation commit: `186f73930580472da79cf275cc582bce393eb066`;
- M14-T validation source basis: `ea26295aa5a3666a2b5ca02a1dd0e862966de95a`.

The current fixed-location source matches the M14-T contract in the security
relevant areas. Documentation-only M14-A finalization changes do not alter
that contract.

## Findings

| Required invariant | Current committed-source result |
|---|---|
| Fixed location | `DefaultDirName={autopf}\\Quail`, directory page disabled, and prior directory reuse disabled. |
| Override resistance | `ValidateFixedInstallationContract` compares normalized `WizardDirValue` and effective `{app}` with the canonical path before prerequisite bootstrap. |
| `/DIR=` and `/LOADINF` | Alternate effective destinations fail the same validation before payload, PATH, shortcut, or uninstall registration mutation. |
| Cleanup | No `[InstallDelete]`, `DelTree`, recursive remove, wildcard cleanup, or legacy 187-file include remains. |
| PATH | `SetQuailPathEntry` uses `CanonicalInstallationDirectory`, never `{app}`. |
| Existing registration | A different version or noncanonical registered location requires a manual uninstall first; an unregistered nonempty canonical directory is fail-closed. |
| ACL handling | No custom ACL, DACL, `icacls`, permission, or security-descriptor manipulation exists. The trusted Program Files location relies on normal Windows inheritance. |

## M14-T evidence applicability

M14-T T1-T10 is applicable without rerun. T2-T4 establish that nonexistent,
empty, and `/LOADINF` foreign paths fail before global state changes. T5-T8
cover same-version reinstall, uninstall-first development-version handling,
custom registration rejection, and uninstall. T9 establishes the canonical
Program Files ACL state without a Quail ACL mutation; T10 covers a reparse
point at the canonical location.

No contradiction or material security-relevant source change was found.

## Disposition

**PASS — the previous custom-destination/MACHINE PATH security blocker is
resolved.**
