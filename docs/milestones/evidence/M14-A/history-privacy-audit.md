# M14-A staging history and privacy audit

## Scope

The scan covered every tracked path and blob reachable from staging `main`,
the local M14-A branch before audit changes, commit identities, filenames,
binary inventory, and current GitHub metadata. At entry the graph had one
parentless commit, so the full-history content set was the exact 222-file tree.

## Results

- Approved author and committer only:
  `calorypher <13728773+calorypher@users.noreply.github.com>`.
- No private key/certificate, known token/key format, credentialed URL,
  `.env`, SSH credential, secret-bearing connection string, or stored password.
- No real personal profile identifier, legal name/private email, hostname,
  private/internal IP address, or MAC address.
- No tracked log, database, dump, archive, installer, virtual disk, package,
  or build-output artifact.
- The only tracked binary assets were four Quail branding PNG/ICO files.
- URLs were public documentation or pinned Microsoft prerequisite sources.
- Description/homepage/topics were empty and visibility was private.

## False-positive triage

- `<user>` and `Alice` are controlled placeholders.
- The lab verifier creates a random temporary password only at runtime; no
  password value is stored in source.
- `Private.QuailLab.psm1` is an implementation-module name, not private data.
- Branding provenance contains a developer-local project-design location but
  no account name, host, credential, or private identifier.

## Disposition

The independently repeated staging history/privacy gate passed. This does not
waive the installer blocker or complete M14-A readiness.
