# M14-A results — Public-release readiness audit rerun

## Disposition

**STOP — `remediation required`.**

Quail 0.2 must not proceed to M14-B from this branch. The elevated per-machine
installer can accept an empty/nonexistent custom directory that is writable by
an unprivileged user, then add that directory to the machine `PATH`. A bounded
packaging remediation and focused lifecycle/security revalidation are required.

No repository visibility, name, tag, GitHub Release, release asset, or public
metadata was changed. The private archive was not accessed or modified.

## Exact preflight

| Field | Result |
|---|---|
| Repository | `calorypher/Quail-public-staging` |
| Visibility | `PRIVATE` throughout the rerun |
| Entry branch | `main` |
| Entry `HEAD` / `origin/main` | `3c37010f1980662cc05b111592e6fb213661d401` |
| Entry tree | `27e81860bf381ead0f067a5c6722e1d486e0f7b9` |
| Root/history | one parentless root; 13 reachable commits |
| Tracked files | 238 |
| Entry tags / Releases | 0 / 0 |
| Entry pull requests | PRs #1, #2, and #3 merged |
| Worktree | clean |
| Audit branch | `m14-a-public-release-readiness-rerun` |

The local `main` was initially behind; no reset was used. The audit branch was
created directly from the required fetched `origin/main`.

## Rerun results before the stop

### History and repository privacy

PASS. Every snapshot reachable from all 13 staging commits was scanned again,
including the stopped M14-A evidence and the M14-I/M14-P commits. The graph has
only the deliberate public root, approved pseudonymous author identity and
GitHub merge committers, no `v0.1.0`, no legacy private graph, no secret/token
finding, no private profile/account/email/host identity, and no tracked build,
package, installer, database, log, dump, or archive artifact.

The developer-local branding source location is retained as non-identifying
asset provenance: it contains no account, host, credential, legal identity, or
private archive identifier. It is not used by a release build.

### Source-built artifact privacy and provenance

PASS for the pre-stop source build. The canonical builder restored and
published App and CLI from current source only. Its committed provenance guard
passed for 56 payload files. A separate byte scan of payload plus installer
found no physical checkout, profile root/account, private archive path, host,
private email, or available local legal identity.

SourceLink in all three Quail PDBs maps `/_/*` to the exact entry commit under
`calorypher/Quail-public-staging`. GitHub documents redirects for web traffic
after a repository rename, so the current link is expected to continue working
while the old name is reserved. Redirects are nevertheless an avoidable
long-term dependency. M14-B should rename first, update the remote, and build
the final public artifact from the final repository name. This is sequencing,
not the M14-A blocker. Quail must not predeclare the future repository name in
SourceLink before the rename.

### Dependency and vulnerability status

The complete App/Core/CLI graph was resolved again. NuGet's supported
direct+transitive vulnerability query reported no known vulnerable packages
for all three production projects, and the App graph reported no deprecated
package. The bundled native SQLite reports 3.53.3; SQLite's official CVE table
places the current relevant fixes no later than 3.53.2. SQLite 3.53.4 is a
later maintenance patch, not an identified security gate for Quail's bounded
trusted-schema use.

The exact 56-file payload was hash-mapped to its NuGet/runtime-pack origins.
It contains Microsoft.Data.Sqlite, SQLitePCLRaw/e_sqlite3, WebView2 loader and
managed projections, Windows App SDK projections/bootstrap/WinUI, .NET Windows
desktop libraries, and the Windows SDK .NET/CsWinRT runtime files. Build-only
SDK packages and the three downloaded Microsoft prerequisites are not embedded
in the application payload.

License metadata and exact upstream/package texts were inspected. The expected
public materials are MIT for Quail, a payload-derived third-party notice, and
the Apache-2.0 text required by SQLitePCLRaw. WebView2's binary BSD notice and
the separate Microsoft Windows App SDK/Windows SDK terms also need accurate
representation. These files were not added because the security stop occurred
before a final payload/notices disposition.

### M14-I and prerequisite guards

- M14-I cleanup guard: PASS, 187 exact guarded files and no recursive tree
  cleanup.
