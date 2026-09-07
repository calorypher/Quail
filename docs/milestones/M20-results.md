# M20 Results — Continuous Filesystem Maintenance

## Status

**IMPLEMENTATION AND PHASE B VERIFICATION COMPLETE — PR candidate ready for
independent adversarial review and final project QA.**

The implementation and its planned Quail-Lab campaign are complete and published
as PR #21. M20 is not merge-approved: independent adversarial review and
independent project QA remain external acceptance gates.

## Baseline and preparation

- Approved base: `5fc29ac589c8c19abd54131af122e0eac52dbb59`.
- Working branch: `codex/m20-continuous-filesystem-maintenance`.
- `scripts/prepare-milestone.ps1` passed its host, VM, main/upstream, same-HEAD,
  data-volume, branch, and checkpoint guards before creating `M20-clean` and the
  M20 branch.
- The reusable Quail-Lab clone was moved, without copying or deleting data, from
  `C:\Projects\Quail-M19` to the canonical `C:\Projekty\Quail`. The historical
  divergent `C:\Projects\Quail` clone was not modified.
- Before checkpoint creation, the physical VM volume had approximately 1.47 TB
  free. The VM directory occupied approximately 210.2 GB and its existing
  checkpoint chain was left unchanged.

## Implemented architecture

The Phase A implementation uses a dedicated framework-dependent
`Quail.MaintenanceService.exe` targeting `net10.0-windows`/x64. The executable
hosts the runtime directly through the smallest `ServiceBase` lifecycle and the
`System.ServiceProcess.ServiceController` package; it does not introduce the
.NET Generic Host or a reusable service framework.

The service:

- runs as LocalSystem and is the only code path with protected
  `IndexStore.Build`, `Rebuild`, or `Sync` authority;
- owns versioned, atomically replaced machine targets and health under
  `%PROGRAMDATA%\Quail`;
- derives protected database paths from canonical volume identity, rediscovers
  the current fixed NTFS volume, revalidates identity, and reuses
  `PrivilegedIndexStorage` reparse/ACL/sidecar/staging/lock checks;
- serializes protected target-configuration and index mutations through one
  process-wide semaphore, serializes health publication separately, and keeps
  independent per-volume asynchronous waits;
- catches up from the committed checkpoint, closes SQLite, then issues one
  overlapped `FSCTL_READ_USN_JOURNAL` wait with `BytesToWaitFor=1`;
- treats wait completion only as a signal and re-enters authoritative
  validation plus `Sync` after wake;
- retries transient unavailability with exponential backoff from 1 to 60
  seconds, using stable bounded health reasons;
- enters untrusted `RebuildRequired` and stops the target loop when journal ID,
  lower/upper bounds, schema, volume identity, derived state, or checkpoint
  continuity cannot be proved. It never automatically rebuilds after loss.

`AdminIndexWorker` is now only a UAC-authorized local control client. It accepts
an operation ID, a closed Build/Rebuild/Unregister enum, and a canonical volume
identity. It no longer accepts mount or database paths and has no direct writer
implementation. Per-user search preferences remain in
`%LOCALAPPDATA%\Quail\indexes.json`; the service never reads LocalSystem
LocalAppData as user configuration.

Ordinary search remains direct read-only SQLite access. A protected database is
eligible only when its normal index metadata is complete and compatible and the
matching protected machine-health record is current and trusted.

## Control boundary

The local control endpoint is the versioned byte-mode named pipe
`Quail.Maintenance.v1` with:

- an explicit `SYSTEM` plus Builtin Administrators DACL;
- `PIPE_REJECT_REMOTE_CLIENTS`;
- fail-closed caller-token inspection under pipe impersonation, allowing
  LocalSystem or a local administrator and rejecting a network token;
- a 16 KiB maximum frame, strict JSON property handling, a ten-second
  connection deadline, five-minute request freshness, bounded diagnostics, and
  bounded replay history;
- only register-and-build, explicit rebuild, unregister, and operation-status
  commands.

No request can carry a mount path, database path, executable, command line,
plugin, or arbitrary operation.

## Installer work

The Inno Setup payload now includes the service executable and its focused
runtime dependencies. Installation or same-version replacement performs a
bounded service stop, writes the fixed Program Files payload, configures an
exactly quoted image path, LocalSystem delayed automatic start, restart recovery,
and an explicit SCM DACL, then starts the service. Uninstall stops and removes
the registration while preserving ProgramData indexes and maintenance state.

The final Phase A installer was built from core implementation commit
`119200ac54f205464ee24c468a8bccb9621d82a6`:

