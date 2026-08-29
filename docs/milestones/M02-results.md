# M02 Results — Persistent Initial Index

## Result

M02 provides a SQLite-backed, single-volume initial NTFS namespace index. The implementation uses `FSCTL_ENUM_USN_DATA` through thin P/Invoke, writes records in batches, and does not retain a full namespace list in managed memory. M03 incremental processing is deliberately not implemented.

## Tested environments

| Environment | Details |
|---|---|
| Development host | Windows 11 x64, build 10.0.26200; .NET SDK 10.0.400 |
| Quail-Lab | Windows VM `Quail-Lab`, .NET SDK 10.0.400; disposable NTFS volume `QUAIL_LAB_DATA` mounted as `D:` |
| Source baseline | `668e1ca454c8652931d7710b836601df5dc5930a` before M02 changes |

## Persistence design

- Schema version: explicit metadata value `schema_version = 1`.
- `metadata` stores schema version, volume identity, mount point, filesystem and label, build state, timestamps, and record count.
- `namespace_entries` stores `file_id`, `parent_file_id`, `name`, attributes, USN, and source record version. Native identifiers are SQLite `BLOB` values, preserving both 8-byte and representative 16-byte values.
- The canonical namespace is parent ID plus name. Full paths are reconstructed only for diagnostics.
- SQLite uses WAL journal mode and `synchronous=FULL`. Connection pooling is disabled so a closed staging database can be promoted safely on Windows.

## Build lifecycle and path behavior

Fresh indexes are absent. A build writes a separate `.building` SQLite database with state `building`; only after the records and metadata are durable is it marked `complete` and promoted. A rebuild preserves the previous complete database until replacement. A deliberate failure leaves the previous complete database usable; without a previous database, `status` reports incomplete.

The NTFS root directory is added through official file-ID APIs because `FSCTL_ENUM_USN_DATA` does not enumerate that root record. The store includes an 8-byte root representation from `GetFileInformationByHandle` and a width-preserving 16-byte representation from `GetFileInformationByHandleEx(FileIdInfo)`, so V2 and V3 parent chains can both terminate correctly without truncating identifiers. Path reconstruction detects missing parents and cycles; it does not loop indefinitely. Missing parents for inaccessible/system namespace records are reported diagnostically rather than presented as complete paths.

## Verification matrix

| ID | Result | Evidence |
|---|---|---|
| T01 | Pass | Automated tests verify absent/fresh status, schema creation, metadata, and complete state. |
| T02 | Pass | Automated round-trip test covers 8-byte and representative 16-byte IDs. |
| T03 | Pass | Automated reopen/path test plus bounded missing-parent and cycle diagnostics. |
| T04 | Pass | Automated injected failure test reports incomplete when no complete index exists. |
| T05 | Pass | Automated reopen test reads a completed SQLite index after a new `IndexStore` instance is created. |
| T06 | Pass | Final post-QA Quail-Lab regression on `D:` persisted 45 records, with zero parse/unsupported errors, including the controlled `Quail-M02-Test` tree. |
| T07 | Pass | Final post-QA persisted paths included `D:\Quail-M02-Test\alpha\alpha-one.txt`, `D:\Quail-M02-Test\beta\nested\nested-one.txt`, and the empty directory. |
| T08 | Pass | Final post-QA Quail-Lab rebuild completed with 45 records and state `complete`. |
| T09 | Pass | Final post-QA `--fail-after 3` stopped the staging build and preserved the previously complete 45-record index as `complete`. |
| T10 | Pass | Final physical-host `C:` build at `a1aa74adbb1c1a5fb16435180e54859947e58488` completed with the measurements below. |
| T11 | Pass | Final physical-host database reopened with `complete` state and 859,570 records. |

## Independent QA regression

The following corrections were made after independent QA on the same M02 branch:

- Enumeration is now fail-closed: malformed USN records and unsupported major versions throw before staging promotion. Automated tests verify that both preserve a previous complete index; CLI emits an error rather than `BUILD state=complete`.
- Root identities now have both official 8-byte and 16-byte forms, preserving V3 parent relationships. An automated test verifies a complete 128-bit root/child chain and reconstructed path.
- `paths`, path reconstruction, and status open existing databases read-only. An automated test verifies absent diagnostic reads do not create a SQLite database.

QA regression results: release tests passed 11/11; Quail-Lab `D:` build completed with 45 records, zero parse/unsupported errors, 137 ms elapsed, 62 ms CPU, and 29,704,192 B peak working set. Persisted `Quail-M02-Test` paths, including `D:\Quail-M02-Test\beta\nested\nested-one.txt`, reconstructed correctly.

## Quail-Lab runtime evidence

The dataset was recreated only after label, NTFS format, health, and drive letter had been positively verified for `QUAIL_LAB_DATA`. The final post-QA regression reported 45 records, 137 ms elapsed, 62 ms CPU time, 29,704,192 B peak working set, zero parse errors, and zero unsupported records. The earlier 41-record run is superseded and is not the current M02 result.

## Final physical-host `C:` measurement

| Field | Value |
|---|---|
| Windows | Windows 11 x64, version 10.0.26200 |
| .NET | 10.0.400 |
| Tested HEAD | `a1aa74adbb1c1a5fb16435180e54859947e58488` |
| Stable volume identity | `\\?\Volume{90687943-ee64-4954-8544-39f1219dcb9f}` |
| Mount point | `C:\` |
| Records | 859,570 |
| Wall time | 18.909 s |
| CPU time | 14.094 s |
| Peak working set | 86,687,744 B |
| SQLite database size | 121,393,152 B |
| SQLite settings | WAL; synchronous=FULL |
| Parse / unsupported errors | 0 / 0 |
| Final state | complete |

The final 86.7 MB measured peak is materially below the M01 probe's approximately 291 MB peak caused by retaining the full namespace in `List<UsnRecord>`. The M02 enumeration streams each returned native buffer directly into bounded SQLite transactions; no full namespace collection is retained. Earlier physical-host figures were pre-QA and are superseded by this final-code measurement.

## Limitations and M03 implications

- M02 has no USN replay, mutation, rename/move processing, journal-reset recovery, or valid replay checkpoint claim.
- Journal identity, readable range, and next-USN continuity must be designed and atomically coupled with M03 mutations.
- USN v4 remains a bounded future compatibility decision; M02 safely records v2/v3 namespace records only.
- A future service may own privileged volume opening/enumeration and journal access, but no service or IPC exists in M02.
