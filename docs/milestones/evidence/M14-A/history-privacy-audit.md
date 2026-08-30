# M14-A rerun — history and privacy audit

## Scope and identity

The rerun covered every tracked path and blob reachable from staging `main` at
`3c37010f1980662cc05b111592e6fb213661d401`, including the stopped M14-A,
M14-I, and M14-P commits. The graph has one parentless public-history root,
13 reachable commits, 238 tracked files in the entry tree, and no tag.

Commit identities were classified without copying any discovered private value
into this evidence. All author identities use the approved pseudonymous
project identity and GitHub noreply address. Merge commits use GitHub's service
identity. No private-only commit or legacy private graph is reachable.

## Checks performed

- enumerated all reachable commits, parents, refs, tags, filenames, and blobs;
- scanned every commit snapshot for private keys, token formats, credential
  assignments, credentialed URLs, private email, user-profile/account paths,
  host identity, private archive references, and available local legal identity;
- inventoried tracked executable, package, archive, database, log, dump,
  certificate, key, and build-output extensions;
- checked the complete reachable graph for the legacy `v0.1.0` name and old
  release/history identifiers;
- inspected the commits introduced by stopped M14-A evidence, M14-I, and M14-P.

## Results

- no secret, token, credential, private key, or private connection value;
- no private profile/account path, private email, hostname, or legal identity;
- no legacy `v0.1.0`, legacy tag, private-only commit, or private archive graph;
- no tracked installer, executable, library, PDB, package, archive, database,
  log, dump, or other generated release/build artifact;
- only the approved pseudonymous author identity and GitHub merge identity;
- repository remained private throughout the audit.

Controlled test placeholders and runtime-only generated lab credentials were
triaged as non-findings. The recorded developer-local branding source location
contains no account, host, credential, legal identity, or private archive
identifier and is not consumed by the canonical release build.

## Disposition

**PASS — history/privacy CLEAN.**

This result is current for the exact entry source. It does not waive the
separate installer security blocker.
