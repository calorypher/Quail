# M21 Results — Unified Settings & Launch on Startup

## Status

**IMPLEMENTATION AND INSTALLED-STATE VERIFICATION PASS — ready for focused independent re-review.**

User-owned visual Settings and real sign-out/login smoke remain pending final acceptance checks.

## Preparation

- Approved base: `37ee57e47c56ffc22016c457c43e38c5bf2dfa26`.
- `scripts/prepare-milestone.ps1` verified clean host and VM repositories at the same HEAD, created Hyper-V checkpoint `M21-clean`, and created branch `codex/m21-unified-settings-startup`.

## Implementation

- `SettingsWindow` is a single standalone application-level WinUI window with General, Indexing, and About navigation. The Quick Search overlay and tray route Settings to that window; repeat requests activate the existing instance.
- General preserves the existing settings store, hotkey capture/restore behavior, and theme handling. Launch on startup is an owned `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value named `Quail`; it only registers the quoted installed Program Files executable and never a development output path.
- Indexing reuses the M20 catalog, health state, and operation coordinator. Health is shown as product status and Rebuild is a secondary recovery action for healthy registered indexes.
- The installer removes the owned per-user Run value during uninstall without changing M20 protected ProgramData state.

## Earlier verification history

- The initial implementation passed focused Core tests (273/273), full tests (291/291), a zero-warning Release build, and its then-final installer build. Those counts and artifact hashes are superseded by the final post-QA correction evidence below.
- The initial Quail-Lab installer check established that the owned Run value was `"C:\Program Files\Quail\Quail.exe"`, uninstall removed that value, and `%PROGRAMDATA%\Quail` survived uninstall. The startup-registration and installer-cleanup boundary has not changed since that proof.

## Independent QA delta corrections

Independent QA of PR #23 at `027250ce7a6fe657cba60ae0ea1643e50aa76274` required five bounded corrections:

- Indexing action policy now treats `MaintenanceHealthState.RebuildRequired` as a primary Rebuild recovery action even when the protected database remains `Complete`; deterministic policy coverage includes that combination.
- Product health detail maps only stable maintenance states and no longer renders internal reason tokens. Search preference wording is now explicitly `Enabled for Quick Search` or `Disabled for Quick Search`.
- Indexing disables actions while an operation is running, shows an in-progress indicator, catches ordinary catalog/operation exceptions, and displays them on the Indexing page. The user-facing label is `Remove`; the backend remains `Unregister` followed by catalog removal on success.
- Settings restores an active hotkey capture on deactivation, navigation, and close. A successful save does not restore the old hotkey. NavigationView hides its built-in Settings item, leaving only General, Indexing, and About. Standalone dark theme reuses the bounded native DWM title-bar mode handling.
- The previous final-QA delta passed focused Core tests (283/283), full tests (296/296), a zero-warning Release build, and a then-final installer build. Those values are superseded by the final post-manual-findings evidence below.

## Post-QA manual-findings correction

- Settings initial size now uses a one-time dispatcher callback after activation, when the native window and DPI are ready. The 920×680 logical target is converted through the existing DPI scaling helper and is never applied again to an already shown Settings window.
- The Settings root is now a stable themed container. Theme propagation explicitly reaches the root, NavigationView, and content Frame while retaining the existing native DWM title-bar mode path. No new palette or Settings framework was introduced.
- `ShellSettings.Default` is now `Alt+Space`. Missing or invalid settings use that default, while a valid persisted `Ctrl+Alt+Space` or another valid custom value remains unchanged.
- Initial global-hotkey registration failure is non-fatal. Quail remains resident and Settings presents a stable product-facing warning; no fallback is registered or written to settings. The bounded `HotkeyRegistration` seam preserves the prior registration when a later Save fails and permits a later successful replacement.
- The M21 Indexing policy was not changed. The dev-host report was not reproduced in an installed candidate: the final Quail-Lab candidate had a running `QuailMaintenance` service as `LocalSystem`, protected ProgramData state, and a healthy/trusted registered target. A controlled D: fixture successfully performed Unregister (machine target removed) followed by RegisterAndBuild (healthy/trusted again); the service did not mutate the per-user catalog. Existing coordinator tests prove that the Settings Remove flow removes that catalog entry only after successful Unregister. Existing policy tests prove that a healthy registered Complete index has no primary Build action.

## Final verification

- Focused changed-area Core tests — PASS, 42/42: Settings store/defaults, Alt+Space parsing and hotkey registration lifecycle, capture session, Settings size conversion, Indexing policy, and Remove ordering.
- Final full tests: `dotnet test Quail.sln -c Release --no-restore` — PASS, 298/298 (285 Core + 13 Maintenance Service).
- Final Release build: `dotnet build Quail.sln -c Release --no-restore` — PASS, zero warnings and errors.
- Final installer build: `scripts/build-installer.ps1` — PASS; installer SHA-256 `7cd25a7a5f9e239a1af5b725ac4613b7589277352b3e7eccd31bebaec5cefb47`.
- Final Quail-Lab installed-state smoke — PASS for service, protected state, and controlled maintenance behavior: the installer completed successfully; `QuailMaintenance` was Running as `LocalSystem`; `C:\Program Files\Quail\Quail.exe` and `C:\ProgramData\Quail` existed; a healthy/trusted registered D: fixture was unregistered and restored through RegisterAndBuild without service mutation of the user catalog.
- Windows UI automation was unavailable in this Codex session because its trusted RPC service was not configured. Consequently, the visible Settings size, Light/Dark/System appearance, and displayed startup/hotkey-conflict status remain user-owned manual acceptance rather than an automated PASS.
- `git diff --check` — PASS before the final commit.

## Pending user-owned smoke

- Open installed Settings and confirm its initial size, General/Indexing/About only, Light/Dark/System presentation including native title bar, one Settings instance, normal theme/hotkey behavior, understandable index health, and the stable unavailable-hotkey warning if a conflict is deliberately induced.
- On the installed candidate, enable launch on startup, Exit Quail, perform real sign-out/login or reboot/login, and confirm one resident/tray-ready Quail instance with no automatic Quick Search overlay; disable startup and confirm it does not start at the next login.
