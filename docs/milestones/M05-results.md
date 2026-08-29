# M05 Results — Metadata Filters

## Status

**PASS.** The M05 implementation is complete and ready for independent review. The final physical-host investigation found and corrected one small production-path regression, then separated the remaining cost into metadata calls and SQLite persistence of two non-NULL fields. Review accepted that measured cost for Quail 0.1. No scheduler, parallel enrichment, crawler, service, or other architecture expansion was added.

## Implemented candidate and automated evidence

The candidate uses one thin `OpenFileById` helper with desired access `0`, read/write/delete sharing, `FILE_FLAG_BACKUP_SEMANTICS`, `FILE_FLAG_OPEN_REPARSE_POINT`, `FileBasicInfo`, and `FileStandardInfo`. It stores nullable `logical_size` and `last_write_time_utc` in schema version 3 with `metadata_format = file-metadata-v1`.

- Size candidate: signed `FILE_STANDARD_INFO.EndOfFile` for ordinary non-directory, non-reparse entries; zero is valid; directory, reparse, and lookup failure are `NULL`.
- Modified candidate: signed UTC `FILE_BASIC_INFO.LastWriteTime` for files, directories, and reparse objects; non-positive and query failure are `NULL`.
- Metadata fields are independent. Search is SQLite-only; `NULL` does not match a size/time bound.
- Initial staging enriches each streaming record before its staged write and retains journal handoff.
- Incremental acquisition accumulates the M05-A refresh mask by canonical ID and performs at most one lookup per non-deleted affected ID before its SQLite transaction. Namespace, nullable metadata, FTS effects, and checkpoint commit together.
- `RENAME_OLD_NAME` retains the row; `RENAME_NEW_NAME` preserves existing metadata, with a conservative lookup only if the row is missing. `FILE_DELETE` removes the whole row transactionally.
- Candidate CLI syntax: `--min-size <bytes>`, `--max-size <bytes>`, `--modified-after <ISO-8601 Z|offset>`, `--modified-before <ISO-8601 Z|offset>`, `--hidden`, `--read-only`, and `--system`. Size units are deliberately not accepted; timestamps without an explicit `Z` or numeric offset are rejected.

`dotnet test tests\Quail.Core.Tests\Quail.Core.Tests.csproj -c Release --no-restore` passed **38/38**. The candidate coverage includes T01–T07 persistence/query behavior and T08–T11 content/time refresh, create, rename/move across a batch boundary, delete, unavailable values, and injected pre-commit rollback of namespace, FTS, metadata, and checkpoint. The Release CLI build succeeded. A final repeat emitted one external `NU1900` warning because this environment could not reach NuGet's vulnerability-audit feed; it was not a compiler warning or code failure.

| ID | Status | Evidence |
|---|---|---|
| T01 | Pass | Exact minimum/maximum size boundaries, including zero-byte files. |
| T02 | Pass | UTC FILETIME before/after bounds with explicit timestamp parsing. |
| T03 | Pass | Hidden, read-only, and system attribute predicates. |
| T04 | Pass | Name/type/extension/size/time/attribute composition. |
| T05 | Pass | `NULL` unavailable/not-applicable values do not match size/time bounds. |
| T06 | Pass | Deterministic order and bounded result limit. |
| T07 | Pass | Older complete indexes without the metadata marker are rebuild-required. |
| T08 | Pass | Create and data overwrite/extend/truncation refresh persisted metadata. |
| T09 | Pass | `BASIC_INFO_CHANGE` refreshes timestamp-only changes. |
| T10 | Pass | Rename/move, including a pair across a batch boundary, preserves metadata; missing new-name rows use conservative lookup. |
| T11 | Pass | Delete and injected pre-commit rollback are atomic for namespace, metadata, FTS, and checkpoint. |
| T12 | Pass | Controlled real NTFS Quail-Lab fixture. |
| T13 | Pass | Accepted representative physical-host integrated build measurement. |
| T14 | Pass | Warm physical-host metadata-filter search stays below 100 ms. |
| T15 | Pass | This document records final semantics, evidence, limitations, and architecture decision. |

