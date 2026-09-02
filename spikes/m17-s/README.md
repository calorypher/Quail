# M17-S short-query prototype

This project is a measurement-only prototype. It is not referenced by the
application, filesystem source, CLI, tests, production schema, build path, or
sync path.

It builds a deliberately dense external SQLite representation of distinct
one- and two-character name substrings. Its purpose is to quantify the
storage/build cost of the most direct full-recall SQLite approach and to prove
why a fast bounded per-text-class lookup is not sufficient for Quail's full
ranking contract.

Run it only against a complete local index and write the generated database
and JSON evidence below ignored `artifacts/m17-s/`. Do not commit source paths,
query text, index contents, or generated artifacts.

```powershell
dotnet build spikes/m17-s/Quail.M17.ShortQuerySpike.csproj --configuration Release
dotnet spikes/m17-s/bin/Release/net10.0-windows/Quail.M17.ShortQuerySpike.dll build --source <index.db> --output artifacts/m17-s/dense.sqlite --evidence artifacts/m17-s/build.json
```

`measure` accepts comma-separated `query:shape` entries and records only the
shape, length, timing, counts, and result fingerprints. `verify` compares
candidate counts with the authoritative `namespace_entries` predicate.
`self-test` validates the one- and two-character late-exact regression shapes.
