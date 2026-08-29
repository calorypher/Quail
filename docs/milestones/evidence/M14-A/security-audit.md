# M14-A focused security audit

## Disposition

**BLOCKER — production packaging remediation required.**

## Finding: recursive deletion of a caller-selected install directory

Severity: **high impact / publication blocker**.

`packaging/Quail.iss` combines:

- `DefaultDirName={autopf}\Quail` without an ownership-enforcing destination
  contract;
- `PrivilegesRequired=admin`;
- `[InstallDelete] Type: filesandordirs; Name: "{app}\*"`.

The destination page is available for a fresh install under Inno Setup's
default `DisableDirPage=auto` behavior. The command line also accepts a fully
qualified `/DIR=` override. Inno Setup processes `[InstallDelete]` first, and
`filesandordirs` recursively removes matched directories and contents.

An interactive user, deployment wrapper, saved configuration, or mistyped
command can therefore direct elevated setup to an existing non-Quail
directory. Continuing can delete unrelated content before payload copy. The
existing-directory warning does not prove ownership and silent modes can
suppress it.

Primary behavior references:

- [Inno Setup `[InstallDelete]`](https://jrsoftware.org/ishelp/topic_installdeletesection.htm);
- [installation order](https://jrsoftware.org/ishelp/topic_installorder.htm);
- [`filesandordirs` semantics and wildcard warning](https://jrsoftware.org/ishelp/topic_uninstalldeletesection.htm);
- [`DisableDirPage`](https://jrsoftware.org/ishelp/topic_setup_disabledirpage.htm);
- [`/DIR=` override](https://jrsoftware.org/ishelp/topic_setupcmdline.htm).

## Required remediation properties

- Do not recursively delete all children merely because a path is `{app}`.
- Clean only proven prior Quail installer-owned paths, or enforce and validate
  a dedicated destination plus ownership before cleanup. Hiding the wizard
  alone does not address overrides or a pre-existing unowned default folder.
- Preserve accepted 0.1 obsolete-runtime cleanup without deleting unrelated
  sentinels, and fail closed when ownership cannot be proven.
- Revalidate clean install, upgrade, reinstall, uninstall, custom/silent
  destinations, data preservation, PATH, and shortcut behavior.

M14-A does not implement the remediation because it changes the production
packaging contract and technical RC identity.

## Other surfaces reviewed before stop

No additional high-impact finding was identified in the reviewed subset:

- the elevated worker validates elevation, current NTFS volume identity, and a
  matching current-user catalog entry;
- the ProgramData database path is derived from stable identity; there is no
  caller-supplied privileged destination;
- protected directories restrict writes to Administrators/SYSTEM and use
  reparse checks, retained non-delete-shared handles, sidecar validation, and
  a per-volume exclusive lock;
- elevation uses `ProcessStartInfo.ArgumentList` with `Verb=runas`, not a shell
  command string;
- prerequisites are restricted to committed Microsoft HTTPS URLs and SHA-256
  pins verified before execution;
- Quail 0.2 has no updater, telemetry backend, cloud provider, remote index,
  unsafe deserializer, or plaintext application secret;
- indexed open passes an existing reconstructed path directly to Windows Shell
  rather than constructing a command line.

The remaining general/publication review was not claimed complete after the
blocker.
