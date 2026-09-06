# M19 Results — Continuous Maintenance Boundary Spike

## Status

**PHASE 1 — architecture recommendation ready for the explicit decision gate.**

M19 is not complete and M20 has not started. The recommended boundary is a
dedicated, automatically started Windows Service running as LocalSystem, with no
ordinary-search IPC and with the existing elevated App worker reduced to an
administrator-authorized configuration/recovery client. The service is the sole
index writer.

## Baseline and investigation method

The investigation used current source at base
`c62b59cfd56315e09daffc347f6d8e568ebedad0`, focused automated tests, the
existing protected-storage evidence, current Microsoft Windows documentation,
and bounded Quail-Lab probes. M19 changes documentation only; it does not add a
service, task, IPC implementation, or production maintenance code.

Current flow:

```text
Index Manager
  -> IndexOperationCoordinator
    -> runas current Quail.exe
      -> AdminIndexWorker
        -> validate elevation + volume + user catalog
        -> acquire protected ProgramData storage and per-volume lock
        -> IndexStore.Build/Rebuild/Sync
      -> narrow process exit code
  -> unelevated App reads IndexStatus and searches the protected database
```

Confirmed implementation facts:

- `AdminIndexWorker` accepts only Build, Rebuild, or Refresh plus operation ID,
  mount root, and volume identity; `FileSystemIndexAdministration` reloads the
  current user's LocalAppData catalog, revalidates volume identity and derives
  the database path.
- `PrivilegedIndexStorage` protects `%PROGRAMDATA%\Quail`, `Indexes`, and
  `Locks` with SYSTEM/Administrators write and Builtin Users read/execute. It
  rejects reparse points at protected directories, the database, SQLite
  sidecars, staging, previous, and lock paths, and serializes writers with a
  non-shared per-volume lock.
- `IndexStore` commits each parsed journal batch and its checkpoint in one
  `synchronous=FULL` transaction. Protected operations temporarily use WAL,
  checkpoint it, and return the database to DELETE mode before quiescence.
- Search opens the database read-only and holds a read transaction for one
  consistent snapshot. The existing focused suite covers a reader overlapping
  protected mutation and the bounded WAL-to-DELETE transition.
