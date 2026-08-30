# M14-A rerun — GitHub and public surface inventory

## Repository state

Observed through GitHub while the repository remained private:

- repository: `calorypher/Quail-public-staging`;
- visibility: `PRIVATE`;
- default branch: `main`;
- description: empty;
- homepage: empty;
- topics: none;
- Issues: enabled;
- Discussions: disabled;
- Actions workflows: none;
- tags and Releases: none;
- merged pull requests: #1 (stopped M14-A), #2 (M14-I), #3 (M14-P);
- branches: `main` plus the retained M14-A, M14-I, and M14-P work branches;
- branch protection: not configured on the observed branches;
- repository rulesets: unavailable under the current private-plan capability;
- security policy URL: absent;
- security-and-analysis settings: no separately reported configuration.

No metadata, setting, visibility, repository name, branch, tag, Release, or
release asset was changed.

## Intended public history

The deliberate public-history root and its 13-commit staging graph remain the
only intended public history. There is no `v0.1.0`, old release asset, private
archive graph, migrated legacy PR history, or pre-public product branch in the
reachable staging surface. Subject to remediation, a later passing M14-A, and
explicit publication approval, `v0.2.0` remains intended as the first public
tag and GitHub Release.

## M14-B recommendations after remediation

- set a concise description limited to local Windows NTFS file search;
- add only accurate topics such as Windows, search, launcher, NTFS, and C#;
- leave homepage empty unless a maintained public page actually exists;
- keep Issues enabled and enable Discussions only if there is a moderation and
  support reason;
- add the reviewed public `README`, `LICENSE`, notices, contribution/reporting
  route, release notes, and security reporting information;
- configure appropriate main-branch/ruleset protection when supported;
- create the final artifact only after the approved final repository rename,
  then verify SourceLink and exact provenance;
- do not imply cloud, AI, plugins, cross-platform support, a background updater,
  or future roadmap features.

## Public material and decision gates

M14-A finalization adds the canonical MIT `LICENSE`, payload-derived
`THIRD-PARTY-NOTICES.md`, a public-ready README, a release-ready `0.2.0`
CHANGELOG section, and `docs/releases/0.2.0-release-notes.md`.

The installer and binaries are unsigned. The technical recommendation is:
**unsigned installer acceptable for the 0.2 development release with a
documented SmartScreen warning and published release SHA-256.** SmartScreen or
reputation warnings may appear; no code-signing purchase or configuration was
attempted. Code signing can be revisited for a later release.

The final manual `QUAIL`, `Q.QUAIL`, and materially similar software-mark check
in TMview/EUIPO and UPRP remains intentionally deferred to immediately before
publication. No legal clearance is claimed.

## Disposition

Inventory and repository-tracked public material are ready for M14-B. No
publication-sensitive action was performed. The final trademark check,
repository rename/metadata, post-rename source build, tag, release creation,
and asset upload remain deferred.
