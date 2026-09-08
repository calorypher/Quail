# M21 Results — Unified Settings & Launch on Startup

## Status

**IMPLEMENTATION VERIFICATION PASS — ready for independent QA.**

User-owned visual Settings and real sign-out/login smoke remain pending external acceptance checks.

## Preparation

- Approved base: `37ee57e47c56ffc22016c457c43e38c5bf2dfa26`.
- `scripts/prepare-milestone.ps1` verified clean host and VM repositories at the same HEAD, created Hyper-V checkpoint `M21-clean`, and created branch `codex/m21-unified-settings-startup`.

## Implementation

- `SettingsWindow` is a single standalone application-level WinUI window with General, Indexing, and About navigation. The Quick Search overlay and tray route Settings to that window; repeat requests activate the existing instance.
- General preserves the existing settings store, hotkey capture/restore behavior, and theme handling. Launch on startup is an owned `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value named `Quail`; it only registers the quoted installed Program Files executable and never a development output path.
- Indexing reuses the M20 catalog, health state, and operation coordinator. Health is shown as product status and Rebuild is a secondary recovery action for healthy registered indexes.
- The installer removes the owned per-user Run value during uninstall without changing M20 protected ProgramData state.

## Verification

- Focused Core tests: `dotnet test tests/Quail.Core.Tests/Quail.Core.Tests.csproj -c Release --no-restore` — PASS, 273/273.
- Final full tests: `dotnet test Quail.sln -c Release --no-restore` — PASS, 291/291.
- Final Release build: `dotnet build Quail.sln -c Release --no-restore` — PASS, zero warnings and errors.
- Final installer build: `scripts/build-installer.ps1` — PASS; installer SHA-256 `609461b7f988f960cfcd3c600880fe75d0617f8328185465211545c891711014`.
- Quail-Lab focused installer validation — PASS: installed payload existed before uninstall; the owned Run value was `"C:\Program Files\Quail\Quail.exe"`; uninstall removed it; `%PROGRAMDATA%\Quail` existed before and after uninstall.
- `git diff --check` — PASS before final commit.

## Pending user-owned smoke

- Open tray Settings and confirm General, Indexing, and About, one Settings instance, normal theme/hotkey behavior, and understandable index health.
- On the installed candidate, enable launch on startup, Exit Quail, perform real sign-out/login or reboot/login, and confirm one resident/tray-ready Quail instance with no automatic Quick Search overlay; disable startup and confirm it does not start at the next login.
