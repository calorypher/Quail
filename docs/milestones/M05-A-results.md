# M05-A Results — Metadata Acquisition Decision

## Scope and decision boundary

M05-A investigated only acquisition and maintenance of logical size and user-visible modification time. It did not change the persistent schema, public search API, CLI search syntax, or metadata filters. The measurements use a small experimental .NET probe; the production implementation remains deferred to M05-B review.

## Tested mechanisms

### Existing MFT/USN records

The current `FSCTL_ENUM_USN_DATA` path returns `USN_RECORD_V2` namespace records containing file ID, parent ID, name, attributes, USN, and a journal-record timestamp, but no logical size. The USN timestamp is the time of the journal record, not the file's current `LastWriteTime`, and therefore is not suitable for the `modified` field. Controlled rename/move records also demonstrated why an operation timestamp must not be substituted for last-write time.

### ID-based handle lookup

The selected candidate opens each enumerated NTFS record through the official `OpenFileById` API, using the already open volume as the hint, the native 64-bit V2 file ID, all read/write/delete sharing modes, and `FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT`. `dwDesiredAccess=0` is sufficient for metadata queries and avoids requesting unnecessary access.

On the resulting handle:

- `GetFileInformationByHandleEx(FileStandardInfo)` returns `EndOfFile`, the logical end of the unnamed/default stream;
- `GetFileInformationByHandleEx(FileBasicInfo)` returns `LastWriteTime` as a UTC `FILETIME` and distinguishes it from `ChangeTime`;
- `FileStandardInfo.Directory` and the existing attributes determine applicability without following a reparse target.

The relevant official contracts are [OpenFileById](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-openfilebyid), [FILE_STANDARD_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_standard_info), [FILE_BASIC_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_basic_info), and [USN_RECORD_V3](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-usn_record_v3).

### Path-based lookup

Path-opened handles were used only as a correctness oracle for controlled fixtures. They returned exactly the same size and last-write values as the ID-opened handles. Path reconstruction, `FileInfo`, `stat`, directory traversal, or live lookup in the normal search path is rejected.

## Controlled Quail-Lab evidence

The canonical `scripts/vm-verify.ps1` workflow discovered Quail-Lab, confirmed SSH, selected the healthy NTFS `QUAIL_LAB_DATA` volume, deployed the current CLI, verified SHA-256, and passed `CliStatus`. The M05-specific fixture was outside its fixed scenario contract, so one additional hashed probe and a bounded remote script were used on a unique directory on the disposable data volume.

Probe SHA-256: `7A9ED36F9C038F204BABFF3E2EF867159AADA2D41FE926201203A1359F5BCD54` for the controlled correctness run. The later zero-access variant used for the final host decision had SHA-256 `325A42BB306E144037B6BFADD4AC85B210486B3C255B0B85966E01907D9944A8`.

| Case | Path-opened value | ID-opened value | Observation |
|---|---:|---:|---|
| Zero-byte file | 0 B; `2020-01-02T03:04:05Z` | Same | Zero is a valid size, not missing metadata. |
| Small file | 16 B; `2021-02-03T04:05:06Z` | Same | Exact size and UTC time matched. |
| Larger file | 8,388,608 B; `2022-03-04T05:06:07Z` | Same | Exact logical EOF and UTC time matched. |
| Content/size mutation | 16 B to 4,097 B | Same | Last-write changed to the actual write time. |
| Timestamp-only mutation | `2024-05-06T07:08:09Z` | Same | Size stayed 4,097 B; `BASIC_INFO_CHANGE` was emitted. |
| Rename then move | file ID `0x00020000000006f6` | Same ID, 4,097 B, and timestamp throughout | Namespace-only changes do not require a metadata reread. |
| System attribute | attributes changed to `0x00000024` | Size/time unchanged | Attributes come from the existing namespace record; timestamp refresh is still required for `BASIC_INFO_CHANGE`. |
| Symbolic link/reparse entry | 0 B for the link object | Same, without following the target | Reparse-point size is treated as not applicable for M05 size filtering. |
| Exclusive share handle | 4 B | Attribute-only ID lookup still succeeded | Full sharing plus metadata-only access avoids ordinary sharing conflicts; host evidence still observed four unusual sharing violations. |
| Pending delete | 4 B | Lookup succeeded with `DeletePending=true` | A subsequent delete journal record remains authoritative and removes the row. |
| Deleted/stale ID | n/a | Failed with Win32 87 | Failure is represented as unavailable; no value is invented. |

