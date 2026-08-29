# M09 results — WinUI 3 deployment spike

## Post-M09 deployment decision update — 2026-08-25

The original M09 recommendation below remains valid as the historical outcome of the spike under its original decision criteria, but it is **superseded for the Quail 0.2 production deployment direction** by a later product-priority change.

M09 prioritized complete first-install reliability and total first-install transfer on the observed clean lab baseline. After the production WinUI application existed, the installed self-contained payload was judged too large and too runtime-heavy for Quail's lightweight product goals. The current priority is instead to minimize Quail's own installer and installed application footprint while using supported, centrally serviced Microsoft runtimes wherever practical. A one-time prerequisite download on a machine missing those runtimes is acceptable.

M09 already showed that the framework-dependent variant materially reduced Quail-owned footprint without a decision-grade startup or hidden-idle penalty: the fixture publish was 83,820,750 bytes / 56 files versus 237,518,505 bytes / 523 files; the installer was 22,529,096 bytes versus 70,098,985 bytes; and the installed application directory was 88,324,373 bytes versus 242,121,776 bytes. The clean M01 baseline required 135,155,336 bytes of missing VC++ x64 and Windows App Runtime prerequisites, while the deliberately exercised missing-.NET case added 60,001,888 bytes. Reinstall and upgrade did not redownload prerequisites once they were present.

Therefore the preferred 0.2 direction before M13 is now **framework-dependent unpackaged WinUI 3 with prerequisite detection/bootstrap**. Shared .NET Desktop Runtime, Windows App Runtime, and VC++ runtime components should remain shared rather than being copied app-local merely to eliminate first-install network transfer. Quail's installation directory should contain Quail binaries/resources and only dependencies that genuinely must remain app-local under the supported deployment model.

M13 must revalidate this choice against the actual production `Quail.exe`, not merely reuse the M09 fixture result. It must measure installer size, installed-directory size and file count, verify prerequisite-present and missing-prerequisite behavior, and audit the production publish so unsupported manual DLL deletion is not used as a footprint optimization. Trimming, Native AOT, or other more invasive deployment techniques are separate decisions and should not be introduced unless the normal supported framework-dependent model leaves a demonstrated problem.

## Original recommendation

**Choose A — fully self-contained for Quail 0.2.**

The final detector showed that M01-clean contains the Windows App Runtime 2.4 Framework package, but not its complete runtime set. A clean B install therefore correctly downloads the missing VC++ x64 and Windows App Runtime installers (135,155,336 bytes) before copying Quail; the separately exercised missing-.NET branch adds another 60,001,888 bytes. The fully self-contained A installer is 47,569,889 bytes larger on disk, but its complete first install is smaller than B's observed setup-plus-download path and has no prerequisite network/bootstrap failure mode. Startup and hidden-idle results are practically equivalent. The material first-install reliability and transfer advantage outweigh B's shared-runtime servicing benefit for the 0.2 shell.

## Clean-target methodology

The approved `M01-clean` checkpoint is the pre-implementation/project Windows 11 Pro baseline with pre-existing `quailadmin` SSH/minimal automation and no later Quail development. It is not described as an untouched retail/factory Windows image. It was restored before clean-target scenarios; current host artifacts were copied into the guest and SHA-256 checked. No inbox package was removed. `M09-pristine` and `M09-no-prereqs` were not created.

M01 inventory: Windows 11 Pro 10.0.26200 x64; SDK 10.0.400; Microsoft.WindowsDesktop.App 10.0.11; Windows App Runtime 2.4.0 Framework x64 and x86 only; no Windows App Runtime 2 Main, Singleton, or x64 DDLM; and no traditional VC++ x64 Redistributable registration. SSH runs in non-interactive Session 0 and therefore cannot provide a visual desktop or SendInput/hotkey smoke.

## T01–T13

