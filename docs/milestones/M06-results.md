# M06 Results — Usable CLI and 0.1 Core

## Status

**Verified candidate.** M06 consolidates the per-volume SQLite engine into the Windows-only 0.1-core CLI. It is not an installer, service, GUI, public release, tag, or GitHub Release.

## CLI and error contract

`build|rebuild --index <db> --volume <mount>`, `sync --index <db> --volume <mount>`, `status --index <db> [--index <db> ...]`, `search --index <db> [--index <db> ...] <query>`, and `open --index <db> --file-id <hex>` are the final commands. `help`/`--help` and `version`/`--version` succeed. Search preserves literal substring, FTS trigram/fallback, type, extension, size, UTC time, and attribute predicates. `--fail-after` is not a public CLI option.

Exit codes are `0` success, `2` invalid input, and `1` operational/index/runtime failure. Normal errors are concise English `ERROR` lines without stack traces.

## Multi-index and open

Each `--index` remains an independent per-volume DB. `MultiIndexSearch` validates every requested index before returning any result, uses one global limit, and orders by case-insensitive name, binary name, canonical file ID, then canonical source DB path. Each result prints `source`; duplicate names and file IDs are therefore unambiguous. Per-index work is bounded by the global limit. No filesystem stat/traversal occurs during search.

`open` requires explicit source and file ID, reconstructs the persisted namespace path, confirms that it still exists, and uses `ProcessStartInfo` with `UseShellExecute=true`; it never builds a shell command string. Missing/stale/unresolvable paths fail operationally.

## Tooling recovery

The first live M05.5 creation created exactly one standard `M06-clean` checkpoint, ID `8649f4e4-d6f1-464f-a7e8-320174119d72`. Hyper-V reported success, but immediate enumeration raced checkpoint visibility. The explicitly authorized one-time recovery created the branch after guard revalidation. Commit `8b2eefe` adds bounded polling and tests; no generic resume/adopt path exists.

## Verification

| ID | Result | Evidence |
|---|---|---|
| T01–T10 | Pass | 53 Release automated tests: CLI help/version/parser/exit codes, Unicode SQLite-compatible multi-index ordering, search-format status compatibility, open seam, and M01–M05 regressions. |
| T11 | Pass | Quail-Lab controlled D: build (172 records), filters, one controlled shell open, rename, sync (3 records), search and reopen/status. |
| T12 | Pass | Two independent D: DBs; duplicate name/file ID, global limit 3, source identity, and invalid second DB exit 1. |
| T13 | Pass | Elevated C: build: 862,437 records, 157.698 s wall; reopened 862,449 records. Elevated sync applied 1,956 records; final status complete with 862,457 records. |
| T14 | Pass | In-process, warmed 7-trial median/max: broad 73.487/76.833 ms; zero-match 18.228/20.623; filtered DLL 83.041/87.384; two compatible sources 143.251/146.851 ms. Single-index warm-core remains in the established sub-100-ms class. The approximately 143 ms two-source result includes two sequential bounded index searches plus the global merge; it exceeds 100 ms but remains responsive and does not demonstrate a single-index core regression. T12 separately proves correctness across two independent sources; this T14 multi-source measurement characterizes aggregation overhead. Earlier process-level CLI timings remain startup evidence only. |
| T15 | Pass; dbstat unavailable | QA rebuild DB 248,975,360 B / 861,497 records = 289.0 B/entry; page size 4,096, page count 60,785, freelist 46. Bundled SQLite returned `no such table: dbstat`; no dependency, extension, or custom parser was added. |
| T16–T18 | Pass | README quick start, narrow Core aggregation/open boundary, and this durable result. |

The earlier T13 production build was the database used for the end-to-end host verification. The later QA T15 rebuild is the database used for the final storage characterization: 248,975,360 B for 861,497 records, or 131,072 B (+0.053%) above the M05 248,844,288-B / 862,284-record baseline. The record counts differ because of normal host activity, so this is not an ideal apples-to-apples per-entry comparison. The coarse footprint is reasonable for persisted namespace, metadata and FTS5 trigram data, but no stronger component-level conclusion is justified because `dbstat` is unavailable. A later bounded storage spike is reasonable only if `dbstat` is available in that environment; no dominant component is proven here.

## Limitations and follow-up

Sync/build full-volume C: require the established elevation contract; one-shot sync, no service/background lifecycle, GUI, installer, catalog, or compression is added. Short queries retain SQLite fallback. The accepted initial-build cost remains about 158 seconds. A later host-evidence helper is justified: elevated build/sync capture, status, latency and page/dbstat collection repeated with a stable enough contract, but it was deliberately not added here.
