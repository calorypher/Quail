# M13-D AI/ML payload dependency audit

## Scope and candidate decision

The audit began against superseded candidate `19b72731a0ea8796a526d015a7fbc4a7960f427d`
because its installed directory contained AI/ML files even though Quail has no
AI or ML feature. The supported component-package composition below changed
the production package graph, so that candidate is not used for subsequent RC
disposition.

The replacement exact candidate is
`e6089749b5d4c6614c833f37aaf631f90b84ac1d`; its preserved installer is
`artifacts/rc/0.2.0/e608974/Quail-0.2.0-Setup.exe`, SHA-256
`ea2df3ed91fc2a79a87416e044e0e4e3dea0501de9256699b5ee2e10f3009adf`.

## Provenance in the superseded payload

`Quail.App.csproj` directly referenced the `Microsoft.WindowsAppSDK 2.4.0`
metapackage. Its NuGet manifest and `obj/project.assets.json` show direct
component dependencies on `Microsoft.WindowsAppSDK.AI 2.4.4`,
`Microsoft.WindowsAppSDK.ML 2.1.74`, `Microsoft.WindowsAppSDK.Search 2.4.4`,
and `Microsoft.WindowsAppSDK.Widgets 2.0.5` in addition to the WinUI path.
`Quail.deps.json` carried those package libraries into the framework-dependent
publish graph.

| Payload asset | Originating package and chain | Asset class | Superseded file bytes |
|---|---|---|---:|
| `DirectML.dll` | `Microsoft.WindowsAppSDK` -> `Microsoft.WindowsAppSDK.ML` -> `Microsoft.Windows.AI.MachineLearning 2.1.74` | RID-native | 18,700,224 |
| `onnxruntime.dll` | same ML chain | RID-native | 21,659,280 |
| `Microsoft.Windows.AI.MachineLearning.dll` | same ML chain | RID-native | 903,464 |
| `Microsoft.ML.OnnxRuntime.dll` | same ML chain | managed runtime | 237,632 |
| `Microsoft.Windows.AI.MachineLearning.Projection.dll` | same ML chain | managed WinRT projection | 84,232 |
| `System.Numerics.Tensors.dll` | `Microsoft.Windows.AI.MachineLearning` -> `System.Numerics.Tensors 9.0.0` | managed runtime | 410,936 |

The named files alone total 41,995,768 bytes. `Microsoft.WindowsAppSDK.AI`
also supplied several `Microsoft.Windows.AI.*.Projection.dll` managed
projections. They are classified as AI-related payload, not as a required
general `Microsoft.Windows.*` dependency.

## Supported alternative and resulting payload

Microsoft's Windows App SDK component-package guidance permits selecting
individual components instead of the metapackage. Quail now references only:

- `Microsoft.WindowsAppSDK.WinUI 2.3.6`;
- `Microsoft.WindowsAppSDK.Runtime 2.4.0`; and
- `Microsoft.WindowsAppSDK.InteractiveExperiences 2.1.6`, explicitly aligned
  to the Runtime package's version contract.

`WinUI` transitively carries the required Base/Foundation/Interactive
Experiences UI path; `Runtime` retains supported framework-dependent
bootstrapping. No unsupported delete, blacklist, `.deps.json` edit, trimming,
AOT, or version downgrade is used. `scripts/build-installer.ps1` now rejects
the named unused AI/ML files and `Microsoft.Windows.AI.*.Projection.dll` in
the final payload.

| Measure | Superseded candidate | Replacement candidate |
|---|---:|---:|
| Staged payload files | 71 | 56 |
| Staged payload bytes | 86,631,373 | 43,927,601 |
| Installer bytes | 23,664,405 | 9,946,816 |
| Audited AI/ML files in app-local payload | present | absent by guard |

Final normal Quail-Lab installation measured 58 files / 48,449,602 bytes:
the replacement's 56 files / 43,927,601-byte application payload plus
`unins000.exe` and `unins000.dat` (4,522,001 bytes). M13-C had measured 73
files / 91,138,646 bytes, so the final installed footprint is lower by 15 files
and 42,689,044 bytes.

The replacement passed the focused Runtime detector (6 fixtures), full Release
suite (176 tests), warning-free Release App and CLI builds, canonical
installer build, framework-dependent guard, and version/pin checks.

The supported-composition conclusion is based on Microsoft's [Windows App SDK
1.8 release notes](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-8),
which describe the metapackage, selectable component packages, and the Runtime
component for framework package deployment. Microsoft's [framework-dependent
deployment guidance](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)
also documents the Windows App Runtime as the supported dependency for
unpackaged applications. The local 2.4.0 NuGet targets and resolved assets are
the authoritative version-specific evidence for the selected pins above.

## Runtime module-load finding

On the exact installed replacement candidate, 500 ms process-module samples
during resident startup, hidden idle, Quick Search, a real indexed search,
Settings, and an open Index Manager contained none of `DirectML.dll`, `onnxruntime.dll`,
`Microsoft.ML.OnnxRuntime.dll`, `Microsoft.Windows.AI.MachineLearning.dll`,
`Microsoft.Windows.AI.MachineLearning.Projection.dll`,
`System.Numerics.Tensors.dll`, or `Microsoft.Windows.AI.*.Projection.dll`.

No retained durable module-load sample exists for the superseded metapackage
candidate, so this result is not evidence about its historical load state.
The supported conclusion is instead that the replacement contains and loads
none of these modules while passing the final Quail 0.2 runtime and deployment
matrix. The superseded finding is therefore an app-local disk/install-footprint
finding; it does not directly explain private memory, and no 1:1 working-set
attribution is made for shared or mapped framework pages.
