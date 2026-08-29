# M07 Results — Test/Developer Packaging and 0.1 Distribution

## Status

**Completed and released — private/test-developer Quail 0.1.0.** M07 packages the existing Windows-only M06 CLI/core as the private, unsigned, test/developer `0.1.0` distribution. It does not add a service, IPC, background indexing, scheduler, GUI, updater, per-user installer, source catalog, or new filesystem backend.

## Tested source and artifact

- Branch: `m07-packaging-distribution`
- Tested implementation commit: `6e45d5546e93f3bbd4bebf6b4f7443b014417ae2`
- Canonical product version: `0.1.0`, defined once in `Directory.Build.props`.
- M07 branch candidate artifact: `artifacts\installer\Quail-0.1.0-Setup.exe`
- M07 branch candidate artifact size: `31,218,232` bytes
- M07 branch candidate artifact SHA-256: `88cffdffa61119c323d6206990222460a53da9a4ee26707808c2625ed8895f60`

`Quail.Cli --version` reads assembly informational metadata and removes only the SemVer build-metadata suffix added by the .NET source-revision integration. Its user-facing result is therefore the canonical product version `quail 0.1.0`, not the former hard-coded `0.1-core` string.

The final Release publish is a normal self-contained `win-x64` folder layout under `artifacts\publish\win-x64`. It contains `Quail.Cli.exe`, `Quail.Cli.dll`, `Quail.Core.dll`, `coreclr.dll`, `e_sqlite3.dll`, and the normal .NET runtime/dependency set. It is not single-file, trimmed, ReadyToRun, or Native AOT.

## Verification matrix

| ID | Result | Evidence |
|---|---|---|
| T01 | Pass | `dotnet test tests\Quail.Core.Tests\Quail.Core.Tests.csproj -c Release --no-restore` passed 53/53 after all product changes. |
| T02 | Pass | Final `scripts\build-installer.ps1` Release publish succeeded; folder layout contains the CLI, managed assemblies, SQLite native library, and CoreCLR runtime. |
| T03 | Pass | Final published `Quail.Cli.exe --version` exited 0 and printed `quail 0.1.0`. The unit test compares CLI output with assembly informational metadata after removal of only the `+` build-metadata suffix. |
| T04 | Pass | `scripts\build-installer.ps1` found the single working per-user Inno Setup 7 compiler through `$env:LOCALAPPDATA`, produced exactly `Quail-0.1.0-Setup.exe`, and printed the final SHA-256/size. A deliberate `-IsccPath C:\missing\ISCC.exe` invocation failed before publishing with `-IsccPath does not name an existing ISCC.exe`. |
| T05 | Pass | `.gitignore` ignores `artifacts/`; `git check-ignore` confirmed both generated publish and setup paths are ignored. No generated runtime/setup file was staged. |
| T06 | Pass | Quail-Lab clean silent per-machine install exited 0, created `C:\Program Files\Quail\Quail.Cli.exe`, and added exactly one system PATH entry. |
| T07 | Pass | A newly started `cmd.exe` process supplied only the current machine PATH resolved `Quail.Cli --version` (0) and `Quail.Cli --help` (0, contains `search --index`) from installed files. |
| T08 | Pass | Same-version reinstall exited 0; system PATH still contained exactly one `C:\Program Files\Quail` entry and the LocalAppData sentinel remained present. |
| T09 | Pass | An ignored temporary `0.0.9` fixture built from the same source/installer definition (`49a46f41feb2071f1e54701500ba982db52be221d00cb8db1674c8c0d02bcc77`) installed successfully after a controlled uninstall. The canonical installer then replaced it in place with the stable AppId: `quail 0.0.9` became `quail 0.1.0`, PATH count remained one, and no manual uninstall occurred between fixture and canonical install. |
| T10 | Pass | Installed Quail-Lab CLI performed `build`, `status`, `sync`, and `search` on disposable `QUAIL_LAB_DATA`: build 176 records in 355 ms; status complete; sync applied 18 records; search found `m07-smoke.txt`. |
| T11 | Pass | Final Quail-Lab uninstall exited 0, removed `C:\Program Files\Quail`, the Quail system-PATH entry, and the Quail uninstall registration. It preserved `%LOCALAPPDATA%\Quail\Indexes\m07-sentinel.txt` and explicit `D:\m07-packaging.db`. |
| T12 | Pass | Physical host silent install exited 0. A new machine-PATH process resolved `Quail.Cli --version` as `quail 0.1.0` and help successfully. Installed `status` and `search` both succeeded against an SHA-256-verified compatible lab fixture DB; search found `m07-smoke.txt`. |
| T13 | Pass | Physical-host uninstall exited 0 and removed the installed directory, Quail PATH entry, and uninstall registration. The explicitly created `%LOCALAPPDATA%\Quail\Indexes\m07-host-sentinel.txt` remained. |
| T14 | Pass | README documents the build prerequisite/discovery order, test/developer quality, unsigned status, application/data layout, explicit `--index`, PATH/new-shell use, elevation boundary, installation/uninstall, and current limitations. |
| T15 | Pass | Source and installer-definition review found only the M06 CLI/core plus folder publish, Inno Setup package, and system PATH integration. No service, scheduler, GUI, updater, per-user installer/backend, source catalog, or packaging framework was added. The installer definition creates no shortcut, service, task, startup entry, or background process. Quail-Lab runtime checks found zero `Quail*` services and zero `Quail*` scheduled tasks. |
| T16 | Pass | This durable result records source identity, T01–T15, artifact name/size/hash, publish layout, Quail-Lab and physical-host lifecycle evidence, limits, and workflow observations. |

