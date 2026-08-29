# M04 Results — Basic Search

## Status

**Verified.** M04 adds persistent, bounded file-name search over the current schema-v2 namespace index. The direct SQLite-table baseline was measured on the physical host and rejected because it missed the latency target. SQLite-native FTS5 with the built-in trigram tokenizer was then introduced, measured, and retained. No external search engine, native extension, daemon, service, or live filesystem traversal was added.

## Search semantics and CLI

- Search reads only a complete, current schema-v2 SQLite namespace index. It neither traverses the live filesystem nor reads file contents.
- A non-empty name query is a literal substring match against the indexed entry name; path text does not participate in matching.
- Queries of three or more characters use the FTS5 trigram index. One- and two-character queries use the same literal SQLite substring fallback so that partial matching remains correct.
- Matching is case-insensitive through the SQLite trigram tokenizer for indexed queries and SQLite `lower` for the short-query fallback. Unicode-specific case-folding behavior was not separately characterized.
- `--type any` is the default. `--type file` and `--type dir` are applied by SQLite from the persistent directory attribute.
- `--ext pdf` and `--ext .pdf` normalize to `pdf`; matching is case-insensitive, directories never match, and the filter matches the final file-name suffix. Empty, dotted, path-like, and wildcard extensions are rejected.
- Results expose the canonical file identifier, indexed name, reconstructed full path when the namespace chain is resolvable, file/directory state, and file extension without a leading dot when applicable.
- Results are ordered by case-insensitive name, binary name, then canonical file identifier. This is deterministic lexical ordering, not relevance ranking.
- The default result limit is 50. Explicit limits must be in the 1–1,000 range.

```text
quail search <database-path> <query> [--type file|dir|any] [--ext pdf] [--limit 50]
```

The CLI is a thin surface over `IndexStore.Search`. It prints a bounded `SEARCH` summary followed by `RESULT` lines. Unknown options, missing option values, invalid type values, non-integer limits, and invalid bounds fail with concise English diagnostics.

## SQLite mechanism and maintenance

The selected mechanism is an external-content FTS5 table using `tokenize='trigram case_sensitive 0'`. It is joined to the authoritative `namespace_entries` table for type and extension predicates and for result reconstruction. FTS5 is used only for name matching; no relevance/rank order is requested.

`search_entries` is built as part of every staged initial build/rebuild. Insert, update, and delete triggers maintain it inside the same SQLite transactions as namespace mutations. The explicit metadata marker `search_index_format = fts5-trigram-v1` is required for search; an older schema-v2 database without it is rejected for search and must be rebuilt through the normal production path. M03's namespace/checkpoint frontier remains unchanged, and a pre-commit failure rolls back both namespace and FTS state.

No migration is guessed. Search reads neither a separate process-owned index nor an eventually consistent side store.

## Physical-host mechanism decision

The physical host used a freshly built `C:` index. The direct-table baseline and FTS index were separate consecutive production builds, so their record counts differ slightly due to ordinary host activity.

| Mechanism | Records after build | DB size | Build wall / CPU | Search storage impact |
|---|---:|---:|---:|---:|
| Direct table scan with `instr(lower(name), lower($query))` | 859,386 | 151,207,936 B | 29.486 s / 22.000 s | baseline |
| FTS5 trigram external-content index | 859,406 at build; 859,414 after reopened status | 237,887,488 B | 70.491 s / 62.969 s | +86,679,552 B, approximately +57.32% |

The increased build time and storage are accepted for M04 because the direct-table scan missed the search latency target by a large margin, while the FTS5 search core meets it without introducing a separate engine or weakening M03 consistency.

`EXPLAIN QUERY PLAN` for the direct baseline reported:

```text
SCAN namespace_entries
USE TEMP B-TREE FOR ORDER BY
```

The FTS query reported:

```text
SCAN search_entries VIRTUAL TABLE INDEX 0:M1
SEARCH namespace_entries USING INTEGER PRIMARY KEY (rowid=?)
USE TEMP B-TREE FOR ORDER BY
```

The remaining sort implements documented deterministic lexical ordering only over FTS candidates. The table-wide direct scan is removed for ordinary three-or-more-character queries.

## Physical-host latency evidence

All result limits were 50. Each process-level trial invoked a fresh `Quail.Cli` process after one warm-up for its category; this is a process-reopened, warm-OS-cache measurement with no artificial cache clearing. The search-core series invokes `IndexStore.Search` repeatedly in one already initialized process after one warm-up. It separates search work from CLI/.NET startup cost.

