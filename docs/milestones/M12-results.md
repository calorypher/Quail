# M12 results

## Status

**M12 verification complete; ready for merge pending explicit user approval.**

PR #14 contains persistent index management, freshness reporting, the physical-QA lifecycle fixes, the protected-storage adversarial fixes, and the follow-up SQLite read-lifecycle fix. The exact protected-storage read path has now passed the bounded physical-host retest on the final `72b6d97` publish.

## Implemented product slice

- `%LOCALAPPDATA%\Quail\indexes.json` is a separate versioned, inspectable, atomically written catalog keyed by stable volume identity.
- Managed database names are SHA-256-derived from the stable identity. Privileged databases now live beneath `%PROGRAMDATA%\Quail\Indexes`; pre-release M12 catalog entries using the earlier LocalAppData development path are normalized on load, but their database files are not adopted or migrated.
- Discovery is limited to ready fixed NTFS drives and validates candidates through `NtfsVolume.Validate`.
- Normal startup uses enabled, current-mount-compatible, complete catalog entries. Repeated `--index` remains an exclusive developer/test override.
- `Quail Indexes` is a single normal window opened from Settings. It supports add, build/rebuild, refresh, enable/disable, and non-destructive remove.
- The same final `Quail.exe` recognizes a narrow internal worker before WinUI, single-instance, tray, or hotkey startup. It validates elevation, the catalog entry, current volume identity, protected destination, and per-volume operation lock.
- `last_refreshed_utc` is optional Core metadata. Build/rebuild and every successful sync, including zero-change sync, update it; failed or rebuild-required sync does not. Complete indexes remain searchable with `Refresh recommended` at 24 hours or with an unknown-freshness notice when the value is absent.

## Final adversarial QA fixes

### Protected privileged storage

The elevated worker no longer writes a database or result JSON beneath a medium-integrity-writable LocalAppData directory. It creates `%PROGRAMDATA%\Quail`, `Indexes`, and `Locks` with protected ACLs owned by Administrators/SYSTEM; only Administrators and SYSTEM receive write rights, while Users receive read/execute. It opens the ProgramData parent and every protected directory with `FILE_FLAG_OPEN_REPARSE_POINT`, verifies the opened handle is not a reparse point, and retains non-delete-shared handles for the entire Core operation. It also rejects existing reparse points at the final database, SQLite rollback journal/WAL/SHM, `.building`, all staging journal sidecars, `.previous`, and lock paths. Because the protected parent cannot be renamed while held and a medium-integrity user cannot create or replace children, the later SQLite path opens cannot be redirected by a check/use race.

Worker results use a narrow process exit code. After a successful worker exit, the unelevated App reads normal Core status from the deterministic database path. The removed GUID result-file transport therefore offers no elevated write target in user-controlled storage.

### Protected SQLite read lifecycle

The first protected-storage physical run exposed a second, independent defect. `IndexStore.Open()` left the database header in WAL mode. Once the elevated worker closed its last connection, SQLite could checkpoint and remove `-wal`/`-shm`. A later ordinary read-only connection then needed existing readable WAL sidecars or permission to create them, but Users intentionally have only read/execute on `%PROGRAMDATA%\Quail\Indexes`. A deterministic Windows ACL regression reproduced the physical result exactly: status became Incomplete and search threw SQLite Error 14 while a direct child-create probe was denied.

The fix is scoped to the M12 protected worker. Default Core/0.1 callers retain the existing persistent-WAL behavior. Protected Build/Rebuild/Refresh enters WAL while the privileged mutation is active, uses full synchronous transactions, checkpoints with `TRUNCATE`, and switches the database to `journal_mode=DELETE` before successful worker exit. The transition retries bounded busy/locked results so an existing reader may finish. Build staging is finalized before atomic promotion, Refresh finalizes in `finally`, and validated orphaned final/staging journal sidecars are removed during protected Build preparation. `immutable=1` is not used.

The quiescent contract is therefore a normal read-only SQLite database in DELETE mode with no required `-journal`, `-wal`, or `-shm`. During a privileged mutation, WAL sidecars remain inside the same protected directory and are covered by the same fail-closed reparse validation. The ProgramData ACL and process model are unchanged.

