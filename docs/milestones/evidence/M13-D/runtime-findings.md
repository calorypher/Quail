# M13-D installed runtime findings

## Candidate integrity

The exact installer was re-hashed before each environment and after the
Quail-Lab transfer. The installed identities matched the frozen payload:

| Environment | `Quail.exe` SHA-256 | `Quail.Cli.exe` SHA-256 |
|---|---|---|
| Quail-Lab | `770aed13e5a433e0d8b2061f5dcf1248fb304b21f7a958554caa893731ac24cc` | `bd86f7f60aa6c74fa96f2b1c129d9c8b585360cfe97ff1625646d8a9c65d56a7` |
| Physical host | `770aed13e5a433e0d8b2061f5dcf1248fb304b21f7a958554caa893731ac24cc` | `bd86f7f60aa6c74fa96f2b1c129d9c8b585360cfe97ff1625646d8a9c65d56a7` |

Installed CLI reported `quail 0.2.0` in both checks.

The final Quail-Lab deployment revalidation repeated the exact candidate's
identity check after prerequisites-present/offline reinstall, real `v0.1.0` ->
0.2 upgrade, and same-version reinstall. All installed hashes remained those
in the table. Final installed footprint was 58 files / 48,449,602 bytes: the
56-file / 43,927,601-byte staged application payload plus two uninstaller files
(4,522,001 bytes).

## Quail-Lab installed product

- The user normally upgraded the SHA-verified replacement installer.
- Normal resident start, Quick Search, Escape/hide, Settings Save and Cancel,
  Index Manager open/close/reopen, tray Show and Exit, restart, and clean Exit
  were manually confirmed.
- The existing healthy `D:` catalog was enabled through Index Manager and
  remained enabled after reopening it.
- A controlled `D:` file was created, then a supported same-account UAC
  Refresh was started through the installed Index Manager. UAC appeared as
  expected; the status returned to Ready/Complete, the unelevated application
  remained usable, and no worker remained.
- Independent remote read confirmed the protected ProgramData database,
  enabled catalog, and one installed-CLI result for the controlled file.
- Process-module samples on the replacement candidate at resident startup,
  hidden idle, Quick Search, real search, Settings, and an open Index Manager
  contained none of the AI/ML modules listed in `payload-dependency-audit.md`.

## Physical installed product

- Normal resident start, hotkey focus, compact/expanded layout, indexed search,
  keyboard navigation, successful keyboard Enter/open, zero-result state,
  Settings Save and Cancel, Index Manager reopen, tray Show/Exit, restart, and
  no process after the verified Exit were manually confirmed on the exact
  installed executable.
- The physical host had no active mixed-DPI topology. This check is therefore
  unavailable; M08/M11 physical mixed-DPI coverage remains applicable because
  no later geometry/DPI production path changed.
- Run-at-login/autostart is absent from 0.2 and is not applicable.

See `physical-measurements.md` for automated physical startup, hotkey,
lifecycle, resource, and search results.

## Final deployment lifecycle

- Prerequisites-present/offline reinstall passed with 0 B prerequisite transfer
  while the final setup executable was outbound-blocked; its setup log contained
  no download branch.
- Final uninstall removed all app files, uninstall registration, PATH entry,
  and shortcut, while retaining the empty application directory, controlled
  LocalAppData/ProgramData/external-index sentinels, VC++ runtime, and Windows
  App Runtime 2.4.0.
- The verified released 0.1 installer then upgraded to this final candidate.
  The result had one 0.2.0 uninstall entry, one PATH entry, Start Menu GUI,
  correct hashes, no obsolete self-contained files, no audited AI/ML files,
  and unchanged controlled data. Same-version reinstall retained those
  invariants.

## Memory decision

The replacement exact candidate settled at 166.4 MiB working set and 165.9 MiB
private bytes after the 500-cycle run. It is stable, practically idle, and
below the approved 0.2 approximately 200 MiB physical release criterion. The
earlier 167.6 MiB / 165.1 MiB M13-D observation and approximately 174.5 MiB /
170.8 MiB M08 minimal WinUI reference remain the basis for treating
approximately 100 MiB as a long-term aspiration rather than claiming it was
met by 0.2.
