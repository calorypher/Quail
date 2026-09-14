# Quail

> **WARNING — QUAIL 0.3.0 WITHDRAWN**
>
> Quail 0.3.0 has been withdrawn due to a critical maintenance-service defect
> that can cause sustained CPU and disk-write activity while idle on the system
> volume. Do not install 0.3.0. Use 0.2.0 until the 0.3.1 hotfix is available.

Quail is a Windows-first, local-first filesystem search application for
Windows 11 x64. It indexes local NTFS volumes and searches local file and
directory names without sending indexes or queries to a cloud service.

## Quail 0.3.0 (withdrawn)

Quail 0.3.0 is a withdrawn development release. Its primary
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

Do not download or install Quail 0.3.0. Its historical installer asset is
`Quail-0.3.0-Setup.exe`.

The historical asset's SHA-256 is recorded below for provenance only:

```text
2b17072506027d304d295273f1998d467586d2828ac27d63221751c5a7ba495c
```

The withdrawn installer was fixed at `C:\Program Files\Quail`; custom
destinations were not supported. Its pinned prerequisites were .NET 10 Desktop
Runtime, Windows App Runtime, and the x64 Visual C++ Redistributable.

The representative released `0.2.0` to `0.3.0` transition at the canonical
installation path is supported and preserves existing ProgramData and
LocalAppData. Other historical development builds remain uninstall-first.

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

Quail 0.3.0 is a withdrawn development release. See [ROADMAP.md](ROADMAP.md)
for planned work; the roadmap is not a feature list for the current release.

## License

Quail source code is licensed under the [MIT License](LICENSE). See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for redistributed-component
notices and terms. The license does not grant rights to the Quail name, logo,
or other branding.
