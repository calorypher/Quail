# Branding asset provenance

M10 uses unchanged files copied from `G:\Mój dysk\Moje notatki\Projekty\AI i Codex\Quail\Branding\Logo A - Flow`, the locally synchronized approved Google Drive folder:

- `App Icon/Selected/quail-app-icon-transparent-32px.png` → `Assets/quail-app-icon-32px.png`;
- `App Icon/Selected/quail-app-icon-transparent-48px.png` → `Assets/quail-app-icon-48px.png`;
- `App Icon/Selected/quail-app-icon-transparent-32px.png` → 32 px frame of `Assets/quail-app-icon.ico`;
- `App Icon/Selected/quail-app-icon-transparent-48px.png` → 48 px frame of `Assets/quail-app-icon.ico`;
- `App Icon/Selected/quail-app-icon-transparent-64px.png` → 64 px frame of `Assets/quail-app-icon.ico`;
- `App Icon/Selected/quail-app-icon-transparent-128px.png` → 128 px frame of `Assets/quail-app-icon.ico`;
- `App Icon/Selected/quail-app-icon-transparent-256px.png` → 256 px frame of `Assets/quail-app-icon.ico`;
- `Small Icons/quail-feather-A-small-16px.png` → `Assets/quail-tray-icon-16px.png` and 16 px frame of `Assets/quail-app-icon.ico`;
- `quail-feather-A-gradient.svg` remains the approved Quick Search mark.

SHA-256 at import:

- 32 px application icon: `015DA35E33C734B93BFA15B47DF1D9BA93E7DF0369427410908489F9D25A6637`;
- 48 px application icon: `A889C25C22D80EE8B5AAFBF6E6E3FFBD8D5FE8091B52F965B2AC9EFFD45F3C9B`;
- 64 px application icon: `25B1D79A758A2AFF5ABEAF671F2A8FCF1C66E290BC7351B290BF0F699045F9E5`;
- 128 px application icon: `214D80584D034D9B9647E0E670793C9EF42DDED42A3EA0EFE5E349485DFA32B8`;
- 256 px application icon: `B08E1DC37E3B42D06C835E733A0FE5A2F6143B33F580FB5D34A53E2EDEE18A4D`;
- 16 px tray icon: `E19CE347F912941CC8118098AF9458712C65483B7E1F39AC1006911162838242`.

`scripts/create-m10-app-icon.ps1` validates the six source hashes and PNG signatures, then writes the ICO header, directory, and original PNG byte streams in ascending size order. It has no external dependency and does not redraw, resample, recolor, crop, or otherwise transform the frames. Its deterministic output is `quail-app-icon.ico` with SHA-256 `2A8FC0483A6B62F94FB19D9AA63130BA8955E1B4A0AA3FE3D31213F411B05389`.

`Quail.App.csproj` embeds the ICO through `ApplicationIcon`. `scripts/verify-m10-app-icon.ps1` reads the PE `RT_GROUP_ICON` and `RT_ICON` resources and verifies all six embedded frame hashes. The ICO is build-time metadata, not a runtime asset, so the publish guard does not copy it. Runtime `WM_SETICON` uses the native 32 px and 48 px files; the tray uses the native 16 px file.