### Transactional catalog

Catalog mutations use one async mutation gate. Each candidate is derived from the committed document, atomically persisted, and only then published as `_catalog` and its matching active-path snapshot. Add, enable/disable, and remove persistence failures leave disk, `Entries`, and `ActivePaths` unchanged. A later retry starts from the committed generation, and overlapping mutations serialize without losing updates. Manager event handlers convert persistence exceptions into normal UI detail instead of allowing an `async void` exception to escape.

### Operation ownership and concurrency

`IndexOperationCoordinator` belongs to the resident App rather than an Index Manager instance. Closing the manager does not cancel or forget an operation; reopening displays the running operation and disables conflicting actions. Completion reevaluates catalog/search state even when no manager exists. Tray Exit is deterministically blocked with a message while any operation is running. At worker level, a protected per-volume lock file opened with `FileShare.None` rejects a second build/rebuild/refresh before either worker touches the database or staging file.

### Search source generation

An active-path change synchronously invalidates `LatestFileSearchCoordinator` before dispatching UI work. A visible query is rerun on the new immutable snapshot; hidden state is cleared. Disable, remove, current-volume loss, Complete-to-RebuildRequired, one-to-zero, and one-to-two source changes can no longer allow an older completion to publish results from a stale source generation.

### Rebuild enabled state

Only an initial `Build` may auto-enable an index, and its completion is guarded by the catalog entry revision captured at operation start. `Rebuild` never changes the explicit enabled flag. Enabled rebuild stays enabled, disabled rebuild stays disabled, and a disable committed during a long operation is not overwritten by completion.

### Manage indexes hotkey guard

`Manage indexes…` now uses the same guarded hotkey restoration as Cancel. If restoration fails, Settings remains visible, the manager is not opened, and the dialog displays the restoration error. A successful restore retains the established Settings-close-then-manager lifecycle.

## Automated and build verification

- Final suite: 147 passed, 0 failed.
- Release App build: passed with 0 warnings and 0 errors.
- Guarded self-contained publish: 533 files, 240072474 bytes; SQLite payload guard passed.
- Published `Quail.exe` SHA-256: `0A19F26C7AA28A2F5FABEF7BEFA9E649A141D95C9EB2EF436150B52787CB9287`.
- Published `Quail.Core.dll` SHA-256: `5163AF248B61B8B0C3107CE4DFD5B035331F218B70D208740C51ED225BD4BC95`.
- Published ZIP: `artifacts\m12\publish\72b6d97\quail-m12-72b6d97-self-contained.zip`; 95877267 bytes; SHA-256 `1F9AE1DC8643E89AF0399879C59764D24FCDC2BEDC09D24E110F1701CD1AC8F6`.
- The executable payload corresponds exactly to code commit `72b6d9704621d0576bb379685e729cc4fb7e1d88` (`Fix protected index read lifecycle`).
- The earlier adversarial-publish short production harness remains PASS evidence for unchanged UI behavior: real mouse-open, Enter-open, Settings Save/Cancel with the 700 × 500 Settings host, 5/5 hotkey/focus, 20/20 summon/Escape, compact/expanded restoration, and 0% measured hidden-idle CPU. It was not rerun for this headless protected SQLite lifecycle change. Mixed-DPI coverage was unavailable on that single-DPI desktop run.

## Quail-Lab security and concurrency evidence

Canonical `scripts\vm-verify.ps1 -Scenario M12Runtime` passed on healthy NTFS `QUAIL_LAB_DATA` (`D:`) using the exact code-commit ZIP above. Artifact transfer used the existing SCP/hash, HostKeyAlias, ignored trust store, volume checks, and `Invoke-QuailRemotePowerShell` encoded-command path. Guest ExecutionPolicy was not changed and no bypass was used. The scenario integrity summary was `172d674eded6bc9bf570eeb4cb4641e2ec661c0a077c1410b4ec2d25fc784bc7`.