- payload files: 66;
- payload bytes: 45,420,782;
- installer bytes: 10,198,908;
- SHA-256: `72731f5b4a68359f41516b359b4f19f73239ea7dcf3893249f1a59d21e94a3f7`.

## Deterministic and build verification

- `dotnet test Quail.sln -c Release --no-restore`: PASS, 250 Core tests and 4
  service-host tests, zero failures.
- M20 focused boundary tests: PASS, covering continuity journal ID, lower and
  upper bounds, inconsistent state, protected writer rejection, machine-state
  round-trip/atomic replacement, malformed/oversized/stale/duplicate requests,
  arbitrary-path rejection, local authorization policy, and transient journal
  classification.
- USN wait tests: PASS, covering the committed frontier, nonzero wait threshold,
  signal-only completion, cancellation, abort/error mapping, and preservation of
  native resources until completion even when cancellation itself fails.
- Service lifecycle tests: PASS, 4/4, covering start/cancellation composition,
  startup and background failure propagation, and bounded non-cooperative stop.
- `dotnet build Quail.sln -c Release --no-restore`: PASS with zero warnings and
  zero errors.
- `scripts/build-installer.ps1`: PASS with release/XAML provenance and required
  payload checks.

## Quail-Lab Phase A evidence

The final installer ran successfully in Quail-Lab and the installed
`Quail.FileSystem.dll` hash matched the final staged payload. The service was
`Running` as `LocalSystem` with the exact quoted image path
`"C:\Program Files\Quail\Quail.MaintenanceService.exe"`.

The service DACL grants full service control only to SYSTEM and Builtin
Administrators; Authenticated Users receive query/interrogate rights only. The
service executable and `%PROGRAMDATA%\Quail` were read-only for ordinary users
and writable only by SYSTEM/Administrators.

A local System-token control request was accepted as operation
`a22e0fb5-2bae-421e-bb4a-a410f5a98918`. The service independently resolved
`QUAIL_LAB_DATA`, saved generation 1 machine configuration, built the protected
index, and published `Healthy`, `TrustedForSearch=true` with a real journal
checkpoint.

A separate local high-integrity `Quail-Lab\quailadmin` S4U token, with Builtin
Administrators enabled and no NETWORK group, sent a non-mutating
`GetOperationStatus` request. The service logged `control-authorized
administrator`, reached the closed handler, and returned the expected
`unknown-operation` response. The ordinary SSH network token remained rejected.

One file creation on disposable `QUAIL_LAB_DATA` woke the outstanding wait and
advanced the committed checkpoint from `26592000` to `26592520`. A direct
`Quail.Cli.exe search --index ... m20wake-7f43c51a` returned exactly the created
file with exit code 0, demonstrating that ordinary search did not use control
IPC. Removal of that exact smoke file produced a second healthy catch-up to
`26592728`; the disposable directory and temporary tasks/processes were then
removed.

A controlled stop while the service was idle in the USN wait completed in 291
ms with no remaining service process. Restart returned to `Running`, `Healthy`,
and trusted at the same durable checkpoint. A final same-version installer run
preserved generation 1 state and the healthy checkpoint.

## Security self-review

The Phase A review covered service replacement and quoting, Program Files and
SCM permissions, machine-state ACL/reparse/staging/sidecar handling, pipe ACL and
remote rejection, caller-token authorization, arbitrary path and volume-swap
rejection, client-supplied state, duplicate writer paths, staged publication,
future/below-range checkpoints, configuration mutation races, cancellation
lifetime, and service stop during an outstanding wait.

The review found and corrected:

- an incorrectly quoted `sc create` image path by replacing that creation path
  with direct SCM APIs and an exact quoted binary path;
- a pipe impersonation-level mismatch that made LocalSystem authorization fail
  closed; the client now requests impersonation and the server uses native
  membership checks without a managed identity-loader dependency;
- concurrent read-modify-write risk between control operations by moving the
  full protected mutation into the single writer bound;
- premature overlapped-resource release if `CancelIoEx` itself failed;
- raw transient exception text in machine health and uncaught client timeout
  cancellation.

No unresolved credible privilege-escalation, service-replacement, arbitrary-path,
second-writer, speculative-continuity-repair, or partial-publication blocker was
found for the Phase A integration gate.

## Independent-review correction pass

Independent review of Phase A commit
`6e2a2dc58dfa0722dd45df203efc140d33740193` found two integration blockers.
They were corrected in implementation commit
`5313b2ce61b43df56e96b9ab7d11cbf35ce21002` without starting Phase B.

