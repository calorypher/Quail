# Changelog

## 0.2.0 — release-ready

### Changed

- File-search results now use deterministic relevance ranking instead of purely alphabetical ordering, prioritizing stronger name matches and normal user-visible locations over internal and system-heavy paths.

### Added

- WinUI Quick Search desktop shell with a global hotkey, tray integration, configurable hotkey, and System, Light, and Dark themes.
- Real indexed file and directory search in Quick Search, including Windows Shell type icons, keyboard result navigation, and Enter-to-open.
- Persistent GUI-managed local NTFS indexes, including build, rebuild, refresh, and enable/disable for Quick Search.
- Explicit administrator approval for privileged index operations while the normal Quail GUI remains unelevated.

This is the first intended public development release. Its publication date,
tag, and release hash are finalized in M14-B.