## Post-QA final-artifact revalidation

Independent QA identified two bounded issues in the initial candidate: README still described the pre-M07 state, and the installer accepted x64-compatible architectures without a Windows 11 minimum version. The final installer definition now uses `MinVersion=10.0.22000`, `ArchitecturesAllowed=x64os`, and `ArchitecturesInstallIn64BitMode=x64os`. Inno Setup 7.1.0 compiled that exact definition successfully.

The M07 branch candidate artifact above was rebuilt after those fixes. Full Release tests again passed 53/53. Its publish output reported `quail 0.1.0`. This remains historical branch-candidate evidence and is distinct from the published merged-main artifact below.

Final-artifact deployment QA was rerun separately from the earlier full lifecycle evidence:

- **Quail-Lab:** clean final-artifact install exited 0; a new machine-PATH process returned `quail 0.1.0` and help containing `search --index`; final uninstall exited 0, removed the application directory and Quail PATH entry, and preserved `%LOCALAPPDATA%\Quail\Indexes\m07-final-qa-sentinel.txt`.
- **Physical host:** final-artifact install exited 0 with normal UAC; a new machine-PATH process returned `quail 0.1.0` and help containing `search --index`; final uninstall exited 0, removed the application directory and Quail PATH entry, and preserved `%LOCALAPPDATA%\Quail\Indexes\m07-final-qa-sentinel.txt`.

The earlier same-version reinstall, temporary `0.0.9` in-place upgrade, and installed build/status/sync/search evidence remains applicable because the final QA changes only documentation and installer platform admission. The final-artifact smoke above revalidates the install, PATH, executable, uninstall, and user-data preservation behavior affected by the rebuilt setup executable.

## Published release provenance

The branch candidate above is historical M07 implementation and QA evidence. After M07 was merged, release-preparation rebuilt and verified the installer from canonical `main`.

- Source commit: `a2b6927d2d550b183b8597df5d20ba87d44caa2a`
- Tag: `v0.1.0`
- GitHub Release: [Quail 0.1.0](https://github.com/calorypher/Quail/releases/tag/v0.1.0)
- Published artifact: `Quail-0.1.0-Setup.exe`
- Published artifact size: `31,218,184` bytes
- Published artifact SHA-256: `b39894a0ad807391af6abc1874ae02ff6e3e0c4133eab51e4bcd5f4460ab66a4`

## Release-preparation closeout

Release-preparation passed from merged `main` at `a2b6927d2d550b183b8597df5d20ba87d44caa2a`:

- Release test suite: 53/53 passed.
- Physical-host final smoke: PASS.
- Quail-Lab final smoke: PASS after investigation.

The initial Quail-Lab uninstall anomaly was classified as a `HARNESS/SYNCHRONIZATION ISSUE`, not an installer defect. Inno Setup temporarily reported directory error 145, completed its normal delayed retry, and two instrumented repetitions succeeded. Both ended with `C:\Program Files\Quail` absent, zero Quail-owned machine PATH entries, and no Quail uninstall registration.

## Install and ownership behavior

The installer uses one stable Inno Setup AppId, installs only the complete generated publish folder to the normal 64-bit `Program Files\Quail` location, and modifies the machine `Path` registry value only by removing exact normalized Quail install-directory entries before adding one on install. Uninstall removes those same entries and does not touch `%LOCALAPPDATA%\Quail`, index databases, settings, caches, or logs outside the application directory.

No desktop or Start Menu shortcut is defined. Standard Inno Setup silent parameters work for automated lab testing but do not bypass normal UAC for per-machine install/uninstall.

## Limitations

- The package is private/test-developer quality and unsigned. The verified `v0.1.0` tag and private GitHub Release are published; public distribution and code signing remain separate future concerns.
- Quail remains Windows 11 x64 only and retains the M06 explicit `--index <database-path>` contract.
- `%LOCALAPPDATA%\Quail\Indexes` is recommended documentation guidance only; the installer does not create it or discover indexes automatically.
- Full-volume NTFS MFT/USN build and sync can still require an elevated CLI process. M07 intentionally adds no service or no-admin backend.
- There is no background maintenance, scheduler, GUI, tray process, updater, per-user installer, source catalog, or implicit/default index behavior.

## Reusable workflow observations

The canonical preparation path reliably validated host/VM source identity, data volume, checkpoint creation, and branch creation before M07. For installer lifecycle evidence, the existing SSH/SCP helper primitives provided stable HostKeyAlias-based transfer and SHA-256 verification. The lifecycle sequence itself was kept as bounded milestone verification rather than becoming a new general-purpose installer/VM framework; repeat it only after a later milestone establishes a stable recurring contract worth maintaining.
