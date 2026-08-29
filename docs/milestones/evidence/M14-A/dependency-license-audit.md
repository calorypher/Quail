# M14-A dependency and license audit — stopped partial record

## Status

The production package graph and vulnerability query were completed before the
security stop. Final app-local payload/license/notice and prerequisite
redistribution audits were not completed and have no release disposition.

## Resolved production graph

`Quail.Core` directly references `Microsoft.Data.Sqlite` 10.0.11;
`Quail.Cli` receives it through Core. The resolved SQLite chain is
`Microsoft.Data.Sqlite.Core` 10.0.11 and `SQLitePCLRaw.bundle_e_sqlite3`,
`SQLitePCLRaw.core`, `SQLitePCLRaw.lib.e_sqlite3`, and
`SQLitePCLRaw.provider.e_sqlite3`, all 2.1.12.

`Quail.App` directly references:

- `Microsoft.Data.Sqlite` 10.0.11;
- `Microsoft.WindowsAppSDK.WinUI` 2.3.6;
- `Microsoft.WindowsAppSDK.Runtime` 2.4.0;
- `Microsoft.WindowsAppSDK.InteractiveExperiences` 2.1.6;
- `System.Drawing.Common` 10.0.0.

Additional resolved transitives are `Microsoft.Web.WebView2` 1.0.3719.77,
`Microsoft.Win32.SystemEvents` 10.0.0,
`Microsoft.Windows.SDK.BuildTools` 10.0.26100.4654,
`Microsoft.Windows.SDK.BuildTools.MSIX` 1.7.251221100,
`Microsoft.WindowsAppSDK.Base` 2.0.4,
`Microsoft.WindowsAppSDK.Foundation` 2.3.9, and the SQLite chain above.

The project graph alone does not prove which build-only/runtime assets are in
the final payload. The final payload inventory was deferred after the blocker.

## License metadata observed

- `Microsoft.Data.Sqlite*`, `System.Drawing.Common`, and
  `Microsoft.Win32.SystemEvents`: NuGet expression `MIT`.
- `SQLitePCLRaw*`: NuGet expression `Apache-2.0`.
- Windows App SDK components and WebView2: package-local Microsoft license
  files, with package-local notice files where supplied.
- Windows SDK BuildTools: Microsoft SDK license metadata; final build-only
  versus redistributed classification remains deferred.

This metadata is input, not the completed redistribution/notice conclusion.

## Installer prerequisites

The committed bootstrap pins .NET Desktop Runtime 10.0.11, Windows App Runtime
2.4.0.0 stable, and VC++ x64 Redistributable minimum 14.51.36247.0. Sources are
direct Microsoft HTTPS locations with committed SHA-256 values. Their complete
legal disposition and the Inno Setup stub/runtime review remain deferred.

## Vulnerability audit

On 2026-08-29,
`dotnet list package --vulnerable --include-transitive --no-restore` queried the
configured NuGet feeds for Core, CLI, and App. All three reported no known
vulnerable direct or transitive package.

## Deferred artifacts

No `LICENSE` or `THIRD-PARTY-NOTICES.md` was added and no final license
inventory was claimed. Those outputs must use the remediated exact payload.
