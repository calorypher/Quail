# M13-C results — Production packaging and 0.1 to 0.2 upgrade

## Status

**COMPLETE — ready for M13-D.** No packaging decision is required. The automated packaging, bootstrap, upgrade, data-preservation, reinstall, uninstall, bounded interactive GUI smoke, and post-QA prerequisite hardening gates passed on Quail-Lab.

## Source and deployment model

- Branch: `m13-c-production-packaging-upgrade-prepared`.
- Initial production-packaging implementation source: `71466fb93c71299d18bf11c531ee7fa9ec2e34de`.
- Final production implementation/artifact source: `c6ceba94605a3cbfd85972d9be945e7a181ca470`. Documentation-only evidence commits after this source do not change the staged payload or installer.
- Canonical product version is changed to `0.2.0` for App, CLI, and Inno Setup metadata.
- The payload is Windows 11 x64, unpackaged WinUI 3, framework-dependent, untrimmed, non-AOT, and folder based. `Quail.exe` and `Quail.Cli.exe` are merged only after every duplicate file has an identical SHA-256.
- Final staged payload: 71 files, 86,631,373 bytes. Final installer: 23,664,414 bytes, SHA-256 `a621be3eeb85019f422dd9e763a90e0fb0836ff19b13a3aad42e2f59c49c2e2f`. The previously measured installed payload footprint is 73 files, 91,138,646 bytes; the post-QA change did not alter payload-copy, cleanup, PATH, or data-ownership semantics.

## Prerequisites

The installer uses a directory-based .NET Desktop Runtime detector, a versioned VC++ x64 detector, and a Windows App Runtime detector that evaluates the package set registered for the calling user rather than `Get-AppxPackage -AllUsers`. It requires a complete, healthy x64 stable Framework/Main/Singleton/DDLM set. Framework, Main, and DDLM must have the required major version and be at least the supported minimum; a compatible newer stable 2.x release is accepted, while preview/experimental names and a package registered only for another profile are rejected. Missing Microsoft installers run only before `[Files]`, and download, integrity, or prerequisite-install failure occurs before payload copy.

`packaging/prerequisite-pins.json` is the source-controlled production pin contract. Its direct Microsoft HTTPS source, SHA-256, and version metadata are checked for every cached or downloaded input. A normal build only consumes matching pins; it cannot refresh a URL, hash, or runtime minimum from newly downloaded bytes. `scripts/get-installer-prerequisites.ps1` is the explicit acquisition/audit path and still verifies the committed SHA-256 before use.

Pinned inputs: .NET Desktop Runtime 10.0.11 (`61d2e1447b185d6f99c0d5799896240b48246f5440648bc031ebdb159a3bf3d1`); Windows App Runtime 2.4.0.0 (`851c35b0b0a59ce4c55f9171f601193322fc3413143b0dc3390ea11e14cfa7fc`); VC++ x64 minimum 14.51.36247.0 (`843068991daaa1f73ad9f6239bce4d0f6a07a51f18c37ea2a867e9beca71295c`).

## Real 0.1 baseline and lab evidence

- Real released baseline: tag `v0.1.0`, source `a2b6927d2d550b183b8597df5d20ba87d44caa2a`; retained published `Quail-0.1.0-Setup.exe`, 31,218,184 bytes, SHA-256 `b39894a0ad807391af6abc1874ae02ff6e3e0c4133eab51e4bcd5f4460ab66a4`.
- The initial natural baseline had .NET Desktop Runtime 10.0.11 but no VC++ x64 registration and no Windows App Runtime packages. The final post-QA natural baseline had .NET Desktop Runtime 10.0.11, no VC++ registration, and only incomplete older Windows App Runtime registrations; it still correctly required the complete supported runtime set. No runtime was removed to create either state.
- The retained 0.1 installer and the final 0.2 installer were transferred and hash-verified in the lab. 0.1 installed successfully. The final 0.2 installer then upgraded in place successfully: `Quail.exe` and `Quail.Cli.exe` existed, `coreclr.dll` no longer existed, and the single uninstall entry reported `0.2.0`.
- The final natural missing state required VC++ x64 plus Windows App Runtime: `18,731,856 + 116,423,480 = 135,155,336` bytes. The setup's committed-pin SHA-256 verification and prerequisite install completed before the 0.2 payload was copied. .NET Desktop Runtime was detected present, so its download was `0 B`.
- With all prerequisites subsequently present, an outbound firewall block scoped only to the final setup executable produced a successful 0.2 install. The detector took no download branch (`0 B`); `Quail.exe` and `Quail.Cli.exe` existed.
- A clean 0.2 installation after uninstall succeeded with the prerequisites already present, leaving exactly one machine PATH entry for `C:\Program Files\Quail` and the expected `Quail.lnk` Start Menu entry.
- Same-version reinstall completed successfully and preserved all three controlled sentinels. Their SHA-256 remained `553072ffe1308faf1e710dce28d8bc86261b5f4b631cbac5bcb607304d58f378` across install, reinstall, and uninstall. This evidence remains applicable after the post-QA detector/pin change because `[InstallDelete]`, payload-copy, PATH, data-ownership, and uninstall paths were unchanged.
- The file-level uninstall passed: 73 installer-owned files existed before uninstall; after exit 0, `{app}` was empty (the empty directory remained), `Quail.exe`, `Quail.Cli.exe`, and the Start Menu link were absent. VC++ (`Installed=1`) and Windows App Runtime packages remained. This result likewise remains applicable to the final installer because its uninstall and payload-cleanup paths were unchanged.

## Verification

- Focused Windows App Runtime detector fixture seam: 6 passed. It covers required stable 2.4.0, compatible newer stable 2.x, older/missing DDLM rejection, other-user-only registration rejection, and preview/experimental rejection.
- Committed-pin acquisition/validation: PASS; all three cached Microsoft installers were re-hashed against `packaging/prerequisite-pins.json` before the final build.
- Release tests: 176 passed, 0 failed.
- Release CLI build: 0 warnings, 0 errors.
- Release App build: initially exposed PRI warnings from ignored historical files under `src/Quail.App/artifacts`; the project now excludes that directory and a clean Release App build passed with 0 warnings, 0 errors.
- Inno Setup 7.1.0 compiled the final installer successfully.
- Final installed CLI smoke: PASS — `C:\Program Files\Quail\Quail.Cli.exe --version` reported `quail 0.2.0`.
- Final installed-GUI smoke: PASS. A user at the unlocked Quail-Lab desktop confirmed that the final installed `C:\Program Files\Quail\Quail.exe` works. This was intentionally a bounded packaging smoke, not the M13-D lifecycle/resource campaign.

## Controlled data locations

The privacy-safe sentinels were a LocalAppData Quail settings-class file, a ProgramData Quail Indexes storage-class file, and a separate user-owned data-volume index-class file. No host-specific paths or real user data are recorded. The installer never touched them.

## Disposition and limitations

Disposition: **ready for M13-D**. The M13-D independent QA and exact-candidate RC campaign may start after this branch is independently reviewed and merged. M13-C did not run M13-D's full startup, idle RAM/CPU, hotkey timing, lifecycle, focus, or physical-host campaign.
