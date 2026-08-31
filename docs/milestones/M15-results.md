# M15 Results — Core / FileSystem Boundary Extraction

## Outcome

M15 extracted the existing NTFS/filesystem implementation into the production
`Quail.FileSystem` assembly without introducing a provider SDK, plugin framework,
storage redesign, or search/ranking change.

The resulting production dependency graph is:

```text
Quail.App -> Quail.Core -> Quail.FileSystem -> Microsoft.Data.Sqlite
Quail.App -> Quail.FileSystem (filesystem administration only)
Quail.Cli -> Quail.FileSystem
```

`Quail.Core` now exposes only the current search boundary: request, product result
data, index summary/status, and an opaque `SearchResultAction`. `SearchResult`
does not expose raw filesystem attributes or Windows file times. Native file
identity and database paths remain inside `Quail.FileSystem`.

`SearchResultAction` now owns an internal, per-returned-result open delegate.
`FileSearchApplicationService` has no historical action dictionary, so completed
searches do not retain actions globally while a selected returned result can still
be opened.

The App remains the WinUI/UAC host and orchestration layer. Filesystem catalog
persistence and path validation, protected index storage, volume discovery,
filesystem status validation, and Build/Rebuild/Refresh mechanics now belong to
`Quail.FileSystem`. The App's direct FileSystem reference remains limited to the
approved filesystem-administration paths.

The historical Search-Ranking specification and results were restored verbatim
from `main`; this result records the M15 ownership move of the ranking
implementation to `Quail.FileSystem`.

## Storage and compatibility

No filesystem schema or index-format change was made, and no migration or rebuild
requirement was introduced. The pre-M15 repository baseline
`4c408888ec07421825ec75482301d97be271f1fe` is not a release baseline; the
production code remains compatible with the existing 0.2 index format.

## Verification

| Area | Result | Evidence |
| --- | --- | --- |
| Focused boundary tests | PASS | Focused Core/FileSystem and M12 boundary tests: 25 passed. The new regression verifies repeated searches preserve a returned opaque action without a service-level registry, while `SearchResult` exposes no raw filesystem representation. |
| Automated suite | PASS | `dotnet test tests\\Quail.Core.Tests\\Quail.Core.Tests.csproj -c Release --no-restore`: 177 passed. |
| Release builds | PASS | Release builds of `Quail.App` and `Quail.Cli` completed with zero warnings and zero errors. |
| Static boundary review | PASS | `Quail.Core` has no SQLite, `IndexStore`, NTFS/USN, or `NativeFileId` reference. `Quail.App` has no direct `IndexStore`, `NtfsVolume`, protected-storage, catalog-store, or managed-index-path implementation reference. |
| Publish/package payload | PASS | The final self-contained publish contained 470 files / 182,462,762 bytes. The canonical installer guard produced a 58-file / 43,965,553-byte payload, including `Quail.FileSystem.dll`, `Microsoft.Data.Sqlite.dll`, SQLitePCL assemblies, and `e_sqlite3.dll`; installer SHA-256: `ae137837807869a930ed86d51f0ff75e281c25f3788c8652f153908417729f36`. |
| Quail-Lab protected runtime | PASS | A hash-verified final archive passed `ProtectedIndexRuntime`: Build, Refresh, zero-change Refresh, controlled-change search, unelevated protected-index reads, reparse/junction defenses, protected result transport, and the per-volume concurrency invariant. |
| Ordinary Quick Search smoke | NOT RUN | WinApp was unavailable. The already-established VMConnect input limitation prevented the short interactive fallback from delivering input even to File Explorer, so no additional console-input campaign was attempted. Independent QA must perform one normal startup → controlled-result search → Enter/open smoke in an interactive desktop session. |

## Security boundary

M15 preserved same-account elevation and the narrow worker command surface. The
final lab run confirmed protected ProgramData storage, unelevated post-worker
reads, reparse-point protection, result transport protection, SQLite quiescence,
and the one-success/one-fail concurrent-worker invariant. No service, IPC,
updater, ACL, or trust-model change was introduced.

## Scope and limitations

`WindowsShellLauncher` retains the historical `Process.Start(...) == null`
behavior. The ShellExecute return-value issue found by the harness is an
out-of-scope follow-up, not an M15 fix.

No migration, compatibility machinery, provider framework, installer redesign,
or M16 work was introduced. The ordinary interactive Quick Search open smoke
remains the only final verification item not evidenced in this session.
