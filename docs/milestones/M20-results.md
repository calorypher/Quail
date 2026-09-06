# M20 Results — Continuous Filesystem Maintenance

## Status

**PHASE A INTEGRATION GATE — awaiting independent security/integration review.**

M20 is not complete and is not ready for final QA or merge. Phase B runtime,
recovery, adversarial, resource, and installer campaigns have deliberately not
started.

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

## Remaining Phase B work

Independent review must happen before Phase B. Phase B still needs the planned
multi-restart and recovery variants, Windows restart and sleep/resume catch-up,
final no-change idle CPU measurement, the complete interactive UAC/App flow and
working standard-user negative probes, the full bounded adversarial matrix,
continuity-loss runtime cases without destructive journal reset, and final
installer upgrade/uninstall verification. No final M20 pull request is opened at
this gate.
