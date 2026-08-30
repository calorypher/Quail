# M14-A rerun — asset provenance audit

## Bundled Quail-owned branding

| Asset | SHA-256 | Classification |
|---|---|---|
| `quail-app-icon-32px.png` | `015DA35E33C734B93BFA15B47DF1D9BA93E7DF0369427410908489F9D25A6637` | Quail-owned runtime asset |
| `quail-app-icon-48px.png` | `A889C25C22D80EE8B5AAFBF6E6E3FFBD8D5FE8091B52F965B2AC9EFFD45F3C9B` | Quail-owned runtime asset |
| `quail-tray-icon-16px.png` | `E19CE347F912941CC8118098AF9458712C65483B7E1F39AC1006911162838242` | Quail-owned runtime asset |
| `quail-app-icon.ico` | `2A8FC0483A6B62F94FB19D9AA63130BA8955E1B4A0AA3FE3D31213F411B05389` | Quail-owned executable resource |
| `quail-feather-A-gradient.svg` | `4D2D5BE28DE700AA10CC744B48153022F545621D57C31AF3C64C887C73E19F1D` | Quail-owned runtime mark |

M10 provenance maps the source frames to the runtime files and deterministic
ICO. The checked source hashes still match that record. These files are Quail
project branding, not third-party assets, and do not belong in third-party
notices. The future MIT code license does not grant or define branding rights.

## System-provided assets

- UI text references installed Windows `Segoe UI Variable`.
- action and fallback glyphs reference installed `Segoe Fluent Icons`.
- result icons are requested at runtime from Windows Shell through
  `SHGetFileInfo` and cached for display.

No Segoe/Fluent font file, Windows Shell icon file, or third-party icon pack is
stored in the repository or copied into the canonical payload. No public
screenshot or separately licensed font/icon asset is bundled.

## Disposition

**PASS — bundled asset provenance resolved.**

This result does not waive the installer security blocker.