Protected health is now re-read on demand before normal Search evaluates source
availability and again before a filesystem source opens its databases. A
trusted-to-untrusted transition removes the database from `ActivePaths` and
raises the existing `ActivePathsChanged` event, preserving the established
`SearchRuntime` generation invalidation. The reverse transition adds the source
on the next normal Search. There is no timer, watcher, service IPC, or polling
loop. The public machine-health API is read-only; target and health mutation
methods are internal to the trusted service/friend boundary, while the protected
directory SDDL continues to grant Builtin Users only generic read and execute.

`MaintenanceServiceLifecycle` now observes the runtime task continuously after
`OnStart`. Unexpected successful completion, cancellation without a stop
request, or a fault claims exactly one terminal failure path. The production
`ServiceBase` wrapper calls `Environment.FailFast` with the original failure so
the process cannot remain registered as Running after maintenance has stopped
and SCM recovery can act. An intentional `OnStop` marks the lifecycle as
stopping before cancellation, retains the bounded timeout, and cannot race into
the crash callback or dispose the cancellation source twice.

The existing Index Manager `Remove` wording means removal of the index
configuration, not merely disabling Search. It now performs the bounded,
administrator-authorized service `Unregister` first and removes the per-user
catalog entry only after success. `Unregister` is idempotent when the machine
target is already absent and never deletes the database. `Disable` remains only
a per-user `EnabledForSearch` preference and does not contact the service.

Final correction verification:

- focused catalog/source-generation, ACL/API, and operation-coordination tests:
  PASS, 17 tests;
- service lifecycle tests: PASS, 5 tests, covering post-start fault, unexpected
  completion, normal stop cancellation, bounded non-cooperative stop, startup
  failure, idempotent stop, and exactly one terminal callback;
- `dotnet test Quail.sln -c Release --no-restore`: PASS, 258 Core tests and 5
  service-host tests, zero failures;
- `dotnet build Quail.sln -c Release --no-restore`: PASS, zero warnings and zero
  errors;
- `git diff --check`: PASS.

A focused Quail-Lab single-process catalog/direct-Search probe kept one loaded
controller alive across protected health transitions. With one real disposable
indexed file it observed `Healthy/trusted -> Retrying/untrusted ->
Healthy/trusted` as active-path/result counts `1/1 -> 0/0 -> 1/1`. This was not
a GUI smoke because the VM had no active interactive desktop session. The S4U
task requested as Limited still received an administrator token, so it is not
claimed as standard-user evidence; the actual ordinary-user runtime probe
remains in Phase B. Health was atomically restored, the disposable file was
removed, and the production service remained Running and Healthy.

A separate temporary SCM probe used the production lifecycle and
`ServiceBase` wrapper under the real Quail service name. It reached Running as
PID 1320, faulted its runtime two seconds after start, generated Service Control
Manager event 7031, and was restarted by SCM as PID 4152. The probe then stayed
Running, proving that the first process did not remain inert. The temporary
registration, process, marker, task, and data were removed, and the production
service was restored by the installer.

The final package was rebuilt from implementation commit
`5313b2ce61b43df56e96b9ab7d11cbf35ce21002`:

- payload files: 66;
- payload bytes: 45,423,398;
- installer bytes: 10,199,733;
- installer SHA-256:
  `d92c53173b812f0c92f6fce6a972cc0015456803aa5424a6f1d4c33b4e7651e1`.

The final package was installed in Quail-Lab. The installed service and
filesystem DLL hashes matched the staged payload
(`8bbcf0624ae1c2b8610b9fd34ee36151be49fef9215d298056ba6816dc4f1c60`
and `aacecbef3f299dd636a6bc8454a0364848a752c65351c8d2800674296f09693f`).
The restored production service was Running as LocalSystem from the exact
quoted Program Files path, with `Healthy`, `TrustedForSearch=true`, its bounded
5-second/30-second recovery actions, no probe process/task/marker, no disposable
test file, and a clean VM repository at the approved base.

## Phase B implementation delta

Phase B removed the obsolete time-based freshness notice. A complete index is no
longer classified as `RefreshRecommended` merely because its last maintenance
timestamp is more than 24 hours old; protected maintenance health is the
authoritative freshness and trust state, and the UI now describes the timestamp
as last maintained.

Service runtime supervision was tightened before the final campaign. A target
loop fault now terminates the aggregate runtime, while controlled service stop
publishes `Unavailable`, untrusted health and preserves the last successful
checkpoint. Unexpected aggregate completion or failure remains visible to the
`ServiceBase` terminal-failure path and SCM recovery rather than leaving an inert
service reported as Running.