The controlled journal window contained 47 V3 records and ended with a valid catch-up cursor. It observed:

- creation with `FILE_CREATE` and close summaries;
- content growth/overwrite with `DATA_EXTEND | DATA_OVERWRITE` and `CLOSE`;
- timestamp-only and attribute changes with `BASIC_INFO_CHANGE` and `CLOSE`;
- paired `RENAME_OLD_NAME` / `RENAME_NEW_NAME` records for rename and move;
- `FILE_DELETE` for deletion;
- `REPARSE_POINT_CHANGE` for the symbolic link.

The USN journal indicates that a kind of change occurred and can accumulate reason flags until close; it does not itself contain the resulting size or authoritative last-write value. This agrees with Microsoft's [change journal record semantics](https://learn.microsoft.com/en-us/windows/win32/fileio/change-journal-records).

## Quail-Lab cost

The data volume contained 1,756 records: 1,718 files, 38 directories, one reparse entry, and 17 system entries. After one warm-up, three measured runs produced:

| Mechanism | Median wall | Observed range | Success/failure | Median peak working set |
|---|---:|---:|---:|---:|
| Enumeration only | 6.158 ms | 6.143–6.198 ms | n/a | 25,014,272 B |
| Open by ID + basic/standard info | 26.888 ms | 26.861–27.598 ms | 1,756 / 0 | 25,235,456 B |

The measured warm additional cost was approximately 20.730 ms, or 11.8 microseconds per record. All reparse and system entries opened successfully, no successful lookup returned an unavailable timestamp, and no directory flag disagreement was observed.

## Physical-host cost

The host run was bounded and read-oriented for `C:`. It wrote only a controlled index and temporary evidence under `target/m05a`; it did not mutate host filesystem content or the journal. Those generated artifacts were removed after their summarized values were recorded here.

The current production M04.5 build completed with:

- 861,998 records;
- 81.053 s wall and 72.875 s CPU;
- 96,854,016 B peak working set;
- 239,095,808 B database;
- zero parse or unsupported-record failures.

The selected zero-desired-access lookup used one warm-up plus three measured runs. The record count drifted by seven records during ordinary host activity, so ratios use the representative approximately 862,000-record population.

| Mechanism | Median wall | Median CPU | Median peak working set | Lookup outcome |
|---|---:|---:|---:|---:|
| Enumeration only | 1.159 s | 1.141 s | 34,693,120 B | n/a |
| Open by ID, access 0, basic/standard info | 35.991 s | 35.906 s | 92,573,696 B | 861,684 success; 324 unavailable |
| Increment over enumeration | 34.831 s | 34.766 s | +57,880,576 B in the standalone probe | approximately 40.4 microseconds per record |

The unavailable rate was approximately 0.0376%: 320 `ERROR_ACCESS_DENIED` and four `ERROR_SHARING_VIOLATION`. Of those, 178 were system-attributed records; none of the 275 reparse entries failed. The stable failure count across runs indicates deterministic inaccessible entries plus a very small sharing-conflict set, not random broad instability.

The selected run's wall and CPU times differed by only about 85 ms at the median. This, plus the absence of content reads, indicates CPU/kernel metadata-query overhead rather than a material blocking data-I/O penalty in the warm-cache measurement. Cold-storage behavior was not separately forced because cache flushing would be disruptive and unrepresentative of the normal-system-state methodology.

If the lookup is integrated into the existing single enumeration instead of run as a second pass, the measured projection is approximately 115.9 s for the current 862k-record production build: about +34.8 s or +43% over the observed 81.1 s build. This is an estimate, not a substitute for the integrated M05-B measurement. The work is linear and streaming; an initial build/rebuild is infrequent, and the result does not justify a service, crawler, parser, scheduler, or parallel complexity.

Forty host records changed attributes between enumeration and lookup. This is expected on a live volume and confirms that initial enrichment must remain inside the existing journal-handoff window rather than claiming a frozen filesystem snapshot.

## Persistent semantics for M05-B

### Logical size

- Persist the signed 64-bit `FILE_STANDARD_INFO.EndOfFile` value for an ordinary non-directory, non-reparse entry.
- It is the logical length of the unnamed/default data stream, not allocation size, compressed size, total alternate-stream size, or physical disk usage.
- A legitimate empty file stores `0`.
- Directories and reparse entries store `NULL` because size is not applicable to the M05 user-visible file-size filter.
- A failed standard-information query stores `NULL`; no zero fallback is allowed.

