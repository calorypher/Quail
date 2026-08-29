# Quail

Quail is a fast, lightweight Windows search application built around its own local index.

The current implementation starts with local NTFS file search, but the long-term product direction is broader: a local-first universal search system for the user's digital information. Files, mail, cloud documents, browser history/bookmarks, calendars, notes, contacts, and other accessible sources should eventually be searchable through one fast interface and one local ranking/index model where practical.

The source or storage location should be secondary to the searchable object. Quail should understand durable identity, history, metadata, content fragments, and source-specific actions where a source can provide them. The working internal shorthand is "Find literally anything, literally anywhere." See [docs/product-vision.md](docs/product-vision.md) for the current north-star product vision.

## Current status

The Windows 11 x64 NTFS file-engine implementation completed M01 through M07 for the private/test-developer 0.1 distribution. M10 completed the production WinUI desktop shell, M11 completed real persistent file-search integration, and M12 completed persistent GUI-managed index configuration, protected privileged build/refresh operations, and freshness/status reporting.

Quail 0.2 is a pre-RC candidate, not a public release. M13-C has validated its framework-dependent production installer, including upgrade from the released 0.1.0 test/developer baseline. M13-D remains responsible for the independent technical RC campaign.

## 0.1 core CLI quick start

Quail currently targets local Windows 11 x64 NTFS volumes. Build with `dotnet build src\Quail.Cli\Quail.Cli.csproj -c Release`, then run the executable or `dotnet run --project src\Quail.Cli\Quail.Cli.csproj --`.

```text
Quail.Cli build --index C:\Quail\c.db --volume C:\
Quail.Cli sync --index C:\Quail\c.db --volume C:\
Quail.Cli status --index C:\Quail\c.db
Quail.Cli search --index C:\Quail\c.db report --type file --ext pdf --limit 50
Quail.Cli search --index C:\Quail\c.db --index D:\Quail\d.db report
Quail.Cli open --index C:\Quail\c.db --file-id 0011223344556677
Quail.Cli --help
Quail.Cli --version
```

Search is a literal case-insensitive indexed-name substring match. Three or more characters use FTS5 trigram; one or two use the persistent SQLite fallback. Filters `--type`, `--ext`, `--min-size`, `--max-size`, `--modified-after`, `--modified-before`, `--hidden`, `--read-only`, and `--system` compose. Repeated `--index` is fail-closed, has one global limit, and prints source identity for every result. Search never stats or traverses the live filesystem.

`open` resolves the persisted namespace path, confirms it still exists, and uses the normal Windows shell. Supply the source and `fileId` printed by search. Exit codes are `0` success, `2` invalid input, and `1` operational/index failure.

The released 0.1.0 test/developer baseline had a CLI/core-only installer. It remains relevant only as the real upgrade baseline; it did not include the GUI, background maintenance, a service, an updater, or a persistent source catalog. Full-volume MFT/USN access can require administrator privileges; this core does not add a service boundary.

## Production installer candidate (0.2 pre-RC)

The current `scripts\build-installer.ps1` builds the Windows 11 x64 Quail 0.2 production candidate. It packages the normal unpackaged WinUI `Quail.exe` together with `Quail.Cli.exe` as a folder-based, framework-dependent payload. It is not a public-release artifact.

Build the installer from a clean repository checkout with the .NET 10 SDK and Inno Setup installed:

```powershell
.\scripts\build-installer.ps1
```

The script publishes separate framework-dependent `win-x64` App and CLI outputs, verifies their merged staged payload, and writes `artifacts\installer\0.2.0\Quail-0.2.0-Setup.exe` with its SHA-256. It finds `ISCC.exe` from an explicit `-IsccPath`, PATH, conventional machine-wide locations, or the current user's `%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe` installation.

The installer uses shared Microsoft runtimes: .NET Desktop Runtime, Windows App Runtime, and the x64 Visual C++ Redistributable. It detects the supported runtime set for the installing user, downloads only missing or insufficient prerequisites from committed Microsoft URL/SHA-256 pins, verifies them before execution, and fails before copying Quail if bootstrap fails. When the prerequisites are already present, no network access is required.

Run the setup executable with normal Windows elevation. It installs `Quail.exe` and `Quail.Cli.exe` under `C:\Program Files\Quail`, adds that directory once to the system `PATH`, and creates the Start Menu entry for the GUI. Start a new command shell after installation:

