# Quail

> **WARNING — QUAIL 0.3.0 WITHDRAWN**
>
> Quail 0.3.0 has been withdrawn due to a critical maintenance-service defect
> that can cause sustained CPU and disk-write activity while idle on the system
> volume. Do not install 0.3.0. Quail 0.3.1 is the narrowly scoped maintenance
> hotfix release. Install Quail 0.3.1 instead.

Quail is a Windows-first, local-first filesystem search application for
Windows 11 x64. It indexes local NTFS volumes and searches local file and
directory names without sending indexes or queries to a cloud service.

## Quail 0.3.1

Quail 0.3.1 preserves the withdrawn 0.3.0 feature set and changes only the
critical maintenance idle-write path, its regression coverage, and release
evidence. Its primary
surfaces are Quick Search, a global hotkey, tray integration, Full Search,
Settings, and an administrative/diagnostic `Quail.Cli`.

Protected machine-wide indexes are maintained by the LocalSystem
`QuailMaintenance` service, which is their sole writer. Quick Search and Full
Search read those indexes directly in read-only SQLite mode; ordinary searches
do not use service IPC. NTFS USN tracking keeps normal filesystem changes
current, so users normally do not need to refresh or synchronize after routine
file changes. If USN continuity cannot be proven, Quail fails closed with
`RebuildRequired` rather than silently running a full rebuild.

## Install

Quail 0.3.1 is the current 0.3 release. Download it from the
[GitHub Release](https://github.com/calorypher/Quail/releases/tag/v0.3.1). Do
not download or install Quail 0.3.0; its historical installer asset remains
available only as withdrawn provenance.

The historical asset's SHA-256 is recorded below for provenance only:

```text
2b17072506027d304d295273f1998d467586d2828ac27d63221751c5a7ba495c
```

The 0.3.x installer is fixed at `C:\Program Files\Quail`; custom destinations
are not supported. Its pinned prerequisites are .NET 10 Desktop Runtime,
Windows App Runtime, and the x64 Visual C++ Redistributable.

The 0.3.1 hotfix supports the bounded `0.2.0` to `0.3.1` and
withdrawn `0.3.0` to `0.3.1` transitions at the canonical installation path,
preserving existing ProgramData and LocalAppData. Other historical development
builds remain uninstall-first.

## Basic usage

### Quick Search

Use the global hotkey to open keyboard-first Quick Search, type a filename or
directory name, and open the selected result. From Quick Search, you can move
to Full Search or Settings.

### Full Search

Full Search is a persistent, resizable window for inspecting a larger result
set. It provides filters and sorting, plus Open, Reveal, and Copy path actions.

### Settings

Settings includes General, Indexing, and About pages, including the Windows
startup option.

### CLI

`Quail.Cli` is an administrative and diagnostic surface, not the normal daily
search workflow. Open a new terminal after setup:

```text
Quail.Cli --version
Quail.Cli --help
```

## Privacy and limitations

Quail is filesystem-only: it searches local NTFS file and directory names, not
file contents. It has no browser, cloud, mail, or network-folder source; no
third-party plugin system; no automatic updater; no Linux or cross-platform
build; and no preview, history, or file-manager functionality. The application
UI remains English-first.

The installer and Quail-owned binaries are unsigned. Windows SmartScreen or
Smart App Control may warn about or block them. Disabling Windows security
features is not a supported workaround.

## Uninstall

Use Windows Installed apps or Quail's generated uninstaller. It removes Quail
and Quail's own system `PATH` entry, but does not remove existing ProgramData
or LocalAppData.

## Development status

Quail 0.3.1 was released on 2026-09-23 as the maintenance hotfix for withdrawn
0.3.0. It does not begin Quail 0.4 or change the 0.4 roadmap goal. See
[the 0.3.1 release notes](docs/releases/0.3.1-release-notes.md) and
[ROADMAP.md](ROADMAP.md) for the release history and planned work.

## License

Quail source code is licensed under the [MIT License](LICENSE). See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for redistributed-component
notices and terms. The license does not grant rights to the Quail name, logo,
or other branding.