| ID | Outcome |
|---|---|
| T01 | Pass — Quail.Core Release tests 53/53, M08 harness Release build, final publish and Inno Setup 7.1.0 builds. |
| T02 | Pass — final publish, installer, and installed-directory metrics recorded. |
| T03 | Pass — host, 5 warmups + 30 same-boundary `visible-ready` runs per variant. |
| T04 | Pass — host, 3-second settle + 10 one-second hidden-idle samples per variant. |
| T05 | Pass — final A installed on M01 without central runtime changes and uninstalled cleanly. |
| T06 | Pass — final B detected the incomplete Windows App Runtime set, downloaded and SHA-256-verified only missing VC++ x64 and Windows App Runtime installers (135,155,336 B), then installed payload. |
| T07 | Pass — B same-version reinstall had zero prerequisite download lines. |
| T08A | Pass — B succeeded with process outbound traffic blocked when prerequisites were present; zero downloads. |
| T08B | Pass with automation limitation — M01's natural VC++ absence plus VM HTTP(S) block produced download attempt, no verification, no VC++ registration, and no Quail payload. The silent Session 0 run could not display the explicit UI failure dialog. |
| T09 | Pass — A/B same-version reinstall returned 0; B did not redownload prerequisites. |
| T10 | Pass — final staged 0.9.0 -> 0.9.1 ran for A and B. Each left one correct 0.9.1 uninstall entry, contained `App.xbf`, `MainWindow.xbf`, and `Quail.M08.WinUi.pri`, and reached the host `visible-ready` boundary from the corresponding final 0.9.1 staged payload. B had zero prerequisite downloads on upgrade. |
| T11 | Pass — A removes own payload; B removes own payload but retains .NET/Desktop, Windows App Runtime, and VC++ x64. |
| T12 | Pass — servicing analysis below. |
| T13 | Pass — A selected after the corrected clean-baseline prerequisite evidence. |

## Host measurements

| Variant | Startup p50 | p95 | max | avg | Hidden idle WS avg | Private bytes avg | CPU avg / max |
|---|---:|---:|---:|---:|---:|---:|---:|
| A self-contained | 395.286 ms | 416.002 ms | 432.476 ms | 396.687 ms | 150,791,373 B | 163,876,864 B | 0.0000% / 0.0000% |
| B framework-dependent | 389.425 ms | 427.986 ms | 431.002 ms | 395.606 ms | 151,880,909 B | 164,941,824 B | 0.0097% / 0.0971% |

The small startup difference is not decision-grade. Both are effectively idle in this bounded sample. The fixture itself exceeds Quail's future 100 MB working-set target, so these are deployment comparison figures, not a production resource claim.

## Servicing and support

A services .NET and Windows App SDK only when Quail ships a new app-local payload; security remediation requires a Quail update. B receives normal servicing from the shared runtime owners but its installer must retain precise full-set Windows App Runtime detection, runtime-family-aware .NET detection, version-aware VC++ detection, immutable official URLs, hashes, and early failure behavior. On M01, B's network cost is VC++ x64 plus the missing Windows App Runtime components; when all requirements are present, including offline, it needs no network. The bootstrap is real Inno Setup code using Microsoft installers, not a manual prerequisite instruction.

## Limitations

- Guest visual GUI/hotkey/tray smoke was not claimed because SSH is Session 0; final publishes did reach host `visible-ready`.
- `dotnet publish` did not automatically copy `App.xbf`, `MainWindow.xbf`, and `Quail.M08.WinUi.pri` for this unpackaged fixture. M09 stages them and final T10 verifies them after upgrade.
- The missing-.NET branch was exercised by normally uninstalling SDK 10.0.400 inside Quail-Lab. The first recheck exposed and corrected a registry-based false positive; the final directory-based detector then completed an end-to-end missing-.NET bootstrap, SHA-256 verification, runtime installation, and payload install. The guest's non-interactive Session 0 remains unsuitable for visual UI smoke; host pipe-based `visible-ready` is the functional launch boundary.
- No Store, winget, updater, signing, production `Quail.exe`, or true 0.1 -> 0.2 upgrade was implemented.

## Official sources

- https://learn.microsoft.com/windows/apps/windows-app-sdk/deploy-unpackaged-apps
- https://learn.microsoft.com/windows/apps/windows-app-sdk/deploy-self-contained-apps
- https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist
- https://learn.microsoft.com/dotnet/core/versions/selection
