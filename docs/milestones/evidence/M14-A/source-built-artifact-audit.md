# M14-A rerun — source-built artifact privacy and provenance audit

## Finalization candidate addendum

The current technical candidate supersedes the pre-stop artifact identity:

- source: `37e337f6793ae68889b964b0232637cc076aec2b`;
- installer: 9,947,284 B;
- installer SHA-256:
  `efd6bf4846d9d82fb4e4b432522ce73b0a47a581f213d784406c1f31aef6be49`;
- payload: 56 files, 43,926,881 B; tree SHA-256:
  `53e528b6291a34804c85a403d2f446509f86f35b785c0882564778927d7f7b9a`;
- `Quail.exe`: `8b2c49da52ac611a71544aaf4ad6a060350ccd047f43617903e6d1219b173021`;
- `Quail.Cli.exe`: `1decfb0aed48d7b3dea83d271cfe3e1e62f2176155fa9eae7b60951d2a29cb5b`.

The M14-P provenance guard passed for all 56 payload files. The same guard,
applied to the one-file installer directory, passed separately. Both scans used
the physical checkout and both available user-profile roots as forbidden input
classes. This candidate is still non-public and must be rebuilt after the
approved repository rename for final SourceLink identity.

## Historical pre-stop canonical build input

- source: `3c37010f1980662cc05b111592e6fb213661d401`;
- canonical entry point: `scripts/build-installer.ps1`;
- payload: App and CLI restored/published from the current checkout only;
- committed provenance guard: PASS, 56 files.

The build completed before the security stop. It is audit input, not a release
candidate and must not be published.

## Privacy scan

A separate byte scan covered the complete 56-file payload and generated
installer. It searched for the physical checkout root, user profile/account,
private archive path, hostname, private email, and available local legal
identity without writing any discovered private value into evidence.

All private-value classes returned zero matches. The only approved identity
present is the pseudonymous repository owner in SourceLink metadata.

## SourceLink

All Quail PDBs map `/_/*` to the exact source commit under
`calorypher/Quail-public-staging`. This is truthful for the current build.
GitHub documents repository redirects after rename, so the existing URL is
expected to resolve while the old name remains reserved. SourceLink uses raw
repository content URLs, making a redirect dependency undesirable for a final
public artifact.

M14-B sequencing recommendation:

1. perform the separately approved repository rename;
2. update the build checkout remote to the final repository identity;
3. build the final artifact from exact post-rename source;
4. verify the embedded SourceLink URLs and provenance guard again.

Do not configure the current staging build with a future repository name that
does not yet identify its source. The post-rename rebuild is a sequencing
requirement, not the M14-A blocker.

References:

- [GitHub repository rename behavior](https://docs.github.com/en/repositories/creating-and-managing-repositories/renaming-a-repository);
- [Source Link specification and behavior](https://github.com/dotnet/sourcelink/blob/main/docs/README.md).

## Pre-stop artifact identity

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

The audit-manifest hash covers UTF-8 records of normalized relative path, byte
length, and lowercase file SHA-256 in ordinal path order.

## Disposition

**PASS — source-built artifact privacy/provenance CLEAN for the M14-A
technical candidate.** The final public candidate remains deferred only for the
approved post-rename M14-B sequence.