- Windows App Runtime detector: PASS, 6 cases.
- Canonical prerequisite acquisition/pin/hash guard: PASS.
- Current cached prerequisites: committed SHA-256 values matched and all three
  Microsoft installers had valid Authenticode signatures.

The prerequisite mechanism downloads from pinned Microsoft HTTPS URLs only
when required; the installers are not embedded in the Quail setup. Exact terms
were reviewed far enough to confirm official deployment routes, but the final
redistribution evidence and notice set is intentionally not claimed complete
after the stop.

### Assets and GitHub surface

The five Quail branding assets retain their recorded hashes and Quail-owned
classification. Segoe fonts/glyphs and Windows Shell icons are system-provided
and not bundled. No screenshot or third-party font/icon asset is present.

GitHub remained private. `main` is the default branch; Issues are enabled;
Discussions are disabled; description, homepage, topics, Releases, tags, and
Actions workflows are empty. Existing branches are unprotected and repository
rulesets were unavailable for the private plan. Recommended public metadata and
security settings are deferred to M14-B after remediation and a passing rerun.

## Blocking finding

`packaging/Quail.iss` combines these facts:

- `PrivilegesRequired=admin` and a system-wide `PATH` update;
- custom directory selection and `/DIR=` remain supported;
- `ValidateDestinationOwnership` accepts a nonexistent or empty destination
  without checking owner or ACL;
- `SetQuailPathEntry(True)` appends `{app}` to the machine `PATH`.

M14-I correctly rejects non-empty unrecognized destinations and reparse points,
but it deliberately accepts empty custom destinations. If such a directory is
owned or writable by an unprivileged user, that user can place arbitrary command
names in a machine-wide executable search directory. An administrator or
service that resolves a previously absent command through `PATH` can then
execute attacker-controlled code at higher privilege.

The defect also leaves installed Quail binaries and their app-local DLL search
location replaceable when the chosen destination inherits an unsafe ACL.
This is not addressed by reparse checking.

## Required remediation

Use a separately approved packaging remediation that establishes one of these
equivalent fail-closed contracts:

- restrict per-machine installation to a trusted Program Files destination; or
- securely create/validate every accepted custom destination with trusted
  ownership and ACLs, including existing empty directories, before copying
  payload or adding it to machine `PATH`.

The remediation must account for interactive choice, `/DIR=`, `/LOADINF`,
silent modes, saved previous directories, upgrade/reinstall, and uninstall.
It must preserve the M14-I no-recursive-delete and exact legacy-cleanup
invariants, then repeat focused custom-directory, PATH, ACL, reparse, upgrade,
reinstall, and uninstall checks. Independent adversarial review is required
before resuming M14-A.

## Pre-stop technical artifact

This is audit input only, not a final candidate or release asset:

- source: `3c37010f1980662cc05b111592e6fb213661d401`;
- installer: 9,949,135 bytes;
- installer SHA-256:
  `2b4d18ae7045a9f77b2994bbe5f09764de824677a7e28a5185dff22e34930638`;
- payload: 56 files, 43,926,893 bytes;
- audit-manifest tree SHA-256:
  `0e02f75f442cd67737686d67293e0c0a8471264dc4419e86d747ec49113020ed`;
- `Quail.exe` SHA-256:
  `f75301dff406f73b1f69caf81b9a2bc8f4a00148f247776db58d9d3241a11e1e`;
- `Quail.Cli.exe` SHA-256:
  `9b1a4c3038c22095ce447828dc17cfaf4c239f2eadd0b6577904df8354112842`.

The tree identity hashes UTF-8 lines of normalized relative path, byte length,
and lowercase file SHA-256 in ordinal path order. This artifact predates any
future remediation and must not be published.

## Gates intentionally not completed

The stop prevents a passing disposition for final license/notices files,
project `LICENSE`, README/CHANGELOG/release notes, unsigned-installer release
recommendation, final prerequisite redistribution record, final technical
candidate, final trademark gate, and M14-B metadata plan. No absence of a new
finding in those unfinished areas should be read as a pass.

Final disposition: **`remediation required`**.
