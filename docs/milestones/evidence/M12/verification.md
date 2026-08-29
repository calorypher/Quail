# M12 verification

## Final branch-side gate

| Check | Result |
|---|---|
| Core/App logic tests | PASS — 147 passed, 0 failed |
| Release App build | PASS — 0 warnings, 0 errors |
| Self-contained publish and SQLite payload guard | PASS — 533 files, 240072474 bytes; all five required managed/native SQLite files present |
| Published executable | `artifacts\m12\publish\72b6d97\self-contained\Quail.exe`; SHA-256 `0A19F26C7AA28A2F5FABEF7BEFA9E649A141D95C9EB2EF436150B52787CB9287` |
| Published Core | `artifacts\m12\publish\72b6d97\self-contained\Quail.Core.dll`; SHA-256 `5163AF248B61B8B0C3107CE4DFD5B035331F218B70D208740C51ED225BD4BC95` |
| Lab ZIP | `artifacts\m12\publish\72b6d97\quail-m12-72b6d97-self-contained.zip`; 95877267 bytes; SHA-256 `1F9AE1DC8643E89AF0399879C59764D24FCDC2BEDC09D24E110F1701CD1AC8F6` |
| Exact executable source | Code commit `72b6d9704621d0576bb379685e729cc4fb7e1d88` (`Fix protected index read lifecycle`) |

The automated suite includes existing Core regressions and the M12 catalog, freshness, worker parsing, source selection, window lifecycle, and operation orchestration coverage. New SQLite regressions cover an actual Windows directory ACL that denies the reader child creation, final read-only status/search after Build and update, controlled failed update cleanup, a concurrent read connection during finalization, default Core persistent-WAL compatibility, and orphaned protected sidecar removal.

## Adversarial QA findings

| Finding | Deterministic verification | Result |
|---|---|---|
| 1. Privileged reparse safety | Protected ACL classification tests plus real elevated file-symlink, staging-symlink, Indexes-junction, root-junction, and retired-result-path-junction lab scenarios | PASS |
| 2. Transactional catalog | Injected Add failure; Disable/Remove failure; retry after failure; overlapping mutation serialization | PASS |
| 3. One operation per volume | App-owned coordinator rejects a second operation; real concurrent elevated Rebuild workers exit exactly `0,13` | PASS |
| 4. Search source generation | Slow-search invalidation on Disable, Remove, 1→0, 1→2, and Complete→RebuildRequired snapshots | PASS |
| 5. Rebuild enabled state | Initial Build auto-enable, enabled Rebuild preservation, disabled Rebuild preservation, and concurrent Disable revision guard | PASS |
| 6. Manage indexes restore guard | Injected `_restoreHotkey == false` keeps Settings open, reports the error, and blocks manager navigation | PASS |
| 7. Protected SQLite read lifecycle | Deterministic non-writable-reader reproduction, exact-commit Quail-Lab coverage, and final-publish physical-host Build/Refresh/zero-change status/search | PASS |

## Protected-storage mechanism

The narrow worker derives `%PROGRAMDATA%\Quail\Indexes\volume-<sha256-prefix>.db`; no caller-supplied destination exists. Elevated creation uses an inheritance-protected ACL owned by Administrators with full control for Administrators/SYSTEM and read/execute for Users. Runtime validation rejects any owner/ACL that grants non-admin/system write, delete, or ACL-control rights.

Every directory from the ProgramData parent through `Quail`, `Indexes`, and `Locks` is opened with `FILE_FLAG_OPEN_REPARSE_POINT` and inspected through `GetFileInformationByHandleEx`. The handles omit delete sharing and remain alive across `IndexStore.Build`/`Sync`, preventing parent substitution after validation. Existing database, rollback journal/WAL/SHM, `.building`, staging rollback journal/WAL/SHM, `.previous`, and lock objects are separately opened without following reparse points and rejected if tagged. Once these checks complete, medium-integrity Users have no directory write right with which to introduce a new child race. Worker result transport is the process exit code; there is no elevated result-file write.

## SQLite Error 14 root cause and fix

