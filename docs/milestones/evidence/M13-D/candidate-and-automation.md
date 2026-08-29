# M13-D candidate and automated gates

## Exact candidate

- Candidate implementation source: `e6089749b5d4c6614c833f37aaf631f90b84ac1d`.
- Candidate location (ignored local preservation):
  `artifacts/rc/0.2.0/e608974/Quail-0.2.0-Setup.exe`.
- Installer SHA-256: `ea2df3ed91fc2a79a87416e044e0e4e3dea0501de9256699b5ee2e10f3009adf`.
- Installer size: 9,946,816 bytes.
- Staged framework-dependent payload: 56 files, 43,927,601 bytes.
- Payload tree SHA-256: `23d0206bfcf65bc2c319f75eb00908daeb88fa263319f1962334e7f46db1e312`.
- Staged `Quail.exe` SHA-256:
  `770aed13e5a433e0d8b2061f5dcf1248fb304b21f7a958554caa893731ac24cc`.
- Staged `Quail.Cli.exe` SHA-256:
  `bd86f7f60aa6c74fa96f2b1c129d9c8b585360cfe97ff1625646d8a9c65d56a7`.
- Canonical prerequisite-pins SHA-256:
  `36834e841e2d0b0d24a372ed106800bc01f099197d8b0336b6b5bc5e21085a88`.

The original M13-D candidate `19b72731a0ea8796a526d015a7fbc4a7960f427d`
was invalidated only because this candidate replaces the Windows App SDK
metapackage with supported explicit components. It is not combined with this
candidate's result set. The preserved replacement installer and both manifests
were copied before the replacement runtime campaign; later commits contain
only M13-D evidence and results.

## Automated gates before freeze

| Gate | Result |
|---|---|
| `scripts/test-windows-app-runtime-detection.ps1` | PASS, 6 deterministic fixtures |
| `dotnet test tests/Quail.Core.Tests/Quail.Core.Tests.csproj -c Release --no-restore` | PASS, 176 passed / 0 failed |
| Release `Quail.App` `win-x64` build | PASS, 0 warnings / 0 errors |
| Release `Quail.Cli` `win-x64` build | PASS, 0 warnings / 0 errors |
| Canonical `scripts/build-installer.ps1` | PASS, Inno Setup 7.1.0 |
| Payload guard | PASS; no private .NET runtime and no AI/ML payload listed in `payload-dependency-audit.md` |
| Version checks | PASS; `Quail.exe` and installed CLI report 0.2.0 |
| Prerequisite pins | PASS; build consumed the committed three-input manifest without pin refresh |

The count remains 176, matching M13-B and M13-C. The package composition and
installer guard change no Core tests and add no production feature.

## Final deployment revalidation

The payload-contract change required the final installer to repeat the M13-C
deployment matrix. On Quail-Lab, the SHA-verified installer completed a
prerequisites-present reinstall while outbound access for that setup executable
was blocked; its log contained no download branch and prerequisite transfer was
0 bytes. The final GUI/CLI hashes, `quail 0.2.0` CLI version, one machine PATH
entry, and Start Menu shortcut were correct.

The final candidate also passed real released `v0.1.0` -> final 0.2 upgrade,
same-version reinstall, and uninstall/data-preservation checks. The natural
missing-prerequisite baseline was no longer available without checkpoint restore
or shared-runtime removal, so it was not manufactured; M13-C's natural
bootstrap evidence and the unchanged Runtime 2.4 deployment contract remain
the applicable missing-state evidence.
