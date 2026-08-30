# M14-A rerun — public-release security audit

## Disposition

**BLOCKER — `remediation required`.**

## Finding: untrusted custom directory enters machine PATH

Severity: **high impact / publication blocker**.

`packaging/Quail.iss` combines:

- `PrivilegesRequired=admin`;
- interactive custom destination support and Inno Setup's `/DIR=` mechanism;
- a destination validator that accepts nonexistent and empty directories after
  reparse checks, without validating owner or ACL;
- payload copy into `{app}`;
- addition of `{app}` to the machine `PATH`.

M14-I correctly removed recursive `{app}\*` cleanup, rejects reparse paths,
requires recognized content for non-empty destinations, and limits legacy 0.1
cleanup to 187 exact entries. Its T2 evidence deliberately confirms that an
empty custom destination is accepted.

If an accepted directory is owned or writable by an unprivileged user, that
user can replace Quail files and place arbitrary command names in a system-wide
executable search directory. A later administrator or service resolving a
previously absent command through `PATH` can execute attacker-controlled code
at higher privilege. App-local executable/DLL replacement is also possible.

Inno Setup's enabled `RedirectionGuard` prevents traversal through an
unprivileged-created reparse point in the setup process. Its documentation
explicitly recommends avoiding publicly writable directories and does not make
a legitimate user-writable directory trusted or protect child applications.

References:

- [Inno Setup RedirectionGuard](https://jrsoftware.org/ishelp/topic_setup_redirectionguard.htm);
- [Inno Setup `/DIR=` and command-line parameters](https://jrsoftware.org/ishelp/topic_setupcmdline.htm).

No destructive exploit was needed: installer source and the existing T2
lifecycle evidence establish the accepted empty-directory path.

## Required remediation contract

Use a separately approved packaging remediation that either:

- confines per-machine installation to a trusted Program Files destination; or
- securely creates and validates every accepted custom directory with trusted
  ownership and ACLs before payload copy or machine `PATH` modification.

The rule must cover wizard choice, `/DIR=`, `/LOADINF`, silent modes, saved
previous directories, clean install, upgrade, reinstall, and uninstall. It must
preserve M14-I's no-recursive-delete, exact legacy-cleanup, and reparse guards.
Focused custom-directory, ACL, PATH, reparse, upgrade/reinstall/uninstall tests
and an independent adversarial review are required.

## Other reviewed boundaries

No second public-release blocker was identified before the mandatory stop:

- the elevated index worker revalidates elevation, current NTFS volume
  identity, and the current-user catalog entry;
- its protected ProgramData path is derived from stable identifiers rather
  than caller input;
- protected storage applies administrator/SYSTEM ACLs, reparse/sidecar checks,
  retained non-delete-shared handles, and a per-volume exclusive lock;
- elevation uses `ProcessStartInfo.ArgumentList` with `Verb=runas`, not shell
  string construction;
- prerequisite execution is restricted to committed Microsoft URLs, SHA-256
  pins, and verified downloaded content;
- reviewed CLI/GUI path and launch surfaces do not construct a command line
  from search input or deserialize an unsafe polymorphic/network format;
- Quail 0.2 has no updater, telemetry backend, cloud provider, remote index,
  stored application secret, or generic privileged IPC command surface.

## Guard status

- M14-I installer cleanup guard: PASS, 187 exact entries;
- Windows App Runtime detector: PASS, 6 cases;
- committed provenance guard: PASS, 56 payload files;
- prerequisite acquisition/version/hash/payload guards: PASS;
- installer and application executables: unsigned, as expected at this stage.

The blocker prevents a final candidate and full release disposition.