## T13 physical-host integrated measurement and cost investigation

All host runs were bounded and read-oriented against `C:` and wrote only controlled databases under `target/m05-investigation`. They had zero parse and unsupported-record failures. The temporary phase timer and `--skip-metadata` A/B control were removed after the measurements; the only retained correction is reuse of the metadata helper's one volume handle for initial journal queries, enumeration, ID opens, and journal handoff/replay.

### Production-path inspection

- The metadata helper owns one volume handle for the complete build. Each initial namespace callback performs exactly one `OpenFileById`, one `FileBasicInfo` query, and one `FileStandardInfo` query. No writer, diagnostic, or normal handoff path repeats the initial lookup.
- Basic/standard queries are independent only for nullable failure semantics. Diagnostics update a small failure-code dictionary only on failures and serialize it once after the build.
- Roots add two synthetic records, not a second namespace traversal. Handoff caused only 41–885 extra ID acquisitions during normal host activity, not a material extra pass.
- The original candidate did, however, give `NtfsEnumerator` and `NtfsJournal` separate volume handles from the metadata helper. This differed from the M05-A probe's single-handle mechanism. Sharing the helper handle is a small contract-conforming correction; it reduced the measured integrated wall time by 12.390 s against the instrumented separate-handle B run.
- M05 adds only two nullable columns and their existing-row preservation expression to the M04 namespace UPSERT. FTS triggers are unchanged. There is no path reconstruction, traversal, per-record validation, journal query, volume open/close, or verbose diagnostic loop in the write callback.

### A/B phase evidence

The phase breakdown was measured with one monotonic timer around the producer/callback, metadata acquisition, SQLite write/2048-row commit path, handoff, and completion/promotion. It is a sequential accounting; no per-record output was collected. Record counts drifted slightly with ordinary host activity.

| Run | Records | Wall / CPU | Peak WS | DB size | Enumeration | Metadata calls | SQLite writes | Handoff + completion |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| M04.5 recorded baseline | 861,998 | 81.053 / 72.875 s | 96,854,016 B | 239,095,808 B | n/a | n/a | n/a | n/a |
| A: current schema/UPSERT/FTS, metadata disabled | 862,884 | 88.976 / 80.234 s | 91,303,936 B | 240,459,776 B | 1.645 s | 0.052 s | 87.050 s | 0.158 s |
| B: separate volume handles (instrumented candidate) | 862,868 | 173.986 / 164.438 s | 97,943,552 B | 249,040,896 B | 1.866 s | 61.684 s | 110.102 s | 0.231 s |
| B: shared handle (retained correction) | 862,284 | 161.596 / 152.859 s | 98,004,992 B | 248,844,288 B | 1.741 s | 57.490 s | 102.116 s | 0.178 s |

The final shared-handle B had 862,339 metadata attempts: 862,000 successes and 339 unavailable results (`ERROR_ACCESS_DENIED` 335; `ERROR_SHARING_VIOLATION` 4), a 0.0393% failure rate. This matches M05-A's stable inaccessible population; failures are not the cost source.

### Reconciliation with M05-A

M05-A projected 115.884 s from the M04.5 81.053 s baseline plus the standalone probe increment of 34.831 s. The final shared-handle B was 161.596 s, a 45.712 s difference. The bounded A/B result accounts for it within rounding:

| Component beyond the M05-A projection | Wall cost |
|---|---:|
| Current schema/write path A above M04.5 | 7.923 s |
| Integrated metadata-call phase above the M05-A standalone probe | 22.659 s |
| SQLite write phase with real non-NULL values above A's NULL values | 15.066 s |
| Total explained | 45.648 s |

The current production helper uses the same official APIs, access mask, sharing, and single-handle approach as the standalone probe. The remaining 22.659 s is observed only when calls are interleaved with the long SQLite/FTS build and is therefore an integrated kernel/managed workload cost, not a repeated lookup. The 15.066 s persistence component is the measured cost of storing two signed 64-bit non-NULL values in each SQLite row; removing it would violate M05 semantics. No simple local change remains that removes either cost without changing the approved persistence/acquisition design.