### Modified time

- Persist `FILE_BASIC_INFO.LastWriteTime` as its signed 64-bit UTC `FILETIME` value, with conversion and filtering performed in UTC.
- Use `LastWriteTime`, not `ChangeTime`, creation time, last-access time, the namespace record USN, or the USN record timestamp.
- Modification time is applicable to files, directories, and the reparse object itself; `FILE_FLAG_OPEN_REPARSE_POINT` prevents silently substituting target metadata.
- A failed basic-information query or a non-positive/invalid returned time stores `NULL`; no epoch fallback is allowed.

### Unavailable metadata

Size and modified time are independent nullable fields. If `OpenFileById` fails, both are `NULL`. If only one information query fails, only that field is `NULL`. Size/time filters exclude `NULL` values naturally; unavailable metadata never matches a bound and never masquerades as a zero-byte or epoch entry. M05-B need not add a general metadata-status framework.

Expected per-entry acquisition failures are non-fatal for the build/sync and are counted in diagnostics. Malformed identifiers, unsupported required record versions, database failures, or continuity failures retain the existing fail-closed/rebuild behavior.

## Initial-build acquisition strategy

1. Reuse the current volume handle and streaming `FSCTL_ENUM_USN_DATA` producer.
2. For each V2 record, perform one `OpenFileById` using its native 64-bit file ID, access 0, all sharing modes, backup semantics, and open-reparse-point semantics.
3. Query basic and standard information on that handle and attach nullable size/time to the record passed to the staging writer.
4. Keep the existing 2,048-row SQLite commit batching and staged `.building` promotion.
5. Preserve the existing journal handoff. Any change between enumeration and enrichment, or after enrichment, is replayed before promotion.

The acquisition is per-record streaming and holds only one additional file handle at a time. It requires no path reconstruction, traversal, result-time stat, work queue, or second in-memory namespace.

## Incremental refresh contract

M05-B should define a metadata-refresh reason mask containing:

- `FILE_CREATE`;
- `DATA_OVERWRITE`, `DATA_EXTEND`, and `DATA_TRUNCATION`;
- `NAMED_DATA_OVERWRITE`, `NAMED_DATA_EXTEND`, and `NAMED_DATA_TRUNCATION`;
- `BASIC_INFO_CHANGE`;
- `REPARSE_POINT_CHANGE` and `STREAM_CHANGE`;
- `TRANSACTED_CHANGE` conservatively if observed in the supported V2/V3 stream.

`CLOSE` alone does not trigger refresh, but a close summary carrying any refresh bit does. Namespace-only rename/move reasons do not trigger refresh. Compression, encryption, object-ID, security, and close-only changes do not by themselves require size/mtime lookup; current attributes continue to come from the namespace record.

Within each journal batch, acquire metadata at most once per distinct file ID whose accumulated reasons require refresh and whose final action is not deletion. Acquire before the SQLite mutation transaction, then atomically commit namespace changes, nullable metadata, FTS trigger effects, and the batch checkpoint. A change racing after the lookup necessarily has a later USN and is handled by a later batch. An expected lookup failure commits explicit `NULL` rather than stale data; a later accumulated close/change record can refresh it.

## Rename, move, and delete behavior

NTFS retains the same file ID through rename and move, and the fixture retained exact size and last-write values. M05-B should preserve existing metadata columns on `RENAME_NEW_NAME` and avoid a secondary lookup when the row already exists.

To make that preservation independent of a buffer boundary, `RENAME_OLD_NAME` should no longer destroy metadata that belongs to the same file ID. The simplest compatible behavior is to keep the existing row until `RENAME_NEW_NAME` updates parent/name/attributes/USN in place. If a new-name record has no existing row, perform the normal ID lookup as a conservative recovery path. `FILE_DELETE` remains the authoritative atomic removal of namespace, metadata, and FTS state.

This can temporarily retain the old namespace if a process stops between the old-name and new-name journal records; the next one-shot sync resumes from the checkpoint and applies the new name. It is no worse than the current temporary absence after deleting on the old-name half, and the completed sync remains correct without a pending-rename subsystem.

## Crash consistency and architecture

The selected mechanism fits the current architecture:

