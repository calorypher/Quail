# M14-I results — Installer cleanup safety remediation

## Disposition

`installer cleanup safety remediated and focused lifecycle validated`

The remaining real interactive foreign-directory rejection test (T4) passed after Quail-Lab provided an unlocked desktop session. The actual installer UI displayed the ownership-rejection error before any payload mutation. No VM checkpoint restore, deletion, rename, or overwrite was used.

## Root cause and remediation

M14-A used an elevated `[InstallDelete]` rule that recursively deleted `{app}\*` before payload installation. A user-selected `/DIR=` path could therefore contain foreign data.

M14-I removes that rule. Before prerequisites or payload mutation, a non-empty destination is accepted only when the Quail AppId uninstall registration has a matching `InstallLocation`, a matching existing `unins000.exe`, and no reparse point in its destination path or tree. Empty and non-existent destinations remain valid. The same validator runs from `PrepareToInstall` (including `/DIR=`, `/SILENT`, and `/VERYSILENT`) and the interactive directory-page callback. A recognized install is updated by normal Inno Setup file replacement only.

The legacy cleanup include contains exactly 187 actual Quail 0.1 self-contained file paths, each guarded by `IsRecognizedQuailInstallation`. It uses only `Type: files`; no wildcard, directory, or full-tree cleanup exists. This includes `coreclr.dll`.

## Technical candidate

- Packaging candidate source: `163d853f5a049bc93d2a30a750f8830c6121885e`.
- Installer: `Quail-0.2.0-Setup.exe`.
- Installer SHA-256: `31a34fa3a1eb6639aba5b6b7f61be43aa14a8240b580967fd5281bc4abbcd3ea`.
- Installer size: `9,949,565 B`.
- Application payload: 56 files, `43,927,601 B`, tree identity `23d0206bfcf65bc2c319f75eb00908daeb88fa263319f1962334e7f46db1e312`.
- `Quail.exe`: `770aed13e5a433e0d8b2061f5dcf1248fb304b21f7a958554caa893731ac24cc`.
- `Quail.Cli.exe`: `bd86f7f60aa6c74fa96f2b1c129d9c8b585360cfe97ff1625646d8a9c65d56a7`.

Every one of the 56 staged files was compared by relative path, byte length, and SHA-256 with a payload extracted from the preserved M13-D installer. The source build was also checked separately; M13-D-equivalent bytes require the preserved verified payload because PDB/provenance source-root data is not byte-reproducible from the public-staging checkout. No application source or package changed, so the M13-D runtime/performance evidence remains applicable.

Prerequisite pins were unchanged: .NET Desktop Runtime 10.0.11 SHA-256 `61d2e1447b185d6f99c0d5799896240b48246f5440648bc031ebdb159a3bf3d1`, Windows App Runtime SHA-256 `851c35b0b0a59ce4c55f9171f601193322fc3413143b0dc3390ea11e14cfa7fc`, and VC++ Redistributable SHA-256 `843068991daaa1f73ad9f6239bce4d0f6a07a51f18c37ea2a867e9beca71295c`.

## Verification

- `dotnet test tests\Quail.Core.Tests\Quail.Core.Tests.csproj -c Release --no-restore`: PASS, 176/176.
- App Release build: PASS, 0 warnings / 0 errors.
- CLI Release build: PASS, 0 warnings / 0 errors.
- Windows App Runtime detection tests: PASS, 6 cases.
- Installer cleanup static guard: PASS, 187 exact legacy entries.
- M13-D payload byte-identity check: PASS, 56 files / `43,927,601 B`.
- Canonical installer build and committed prerequisite guard: PASS.

## Quail-Lab focused lifecycle matrix

All runs used the same hash-verified candidate above; legacy setup was hash-verified as `b39894a0ad807391af6abc1874ae02ff6e3e0c4133eab51e4bcd5f4460ab66a4`.

| Test | Result | Evidence |
| --- | --- | --- |
| T1 default fresh install | PASS | GUI/CLI hashes, one PATH entry, one uninstall entry, shortcut |
| T2 empty custom directory | PASS | payload, PATH, shortcut, and uninstall location targeted custom directory |
| T3 silent unowned custom directory | PASS | install rejected; root and nested foreign sentinels remained; no payload/global registration |
| T4 interactive unowned directory | PASS | actual setup UI rejected the non-empty unrecognized directory; root and nested sentinels retained their pre-test SHA-256 values; no payload/global state |
| T5 unowned default directory | PASS | install rejected; root and nested sentinels remained; no partial payload |
| T6 real 0.1 to 0.2 upgrade | PASS | AppId continuity, foreign sentinels retained, `coreclr.dll` absent, all 187 legacy paths absent |
| T7 same-version reinstall | PASS | default and custom owned directories retained foreign and nested foreign files |
| T8 uninstall | PASS | payload, PATH, shortcut, and registration removed; foreign files retained |
| T9 custom reinstall/uninstall | PASS | same ownership behavior as default directory |

No checkpoint restore, deletion, rename, or overwrite occurred. No publication-sensitive action occurred.
