# M15 Results — Core / FileSystem Boundary Extraction

## Outcome

M15 extracted the existing NTFS/filesystem implementation into the new production
`Quail.FileSystem` assembly without introducing a provider SDK, plugin framework,
storage redesign, or search/ranking change.

The resulting production dependency graph is:

```text
Quail.App -> Quail.Core -> Quail.FileSystem -> Microsoft.Data.Sqlite
Quail.App -> Quail.FileSystem (filesystem administration only)
Quail.Cli -> Quail.FileSystem
```

`Quail.Core` now exposes the minimal current search boundary: request, GUI-usable
result data, index summary/status, and an opaque `SearchResultAction`. Native file
identity and the database path remain in `Quail.FileSystem`; Core resolves the
opaque action internally when opening a selected result.

The normal Quick Search presentation no longer carries `IndexStore`,
`NativeFileId`, filesystem result wrappers, or a database-path source identity.
`Quail.App` retains thin search/presentation adapters. Its direct FileSystem use
is limited to the approved filesystem-administration and elevated-worker paths.

## Storage and compatibility

The filesystem schema and index format were not changed. A fresh Quail-Lab clone
at the 0.2 baseline `4c408888ec07421825ec75482301d97be271f1fe` built a test
index; the M15 CLI then read its complete status and found the controlled entry
without a rebuild. Existing healthy 0.2 indexes therefore remain compatible.

## Verification

| Area | Result | Evidence |
| --- | --- | --- |
| Automated suite | PASS | `dotnet test tests\\Quail.Core.Tests\\Quail.Core.Tests.csproj -c Release --no-restore`: 177 passed. The added boundary test verifies Core projection, opaque action identity, action retention across another search, and native-identity opening through FileSystem. |
| Static boundary review | PASS | `Quail.Core` has no SQLite, `IndexStore`, NTFS/USN, or `NativeFileId` references. Quick Search service, presentation, result item, and window have no filesystem result identity or store references. No dependency cycle or speculative provider/capability code was introduced. |
| Release builds | PASS | Release builds of `Quail.App` and `Quail.Cli` completed with zero warnings and errors. |
| Publish/package payload | PASS | Final self-contained publish produced 470 files / 182,460,670 bytes. Final Inno payload contains 58 files / 43,962,965 bytes, including `Quail.FileSystem.dll`, `Microsoft.Data.Sqlite.dll`, SQLitePCL assemblies, and `e_sqlite3.dll`; installer SHA-256: `feca6dccd16e3d76fc84ef8356760b98aefa551e0443e4efc26c8c4ab401a522`. |
| Quail-Lab protected runtime | PASS | Final `ProtectedIndexRuntime` used hash-verified archive `ECFBC8DAD05887D5EC0B1663B51477E591448CEC08363E3676EEF789F34A7C34`; Build, Refresh, zero-change Refresh, controlled-change search, CLI status/search, unelevated protected-index read, reparse/junction defenses, and per-volume concurrency locking passed. Runtime integrity summary: `1a2e392c76a3a20b5074ae881f19f7972060e50cc7e741465420f8e50a591071`. |
| Shell open | PASS | Final packaged `Quail.Cli open` launched the controlled Quail-Lab text-file fixture by native file ID. The launcher now correctly accepts a successful ShellExecute route that reuses an existing shell process and returns no new `Process`. |

## Security boundary

M15 preserved same-account elevation and the narrow worker command surface. The
final lab run confirmed protected ProgramData storage, unelevated post-worker
reads, reparse-point protection, result transport protection, SQLite quiescence,
and the one-success/one-fail concurrent-worker invariant. No service, IPC,
updater, ACL, or trust-model change was introduced.

## UI verification note

The focused interactive Quick Search harness established production startup,
visible state, search scheduling, short-query behavior, and keyboard selection
on the extracted application. Its result-opening step exposed the ShellExecute
return-value issue above; the final packaged CLI-open smoke verified the targeted
fix with a controlled file. WinApp was not available in this environment, so the
historical high-iteration M10/M13-D lifecycle campaign was not repeated. This is
consistent with the repository verification playbook; independent QA should run
one ordinary Quick Search open flow through its preferred interactive UI path.

## Scope and limitations

There was no index migration, no compatibility machinery, and no intended query,
ranking, privilege, storage, or installer behavior change. The known same-tier
candidate-window limitation remains owned by M18. M15 did not enter M16 or any
later milestone.
