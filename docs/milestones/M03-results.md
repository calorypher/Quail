# M03 Results — Incremental Correctness

## Result

**Verified on the controlled Quail-Lab NTFS volume.** M03 adds a schema-v2 authoritative USN checkpoint, one-shot journal synchronization, fail-closed continuity handling, a staged initial-build-to-journal handoff, and one canonical namespace identity per object. It does not add a watcher, service, IPC, search, GUI, or installer.

## Tested source and environments

| Environment | Details |
|---|---|
| Development host | Windows 11 x64; .NET SDK `10.0.400`; branch `m03-incremental-correctness`, based on clean current `main` `ef7c7cddc8d0fa17927478b1ed5d6ba477568807` |
| Quail-Lab | Windows 11 VM; .NET SDK `10.0.400`; Git `2.55.0.windows.3`; gh `2.97.0`; remote administrator token verified |
| Runtime target | Dynamically discovered `D:` labeled `QUAIL_LAB_DATA`, NTFS, `Healthy`, operational `OK` |

Before runtime work, the VM clone was clean on `main` at `ef7c7cddc8d0fa17927478b1ed5d6ba477568807`. `M01-clean`, `M02-clean`, and the already-existing `M03-clean` checkpoints were present; none was removed or overwritten. The VM address was discovered for this run and is intentionally not retained here.

## Schema-v2 checkpoint semantics

- `schema_version = 2` is required for incremental readiness.
- `metadata` persists stable volume identity, Journal ID, `next_usn`, Journal `FirstUsn`, and `LowestValidUsn`.
- The authoritative `next_usn` means every required record before that cursor has been committed to `namespace_entries` in the same SQLite history.
- Every parsed journal batch applies namespace mutations, record-count metadata, and the checkpoint in one SQLite transaction with WAL and `synchronous=FULL`.
- A pre-commit failure rolls back both mutations and checkpoint. Replaying that same batch converges through idempotent upsert/delete behavior.
- Schema-v1 is not assigned a guessed cursor. It is reported as `rebuild-required` because it lacks an authoritative checkpoint.
- `namespace_identity_format = canonical-file-id-128-v1` is required. Older M03 schema-v2 databases without this marker require a safe rebuild rather than being interpreted with mixed V2/V3 keys.

The status command exposes the schema state, volume identity, Journal ID, next USN, First USN, Lowest Valid USN, and any rebuild reason. A `rebuild-required` index remains physically present but is not used for paths diagnostics as though it were current.

## Initial-build handoff and rebuild lifecycle

The production build sequence is:

1. Query and save the starting journal state.
2. Enumerate MFT namespace records into a separate staging SQLite database and add the official root identities.
3. Re-query the journal and validate Journal ID and readable range.
4. Read and transactionally replay from the starting `NextUsn`.
5. Persist the resulting checkpoint and only then mark the staging database complete and promote it.

If journal continuity changes during the handoff, the staging database is left incomplete and the previous complete database remains in place. The committed replacement uses the existing M02 atomic file replacement path. No arbitrary post-enumeration checkpoint is attached.

## Verification matrix

| ID | Result | Evidence |
|---|---|---|
| T01 | Pass | Automated checkpoint round-trip asserts schema-v2 and Journal ID `0x1234567890ABCDEF`, next USN, First USN, and Lowest Valid USN. |
| T02 | Pass | Automated journal batch test reopens the database and observes both renamed namespace data and next USN `200`. |
| T03 | Pass | Deterministic fault before transaction commit leaves path and next USN `100` unchanged; replay then reaches `200`, and a repeat replay converges. |
| T04 | Pass | Malformed and v4 journal buffers throw before batch application; checkpoint remains `100`. |
| T05 | Pass | A database explicitly marked schema v1 reports `rebuild-required` with no exposed checkpoint. |
| T06 | Pass | Lab initial build on `D:` completed; subsequent one-shot sync applied 213 records after controlled file and directory creation. |
| T07 | Pass | Lab file rename/move resolved as `D:\Quail-M03-Test\moved-alpha\moved.txt`. |
| T08 | Pass | Lab directory rename/move resolved existing descendants through `D:\Quail-M03-Test\moved-alpha\...` without descendant row rewrites. |
| T09 | Pass | Controlled `delete-tree` was removed before sync; no matching path was emitted afterwards. |
| T10 | Pass | A mixed 213-record stream from create, modify, rename, move, and delete converged to final namespace paths, without any one-action/one-record assumption. The focused follow-up also exercised real V3 old/new rename records. |
| T11 | Pass | Checkpoint advanced from `4848520` to `4868680` after changes made between finished Quail processes; reopened diagnostics resolved `restart-gap\catch-up.txt`. |
| T12 | Pass | On disposable `D:`, delete/recreate of the journal changed its ID. Sync applied zero records and returned `rebuild-required reason=journal-id-mismatch`. |
| T13 | Pass | A 1 MiB disposable journal was filled by 12,000 controlled create/delete operations. `FirstUsn` advanced to `0x1c0000`, while saved next USN was `1432`; sync applied zero records and returned `saved-usn-before-readable-range`. |
| T14 | Pass | Rebuild after T13 completed only through schema-v2 staged handoff: 1,736 enumerated records, final schema-v2 checkpoint `01DD30D4311FEDDA` / next USN `4568680`, and representative preexisting paths reopened successfully. Automated handoff coverage also proves a record produced during enumeration is replayed before promotion. |

