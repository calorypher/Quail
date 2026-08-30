# Quail

Quail is a Windows-first, local-first file search application. It maintains its
own local NTFS index so that indexed file and directory names can be searched
quickly without sending the index or queries to a cloud service.

## Quail 0.2

0.2 is Quail's first public development release. It includes the WinUI Quick
Search desktop shell, global hotkey and tray integration, persistent
GUI-managed local NTFS indexes, and the `Quail.Cli` command-line tool.

It requires Windows 11 x64 and local NTFS volumes. The application UI and CLI
are currently English-only.

## Install

Download the `Quail-0.2.0-Setup.exe` release asset, verify its published
SHA-256, and run it with normal Windows elevation.

Quail installs only to `C:\Program Files\Quail`; custom destinations are not
supported. The installer detects missing prerequisites and, only when needed,
downloads and SHA-256-verifies the .NET 10 Desktop Runtime, Windows App
Runtime, and x64 Visual C++ Redistributable from Microsoft before copying
Quail.

The installer adds the canonical Program Files directory to the system `PATH`.
Open a new terminal after setup:

```text
Quail.Cli --version
Quail.Cli --help
```

## Basic usage

The GUI manages persistent indexes and makes indexed file and directory names
available through Quick Search. The CLI can build, synchronize, inspect, and
search a chosen index:

```text
Quail.Cli build --index %LOCALAPPDATA%\Quail\Indexes\c.db --volume C:\
Quail.Cli sync --index %LOCALAPPDATA%\Quail\Indexes\c.db --volume C:\
Quail.Cli search --index %LOCALAPPDATA%\Quail\Indexes\c.db report --type file --ext pdf --limit 50
Quail.Cli open --index %LOCALAPPDATA%\Quail\Indexes\c.db --file-id 0011223344556677
```

Search is a literal case-insensitive indexed-name substring match. It supports
filters including `--type`, `--ext`, size, modified-time, hidden, read-only,
and system attributes. Search does not traverse or stat the live filesystem.
Full-volume NTFS MFT/USN build and sync operations can require an elevated CLI
process.

## Privacy and limitations

Quail 0.2 is local-first: it has no telemetry backend, cloud or network
indexing, content indexing, third-party plugin system, Windows Service,
automatic updater, or cross-platform build. User index data is not created or
owned by the installer.

This development release is unsigned. Windows SmartScreen or reputation
warnings may appear; verify the release hash before running it. Code signing
may be reconsidered for a later release.

## Uninstall

Use Windows Installed apps or Quail's generated uninstaller. It removes Quail
and Quail's own system `PATH` entry, but does not remove user index data.

## Development status

0.2 is a development release, not a statement that later roadmap features are
available. See [ROADMAP.md](ROADMAP.md) for planned work; it is not a feature
list for this version.

## License

Quail source code is licensed under the [MIT License](LICENSE). See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for redistributed-component
notices and terms. The license does not grant rights to the Quail name, logo,
or other branding.