- `%LOCALAPPDATA%\Quail\indexes.json` is user-scoped and contains
  `EnabledForSearch`; it is not suitable as service-owned machine maintenance
  configuration. A LocalSystem service is not associated with the logged-on
  user's profile and its HKCU/profile state is not that user's state, as also
  documented by Microsoft for the
  [LocalSystem account](https://learn.microsoft.com/en-us/windows/win32/services/localsystem-account).

## Privilege and identity findings

Microsoft documents that all change-journal operations require administrator
privileges and that the journal identifier is part of their integrity contract:
[Using the Change Journal Identifier](https://learn.microsoft.com/en-us/windows/win32/fileio/using-the-change-journal-identifier).
Current Quail opens `\\.\X:` with `GENERIC_READ` before
`FSCTL_QUERY_USN_JOURNAL` and `FSCTL_READ_USN_JOURNAL`.

The bounded identity conclusion is:

- an ordinary unelevated user cannot be the continuous NTFS journal owner and
  also cannot mutate the protected ProgramData index;
- LocalService is the one credible lower-privilege service identity considered,
  but granting it protected-directory access alone does not supply the required
  raw-volume administrator capability; adding broad local administrative rights
  would remove the intended privilege reduction while adding ACL/account setup;
- LocalSystem already matches the existing protected-storage ACL, needs no
  managed password, and has the required local authority. Microsoft also notes
  that it is highly privileged, so the executable, configuration, commands, and
  network behavior must remain narrowly constrained.

LocalSystem is therefore selected for the first implementation. No domain,
network, updater, shell, or user-profile work belongs in the service.

Quail-Lab directly confirmed successful journal query/read under the existing
elevated administrator and under SYSTEM, plus a SYSTEM write beneath the
protected ProgramData test root. The attempted standard-user probe did not
reach Quail code because the disposable account could not start any process in
the lab (`0xC0000142`, and the LIMITED task never ran). The unelevated rejection
therefore rests on the documented administrator requirement and the existing
protected-storage ACL contract, not on a falsely claimed lab result. M20 must
repeat the negative probe through a working standard-user logon path.

## Candidate comparison

| Candidate | Privilege and startup | Idle/catch-up | Configuration/control | Security and maintenance cost | Decision |
| --- | --- | --- | --- | --- | --- |
| Unelevated user-session maintainer | Cannot perform the required journal operations or protected writes without elevation; also exists only with a user session | Could wait only after obtaining a privileged handle it does not have | Could read the user catalog directly, but that does not overcome the privilege failure | Repeated UAC or a second broker would cease to be an unelevated-only design | **Rejected** |
| Pre-authorized elevated scheduled task/helper | A boot-triggered task can run under a service identity; creating a boot trigger is administrator-only ([Microsoft `IBootTrigger`](https://learn.microsoft.com/en-us/windows/win32/api/taskschd/nn-taskschd-iboottrigger)) | A periodic one-shot task requires polling; a permanently running task can block and catch up | A persistent task still needs protected machine configuration, commands, status, failure recovery, and installer lifecycle | The long-running form recreates a service-shaped process behind Task Scheduler with less direct lifecycle/health semantics; the one-shot form misses the effectively continuous/no-aggressive-polling goal | **Rejected** |
| Windows Service | SCM supplies boot lifecycle, stable identity, stop/cancellation, status and recovery controls | Can catch up from the durable checkpoint, then keep one cancellable USN wait per configured volume with no SQLite connection while idle | Needs one protected machine target catalog and a very small administrator-only control surface; search remains direct | Highest-privilege identity, but the smallest explicit and testable long-lived privileged boundary | **Recommended** |

A scheduled task is technically capable of starting at boot, but it is not
smaller for this workload. If it stays alive to wait on USN, it needs the same
host, cancellation, recovery, configuration, control, and security work as a
service. If it exits, it needs periodic polling and weakens freshness or idle
behavior. No independent product requirement benefits from hiding this process
behind Task Scheduler.

## Configuration ownership

Use two deliberately separate concepts:

1. **Machine maintenance targets** — a new versioned, atomically replaced,
   protected document under `%PROGRAMDATA%\Quail`, owned and written only by the
   service. It identifies configured volumes by stable volume identity. A mount
   point may be retained as display/diagnostic data, but is never authority; the
   service discovers ready fixed NTFS volumes and revalidates the identity before
   every operation. Database paths remain derived by `ManagedIndexPath` and are
   never accepted from a client.
2. **Per-user search preferences** — the existing
   `%LOCALAPPDATA%\Quail\indexes.json`. `EnabledForSearch` continues to decide
   whether that user's App searches a complete compatible database. Disabling a
   source does not silently stop machine maintenance; unregistering a maintenance
   target is a separate administrator-authorized action.

The service must never load a user catalog from its own LocalSystem profile and
must never treat an unelevated LocalAppData document as privileged maintenance
authority. M20 remains explicitly single-machine and compatible with the current
same-account elevation model; a generic multi-user ownership framework is out of
scope.

## Minimum privileged control boundary

Ordinary search, result opening, status reads, and `EnabledForSearch` changes do
not use IPC. They continue to read the protected database and user catalog
unelevated.

Exceptional target registration, removal, initial Build, and explicit Rebuild
continue through UAC. The existing `AdminIndexWorker` becomes a narrow client of
the service rather than a second index writer. The minimum local command channel
is an administrator-only named pipe (or an equivalently narrow local SCM control
mechanism if implementation proves strictly smaller) with these properties:

- explicit DACL allowing only LocalSystem and Builtin Administrators; deny
  network callers and reject remote clients rather than relying on the permissive
  default pipe descriptor;
- a small versioned, length-bounded request/response frame with deadlines;
- closed commands: register-and-build a stable volume identity, request rebuild,
  unregister without deleting index data, and query an operation accepted by the
  same elevated client;
- no arbitrary path, executable, database destination, command line, plugin, or
  generic RPC method;
- the service independently discovers/revalidates fixed NTFS mount and stable
  identity, derives protected paths, reloads its protected configuration, and
  acquires the existing storage lock before work;
- responses limited to accepted/busy/succeeded/rebuild-required/unavailable/
  rejected/error plus a bounded diagnostic and operation ID;
- malformed, oversized, unauthorized, stale, duplicate, or unknown requests fail
  without configuration or index mutation.

Microsoft documents that a named pipe's descriptor controls both ends and that
the default descriptor grants read access too broadly for this use:
[Named Pipe Security and Access Rights](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights).
The service does not impersonate an unelevated caller to perform privileged
work; authorization is the administrator-only endpoint plus independent
validation. If client-token inspection is used as defense in depth, failure must
be fail-closed.

## USN wait, idle, and lifecycle

The current `Timeout=0`, `BytesToWaitFor=0` call is a catch-up read, not evidence
that polling is necessary. Microsoft specifies that nonzero `BytesToWaitFor`
leaves `FSCTL_READ_USN_JOURNAL` outstanding at the end of the journal until new
data satisfies the request, while the zero value returns immediately:
[`READ_USN_JOURNAL_DATA_V1`](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-read_usn_journal_data_v1).

M20 should use this exact lifecycle per target:

1. On service start or target registration, discover and revalidate the volume,
   open/read the existing protected index status and checkpoint, query the
   current journal, and catch up immediately when continuity is valid.
2. After catch-up commits and returns the database to its quiescent readable
   state, keep no SQLite connection or transaction open. Issue one cancellable,
   overlapped USN read with nonzero `BytesToWaitFor` from the committed frontier.
   The wait is a signal; production Sync still rereads from the authoritative
   database checkpoint rather than trusting an uncommitted wait buffer.
3. On signal, cancel/close the wait as needed, run one bounded catch-up through
   the existing transactional batch path, publish health, and wait again from
   the newly committed checkpoint.
4. On service stop, configuration removal, or volume loss, cancel outstanding
   I/O and await loop completion. Do not abandon an in-flight SQLite transaction.
5. On Windows restart, service restart, or sleep/resume, re-query journal ID and
   readable range before consuming. Downtime is ordinary catch-up from the
   durable checkpoint and does not require a rescan while continuity holds.

Independent per-volume waits are asynchronous tasks, not permanent dedicated
threads. Actual index mutations are bounded inside the filesystem maintainer so
background work cannot create unbounded CPU/I/O concurrency or starve interactive
search. M20 needs only a small local bound, not a shared scheduler framework.

The M19 lab wait prototype did block at the end of the journal, but its process
had to be terminated during cleanup and it retained no reliable timing or
cancellation result. This is sufficient to keep the documented blocking read as
the implementation direction, but not to claim that Quail already has a usable
cancellable wait. Prompt cancellation, wake latency, and no-change CPU must be
measured on the actual M20 overlapped-I/O implementation.

## Continuity and recovery model

The authoritative invariant remains: every journal record before persisted
`next_usn` has been applied in the same SQLite history.

- Matching journal ID and a checkpoint at or after both `FirstUsn` and
  `LowestValidUsn`: catch up automatically, including after downtime.
- Volume temporarily absent, service stop, cancellable-wait cancellation, or a
  retryable open/query error: publish unavailable/retrying health, keep the last
  complete database and checkpoint unchanged, and retry with bounded backoff or
  on rediscovery. Do not label a transient outage as continuity loss.
- Journal ID mismatch, saved USN before the readable range,
  missing/incompatible checkpoint or derived state, malformed/unsupported
  required journal data, or another state where all intervening mutations cannot
  be proven: apply no uncertain mutation, set explicit `RebuildRequired` with a
  stable reason, stop that target's incremental loop, and remove it from active
  trusted search through existing status reevaluation.
- A failed batch transaction leaves its checkpoint unchanged; already committed
  earlier batches remain authoritative and may be resumed.
- M20 does not automatically full-rebuild after continuity loss. An explicit
  administrator-authorized Rebuild uses the existing staging/handoff path and
  replaces the old database only after a new complete checkpoint is valid. This
  is exceptional recovery, not routine freshness.

No rename/move/delete history is retained beyond current namespace state.

## Concurrent readers and writer

The intended relationship is unchanged in principle:

- Quick Search uses direct unelevated read-only SQLite snapshots;
- the service is the only writer and serializes mutation per protected volume;
- each catch-up uses WAL with `synchronous=FULL`, commits namespace/derived-state
  changes and checkpoint atomically, then checkpoints and returns to DELETE mode;
- a search already in flight sees its original snapshot; a later search sees the
  committed state;
- the existing 30-second bounded busy transition remains a failure boundary, not
  permission to kill readers or trust a half-finalized state.

M20 should reuse the existing search/sync and protected-storage concurrency tests
and add service-level representative overlap. It must not create multiple SQLite
writers or redesign the search engine.

## Focused threat analysis

| Threat or integrity failure | Required boundary |
| --- | --- |
| Unelevated caller supplies a privileged path | The ordinary App cannot reach the admin pipe; requests contain no database/executable path, and the service derives every protected path from validated identity |
| Mount point or volume swap | Treat mount as a hint only; discover fixed NTFS volumes and validate stable identity immediately before journal and storage work |
| Tampered LocalAppData catalog | Never use it as service authority; elevated registration and the service both revalidate identity, while protected machine configuration is service-written |
| Pipe squatting, unauthorized or remote caller | Service creates the first instance with an explicit SYSTEM/Administrators DACL and remote-client rejection; bounded framing and fail-closed authorization |
| ProgramData junction, symlink, sidecar, staging or lock redirection | Reuse `PrivilegedIndexStorage` handle retention, protected ACL validation, reparse checks and deterministic names for the new config/status objects as well |
| Writable service executable or configuration | Fixed quoted executable under canonical Program Files; verify Users cannot write binary/directory; protected service-owned config and restrictive service-object DACL |
| Concurrent writers or stale client state | One service writer, existing per-volume lock, generation/operation IDs, and service-side reload/revalidation; no client-supplied checkpoint |
| Crash or partial SQLite update | Existing FULL transactions and batch/checkpoint atomicity; staging publication for Build/Rebuild; restart from committed checkpoint |
| Silent stale index after journal gap | Fail closed to explicit `RebuildRequired`; never advance checkpoint or keep the source trusted when continuity is unprovable |
| LocalSystem used as updater/network broker | No updater, shell, arbitrary command, plugin, remote API, or network access in the service contract |

The dominant new high-impact path is replacement or command of the LocalSystem
service. The fixed Program Files ACL, service object DACL, protected machine
configuration, admin-only local control, and independent path/volume validation
are therefore M20 merge blockers, not optional hardening.

## `AdminIndexWorker` disposition

Retire its direct Build/Rebuild/Refresh writer role once the service path is
active. Retain only a narrow UAC bootstrap/recovery client for register/build,
explicit rebuild, and unregister requests to the administrator-only service
endpoint. It must keep the current narrow parser and same-account limitation,
must not contain a second implementation of index mutation, and must not be used
for routine Refresh.

This leaves one privileged writer and one exceptional authorization bridge.

## Installer implications

M20 adds only the service lifecycle required by the chosen boundary:

- ship one dedicated non-WinUI maintenance executable in the existing canonical
  `C:\Program Files\Quail` payload;
- register a uniquely named LocalSystem service with a quoted fixed binary path,
  Automatic (Delayed Start), non-interactive operation, and bounded SCM restart
  recovery;
- start it after a successful install; stop it before replacing its executable;
  stop and delete the service registration on uninstall before removing payload;
- preserve protected index databases, machine target configuration, and user
  catalog on uninstall, consistent with Quail's existing data-ownership policy;
- validate Program Files and service-object permissions and keep the service free
  of prerequisite download/update responsibilities.

The existing Inno Setup remains sufficient. M19 found no need for MSIX/MSI, an
updater, an alternate install root, or broad development-build migration.

## Recommended architecture

```text
unelevated Quail App
  -> LocalAppData user catalog / EnabledForSearch
  -> direct read-only search and health reads
  -> runas narrow AdminIndexWorker only for register/build/rebuild/unregister
       -> administrator-only local command

LocalSystem Quail maintenance service
  -> protected ProgramData machine target catalog and health
  -> validate current fixed NTFS volume identity
  -> cancellable per-volume USN wait
  -> sole IndexStore Build/Rebuild/Sync writer
  -> protected ProgramData databases and locks
```

This is the smallest candidate that satisfies automatic start, no routine UAC,
true no-change waiting, restart catch-up, one authoritative writer, protected
configuration ownership, and explicit Windows lifecycle/security controls.

## Proposed closed M20 contract

### Goal

Implement the selected service boundary so configured healthy local NTFS indexes
remain current without manual Refresh, while the interactive App remains
unelevated and journal uncertainty cannot produce a trusted stale index.

### In scope

- a dedicated LocalSystem Windows Service executable and SCM lifecycle;
- protected versioned machine target/health persistence under ProgramData;
- strict separation from the existing per-user search-preference catalog;
- admin-only bounded local control for register-and-build, explicit rebuild and
  unregister, with `AdminIndexWorker` reduced to that client;
- one cancellable asynchronous USN wait per configured volume, startup/restart/
  resume catch-up, bounded retries, and service stop cancellation;
- the existing transactional Sync and staged Build/Rebuild path adapted only as
  required for a long-lived sole writer;
- explicit healthy/catching-up/unavailable/error/rebuild-required health suitable
  for M21, with ordinary search requiring no IPC;
- focused installer registration/start/upgrade-stop/uninstall-delete behavior;
- deterministic tests, Quail-Lab runtime/security/restart/idle/Search+Sync
  evidence, and independent adversarial review of the final boundary.

### Explicit semantics

- machine configuration is authoritative for maintenance; user configuration is
  authoritative only for that user's search enablement;
- only the service mutates an index after migration to this boundary;
- every mutation independently revalidates stable volume identity and derives
  the database path;
- normal downtime/resume catches up automatically from the durable checkpoint;
- transient unavailability does not advance or invalidate the checkpoint;
- unprovable continuity sets `RebuildRequired` and requires explicit elevated
  rebuild; no automatic destructive recovery;
- idle means an outstanding cancellable USN wait, no aggressive timer loop, no
  open SQLite transaction, and practically zero CPU in the no-change case;
- interactive reads retain snapshot correctness while maintenance commits.

### Out of scope

M21 Settings redesign and launch-on-startup UI; generic multi-user ownership;
history/deleted retention; non-NTFS/removable/network sources; provider or
scheduler frameworks; updater behavior; remote API; dynamic plugins; arbitrary
privileged filesystem commands; automatic rebuild after continuity loss; search,
ranking, or index-format redesign not forced by a concrete implementation defect.

### Acceptance boundary

- install/start/stop/restart/uninstall service lifecycle works on Quail-Lab;
- standard-user journal/protected-write attempts fail and LocalSystem succeeds;
- CREATE/RENAME/MOVE/DELETE become searchable without manual Refresh or UAC;
- service and Windows restart catch up exactly from the committed checkpoint;
- no-change wait is prompt on change, cancellable, and practically idle;
- continuity-loss fixtures cannot leave a trusted stale source and enter the
  documented `RebuildRequired` state;
- transient unavailable/retry behavior preserves the last committed checkpoint;
- representative Quick Search overlaps Sync without corruption, incorrect
  ordering, or an unavailable protected database;
- App/user catalog and service/machine catalog ownership remain separate;
- unauthorized, malformed, remote, arbitrary-path, volume-mismatch, reparse,
  writable-binary/config, sidecar, and concurrent-writer probes fail closed;
- focused/full affected tests and Release builds pass; final implementation gets
  independent security/integrity review before merge recommendation.

### Stop conditions

Stop if implementation requires a broader service, generic IPC/background
framework, user impersonation/multi-user architecture, storage/index redesign,
weakened ProgramData protection, routine UAC, aggressive polling, destructive
journal manipulation without approval, or a material change to the 0.3 boundary.

## Remaining uncertainties for M20 implementation

- Select the smallest .NET service-hosting mechanism already compatible with the
  current package graph; do not choose it by framework preference.
- Tune only the small maintenance concurrency/backoff constants using the final
  service workload; no general scheduler is justified.
- Validate real sleep/resume on the production service implementation. Current
  semantics require re-query and catch-up, but M19 does not claim a final
  power-event implementation.
- Decide whether the admin-only pipe should keep a connection through long Build
  completion or return an accepted operation ID for status polling; either must
  preserve the bounded response model and existing UI operation ownership.

These are implementation choices inside the proposed boundary, not unresolved
architecture or integrity blockers.

## Verification

- `dotnet test tests\Quail.Core.Tests\Quail.Core.Tests.csproj -c Release --no-restore`:
  **237/237 PASS** on the Phase-1 branch.
- Current protected-storage, catalog, worker parsing, batch/checkpoint,
  rebuild-required, read-only search, and concurrent-reader tests remain PASS.
- Canonical preparation passed against an isolated VM clone at the same base:
  `milestone=M19`, host and VM
  `c62b59cfd56315e09daffc347f6d8e568ebedad0`, `checkpoint=M19-clean`
  (`created`), branch `codex/m19-continuous-maintenance-boundary` (`created`),
  VM data volume `QUAIL_LAB_DATA`.
- Quail-Lab administrator and SYSTEM probes both queried and read the D: NTFS
  journal. The SYSTEM probe also wrote the protected ProgramData test object;
  cleanup removed the one-shot task and probe object.
- A SYSTEM `Quail-M19-BootProbe` scheduled task started after an actual VM
  reboot at `2026-09-06T06:55:59.1861853Z`, stopped under control at
  `2026-09-06T06:56:15.4797129Z`, returned `LastResult=0`, and was unregistered.
  This proves boot-task feasibility, not the selected service implementation.
- Restart catch-up over a unique disposable root completed from the persisted
  checkpoint with `recordsApplied=10` and no rebuild.
- A bounded 600-file mutation run overlapped one Sync with four read-only Search
  processes. Sync was still running when searches launched; all searches
  completed without lock/error, then Sync committed `recordsApplied=1800`.
- A separate 12,000-change disposable run naturally exceeded the 1 MiB journal
  range and correctly returned `rebuild-required` with
  `saved-usn-before-readable-range`; concurrent Search rejected the non-current
  index. No journal delete/reset operation was used.
- The standard-user capability probe and cancellable no-change wait measurement
  are explicitly **UNRESOLVED**, as described above; they are M20 runtime
  acceptance work rather than hidden M19 passes.
- No full installer, UI, physical-host performance, or historical release
  campaign was repeated because M19 changes no production implementation.

Machine-specific transcripts remain ignored under `.quail-tooling/`; the
condensed lab report is ignored under `artifacts/m19/`. Cleanup confirmed no
owned M19 processes, tasks, temporary paths, or protected probe object remained.

## Decision requested

Approve or reject the recommended M20 boundary: a dedicated LocalSystem Windows
Service as sole writer, protected machine maintenance configuration separated
from per-user search preferences, administrator-only exceptional control through
the narrowed elevated worker, direct unelevated search without IPC, and
cancellable USN waiting with fail-closed continuity handling.

Until explicit approval, M19 remains at Phase 1 and no canonical `M20.md`, final
M19 PR, or production service/IPC code is created.