The representative Search/Sync overlap campaign exposed a concrete in-scope
correctness defect. A sustained clustered create burst exhausted the existing
128-leaf short-query relabel window, and transaction-application exceptions were
misreported as journal read/parse failures. Commit
`5ace81353bfdbb6488ace8b4b9cf214c79098052` corrects this without changing the
storage design:

- transactional batch-application failures are classified separately from
  native journal read failures and keep the committed checkpoint unchanged;
- bounded diagnostics expose only stable stage/type/error identifiers;
- CLOSE-only and unrelated USN records advance the batch checkpoint but do not
  reapply stale namespace state;
- clustered short-query relabel recovery remains bounded to one existing
  1,024-entry persisted chunk.

The final candidate recovered the previously retained valid backlog from its
committed checkpoint without rebuilding and returned to `Healthy`/trusted. A
500-file create/delete regression advanced the checkpoint from `27192328` to
`27213280` and then `27387688`, remained healthy, and did not enter
`RebuildRequired`.

## Phase B Windows lifecycle and continuity evidence

- Controlled stop completed in 594.3 ms in the lifecycle campaign, preserved
  checkpoint `26594800`, and published `Unavailable`/untrusted. Representative
  restarts replaced the service process and returned to `Healthy`/trusted.
- A real post-start production-runtime failure was induced through the protected
  health-read path while maintenance was active. PID 5928 left Running, SCM
  recorded event 7031, recovery started PID 4684, and the service resumed healthy
  maintenance from checkpoint `26594800` to `26595136`.
- A Windows restart test stopped maintenance, retained checkpoint `26598976`,
  created a disposable change during downtime, and rebooted the guest. Automatic
  Delayed Start correctly required longer than the first 60-second observation;
  without another reboot or state repair, the bounded follow-up observed PID
  4260, `Healthy`/trusted at checkpoint `26600192`, and Search returned the
  downtime file.
- `powercfg /a` showed that this Hyper-V Generation 2 guest exposes no S1, S2,
  S3, hibernation, S0 low-power idle, hybrid sleep, or fast-startup path. One
  capability attempt was therefore recorded as an environment limitation; VM
  save/restore was not substituted as false sleep/resume evidence.
- A non-destructive future-checkpoint fixture changed only the disposable index
  database while the service was stopped. Checkpoint `26601128` was set to
  `1026601128`; startup deterministically published untrusted
  `RebuildRequired` with `saved-usn-after-journal-frontier`, Search failed closed,
  and the database hash stayed unchanged over three seconds, proving no automatic
  rebuild. An explicit administrator Rebuild returned 0 and restored
  `Healthy`/trusted at `26601440`.
- Journal-ID mismatch, checkpoint below the readable lower range, upper-bound
  violation, and inconsistent checkpoint relations also pass deterministic
  focused tests. No test deleted or reset the USN journal.
- Taking only the disposable `QUAIL_LAB_DATA` disk offline published
  `Retrying`/untrusted with `maintenance-unavailable` while preserving checkpoint
  `26603744`. Bringing it online rediscovered the same volume and returned to
  `Healthy`/trusted at `26605056`.

## Phase B security and control evidence

A temporary real local standard user ran from a password-backed scheduled-task
logon at medium integrity. Its token contained neither Builtin Administrators nor
NETWORK. The account received native error 5 when opening the raw volume/USN
capability, could not write protected ProgramData, could not connect to the
administrator control pipe, and received worker exit 10 for a mutating request.
It could read protected machine health and perform direct read-only Search of a
trusted protected database. Final package checks additionally denied write-open
of the Program Files binary and machine-target file and denied `sc config`; all
tested hashes and service configuration remained unchanged. The task and account
were removed.

The bounded control round trip used the production administrator client contract:

- `Unregister` returned 0, removed the machine target, left the protected database
  present with unchanged SHA-256, and did not alter the per-user catalog;
- `RegisterAndBuild` returned 0, restored `Healthy`/trusted maintenance, indexed a
  disposable marker, and again left the user catalog unchanged;
- removal of that marker was caught up normally. The user's pre-existing
  `EnabledForSearch=false` preference stayed false throughout, confirming that
  Disable is not machine Unregister.

Focused coordinator tests cover the actual App ordering: successful initial Build
enables the user entry, Disable/Enable require no administrator operation,
successful Remove performs administrator Unregister before catalog removal, and
canceled or failed elevation preserves the catalog. Quail-Lab had no interactive
desktop session, so the visual UAC prompt and window presentation remain a
user-owned manual smoke; the privilege/control transitions themselves have real
runtime evidence.

