# M14-P results — Public build provenance and privacy remediation

## Disposition

`public build provenance/privacy remediated`

No publication-sensitive action occurred. M14-A remains stopped and is not
resumed by this milestone.

## Exact source and candidate

- Implementation source commit: `a990bf8b9f2cbb6d6583062ee6abaf74a4e573e6`.
- Canonical source-derived candidate: Build A repeat from that exact commit.
- Installer: `9,949,147 B`; SHA-256
  `aebf598cecae87f7978233325df351df32713a30ea4773ee62187e9857ae5bd9`.
- Staged payload: 56 files; `43,926,873 B`; tree SHA-256
  `67217dff6bf723d28b72157a1ff9c04cf164b0715c8ecabd41e1b08247bd9ce4`.
- `Quail.exe` SHA-256:
  `cca2ff95f66a4c4e396d0e7e8162c45f00014a31aaa62c5cbc72578ebc646a2f`.
- `Quail.Cli.exe` SHA-256:
  `51013f2deb24f5cf45469d79c5cbd44e7d3420464fb8e532631b6d2e40d75847`.

The candidate was assembled exclusively by `scripts/build-installer.ps1` from
the exact public-staging source. No prebuilt or historical application payload
was injected or reused.

## Diagnosis

The original canonical source build had multiple provenance channels:

| Class | Affected output | Build stage | Runtime need |
| --- | --- | --- | --- |
| Physical checkout and profile paths | Managed DLL/EXE CodeView records and Portable PDB documents | C# compiler debug emission | No |
| Local identity carried by physical paths | Portable PDB documents and compiled provenance | Compiler input/debug provenance | No |
| XAML checkout path | Generated WinUI `.g.cs` / `.g.i.cs` `#pragma checksum` directives, which affected the App DLL/PDB deterministic inputs | WinUI XAML markup compiler before C# compilation | No |
| SourceLink | Portable PDB custom debug information | .NET CI build metadata | Diagnostics only |

There are no personal assembly metadata fields in the production project
configuration. The production projects have no `Authors`, `Company`, or
personal identity setting. The original configured Git identity is the
approved public project identity `calorypher`; the private email was scanned
separately and was absent from release outputs.

The source-built application hosts retain a pre-existing SDK apphost CodeView
record, but it does not reference either build location, a local profile, a
private archive, or a hostname. It is not emitted from Quail source.

## Implemented Release build contract

- `Directory.Build.props` enables `ContinuousIntegrationBuild` only for
  `Release` and maps the repository and current user-profile compiler inputs
  to neutral virtual roots through `PathMap`.
- `src/Quail.App/Quail.App.csproj` runs a Release-only MSBuild target directly
  before `CoreCompile`.
- `scripts/normalize-release-xaml-provenance.ps1` changes only generated WinUI
  `#pragma checksum` paths that match the current physical checkout root. It
  writes the neutral `/_/` representation before compilation. It does not
  touch XAML, user source, PDBs, DLLs, EXEs, or installer binaries.
- `scripts/test-release-build-provenance.ps1` scans the assembled Release
  payload for dynamically supplied physical checkout and profile roots.
  `scripts/build-installer.ps1` invokes it automatically before packaging.
- `Get-Version` in the canonical installer script now selects the canonical
  XML node directly, so the additional conditional Release property group does
  not change its version-read contract.

The contract is automatic for normal canonical Release builds. Debug builds
retain their ordinary developer paths and diagnostics behavior.

## PDB and metadata policy

PDB files remain in the established 56-file 0.2 payload. They were not removed
to conceal the defect; their compiled DLL/EXE and PDB provenance is now safe.
Changing their packaging policy would be a separate product/diagnostics
decision.

The final Portable PDB metadata contains no local checkout or profile path.
Two generated-document entries use a stable `C:\_` virtual compiler root,
not a physical filesystem root. Managed Quail CodeView records use the neutral
`/_/` mapping. SourceLink is present in three PDBs, contains no physical path,
and refers only to `raw.githubusercontent.com` for
`calorypher/Quail-public-staging`.

## Two-location reproducibility and privacy validation

Build A and Build B were clean detached worktrees of the same exact source
commit in non-overlapping, materially different filesystem locations. Neither
was the private archive. Both used the same SDK, Release configuration,
committed dependency inputs, and verified prerequisite manifest.

| Check | Result |
| --- | --- |
| Source commit | PASS — both `a990bf8b9f2cbb6d6583062ee6abaf74a4e573e6` |
| Payload file list, sizes, and SHA-256 hashes | PASS — byte-identical |
| Payload tree | PASS — 56 files, `43,926,873 B`, tree SHA-256 `67217dff6bf723d28b72157a1ff9c04cf164b0715c8ecabd41e1b08247bd9ce4` |
| PE/managed CodeView | PASS — no physical Build A/Build B/profile root |
| Portable PDB document paths | PASS — no local physical root |
| SourceLink | PASS — public repository only; no physical path |
| Privacy scan of both payloads | PASS — local account, local full name, checkout root, profile roots, private archive path, private email, hostname, and other supplied sensitive roots absent |
| Privacy scan of both installers | PASS — same sensitive classes absent |

The Inno Setup installer container is not byte-reproducible: Build A and Build
B had the same 56-file payload and the same installer length, but different
installer SHA-256 values. The differing bytes were confined to the Inno Setup
overlay rather than the PE program or staged payload. Rebuilding Build A at
the same source root also produced a different overlay, confirming that this
is build-session container metadata rather than a checkout-location input.
The payload, which is the released Quail application identity, is completely
byte-identical; no difference is unexplained and no installer variation
contains sensitive provenance.

## Verification

| Gate | Result |
| --- | --- |
| Full Release test suite | PASS — 176/176 |
| Release App build | PASS — 0 warnings / 0 errors |
| Release CLI build | PASS — 0 warnings / 0 errors |
| WinUI generated-source provenance check | PASS — all relevant generated checksum directives neutral before compilation |
| Windows App Runtime detector | PASS — 6 cases |
| M14-I installer cleanup safety guard | PASS — 187 exact legacy entries |
| Canonical installer/prerequisite/payload guards | PASS — both independent builds and Build A repeat |
| Automatic release provenance guard | PASS — both independent payloads |
| Local exact-payload CLI smoke | PASS — `--version` and `--help` |
| Quail-Lab exact-payload CLI status smoke | PASS — canonical verifier with hash-verified full payload |
| `git diff --check` | PASS |

The scope only changes build provenance. No application C# behavior, XAML
product behavior, package graph, runtime configuration, prerequisite pins, or
installer ownership behavior changed.

## Applicability of prior evidence

M13-D runtime, startup, idle-resource, search, and installed-product evidence
remains applicable because the application behavior, packages, runtime
configuration, and payload composition are unchanged. The byte identities are
intentionally superseded by the new source-derived candidate.

M14-I T1–T9 ownership/lifecycle evidence remains applicable except for its
artifact identity. `packaging/Quail.iss` and the exact guarded legacy cleanup
list are unchanged; the focused static cleanup guard passes. This milestone
does not reopen the M14-I installer vulnerability or repeat its full lifecycle
matrix.

## Follow-up boundary

The recorded installer is a technical source-derived candidate, not a
published Release. Resuming M14-A requires a separate explicit milestone.