- Real Build, controlled create/rename Refresh, and zero-change Refresh passed. After each worker exit, a temporary standard-user process was verified at medium integrity, failed a protected child-create probe with exit 5, reported Complete status, and returned the expected real search result.
- Sidecar observation showed Build staging using `.building-journal`/`.building-wal`/`.building-shm` as applicable and Refresh using final rollback/WAL sidecars while active. After every successful worker exit, only the final `.db` and protected lock remained; final/staging journal, WAL, SHM, `.building`, and `.previous` objects were absent.
- Final DB, final `-journal`, `.building`, and `.building-journal` file symlinks plus `Indexes` and protected-root junctions were rejected before index work with storage exit code 13.
- All external sentinel SHA-256 values were identical before and after each attempted privileged operation.
- A junction at the removed legacy `%LOCALAPPDATA%\Quail\AdminOperations` result location remained empty while Refresh succeeded through exit-code transport.
- Two concurrent Rebuild workers for the same volume exited exactly `0,13`; final status and medium-integrity search remained valid, and no Quail worker process remained.

The first split-payload run exposed a PowerShell 5.1 prompt when `Remove-Item` cleaned a test junction in NonInteractive mode. The exact controlled junction and backup were inspected, the real directory was restored, and the helper now removes file links through `File.Delete` and directory links through `Directory.Delete`. The subsequent complete scenario passed. This was a lab cleanup defect, not a product bypass.

## Physical-host QA

PASS before the adversarial round:

- Quick Search and global hotkey;
- Settings visibility, deactivation/reactivation, Save/Cancel, and Settings → Manage indexes;
- single manager, manager close/reopen, sensible initial size, and tray Exit with a manager;
- Add `C:`, same-account UAC Build, real file/folder search, controlled-file Refresh, disable/enable without restart, and restart with a persistent catalog.

The later physical protected-storage run confirmed that closing/reopening Index Manager preserves the running operation, a second operation/UAC cannot start, all action buttons remain disabled, tray Exit is refused by showing/activating Index Manager with `Finish the running index operation before exiting Quail.`, and the elevated worker exits while the resident unelevated process remains. Those lifecycle behaviors are accepted and were not changed.

The final physical-host retest used exactly `artifacts\m12\publish\72b6d97\self-contained\Quail.exe` with SHA-256 `0A19F26C7AA28A2F5FABEF7BEFA9E649A141D95C9EB2EF436150B52787CB9287`, produced from code commit `72b6d9704621d0576bb379685e729cc4fb7e1d88`.

- Same-account UAC Build/Rebuild to protected `%PROGRAMDATA%\Quail\Indexes` completed successfully; after worker exit Index Manager showed `Ready`, not the earlier `Incomplete. SQLite Error 14: 'unable to open database file'.`
- Unelevated Quick Search worked after worker exit and returned real results.
- The controlled file `quail-m12-refresh-probe-20260825.txt` was absent from search before Refresh, appeared after explicit Refresh plus same-account UAC, and was visible as the correct result.
- A subsequent zero-change Refresh kept status `Ready`; the probe and normal search remained available.

This confirms the physical protected-storage read lifecycle on the exact publish. An independent focused review of `675d3ff1e8b921491692c44d6e6c78dbb762cdd3` → `72b6d9704621d0576bb379685e729cc4fb7e1d88` was `CLEAN`; no BLOCKER/HIGH/MEDIUM finding requires another code fix. The residual bounded risk is a controlled 30-second timeout if a concurrent SQLite reader remains active unusually long during the privileged journal transition; this is not a merge blocker and may be covered by a later M13 stress/lifecycle smoke.

Explorer's localized `Pobrane` display name maps to the actual NTFS name `Downloads`; this is not an indexing defect. Ranking remains deliberately deferred.

Same-account administrator elevation remains the supported flow. Alternate-credential UAC with another administrator account remains an M12/0.2 limitation; no service, cross-user broker, shared writable result path, or ACL workaround was added.

## Deferred

The Quick Search visual-polish follow-up remains unchanged: compact-window border removal, compact-to-expanded animation, recents, pinned results, ranking, and general redesign are outside M12.
