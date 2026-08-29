# Changelog

## Unreleased

### Changed

- File-search results now use deterministic relevance ranking instead of purely alphabetical ordering, prioritizing stronger name matches and normal user-visible locations over internal and system-heavy paths.

### Added

- WinUI Quick Search desktop shell with a global hotkey, tray integration, configurable hotkey, and System, Light, and Dark themes.
- Real indexed file and directory search in Quick Search, including Windows Shell type icons, keyboard result navigation, and Enter-to-open.
- Persistent GUI-managed local NTFS indexes, including build, rebuild, refresh, and enable/disable for Quick Search.
- Explicit administrator approval for privileged index operations while the normal Quail GUI remains unelevated.

## 0.1.0 — 2026-08-22

### Added

- Persistent local NTFS indexes with USN-driven incremental synchronization.
- Indexed file-name search with metadata filters and multi-index CLI support.
- Windows Shell open for indexed files and directories.
- A self-contained Windows installer for the Quail CLI.