```text
Quail.Cli --version
Quail.Cli --help
```

Uninstall from Windows Installed apps or the generated uninstaller. It removes the installed application and Quail's own PATH entry, but never removes user data.

Use an explicit database path for every command. `%LOCALAPPDATA%\Quail\Indexes\` is the recommended current location, but the installer neither creates nor owns it:

```text
Quail.Cli build --index %LOCALAPPDATA%\Quail\Indexes\c.db --volume C:\
```

The installed CLI keeps the M06 privilege model: full-volume NTFS MFT/USN build and sync operations can require an elevated CLI process. Quail 0.2 pre-RC has no Windows Service, updater, per-user/no-admin installer, or implicit discovery of index files outside its persistent GUI-managed index catalog.

## First measurable success

> Quail maintains its own local NTFS file index, keeps it current, and can find a file by name very quickly.

This first measurable success is the foundation, not the final product definition.

## Initial technical direction

- Windows 11 x64 first.
- C# on the current stable .NET release; .NET 10 LTS is the current preferred baseline.
- SQLite for persistent index storage.
- Official Windows/NTFS mechanisms for initial file enumeration and change tracking.
- A privileged Windows Service is expected to be the likely mechanism for NTFS/MFT/USN access without elevating the launcher UI during normal use; the production service/application boundary remains to be implemented.
- Thin Windows interop only where required; no native C++ or Rust component without demonstrated need.
- Later heterogeneous sources should be added as real, source-specific adapters and only then generalized into shared contracts where demonstrated common requirements justify it.
- A dynamic or third-party plugin framework is deliberately deferred; early releases should not add plugin loading, discovery, SDK/versioning, or compatibility machinery.
- No speculative cross-platform abstraction in the early releases.
- Content indexing and remote/cloud sources are future capabilities, not part of the current 0.1 implementation.

## Product priorities

1. Correct index state.
2. Fast search.
3. Reliable incremental updates and restart catch-up.
4. Near-zero idle CPU when no work is required.
5. Low memory and disk usage; the working target for the complete idle application is approximately 100 MB working set or less.
6. Fast startup and responsive interaction.
7. Simple installation and maintenance.
8. Local-first privacy and explicit source/retention behavior as broader personal-data indexing is introduced.

Do not optimize for benchmark records at the cost of architecture simplicity or reliability.

## Language and localization

The initial application and CLI are English-first. Localization is planned for a later stage, after user-facing UI text exists and is sufficiently stable. Early backend/file-engine milestones should not add localization infrastructure unless explicitly required by their scope.

Repository artifacts are written in English. Planning discussions and implementation-agent task prompts may be written in Polish; see [AGENTS.md](AGENTS.md) for the durable repository convention.

## Development model

The repository is the canonical technical source once implementation begins. Work is divided into small, closed milestones. Each implementation milestone is executed by an implementation agent on its own branch, verified there, and reviewed independently before merge. Merge requires explicit user approval.

A reusable Hyper-V VM named `Quail-Lab` is available for elevated and destructive Windows testing. Milestone-specific clean states are represented by checkpoints rather than separate milestone-named VMs. Exact lab procedures belong in `AGENTS.md` and the active milestone specification.

See [ROADMAP.md](ROADMAP.md) for product phases, [docs/product-vision.md](docs/product-vision.md) for the strategic north star, and [AGENTS.md](AGENTS.md) for repository workflow and guardrails.

## Public release

Quail is currently private. The project name is retained as **Quail** for a future public release, and **MIT** is the approved license for the public code release. The code license does not define rights to the Quail name, logo, or other branding assets.

Before changing the repository from private to public, complete a dedicated public-release readiness audit of the actual release candidate, including dependency and redistributed-asset licensing, the publish/installer payload, required notices, repository history for secrets/private material, the standard MIT `LICENSE`, and public repository metadata.

A final manual `QUAIL` / `Q.QUAIL` / materially similar software-mark check in the relevant EU/Poland sources, including TMview/EUIPO and UPRP, remains required immediately before public release. Reopen the naming decision only if that check finds a material conflict or the project later changes substantially toward hosted/SaaS search or business/cloud-data services.