### Direct-table baseline — process-level, 11 measured trials

| Category | Results | Median | P95 / max |
|---|---:|---:|---:|
| Broad any (`a`) | 50 | 282.671 ms | 304.064 ms |
| Selective any (`ntfsjournal`) | 1 | 255.802 ms | 260.766 ms |
| Zero match | 0 | 248.999 ms | 273.598 ms |
| File only (`a`) | 50 | 286.246 ms | 289.443 ms |
| Directory only (`a`) | 50 | 272.135 ms | 278.324 ms |
| Extension (`a`, `dll`) | 50 | 350.869 ms | 366.430 ms |

### FTS5 trigram — process-reopened, 11 measured trials

| Category | Results | Median | P95 / max |
|---|---:|---:|---:|
| Broad any (`pro`) | 50 | 115.913 ms | 120.910 ms |
| Selective any (`ntfsjournal`) | 1 | 88.695 ms | 90.370 ms |
| Zero match | 0 | 83.229 ms | 91.222 ms |
| File only (`dll`) | 50 | 127.281 ms | 131.914 ms |
| Directory only (`use`) | 50 | 98.151 ms | 100.358 ms |
| Extension (`dll`, `dll`) | 50 | 145.122 ms | 148.270 ms |

### FTS5 trigram — warm search core, 21 measured trials

| Category | Results | Median | P95 / max |
|---|---:|---:|---:|
| Broad any (`pro`) | 50 | 34.586 ms | 36.434 ms |
| Selective any (`ntfsjournal`) | 1 | 6.562 ms | 7.266 ms |
| Zero match | 0 | 7.152 ms | 7.961 ms |
| File only (`dll`) | 50 | 46.922 ms | 49.159 ms |
| Directory only (`use`) | 50 | 16.413 ms | 18.118 ms |
| Extension (`dll`, `dll`) | 50 | 62.742 ms | 64.041 ms |

The typical broad, selective, empty, file-only, and directory-only search-core scenarios are below the 50 ms working target; the deliberately broad extension category is 62.742 ms median but remains below 100 ms at every measured trial. CLI process startup remains material for this diagnostic CLI and is intentionally not treated as search-core cost.

## Architecture boundary decision

`Quail.Core` remains the interim boundary. M04 adds small neutral `FileSearchQuery`, `FileSearchResult`, and `SearchEntryType` concepts there, while parsing and rendering remain in `Quail.Cli`. This makes search independently testable without a cosmetic project split. No provider framework, discovery/loading, extension SDK, or speculative application-core abstraction was introduced.

## Verification matrix

| ID | Result | Evidence |
|---|---|---|
| T01 | Pass | Automated tests cover case-insensitive ASCII substring matching, including one- and two-character fallback queries. |
| T02 | Pass | Automated no-match test returns an empty result set without error. |
| T03 | Pass | Automated tests cover default/any, file-only, and directory-only results. |
| T04 | Pass | Automated tests cover case-insensitive PDF filtering, leading-dot normalization, and directory exclusion. |
| T05 | Pass | Automated tests cover deterministic limiting and invalid zero-limit rejection; the CLI returned exit code 2 with concise English diagnostics for `--limit 0` and invalid `--type`. |
| T06 | Pass | Automated tests repeat an equivalent limited query and compare ordered stable identifiers. |
| T07 | Pass | Automated tests reject absent, incomplete, schema-incompatible, and missing-search-index-format databases. |
| T08 | Pass | Automated tests apply committed M03-style create, rename, and delete batches, then observe each state through search without rebuild. |
| T09 | Pass | Automated fault injection before commit leaves the original FTS result visible and the uncommitted rename absent. |
| T10 | Pass | Quail-Lab target was dynamically discovered by label `QUAIL_LAB_DATA` as healthy NTFS before work. A current binary SHA-256 matched after deployment. A 1,752-record build found controlled partial, file/directory, and PDF results; a seven-record sync made the rename visible and old name absent; a one-record delete sync returned zero draft matches. The final reopened index was complete with 1,750 records and a 524,288-byte database. |
| T11 | Pass | The physical host production build completed with the record, DB-size, warm-core, and process-reopened evidence above. No destructive host filesystem or journal operation was performed. |
| T12 | Pass | The direct baseline, FTS5 availability probe, query plans, storage impact, build cost, and latency distributions above support retaining FTS5 trigram. |
| T13 | Pass | The minimal `Quail.Core` boundary was retained; the rationale is recorded above. |

