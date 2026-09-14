# Changelog

## 0.3.0 — WITHDRAWN (released 2026-09-14)

### Added

- Quick Search now offers clear-query, Full Search, and Settings controls plus supported secondary result actions.
- Full Search has compact default filters, expandable advanced filters, sortable result columns, and direct Settings access.
- Full Search v1 adds a persistent, resizable filesystem result browser with larger bounded result sets, structured sorting and filters, keyboard navigation, reveal, and copy-path actions over the same Core search path as Quick Search.
- Filesystem indexes are maintained continuously by a protected LocalSystem service, with direct read-only Quick Search, restart/downtime catch-up, explicit fail-closed recovery, and no routine Refresh or UAC for normal changes.
- A unified standalone Settings window now combines General settings, index administration, maintenance status, and About information.
- Quail can launch on Windows sign-in through an owned per-user startup registration.

### Changed

- Quick Search, Full Search, and Settings now share a calmer Fluent-adjacent surface, typography, control, and card treatment.
- Filesystem search now preserves the best-ranked results when many names match, including duplicate names across indexes. Exact and prefix matches in ordinary visible locations rank above weaker matches in the current profile, while internal and system-heavy results remain deprioritized.
- Full Search and Settings set their native caption-control theme from Quail's effective Light or Dark theme, including when it differs from the Windows theme.

### Release status

- **WITHDRAWN.** Quail 0.3.0 was withdrawn on 2026-09-14 because of a critical
  maintenance-service idle CPU/disk-write defect affecting maintenance on the
  same system volume. The tag `v0.3.0` remains from source commit
  `e6802343d8463a68d20d9f35659a550bd1eabfc6`. The historical installer is
  `Quail-0.3.0-Setup.exe` (10,244,038 bytes, SHA-256
  `2b17072506027d304d295273f1998d467586d2828ac27d63221751c5a7ba495c`).
- Quail 0.3.0 is distributed unsigned. Windows SmartScreen and Smart App
  Control may warn about or block unsigned binaries on some systems. Disabling
  Windows security features is not a supported workaround. Trusted public code
  signing is deferred outside 0.3 by the project owner's release-policy
  decision.

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
