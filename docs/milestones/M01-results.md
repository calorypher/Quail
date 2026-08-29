# M01 Results — NTFS/MFT/USN feasibility

## Status

**Verified.** The physical-host and disposable-lab evidence supports `GO WITH LIMITATIONS`.

## Environments and preflight

| Environment | Tooling | Volume | Access |
|---|---|---|---|
| Physical host | Windows 11 x64 build `10.0.26200`, .NET SDK `10.0.400` | `C:` NTFS | unelevated for T01–T02; user-controlled elevated for T03–T05 |
| Quail-Lab | Windows 11 x64, .NET SDK `10.0.400`, Git `2.55.0.windows.3`, gh `2.97.0` | `D:` `QUAIL_LAB_DATA`, NTFS, healthy, 10,719,588,352 bytes | remote administrator |

VM preflight passed: passwordless SSH identified `quail-lab\quailadmin`, `net session` succeeded, checkpoint `M01-clean` existed, and `fsutil usn queryjournal D:` succeeded. The dynamic VM address is deliberately not retained.

`experiments/Quail.NtfsProbe` is a .NET 10 Windows-only probe using `CreateFileW`, `FSCTL_QUERY_USN_JOURNAL`, `FSCTL_ENUM_USN_DATA`, and `FSCTL_READ_USN_JOURNAL`. It contains no custom NTFS parser, persistence, SQLite, service, or IPC.

## Test matrix

| ID | Result | Durable runtime evidence |
|---|---|---|
| T01 | Pass | Unelevated host discovery reported `C:` as fixed/ready/NTFS and `Q:` as NTFS with label `QUAIL_M01`. |
| T02 | Pass | Unelevated `CreateFileW` for `\\.\C:` returned `WIN32_ERROR=5` (`Access is denied`). |
| T03 | Pass, physical host | Elevated journal query succeeded: ID `0x01dce2a9ec38f4e6`, First USN `8564768768`, Next USN `8600216624`, Lowest Valid USN `0`, supported major versions 2–4. |
| T04 | Pass, physical host | Elevated full enumeration completed with 854,597 records, zero parse errors, and zero unsupported records. |
| T05 | Pass, physical host | One warm-up plus three measured enumerations completed; all had zero parse errors and zero unsupported records. Median is recorded below. |
| T06 | Pass, VM | From checkpoint `7424`, create/modify/rename/move/delete operations returned 19 USN v3 records. Rename emitted `0x00001000`/`0x00002000` old/new records; a user operation emitted multiple records. |
| T07 | Pass, VM | Saved `{ JournalId=0x01dd2cf9ca22cf1b, NextUsn=9128 }`, changed a file while stopped, then caught up five records (`CATCH_UP status=ok`, next USN `9648`). |
| T08 | Pass, Quail-Lab / `QUAIL_LAB_DATA` | Fresh `D:` enumeration returned 21 records, zero parse errors and zero unsupported records. |
| T09 | Pass, Quail-Lab / `QUAIL_LAB_DATA` | The controlled `Quail-M01-Test` tree reconstructed paths including `alpha\alpha-one.txt` and `beta\nested\nested-one.txt` from FRN + parent FRN + name. |
| T10 | Pass, VM | Deliberate saved-ID mismatch returned `rebuild-required reason=journal-id-mismatch`. |
| T11 | Pass, VM | Controlled churn/deletion advanced `FirstUsn` to `2621440`; saved USN `0` on the same ID returned `rebuild-required reason=saved-usn-before-readable-range`. `LowestValidUsn` was `0`. |
| T12 | Pass, VM | After confirming healthy NTFS label `QUAIL_LAB_DATA`, delete/recreate changed journal ID from `0x01dd2cf9ca22cf1b` to `0x01dd3092b74a3927`; prior checkpoint required rebuild. |

T08 and T09 intentionally used Quail-Lab's disposable `QUAIL_LAB_DATA` volume rather than the host `QUAIL_M01` VHDX. This follows the M01 preference for autonomous T06–T12 execution in the VM; the host VHDX was therefore not needed for those tests.

## Record, continuity, and performance findings

The lab journal advertised major record versions 2–4. The query structure is correctly represented as `USN_JOURNAL_DATA_V1`; M01 does not consume the additional V2-only fields. `FSCTL_ENUM_USN_DATA` returned USN_RECORD_V2; journal reading returned USN_RECORD_V3. The probe parses both and retains the full 128-bit V3 identifiers. For journal reads it explicitly negotiates only major versions 2–3, so it does not request v4 records it cannot parse; an unsupported or malformed journal-read record now fails the command rather than allowing `CATCH_UP status=ok`. M02/M03 must not assume one record version or truncate incoming identifiers to 64 bits.

