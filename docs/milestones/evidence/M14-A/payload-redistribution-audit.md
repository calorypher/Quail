# M14-A rerun — exact payload and prerequisite redistribution audit

## Exact application payload

The canonical source build produced 56 files totaling 43,926,893 bytes. Each
file was hashed and mapped to Quail source output, generated build metadata, a
NuGet package, or a .NET/Windows runtime pack.

Bundled third-party/runtime families include:

| Component | Version/source | Bundled | License/notice action |
|---|---|---:|---|
| Microsoft.Data.Sqlite | 10.0.11 | yes | MIT attribution in final notice |
| SQLitePCLRaw components | 2.1.12 | yes | Apache-2.0 notice and required text |
| native SQLite (`e_sqlite3`) | 3.53.3 | yes | record public-domain upstream classification |
| Microsoft.Web.WebView2 managed/loader files | 1.0.3719.77 | yes | reproduce package BSD license/notice as required |
| Windows App SDK projections/bootstrap/WinUI | resolved 2.x graph | yes | record exact Microsoft package terms/notices |
| System.Drawing.Common / SystemEvents | 10.0.x | yes | MIT and package notice attribution |
| Windows SDK .NET projection / CsWinRT runtime | runtime-pack files | yes | record Windows SDK and MIT terms accurately |
| Segoe fonts and Windows Shell icons | Windows system | no | no bundled notice; document as system-provided |

Build-only Windows SDK packages were not misclassified as application payload.
Quail-owned executables, libraries, PDBs, JSON configuration, and branding were
separated from third-party files.

The exact final notice file was not generated because a packaging remediation
may change the payload. The inventory above is audit evidence, not final legal
clearance.

## Installer-time prerequisites

Prerequisites are downloaded only when required from committed Microsoft HTTPS
URLs. They are hash-verified before execution and are not embedded in the Quail
installer.

| Prerequisite | Publisher | Required/pinned version | Committed SHA-256 |
|---|---|---|---|
| .NET Desktop Runtime x64 | Microsoft | 10.0.11 | `61d2e1447b185d6f99c0d5799896240b48246f5440648bc031ebdb159a3bf3d1` |
| Windows App Runtime x64 | Microsoft | 2.4.0.0 stable | `851c35b0b0a59ce4c55f9171f601193322fc3413143b0dc3390ea11e14cfa7fc` |
| VC++ Redistributable x64 | Microsoft | minimum 14.51.36247.0 | `843068991daaa1f73ad9f6239bce4d0f6a07a51f18c37ea2a867e9beca71295c` |

The canonical builder's acquisition/pin/payload guard passed. The cached files
matched the committed hashes and each had a valid Microsoft Authenticode
signature. Observed sizes were 60,001,888 bytes, 116,423,480 bytes, and
18,731,856 bytes respectively.

Official deployment/redistribution sources reviewed:

- [.NET library and redistribution license](https://dotnet.microsoft.com/en-us/dotnet_library_license.htm);
- [Windows App SDK downloads](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads);
- [Windows App SDK framework-dependent deployment](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps);
- [VC++ runtime redistribution](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files?view=msvc-170);
- [supported VC++ Redistributable downloads](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170).

## Disposition

The source URLs, publisher, versions, pins, non-embedded download mechanism,
hash verification, and signatures were verified. No fundamental incompatibility
was found before the security stop. The final public redistribution statement,
Inno Setup disposition, and exact notice/license output remain **incomplete**
and must be finalized against the remediated payload.