The earlier uninstrumented candidate result was 167.438 s / 158.188 s CPU. The retained shared-handle correction produced 161.596 s / 152.859 s CPU in the controlled repeat, an improvement of 5.842 s wall and 5.329 s CPU; the remaining accepted cost is documented below as a Quail 0.1 limitation.

## T14 physical-host warm search evidence

The candidate database was reopened once, each scenario was warmed once, then `IndexStore.Search` was measured 11 times in the same process with a limit of 50. P95 is the maximum of this small series.

| Scenario | Results | Median | P95 / max |
|---|---:|---:|---:|
| Size selective (`dll`, min 1,000,000 B) | 50 | 46.414 ms | 54.385 ms |
| Size broad (`dll`, min 0 B) | 50 | 51.562 ms | 54.170 ms |
| Modified selective (`ntfs`, after 2026-01-01 UTC) | 50 | 9.010 ms | 10.716 ms |
| Modified broad (`ntfs`, after 2000-01-01 UTC) | 50 | 9.282 ms | 10.289 ms |
| System attribute (`dll`, system) | 0 | 43.711 ms | 46.733 ms |
| Combined name/type/ext/size/time/system | 0 | 6.697 ms | 7.269 ms |
| Zero match | 0 | 7.229 ms | 8.013 ms |

All measured warm search cases stayed below 100 ms. No SQLite index was added: the simple predicates are sufficient in this measurement, and optimizing them does not address the initial-build gate.

## Quail-Lab (T12)

The canonical `scripts/vm-verify.ps1` passed first: it dynamically discovered Quail-Lab, selected the healthy `QUAIL_LAB_DATA` NTFS volume, deployed the self-contained CLI, verified SHA-256 `3cf5d5ca470f0d3945162a1e6610e6ce9579a86b89e67417b077bae93f392b23`, and completed `CliStatus`. The additional native SQLite library required outside the one-file smoke contract was transferred separately and SHA-256 verified as `b7385d722c83fb52142a00477a726723745916d22a555711ee89834c1111fb2e`.

A unique controlled fixture on `D:` then passed all assertions and cleaned up its fixture directory and index database after verification:

- initial build: 173 persisted records; 176 metadata acquisitions, 176 successes, zero failures; 253 ms wall, 156 ms CPU, 32,985,088 B peak working set, 98,304 B index;
- zero-byte, 16-byte, and 1,048,576-byte exact bounds passed;
- controlled `2020-01-02T03:04:05Z` and `2024-05-06T07:08:09Z` UTC last-write bounds passed;
- hidden/read-only/system filtering passed for the controlled file;
- incremental create (33 B), growth/truncation-style size replacement (to 8,192 B), timestamp-only update, rename plus move, delete, and reopen/status were all observed through three successful syncs;
- the three syncs applied 30, 24, and 15 journal records respectively, acquired 6, 5, and 4 metadata values respectively, and had zero metadata failures.

T12 is therefore passed.

## Architecture and tooling decision

The implementation keeps `Quail.Core` as the only required boundary and adds no generic metadata framework, service/IPC, background queue, or live query-time filesystem calls. The final 161.596 s initial-build wall time is an accepted Quail 0.1 limitation and a future optimization candidate only; any later work must measure a concrete alternative before adding complexity.

`agent-preflight.ps1` remained the canonical preflight. `vm-verify.ps1` dynamically discovered Quail-Lab and passed its smoke path after the VM host key was accepted; no tooling change was made. The disposable `experiments/Quail.MetadataProbe` was removed: the production helper, integration tests, and recorded evidence now cover its only diagnostic value. The repeated host build/measurement collection remains a candidate for a later bounded tooling extension only if the same stable inputs and summary recur.

## Limitation

Initial metadata enrichment is materially slower than M04.5: 161.596 s wall / 152.859 s CPU for the representative 862,284-record host build. This is accepted for Quail 0.1, but it is not a general performance target. A future optimization must preserve the documented semantics and must not silently add parallelism, a scheduler, service, crawler, or migration framework merely to improve this number.