Automated verification:

```powershell
dotnet test tests\Quail.Core.Tests\Quail.Core.Tests.csproj -c Release --no-restore
dotnet build src\Quail.Cli\Quail.Cli.csproj -c Release --no-restore
```

Result: **32 passed, 0 failed**; the Release CLI build succeeded with zero warnings/errors.

## Limitations

- Trigram acceleration applies to three-or-more-character queries. One- and two-character queries retain correct literal substring semantics through the direct-table fallback but were not separately host-benchmarked.
- Unicode-specific case-folding behavior was not separately characterized beyond the SQLite tokenizer and built-in `lower` behavior.
- FTS5 increases one-volume index size by approximately 57% and the observed initial-build wall time from 29.486 s to 70.491 s on this host.
- M04 does not add fuzzy matching, typo correction, ranking, metadata filters, content search, service/IPC, monitoring, GUI, other providers, or plugin infrastructure.

## Reusable / scriptable workflow observations

These are candidates for a later, separate M04.5 tooling decision. They are observations only; no scripts were added in M04. Any future repository tooling must use paths relative to `$PSScriptRoot`, dynamic discovery, explicit parameters, or ignored local configuration. It must not hardcode developer-specific paths, usernames, addresses, drive letters, or secrets.

| Workflow | Current sequence | Deterministic portion and estimated call reduction | Minimal future interface | Compact model summary / failure detail |
|---|---|---|---|---|
| Repo/Git preflight | Read branch, HEAD, status, remote main, and required source documents in separate calls. | Branch/HEAD/clean/current comparisons are deterministic; one entry point could replace about 4–6 calls. | `Test-QuailRepoPreflight -ExpectedBranch <name> -RequireClean -RequireCurrentOrigin` | `PASS branch=<...> head=<...> clean=<bool> current=<bool>`; on failure include only divergent refs and status paths. |
| Host/VM synchronization and checkpoint guard | Discover VM state/IP/checkpoints, SSH to inspect remote repo, then discover the labeled data volume. | VM state, required checkpoint existence, remote ref equality, label/filesystem/health checks are deterministic; about 4–5 calls. | `Test-QuailLabPreflight -VmName <name> -DataVolumeLabel <label> -RequiredCheckpoint <name>` | `PASS vm=<...> checkpoint=<...> remoteHead=<...> volume=<label>`; failure log includes missing checkpoint, remote status, or volume health only. |
| Dynamic VM access and cmd-to-PowerShell transition | Discover current IP, establish SSH, then explicitly invoke remote `powershell -NoProfile` for cmdlets. | IP selection, SSH reachability, and command-host wrapping are deterministic; about 2–3 calls. | `Invoke-QuailLabPowerShell -VmName <name> -ScriptBlock <script>` | `PASS reachable=<bool> remotePowerShell=<version>`; retain raw SSH error and discovered addresses only on failure. |
| Artifact deployment | Build locally, hash a chosen artifact, copy files, then hash the remote copy. | Hashing and equality comparison are deterministic; about 2–3 calls. | `Publish-QuailLabArtifact -Source <repo-relative-path> -Destination <remote-path> -VerifySha256` | `PASS artifact=<name> sha256=<hash>`; report copied-file list and mismatch details only on failure. |
| VM runtime verification | Create a uniquely named controlled dataset, build, search filters, rename/delete, sync, reopen status, and record compact evidence. | The test sequence and expected result counts are deterministic when the fixture names are generated per run; about 2–4 calls. | `Test-QuailLabSearch -DataVolumeLabel <label> -ArtifactPath <path> -RunId <id>` | `PASS T10 build=<records> sync=<counts> final=<records>`; retain full CLI output and fixture path only on failure. |
| Host benchmark and evidence collection | Elevate only the production build, then separately collect status/size, process-reopened timings, warm-core timings, FTS capability, and `EXPLAIN QUERY PLAN`. | Query cases, trial counts, percentiles, size deltas, and compact summaries are deterministic; one entry point could replace about 5–7 calls. | `Measure-QuailSearchHost -Volume <volume> -OutputDirectory <ignored-local-path> -Trials <n> -QueryCase <config>` | `PASS records=<n> dbBytes=<n> coreMedianMs=<n> mechanism=<...>`; retain raw timings, plans, and only non-sensitive query/result diagnostics on failure. |