The final bounded adversarial evidence combines the real standard-user, volume,
continuity, recovery, and reader/writer cases above with the accepted Phase A
focused coverage for arbitrary paths, volume identity substitution, protected
config/health and reparse/sidecar/staging/lock redirection, malformed/oversized/
stale/duplicate framing, remote/network rejection, and the sole-writer lock.
No unresolved privilege escalation, protected-storage integrity, second-writer,
or speculative-recovery defect was found.

## Phase B search, concurrency, and resources

CREATE, RENAME, MOVE, and DELETE on disposable `QUAIL_LAB_DATA` all became visible
or absent in Search without manual Refresh, service restart, or UAC. Observed
latencies were 123.8 ms for create; 84.4/96.9 ms for rename old/new; 84.0/95.1 ms
for move old/new; and 86.0 ms for delete.

The final Search/Sync overlap ran 39 direct read-only Search calls while producing
500 files. It had zero search failures; the checkpoint advanced from `27387688`
through an observed `27408352` to `27534176`, the final file was searchable, and
cleanup advanced to `27583032` with the result absent and health still trusted.

A focused same-process paired measurement covered the exact
`FileSystemSearchComposition -> GetActivePathsForSearch -> protected health and
status -> filesystem Search` path. Across 80 warm pairs, the explicit-path
baseline median/p95 was 0.508/0.623 ms and the health-revalidated path was
1.730/2.193 ms. The 1.223 ms median delta is small in absolute terms and did not
justify further performance work.

The final installed candidate spent 30.00 seconds in a real no-change USN wait
with 0 ms CPU delta (0% across four logical processors), 43,909,120 bytes working
set, and 12,263,424 bytes private memory. Checkpoint and health timestamp stayed
unchanged. Windows Restart Manager reported no process holding the protected
SQLite database, and specifically no service database handle. A final wake made
a disposable file searchable and its deletion absent; controlled stop completed
in 278.6 ms, published `Unavailable`/untrusted with `service-stopped`, and restart
returned under a new PID to `Running`, `Healthy`, and trusted.

## Phase B installer and final automated verification

Same-version replacement stopped the service before the fixed Program Files file
phase, completed successfully, replaced the process, and preserved protected
state. A separate uninstall removed the running service registration and payload
while preserving machine targets, protected database, health/checkpoint, and the
per-user catalog. Reinstall restored the exact quoted Program Files path,
LocalSystem Automatic (Delayed Start), 5-second/30-second recovery actions, the
SCM DACL, and healthy maintenance. The lab was returned to the installed state
required for final QA.

Final verification after the last code change:

- focused incremental/M20 boundary tests: PASS, 46/46;
- service lifecycle/composition tests: PASS, 10/10;
- `dotnet test Quail.sln -c Release --no-restore`: PASS, 261 Core tests and 10
  service tests, zero failures;
- `dotnet build Quail.sln -c Release --no-restore`: PASS, zero warnings and zero
  errors;
- `git diff --check`: PASS;
- `scripts/build-installer.ps1`: PASS, release/XAML provenance, pinned
  prerequisites, and payload validation.

Final package from the verified candidate:

- payload files: 66;
- payload bytes: 45,430,034;
- installer bytes: 10,201,656;
- staged/installed service DLL SHA-256:
  `30de37bb23857ede138035934cd782efdbac0554619007d5c30095c794690a0c`;
- staged/installed filesystem DLL SHA-256:
  `c79d0b3e2f94332100ad3cf8a63966b8a3f9c4414709d4c7d42987c94631131c`;
- installer SHA-256:
  `6e9b7794c6050018432af5f18690592756c3136817b6ffbd586b5386cd8e7a52`.

## Remaining external gates

The final cleanup audit found no temporary standard users, scheduled tasks,
probe processes, or `D:\m20-*` artifacts. It removed only
`C:\QuailLab\M20-PhaseB`; the service remained Running as LocalSystem,
`Healthy`/trusted at checkpoint `27584048`, the protected database remained
present, and the canonical VM repository was clean at
`5fc29ac589c8c19abd54131af122e0eac52dbb59`. Hyper-V checkpoints were not
modified.

Independent adversarial review and independent final project QA must evaluate the
published PR before any merge recommendation. The only user-owned product check
is the visual interactive UAC/window smoke noted above. The sleep/resume case is
an explicit Quail-Lab environment limitation, not a product PASS. No M21 work is
included.
