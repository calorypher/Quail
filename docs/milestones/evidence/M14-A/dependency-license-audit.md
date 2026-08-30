# M14-A rerun — dependency and license audit

## Status

The exact production dependency graph, vulnerability/deprecation queries, and
payload-origin mapping were completed before the security stop. Exact package
license files were inspected, but the final public notice set and complete
prerequisite redistribution disposition were not finalized after the stop.

## Resolved production graph

`Quail.Core` directly references `Microsoft.Data.Sqlite` 10.0.11.
`Quail.Cli` receives it through Core. The SQLite chain is
`Microsoft.Data.Sqlite.Core` 10.0.11 plus
`SQLitePCLRaw.bundle_e_sqlite3`, `SQLitePCLRaw.core`,
`SQLitePCLRaw.lib.e_sqlite3`, and `SQLitePCLRaw.provider.e_sqlite3`, all
2.1.12.

`Quail.App` directly references:

- `Microsoft.Data.Sqlite` 10.0.11;
- `Microsoft.WindowsAppSDK.WinUI` 2.3.6;
- `Microsoft.WindowsAppSDK.Runtime` 2.4.0;
- `Microsoft.WindowsAppSDK.InteractiveExperiences` 2.1.6;
- `System.Drawing.Common` 10.0.0.

Additional resolved transitives are:

- `Microsoft.Data.Sqlite.Core` 10.0.11;
- `Microsoft.Web.WebView2` 1.0.3719.77;
- `Microsoft.Win32.SystemEvents` 10.0.0;
- `Microsoft.Windows.SDK.BuildTools` 10.0.26100.4654;
- `Microsoft.Windows.SDK.BuildTools.MSIX` 1.7.251221100;
- `Microsoft.WindowsAppSDK.Base` 2.0.4;
- `Microsoft.WindowsAppSDK.Foundation` 2.3.9;
- the SQLitePCLRaw 2.1.12 chain above.

The canonical 56-file payload was separately mapped to package/runtime origins;
build-only SDK package assets were distinguished from redistributed files.

## Security and maintenance status

On 2026-08-30, NuGet's supported
`--vulnerable --include-transitive` query reported no known vulnerable direct
or transitive package for Core, CLI, or App. The App graph reported no
deprecated package. Available newer versions were recorded as normal
maintenance information, not a security gate.

The bundled native SQLite reports 3.53.3. SQLite's official CVE chronology
places the relevant published fixes no later than 3.53.2. The later 3.53.4
release was not identified as a required security remediation for Quail's
trusted application database use.

## License text review

- Quail, `Microsoft.Data.Sqlite*`, `System.Drawing.Common`, and
  `Microsoft.Win32.SystemEvents`: MIT-family package texts/notices;
- SQLitePCLRaw components: Apache-2.0;
- WebView2: package-local BSD-3-Clause license and notice requirements;
- current Windows App SDK package files: identical corrected Microsoft Windows
  App SDK terms that expressly cover NuGet-binplaced redistribution;
- Windows SDK .NET projection: Microsoft Windows SDK terms;
- CsWinRT `WinRT.Runtime`: MIT upstream terms;
- SQLite native library: public-domain upstream classification.

Package-local texts were read rather than relying only on NuGet SPDX labels.
The expected final repository work is a canonical MIT `LICENSE`, a minimal
payload-derived notice file, and any exact full text required by the final
redistributed set. Those files were intentionally not added after the security
stop because the remediated payload may change.

## Disposition

**PASS — vulnerability/deprecation gate for the pre-stop graph.**

**INCOMPLETE — final redistribution/notices disposition deferred by the
security stop.** No incompatibility was found before the stop, but this file is
not a release clearance.