Automated verification command:

```powershell
dotnet test tests\Quail.Core.Tests\Quail.Core.Tests.csproj -c Release --no-restore
```

Result: **24 passed, 0 failed**.

## Quail-Lab runtime evidence

Initial build on the positively identified target completed with 52 enumerated records and a schema-v2 checkpoint. The first mixed sync applied 213 records in two batches, ending at Journal ID `01DD3092B74A3927`, next USN `4848520`. The restart catch-up sync applied 193 records in two batches and ended at `4868680`.

The initial Journal ID reset deliberately produced:

```text
SYNC state=rebuild-required recordsApplied=0 batches=0 reason=journal-id-mismatch
```

The controlled journal-overwrite test then reported the same Journal ID but an unreadable saved position:

```text
First Usn: 0x00000000001c0000
SYNC state=rebuild-required recordsApplied=0 batches=0 reason=saved-usn-before-readable-range
```

The final safe rebuild replaced that stale state only after a valid staged build/handoff and reopened `after-journal-reset.txt` and `restart-gap\catch-up.txt` from the persistent namespace.

### V2/V3 identity follow-up

The original alias implementation was rejected by focused QA. The added deterministic tests reproduced both failures: an ordinary V3 update created two rows for an existing V2 entry, and a V3 directory rename made an untouched V2 descendant unresolvable.

On the supported Quail-Lab NTFS runtime, the probe observed this exact relation for a controlled directory:

```text
MFT V2: 0x00060000000006e2
USN V3: 0x000000000000000000060000000006e2
```

The V3 identifier's low eight bytes exactly matched the V2 identifier and its high eight bytes were zero. M03 now stores one canonical 16-byte identity: every initial V2 file and parent ID is zero-extended, and a V3 record is accepted only when it has that observed shape. The accepted V3 bytes are stored unchanged; no 16-byte incoming identifier is truncated and no V2/V3 duplicate namespace row is retained.

Records with a non-zero V3 high half fail closed before mutation/checkpoint advancement because MFT V2 enumeration cannot safely correlate them. This is a bounded compatibility gate for an unobserved runtime shape, not an arbitrary first-eight-byte alias rule.

Focused runtime regression built a V2 initial index for `D:\Quail-M03-Identity-Regression`, then used actual V3 records to rename and move its directory while `untouched.txt` had no journal record. Sync applied 19 records and resolved exactly one final path:

```text
D:\Quail-M03-Identity-Regression\destination\moved\untouched.txt
```

The old M03 database, which lacks the identity-format marker, reported `rebuild-required` without exposing a checkpoint. No additional destructive journal churn was used for this regression.

A later follow-up sync correctly reported `saved-usn-before-readable-range` because the disposable journal remained at the 1 MiB capacity deliberately used by T13 and later VM activity had expired that test checkpoint. A normal staged rebuild of only the identity-regression database restored a valid frontier. The immediately following ordinary V3 update of the preexisting `untouched.txt` applied 20 records and still produced exactly one path line. A controlled create/delete then applied 5,241 and 11 records respectively; `delete-me.txt` produced zero path lines after deletion.

## Parser and identifier compatibility

The production reader requests only USN major versions 2 through 3. It parses v2 and v3 records with their native 8-byte and 16-byte identifier widths and stores identifiers as SQLite BLOB values. Malformed records, invalid bounds, backwards cursors, and unsupported required versions fail closed; the checkpoint does not advance with an unparsed batch.

USN v4 remains intentionally unsupported and outside M03 scope. If a selected runtime requires v4 despite the explicit v2-v3 request, sync becomes `rebuild-required`; it never silently claims currentness.

MFT enumeration currently exposes v2 records while journal reads expose v3 records. Under the validated Quail-Lab invariant, M03 zero-extends V2 IDs into the canonical 16-byte form and retains the accepted V3 bytes unchanged. Parent references therefore use the same canonical key before and after V3 rename/move records; untouched descendants follow the renamed ancestor without a subtree rewrite.

## Hard-link limitation

The namespace schema still has one row per `file_id`, so it does not represent general NTFS hard-link multiplicity. No hard links were created for the required M03 scenarios, and ordinary create/delete/rename/move behavior did not require a redesign. M03 must not be described as complete hard-link namespace support. A future decision requiring hard-link correctness needs a separate namespace representation milestone.

## Known limitations and later implications

- Synchronization is one-shot. A valid checkpoint is the durable frontier reached by that invocation, not a background-currentness promise after the process exits.
- A later Windows Service may own privileged volume handles, journal queries, and reads, then deliver only validated batches to an unelevated client. M03 adds neither service nor IPC.
- Rebuild-required diagnostics are deliberately conservative: volume mismatch, journal mismatch, unreadable position, missing checkpoint, malformed records, and read/query failures never advance the frontier.
- Multi-volume orchestration, background monitoring, search, and lifecycle automation remain outside M03.
