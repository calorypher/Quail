# M14-A results — Restarted public-release readiness audit

## Disposition

**STOP — `remediation required`.**

Quail 0.2 must not proceed to publication freeze from this branch. The
production installer contains a high-impact destructive-directory failure
mode that requires a packaging change and focused M13 lifecycle revalidation.

No repository visibility, tag, GitHub Release, release asset, or public
metadata was changed. No private-archive ref or history was migrated.

## Exact staging preflight

| Field | Result |
|---|---|
| Repository | `calorypher/Quail-public-staging` |
| Visibility | `PRIVATE` |
| Entry branch | `main` |
| Entry `HEAD` and `origin/main` | `62bbf3851e64c02af5b6856ff2d513bf05c607af` |
| Entry tree | `26bf068c982422e4c85d4727218fe260c5e730d1` |
| Root/history | one parentless commit |
| Tracked files | 222 |
| Entry remote branches | one: `origin/main` |
| Entry tags / Releases / pull requests | 0 / 0 / 0 |
| Worktree | clean |
| Audit branch | `m14-a-public-release-readiness` |

The old `calorypher/Quail` repository was not modified. Its stopped M14-A and
M14-R/M14-S results were read only as historical reference after staging had
already passed its independent preflight.

## Work completed before the stop

- Read the required canonical product, roadmap, M12/M13, production project,
  package, prerequisite, packaging, and ignore-policy sources.
- Independently inventoried the reachable history, identities, filenames,
  binary objects, remote refs, tags, Releases, pull requests, and metadata.
- Repeated a bounded full-tree/history privacy and secret scan with
  false-positive triage. No real secret or private-data blocker was found.
- Resolved the production NuGet graph for `Quail.App`, `Quail.Core`, and
  `Quail.Cli`. The supported vulnerability audit reported no known vulnerable
  direct or transitive package for all three projects.
- Inventoried Quail branding, system font/glyph usage, and runtime Windows
  Shell icons. Bundled branding is Quail-owned and hash traceable.
- Reviewed the privileged index worker, protected ProgramData storage,
  command construction, bootstrap pins, and installer ownership behavior until
  the blocking installer finding required the audit to stop.

Durable details are under `docs/milestones/evidence/M14-A/`.

## Blocking finding

`packaging/Quail.iss` defines an administrator installer with a default
directory but leaves the destination selectable or overrideable. It then runs:

```text
[InstallDelete]
Type: filesandordirs; Name: "{app}\*"
```

Inno Setup processes `[InstallDelete]` before `[Files]`, and `filesandordirs`
recursively deletes matching directories and their contents. A fresh install
can select an existing directory, and `/DIR=` can supply another destination.
A warning does not establish ownership and can be suppressed in silent use.

The impact is deletion of unrelated content with administrator privileges.
The affected trust boundary is the canonical production installer, so this is
a release blocker rather than optional hardening.

## Required remediation

Use a separately approved, narrow packaging remediation. It must remove the
blanket deletion or prove and enforce installer ownership before cleanup.
Merely hiding the destination page is insufficient because command-line and
saved-configuration paths must also be addressed.

The remediation must preserve real 0.1-to-0.2 obsolete-payload cleanup without
deleting unowned files. Repeat at minimum:

- full Release tests and warning-free App/CLI builds;
- canonical installer build and payload/version/prerequisite-pin guards;
- clean install, real 0.1 upgrade, same-version reinstall, and uninstall;
- unrelated-sentinel preservation for interactive and silent/custom-directory
  attempts;
- PATH, shortcut, data, prerequisite-present/offline, and installed-identity
  checks affected by the packaging change;
- focused adversarial review of the final cleanup/ownership rule.

After the remediated technical candidate is accepted, rerun every deferred
M14-A gate against its exact staging source.

## Gates intentionally not completed

- final payload license/notice inventory and complete prerequisite, Windows
  App SDK, WebView2, Inno Setup, and installer-stub legal disposition;
- `LICENSE`, `THIRD-PARTY-NOTICES.md`, and `CONTRIBUTING.md`;
- publication-ready README/CHANGELOG and future Release draft;
- unsigned-installer recommendation, trademark checklist, and final metadata;
- final Release builds/tests, audit installer, payload guards, and installer
  SHA-256/size/source identity.

Partial dependency and asset evidence is not a completed license or readiness
disposition.

## Public-history policy retained

The root import remains the deliberate beginning of future public history.
The private archive, legacy `v0.1.0` tag and Release, old pull requests, and
pre-public branches remain private and are not migrated. If a remediated M14-A
later passes, `v0.2.0` remains intended as the first public tag and Release.

## Final assessment

Final disposition: **`remediation required`**.

There is no publication-freeze recommendation, release draft, or audit
installer from this stopped run. Staging remains private and unpublished.
