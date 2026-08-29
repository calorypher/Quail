# Self-contained publish payload check

The M10 self-contained publish guard requires these application-owned unpackaged WinUI resources:

- `App.xbf`;
- `QuickSearchWindow.xbf`;
- `Quail.pri`;
- `Assets/quail-feather-A-gradient.svg`.

The guard is implemented in `scripts/publish-m10.ps1`. It fails publication when the Release build does not contain a required source artifact or the final publish output does not contain it.

This protects the failure mode observed during M10 development: standard publish omitted the XBF/PRI payload, and `Application.LoadComponent` then threw `XamlParseException` while constructing `QuickSearchWindow`.