The adversarial storage fix correctly moved elevated output from user-controlled LocalAppData to protected `%PROGRAMDATA%\Quail\Indexes`, but the original `IndexStore.Open()` always selected WAL. After the final writer closed, SQLite could checkpoint and remove `-wal`/`-shm`. A later ordinary `Mode=ReadOnly` connection could not recreate those files in the Users-RX directory and returned `SQLITE_CANTOPEN`/Error 14. The pre-fix deterministic Windows ACL test reproduced both Incomplete status and failed search while proving the test runner could not create a child in the directory.

`IndexStoreJournalLifecycle.DeleteWhenQuiescent` is selected only by the protected M12 worker. It uses WAL with `synchronous=FULL` for the privileged mutation, then performs a bounded `wal_checkpoint(TRUNCATE)` and `journal_mode=DELETE` transition in `finally`. Build staging is finalized before promotion, existing final state is finalized before replacement, and validated rollback/WAL/SHM sidecars are cleaned with their database. Default Core/CLI callers remain `PersistentWal`. Read-only connections remain ordinary non-immutable SQLite connections, so Refresh, Rebuild replacement, and concurrent resident reads do not rely on immutable-file assumptions.

## Quail-Lab runtime and security

Canonical command shape:

```powershell
.\scripts\vm-verify.ps1 `
  -VmUser quailadmin `
  -ArtifactPath artifacts\m12\publish\72b6d97\quail-m12-72b6d97-self-contained.zip `
  -Scenario M12Runtime `
  -RemoteRoot C:/Temp/Quail-M12-72b6d97-final `
  -TimeoutSeconds 300
