# M15 Results — Core / FileSystem Boundary Extraction

## Outcome

M15 extracted the existing NTFS/filesystem implementation into the production
`Quail.FileSystem` assembly without introducing a provider SDK, plugin framework,
storage redesign, or search/ranking change.

The resulting production dependency graph is:

```text
Quail.App -> Quail.Core
Quail.FileSystem -> Quail.Core
Quail.App -> Quail.FileSystem
    static composition / filesystem administration only
Quail.Cli -> Quail.FileSystem
```

`Quail.Core` owns the closed, minimal `ISearchSource` seam and
`SearchApplicationService` aggregation. The contract is internal to the current
first-party assemblies; it is not a public provider SDK. `Quail.FileSystem`
implements the seam, retains NTFS identity, SQLite, ranking, and shell actions,
and maps its own result data into the Core presentation contract.

`SearchResult` is source-neutral presentation data: title, optional context, kind,
concise metadata, and icon presentation. It does not require a path, file/folder
flag, extension, file size, attributes, timestamps, native identity, or database
path. The FileSystem source preserves the current Quick Search file/folder,
path/context, metadata, icon, and open behavior when it maps its own result.

`SearchResultAction` owns an internal, per-returned-result open delegate.
`SearchApplicationService` has no historical action dictionary, so completed
searches do not retain actions globally while a selected returned result can still
be opened by its owning source.

The normal App search runtime, coordinator, result presentation, and action flow
use Core types with source-neutral names. `FileSystemSearchComposition` is the
isolated static composition point. Filesystem catalog persistence and path
validation, protected index storage, volume discovery, filesystem status
validation, and Build/Rebuild/Refresh mechanics remain in `Quail.FileSystem`.

`SearchRuntime` requires only search, source availability, and disposal. Its
filesystem status notice and search-performance diagnostics are optional,
composition-supplied callbacks. The FileSystem composition retains its refresh
notice and index-scale trace data without making index health, record counts, or
database size a generic source capability. Filesystem shell-icon enrichment is
also optional: FileSystem supplies its existing key, while a source-neutral
result can rely solely on its fallback glyph.

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
| Focused boundary tests | PASS | 5 final focused boundary/trace tests passed. The fake-source tests create Core and `SearchRuntime` without filesystem objects, index diagnostics, freshness callbacks, or shell-icon keys; they aggregate sources, route opaque actions, and verify that Core has no `Quail.FileSystem` assembly reference. The FileSystem projection retains its icon key and the action-retention regression remains covered. |
| Automated suite | PASS | Final `dotnet test tests\\Quail.Core.Tests\\Quail.Core.Tests.csproj -c Release --no-restore`: 179 passed. |
| Release builds | PASS | Release builds of `Quail.App` and `Quail.Cli` completed with zero warnings and zero errors. |
| Static boundary review | PASS | `Quail.Core` has no FileSystem project reference or concrete-source/SQLite/NTFS reference. `Quail.FileSystem` references Core. The normal Quick Search coordinator/presentation flow has no FileSystem type reference, mandatory index/freshness capability, or mandatory shell-icon key; App references remain in static composition and filesystem administration/UAC code. No generic `FileSearch*` or file-shaped Core abstraction remains. |
| Publish/package payload | PASS | The final canonical installer guard produced a 58-file / 43,966,350-byte payload, including `Quail.Core.dll`, `Quail.FileSystem.dll`, `Microsoft.Data.Sqlite.dll`, SQLitePCL assemblies, and `e_sqlite3.dll`; installer SHA-256: `f4c9dc1faa59c3781ca0b75dd7f055439f348eb69c45810e9de1c0971ddcc1d7`. |
| Quail-Lab protected runtime | REUSED PASS | The preceding M15 `ProtectedIndexRuntime` evidence remains applicable: this remediation did not change filesystem administration, protected storage, ACL/reparse defenses, locking, or Build/Rebuild/Refresh implementation. |
| Index compatibility | REUSED PASS | The format was not changed; the preceding M15 existing-index verification remains applicable. |
| Manual UI smoke | PASS | User-owned manual smoke completed against the M15 Release build: Quick Search started, file search and result opening worked, and Rebuild completed. |

## Security boundary

M15 preserved same-account elevation and the narrow worker command surface. The
reused final lab evidence confirms protected ProgramData storage, unelevated
post-worker reads, reparse-point protection, result transport protection, SQLite
quiescence, and the one-success/one-fail concurrent-worker invariant. No service,
IPC, updater, ACL, or trust-model change was introduced.

## Scope and limitations

`WindowsShellLauncher` retains the historical `Process.Start(...) == null`
behavior. The ShellExecute return-value issue found by the harness is an
out-of-scope follow-up, not an M15 fix.

No migration, runtime module loader, provider framework, installer redesign, or
M16 work was introduced. The source seam is deliberately closed and static; a
future loading decision remains a composition change.
