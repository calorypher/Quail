# Pre-M13 File Search relevance/ranking results

## Status

COMPLETE for 0.2 with a bounded candidate-recall limitation; ready for independent QA.

## Delivered

- `IndexStore.Search` now retrieves a bounded text-tiered candidate window before applying filesystem ranking and the requested result limit.
- `FileSearchRanking` is a standalone filesystem component with deterministic text, location, depth, length, name, path, file-ID, and source-ID ordering.
- Current-user visible paths, other-user visible paths, other normal visible locations, current/other internal profile data, hidden/system-attributed non-profile data, and system-heavy roots are classified from indexed paths and attributes only.
- Multi-index search preserves source identity, global limits, and input-order-independent output.

## Controlled runtime regressions

All regressions run through `IndexStore.Search` rather than an isolated comparer:

- `X:\Users\Alice\Downloads\download-guide.txt` ranks above deep `AppData` download history without a Downloads-specific boost.
- `X:\Users\Alice\Desktop\desktop-plan.txt` ranks above its internal `AppData\Roaming\Microsoft\Windows\Recent` link.
- A current-user desktop result ranks above `X:\Windows\WinSxS\FileMaps` infrastructure.
- `D:\Projects\download-guide.txt` ranks above current-user `AppData` `download-cache.txt` at the same prefix text tier.
- A useful `needle-*` desktop result survives more than 50 alphabetically earlier candidates when bounded text-class expansion can retrieve its stronger prefix class.
- `foo!needle.txt` is a token-prefix in both Core ranking and the SQL candidate predicate; the regression also verifies escaping of `_` in the predicate.
- Exact, prefix, token-prefix, and substring filename matches have direct ordered coverage.

## Automated verification

- Focused Core ranking, multi-index, and existing file-search tests: PASS.
- Full Core/App logic suite: PASS (155 tests).
- Release App and CLI builds: PASS with 0 warnings and 0 errors.

## Performance

The original current-branch baseline and final comparison use the same 850,000-entry M11 fixture, Release configuration, seven post-warm-up trials, and median values. The SQL separator-predicate correction was remeasured with the same fixture and configuration; the post-QA run is not compared as a strict performance improvement because normal host variance remains larger than this small predicate change.

| Query/path | Baseline median | Initial final median | Post-QA median |
| --- | ---: | ---: | ---: |
| Core broad `a` | 371.946 ms | 348.163 ms | 201.384 ms |
| Core broad `ab` | 316.215 ms | 336.140 ms | 170.993 ms |
| Core broad `abc` FTS | 145.769 ms | 152.369 ms | 84.203 ms |
| Core selective FTS | 10.265 ms | 11.569 ms | 7.083 ms |
| Core zero-result FTS | 8.752 ms | 10.262 ms | 6.622 ms |
| MultiIndex broad `a` | 366.115 ms | 369.870 ms | 211.204 ms |
| MultiIndex broad `ab` | 331.212 ms | 330.928 ms | 184.949 ms |
| MultiIndex broad `abc` FTS | 163.993 ms | 164.868 ms | 97.666 ms |

The original >=3-character guardrail passed: broad FTS increased by 4.5% and 6.6 ms, not both more than 25% and 10 ms. The direct short-query guardrail also passed: `ab` increased by 6.3% and 19.9 ms, while `a` improved. The post-QA run was below the initial final values for every recorded direct and MultiIndex query, so it introduces no regression warning. The corresponding raw baseline and initial final JSON are retained under ignored `artifacts/search-ranking/baseline/` and `artifacts/search-ranking/final-final/`; the post-QA JSON is under ignored `artifacts/search-ranking/qa-finding-final-2/`.

## Read-only real-index smoke

The existing complete managed host index at `%PROGRAMDATA%\Quail\Indexes\volume-b434881e3fb92f49314bff75.db` was queried through the Release CLI without build, rebuild, or refresh. For `desktop`, the top result was `C:\Users\<user>\Desktop` (exact name, current-user visible), followed by other visible profile paths; AppData shortcuts followed those visible entries. For `download`, the leading results were visible `C:\Users\<user>` development paths (prefix) before internal AppData `download` / `Download Service` entries. The exact controlled tests remain the authoritative regression proof because this task process uses the sandbox profile context, while production resolves the actual interactive process profile.

## Known limitations

- The bounded per-source windows are intentionally not an exhaustive broad-query scan. In the adversarial `needle` case, 75 system-heavy prefix matches named `needle-a-*` occupy the same bounded prefix window before a current-user-visible `needle-z-useful.txt`; the useful result does not reach the candidate set. Ranking therefore cannot make it visible. This is an accepted 0.2 candidate-recall limitation, not a change in literal-match membership.
- Ranking uses indexed namespace paths, so stale indexes naturally present stale paths until the normal index refresh; it does not stat or traverse the filesystem.

No schema redesign was made for this limitation. A later follow-up may evaluate indexed ancestry/location metadata or another path-aware candidate model, subject to its own rename/move, boundedness, and performance design.
