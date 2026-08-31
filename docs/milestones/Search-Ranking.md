# Pre-M13 File Search relevance/ranking

## Goal

Replace purely alphabetical file-result ordering with a small deterministic filesystem ranking model while preserving literal indexed-name matching, SQLite FTS5 trigram for queries of three or more characters, the one/two-character fallback, persistent filters, and the global multi-index limit.

## Scope

- Keep search candidate retrieval indexed and bounded.
- Rank existing filename matches by text quality and location/visibility without filesystem reads.
- Preserve the current schema and namespace representation.
- Keep ranking in the filesystem implementation, directly unit-testable and shared by CLI and Quick Search.

## Ranking model

Location/visibility is the primary ranking signal for the bounded candidate set. Text relevance is then ordered as exact name, name prefix, token-prefix, then substring. A token begins after one of the shared ASCII filename separators: space, `-`, `_`, `.`, parentheses, brackets, braces, `;`, `,`, `+`, `&`, `!`, `@`, `#`, `$`, `^`, `=`, `~`, backtick, double quote, or apostrophe. Filesystem classification and the SQL candidate predicates use this same explicit set. This changes ordering only; it does not add fuzzy matching or alter literal name-match membership.

Paths are classified from the persisted reconstructed path and persisted attributes, in this order:

1. visible entries under the current user's profile;
2. visible entries under another user profile;
3. other normal visible locations, including non-profile user-space such as `D:\Projects`;
4. hidden/internal current-user entries, including `AppData`;
5. hidden/internal other-user entries;
6. hidden/system-attributed non-profile entries;
7. system-heavy roots.

The known system-heavy roots are `Windows`, `Program Files`, `Program Files (x86)`, `ProgramData`, `$Recycle.Bin`, and `System Volume Information`. Classification compares normalized path segments case-insensitively; it does not treat a normal directory merely containing the text `Windows` as a system path. The current profile and explicit system roots are supplied through a small filesystem ranking context; normal application calls create it from environment path strings only. No live filesystem metadata is read.

The remaining tie-breaks are path depth, path length, SQLite-compatible no-case name order, binary name order, full path, and file ID. Multi-index aggregation applies the same key, then source identity.

## Candidate strategy

`IndexStore.Search` first asks SQLite for one deterministic alphabetical window equal to the requested limit per source. When that source is on the current-user volume and the window is full or has no visible current-user result, it additionally retrieves independently bounded windows for exact, prefix, token-prefix, and substring classes. The filesystem implementation deduplicates, reconstructs, classifies, ranks, and applies the requested limit. This recovers candidates in a separately queried stronger text class that would be lost by the former single alphabetical `LIMIT 50`.

This remains bounded for broad queries and does not materialize all matches or persist full paths. The supplementary windows are per text class rather than an arbitrary `Limit * N` oversample. It is not a global exhaustive ranking of every literal match: if more than the requested limit of candidates in the same text class precede a better location match alphabetically, that location match can remain outside every bounded SQL window. The controlled `needle-a-*` system-heavy / `needle-z-useful.txt` regression demonstrates this accepted 0.2 limitation. Solving it would require path-aware candidate metadata or an unbounded ancestry evaluation and is deferred.

## Out of scope

No fuzzy matching, typo tolerance, history/recency learning, content ranking, provider framework, persistent ranking data, schema migration, UI ranking logic, service/privilege change, filesystem watcher, or deployment change.

## Verification

See [Search-Ranking results](Search-Ranking-results.md) for automated regressions, controlled runtime smoke, and baseline/final performance comparison.