```

The helper used canonical VM state handling, dynamic IP discovery, SSH `HostKeyAlias`, the ignored project trust store, SHA-256 SCP verification, positive healthy-NTFS `QUAIL_LAB_DATA` identification, and bounded `Invoke-QuailRemotePowerShell` encoded payloads. Guest ExecutionPolicy remained unchanged; no `Bypass`, alternate transport, or trust workaround was used.

| Runtime check | Result |
|---|---|
| Artifact integrity | PASS — ZIP/executable hashes matched; scenario integrity summary `172d674eded6bc9bf570eeb4cb4641e2ec661c0a077c1410b4ec2d25fc784bc7` |
| Protected ACL | PASS — inheritance disabled; SYSTEM/Administrators full control; Users read/execute; medium-integrity child-create probe exit 5 and no child created |
| Real Build | PASS — final headless `Quail.exe`, managed ProgramData path, no WinUI/tray/hotkey |
| Build read after worker exit | PASS — verified medium-integrity standard-user status Complete and real search exit 0 |
| Refresh/search | PASS — controlled create/rename became searchable after worker exit through medium-integrity status/search |
| Zero-change Refresh/freshness | PASS — exit 0, Complete with advanced non-null `lastRefreshedUtc`, medium-integrity search PASS |
| Quiescent sidecars | PASS — after Build, Refresh, and zero-change Refresh: final DB present; `-journal`, `-wal`, `-shm`, `.building`, `.building-journal`, `.building-wal`, `.building-shm`, and `.previous` absent; protected lock present |
| Final DB symlink | PASS — worker exit 13; external target SHA-256 unchanged `436273B44FCF1814ADD8EEB50C4556126DE4FC1E0AF61A5E0C3DF53CF4AA8055` |
| Final rollback-journal symlink | PASS — worker exit 13; external target SHA-256 unchanged `0B7F20948D82412CA40ABDD22666CE0A6A0EE790C12999A9AD4AC6E4CF8E3C6B` |
| `.building` symlink | PASS — worker exit 13; external target SHA-256 unchanged `7E37E25D1DD11FF4B7310F5EB3883472C46B349090B06DA18AB52242600D8E94` |
| `.building-journal` symlink | PASS — worker exit 13; external target SHA-256 unchanged `797B1B424B3BF7E2E3521B9B74A09E9D1139FFF79A2A1D087F9F221E2875D687` |
| `Indexes` junction | PASS — worker exit 13; external sentinel SHA-256 unchanged `BAB16C09122D04549868DF8247CB9B1A16CBAB6B3C46E5941CC83B41D05383B4` |
| Protected-root junction | PASS — worker exit 13; external sentinel SHA-256 unchanged `E44E58056EB847EF8CC9725C9F7BA8D5BEF7F38FC09E9A4DA573D1F03FDE0E42` |
| Retired result-path junction | PASS — Refresh exit 0 and target remained empty |
| Concurrent same-volume workers | PASS — sorted exits `0,13`; final database Complete and post-concurrency medium-integrity status/search passed |
| Worker cleanup | PASS — no orphan Quail process, temporary standard-user account/profile/task removed, temporary batch-logon right removed |

The first junction run stopped because Windows PowerShell 5.1 requested confirmation from `Remove-Item` in NonInteractive mode. Read-only inspection proved that only the controlled `Indexes` junction and its exact backup remained. The junction was removed and the backup restored through the same canonical encoded-command transport. The helper now uses `File.Delete`/`Directory.Delete` for exact reparse objects, and the full clean rerun above passed.

## Physical and production regression state

Physical QA is PASS for Quick Search/hotkey, Settings visibility and deactivation/reactivation, Settings → Manage indexes, single manager, close/reopen, manager size, tray Exit with manager, Add `C:`, same-account UAC, real search, controlled Refresh, Disable/Enable, and restart/persistence. The protected-storage follow-up also passed close/reopen operation preservation, second-operation/UAC blocking, disabled action buttons, the existing non-modal tray-Exit refusal message, and elevated-worker cleanup. Those accepted lifecycle behaviors were not changed. The final physical-host retest used `artifacts\m12\publish\72b6d97\self-contained\Quail.exe` (SHA-256 `0A19F26C7AA28A2F5FABEF7BEFA9E649A141D95C9EB2EF436150B52787CB9287`) and passed same-account UAC Build/Rebuild to protected `%PROGRAMDATA%\Quail\Indexes`, `Ready` status and unelevated real search after worker exit, controlled Refresh with `quail-m12-refresh-probe-20260825.txt` becoming searchable, and a zero-change Refresh retaining `Ready`, the probe, and normal search. The `Pobrane`/`Downloads` observation is Windows shell localization, not a defect. Ranking remains deferred.

The earlier adversarial-publish short M10/M11 production regression passed in `artifacts\m12\m11-short-regression-adversarial-final-3`: real click-open, Enter-open, compact/expanded/compact, Settings Save/Cancel, 5/5 hotkey/focus, 20/20 summon/Escape, and 0% measured hidden-idle CPU/resource assessment `pass`. Settings opened at 700 × 500 logical pixels and returned to 700 × 80 after Save or 700 × 370 after Cancel with a query. It was not rerun for the headless protected SQLite lifecycle fix. The first attempt exposed only a stale harness expectation of 700 × 370 while Settings was open; the harness now distinguishes the accepted Settings host height from the normal expanded-search height. Mixed-DPI automation was unavailable in this run.

The bounded physical SQLite retest is complete. An independent focused re-review of `675d3ff1e8b921491692c44d6e6c78dbb762cdd3` → `72b6d9704621d0576bb379685e729cc4fb7e1d88` was `CLEAN` across the protected journal lifecycle, staging/promotion, Refresh finalization, bounded retry, sidecar cleanup, reparse/ACL invariants, worker lifecycle, default Core/CLI WAL regression, and new regression tests. No BLOCKER/HIGH/MEDIUM finding requires another code fix. Residual bounded risk: an unusually long-lived concurrent reader may produce the controlled 30-second journal-transition timeout; this is not a merge blocker and may be covered by a later M13 stress/lifecycle smoke.

Alternate-credential UAC with a different administrator account remains the documented 0.2 limitation. No product Service, cross-user broker, shared writable result directory, scheduled task, or ACL workaround was introduced. The lab harness uses a temporary scheduled task only to obtain a real medium-integrity standard-user reader in the headless VM; it removes the task, account, profile, and exact temporary batch-logon right after every read.
