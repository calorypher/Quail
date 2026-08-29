# M14-A asset, font, and icon provenance audit

## Bundled Quail-owned branding

| Asset | SHA-256 | Classification |
|---|---|---|
| `quail-app-icon-32px.png` | `015DA35E33C734B93BFA15B47DF1D9BA93E7DF0369427410908489F9D25A6637` | Quail-owned runtime asset |
| `quail-app-icon-48px.png` | `A889C25C22D80EE8B5AAFBF6E6E3FFBD8D5FE8091B52F965B2AC9EFFD45F3C9B` | Quail-owned runtime asset |
| `quail-tray-icon-16px.png` | `E19CE347F912941CC8118098AF9458712C65483B7E1F39AC1006911162838242` | Quail-owned runtime asset |
| `quail-app-icon.ico` | `2A8FC0483A6B62F94FB19D9AA63130BA8955E1B4A0AA3FE3D31213F411B05389` | Quail-owned executable resource |
| `quail-feather-A-gradient.svg` | `4D2D5BE28DE700AA10CC744B48153022F545621D57C31AF3C64C887C73E19F1D` | Quail-owned runtime mark |

M10 provenance maps source frames to runtime files and the deterministic ICO.
The ICO helper validates each source hash and copies original PNG byte streams
without redrawing or resampling. Current hashes match that record.

These are Quail project branding, not third-party assets. They should not be
attributed in third-party notices. The MIT code license does not itself settle
rights in the Quail name or branding; that distinction remains required.

## System-referenced assets

- UI text uses installed Windows `Segoe UI Variable`.
- action/fallback glyphs use installed Windows `Segoe Fluent Icons`.
- result icons are requested at runtime from Windows Shell with
  `SHGetFileInfo` and cached for display.

No Segoe/Fluent font file, Shell icon file, or third-party icon pack is stored
or copied into the payload. These are Windows system resources.

## Disposition

No unresolved redistributed-asset provenance finding was identified. This
does not waive the installer blocker or complete M14-A readiness.