The experimental checkpoint is `{ volume identity, Journal ID, Next USN }`. A Journal ID mismatch or saved USN before `FirstUsn` requires rebuild; otherwise journal reading can catch up. Capture `LowestValidUsn` too, although the observed expired-position bound here was `FirstUsn` with `LowestValidUsn=0`.

Post-QA regression on Quail-Lab saved checkpoint `{ JournalId=0x01dd3092b74a3927, NextUsn=4819160 }`, created one file on `QUAIL_LAB_DATA`, and then read five USN v3 records successfully. The resulting status was `CATCH_UP status=ok ... requestedMinMajor=2 requestedMaxMajor=3`, confirming the probe requests only its v2–v3 parser range at runtime.

Fresh lab enumeration: 21 records, 6.136 ms, 3,423 records/s, 15.625 ms process CPU, 24,346,624-byte peak working set, zero parse errors. This is a correctness sanity check, not a representative performance result.

Physical-host T05 used one warm-up followed by measured runs: (1) 854,566 records, 1,418.700 ms, 602,359 records/s, 1,468.750 ms CPU, 291,778,560-byte peak working set; (2) 854,565 records, 1,423.291 ms, 600,415 records/s, 1,468.750 ms CPU, 291,807,232-byte peak working set; (3) 854,566 records, 1,436.149 ms, 595,040 records/s, 1,484.375 ms CPU, 291,774,464-byte peak working set. Median: **854,566 records, 1,423.291 ms, 600,415 records/s, 1,468.750 ms CPU, 291,778,560-byte peak working set**. The approximately 291 MB peak working set includes materializing every enumerated record in the probe's `List<UsnRecord>`; it is not the minimum cost of the Windows API or enumeration itself. No arbitrary performance threshold is inferred.

## Physical-host elevated evidence

The full host output is retained in UTF-8 in `M01-host-elevated.txt`. It was safely transcoded from the mixed-encoding captured output without changing the observed values. The commands below were executed from elevated PowerShell at repository root. They are read-only for `C:` and write only the evidence file in this repository.

```powershell
$project = ".\experiments\Quail.NtfsProbe\Quail.NtfsProbe.csproj"
dotnet build $project -c Release
"Test ID: T03`nDate/time: $(Get-Date -Format o)`nEnvironment: physical host`nVolume: C:`nElevation: elevated`nCommand: journal" | Set-Content .\docs\milestones\M01-host-elevated.txt
dotnet run --project $project -c Release --no-build -- journal --volume C: 2>&1 | Tee-Object .\docs\milestones\M01-host-elevated.txt -Append
"`nTest ID: T04`nCommand: enumerate" | Tee-Object .\docs\milestones\M01-host-elevated.txt -Append
dotnet run --project $project -c Release --no-build -- enumerate --volume C: 2>&1 | Tee-Object .\docs\milestones\M01-host-elevated.txt -Append
"`nTest ID: T05 warm-up plus three measured runs" | Tee-Object .\docs\milestones\M01-host-elevated.txt -Append
1..4 | ForEach-Object { dotnet run --project $project -c Release --no-build -- enumerate --volume C: 2>&1 } | Tee-Object .\docs\milestones\M01-host-elevated.txt -Append
```

The first of the final four outputs is warm-up; the other three are measured runs. Each summary reports record count, elapsed time, records/s, CPU time, peak working set, parse errors, and unsupported records.

## Service boundary and implications

The smallest sensible future Windows Service responsibility is privileged opening of local NTFS volumes plus initial enumeration, journal querying, and journal reads. It should return only validated structured per-volume metadata/change records to an unelevated application. This does not authorize production service or IPC work in M01.

- M02 must preserve native identifier width and model namespace state through parent identifier plus name.
- M02 initial enumeration must use streaming or batching rather than treating the probe's full `List<UsnRecord>` materialization as an acceptable production memory model.
- M03 must atomically associate volume identity, Journal ID, Next USN, and relevant range metadata with index mutations.
- M03 must handle multiple records per operation and rename old/new pairs, and rebuild after ID mismatch or unreadable position.

## Conclusion

**GO WITH LIMITATIONS.** Official APIs supplied all data required by the tested namespace and change-stream feasibility work. V2 appeared in enumeration and V3 in journal reads; v4 was advertised but not encountered or requested by the probe. M02/M03 must preserve native identifier width and retain a bounded compatibility decision for future v4 records.