- initial data remains inside the staged replacement database;
- incremental metadata and namespace state commit in the same SQLite transaction as the USN checkpoint;
- FTS triggers remain transactionally tied to the authoritative namespace row;
- lookup failures are data values (`NULL`), not a second eventually consistent store;
- crashes before commit leave both metadata and checkpoint unchanged;
- crashes after commit leave both advanced together.

A small concrete metadata value and ID-based acquisition helper are sufficient. No service/IPC, scheduler, background queue, crawler, generic enrichment framework, native component, or additional provider boundary is required. `Quail.Core` remains the appropriate boundary.

M05-B should bump the schema or add an explicit metadata-format marker and reject older complete indexes for metadata search rather than guessing a migration.

## Rejected alternatives

- **USN record timestamp** — it describes the journal event, not authoritative current `LastWriteTime`.
- **USN/MFT enumeration alone** — it contains neither logical EOF nor authoritative current last-write time.
- **Live `FileInfo`, path stat, or traversal during search** — violates persistent-index-only search and introduces query latency, permission failures, and path races.
- **Directory enumeration as the metadata source** — provides useful directory-entry fields but requires a crawler/traversal path, duplicates namespace discovery, and does not improve the current ID-based architecture.
- **`FSCTL_GET_NTFS_FILE_RECORD` or raw MFT parsing** — would require a custom NTFS attribute parser and materially expand risk and scope.
- **Allocation size or compressed size** — does not implement the requested logical file-size semantics.
- **Background repair queue/service** — unnecessary for the measured 0.0376% explicit-unavailable population and outside M05.
- **`FILE_READ_ATTRIBUTES` desired access** — produced essentially the same unavailable set and slightly higher observed cost than access 0, while requesting an unnecessary access mask.

## Limitations

- Approximately 0.038% of representative host entries could not be enriched synchronously and will have explicit `NULL` metadata until a later relevant event or rebuild succeeds.
- A separate deterministic ACL-deny fixture was not successfully established on Quail-Lab; inaccessible-entry semantics are evidenced by the stable 320 real host `ERROR_ACCESS_DENIED` results. Controlled pending-delete, completed-delete, reparse, and system-entry cases were exercised in the lab.
- Directory and reparse-point sizes are intentionally not applicable. The reparse target is never followed.
- Logical size covers only the unnamed/default stream, not alternate data streams or allocation/compression usage.
- The projected integrated build time and memory must be measured again in M05-B; standalone probe peak working set cannot be added mechanically to production build peak working set.
- The existing one-row-per-file-ID hard-link limitation remains unchanged; M05-A does not redesign namespace multiplicity.
- The host run is a warm normal-system-state measurement. No disruptive cache flush or cold-boot claim was made.

## Tooling observations

- `agent-preflight.ps1` replaced branch/HEAD/clean/origin/tool checks with one supported call.
- `vm-verify.ps1` successfully replaced VM IP discovery, SSH probing, remote PowerShell transition, labeled-volume discovery, CLI deployment, SHA verification, and bounded status smoke.
- The fixed `CliStatus`/single-artifact contract does not cover a disposable M05 probe or fixture scenario. One additional artifact copy/hash and one encoded remote PowerShell script were required. This is a candidate only if the same multi-artifact milestone scenario repeats; M05-A does not extend tooling.
- Physical-host build-plus-measure collection has now repeated after M04 and is a plausible later bounded tooling candidate. It should not be generalized until M05-B shows the stable final inputs and summaries.

## Exact M05-B implementation contract

M05-B may proceed only with the following contract:

1. Add nullable logical-size and UTC last-write fields with the semantics above; use schema/format rejection for older indexes.
2. Implement one thin Windows ID-based metadata helper using `OpenFileById` access 0 and `FileBasicInfo`/`FileStandardInfo`.
3. Enrich the existing streaming initial enumeration before staged writes and retain the current handoff.
4. Refresh once per distinct affected file ID per batch using the defined reason mask; commit metadata, namespace, FTS effects, and checkpoint atomically.
5. Preserve metadata across rename/move by file ID; remove it only on real delete. Use lookup on rename-new only when no row exists.
6. Store acquisition failure as independent `NULL` fields and expose failure counts in build/sync diagnostics without adding a repair subsystem.
7. Keep normal search entirely SQLite-backed and implement only the M05 filters accepted by the milestone.
8. Re-measure integrated build overhead, database size, peak working set, failure counts, and metadata-filter latency on Quail-Lab and the physical host.

## Decision

**GO**
