# M17-S Results — Short-Query Search Structure Spike

## Status and decision

**COMPLETE: no credible bounded production candidate.** M17 remains **BLOCKED**. This spike did not modify production search, schema, build, sync, Quick Search defer, or PR #10.

**Preferred direction: none.** The strongest runner-up is a compact per-index posting-list sidecar, but its footprint floor and necessary durable maintenance mechanism keep it outside a small M17 amendment.

## Method and baseline

The spike reused settled M16/M17 evidence rather than repeating the M16 8x3 campaign: the correctness-safe one-character no-exact path is about 1.46 s on physical C:, FTS5 trigram cannot answer one- or two-character terms, and the PR #10 rowid window hid later better results.

Inspection confirmed that `namespace_entries` is authoritative and FTS5 trigram is external-content. One- and two-character search uses `instr(lower(name), lower(query))`; final ranking uses text match, location, path depth, path length, and deterministic name/path/identity ties. Text class alone is therefore not a sufficient bounded pre-ranking key.

`spikes/m17-s/` contains a non-production console prototype. It read a complete C: index and wrote only ignored files under `artifacts/m17-s/`; it has no Quick Search, production schema, build, or sync integration. Committed evidence records no paths or query text.

| Physical C: metric | Value |
| --- | ---: |
| Indexed entries | 851,841 |
| Existing database bytes | 271,421,440 |
| Generated distinct 1/2-char postings | 35,093,907 |
| Postings per entry | 41.2 |

One construction run and two samples per query shape were used. The first reading is cold-ish within the already running prototype; the second is warm. These are not M16 product/UI results.

## Candidate selection

### SQLite-native FTS/B-tree alternatives — rejected early

FTS5 trigram is already known not to index one- or two-character terms. FTS prefix indexing is token-prefix rather than literal arbitrary substring search; a normal B-tree on `name` has the same leading-wildcard limitation. Neither preserves full recall.

### Dense persistent SQLite postings — measured and rejected

The prototype wrote one row for each distinct one- and two-character substring of an indexed name:

```text
term + text-match class + namespace_entries rowid + deterministic name order
```

It was kept outside the production database and is not a proposed schema migration.

| Metric | Result |
| --- | ---: |
| Auxiliary bytes | 4,359,073,792 |
| Growth over source DB | +1,506.0% (16.06x total) |
| Bytes per indexed entry | 5,117.2 |
| Bytes per posting | 124.2 |
| Isolated derived-structure build | 482,801.505 ms (8.05 min) |
| Prototype builder peak working set | 97,161,216 bytes |

This is disproportionate before storing the static location/path rank keys needed for correct bounded final ranking. Its one-time construction is also an unacceptable marginal contribution to an already minutes-scale rebuild. M17-S did not optimize that build.

| Shape | Samples: bounded retrieval plus ranker (ms) |
| --- | ---: |
| One-character exact | 54.048; 17.770 |
| One-character no-exact | 18.294; 17.400 |
| Two-character exact | 65.831; 25.080 |
| Two-character no-exact | 22.904; 22.643 |
| Common/broad one-character | 41.100; 18.090 |

These fast samples are **not valid M17 latency evidence**. The experiment took 50 postings per text class, which omits location, depth, and path ranking keys. It is therefore not ranking-equivalent. Reading every posting restores recall but recreates broad-candidate cost; materializing the missing keys makes the already unacceptable storage larger.

### Memory-resident derived postings — rejected from a hard lower bound

Four bytes per measured rowid give a 133.9 MiB final-data floor before term directories, offsets, rank ordering, fragmentation, or existing application memory. This alone exceeds the roughly 100 MB idle-working-set aspiration for the whole application; ordinary managed maps/lists would be materially larger. Construction would scan and classify all 851,841 entries on every process start. The candidate was stopped before a large allocation test because the measured posting count supplies an implementation-independent rejection.

### Compact persistent posting-list sidecar — strongest runner-up, rejected

Packed lists can reduce payload to the same 133.9 MiB floor plus a term directory and could avoid loading all postings at startup. It remains unsuitable for M17:

- correct top-N needs lists ordered by static location/depth/path/name/identity keys and a deterministic cross-text-class merge; otherwise it recreates the prohibited cutoff;
- incremental inserts, renames, and deletes require copy-on-write blocks/segments, versioning, atomic publication, and a source-generation check, or an equivalent custom durable store;
- rebuilding the sidecar after ordinary USN changes has the measured full-scan cost;
- its payload floor is already 51.7% of the current database before required metadata and recovery support.

This is a future architecture option, not a small correctness-preserving auxiliary structure.

## Correctness, maintenance, and recovery

The dense table was compared with authoritative `instr(lower(name), lower(query))` candidate sets. It had full candidate recall for the checked shapes:

| Shape | Matching authoritative entries | Recall |
| --- | ---: | --- |
| One-character exact guard shape | 535,034 | PASS |
| Two-character guard shape | 7,564 | PASS |
| Common/broad one-character shape | 128,486 | PASS |

The deterministic prototype guard uses the permanent M17 shapes: four earlier substring names followed by a later exact name, for one and two characters. It retained all five candidates and ranked the later exact first in both cases. Thus it does not depend on a rowid window. This does not make the dense bounded lookup ranking-equivalent; that limitation is the rejection reason above.

Filters can remain a join to `namespace_entries`, and `MultiIndexSearch` can retain its deterministic per-index merge. Neither removes the rank-key or footprint failures. No Core-to-FileSystem dependency was introduced.

For dense SQLite, a theoretical bounded maintenance path is transactional insertion/update/deletion of the roughly 41.2 derived rows per affected entry, with deterministic rebuild from `namespace_entries` and a generation marker to detect partial derivation. Storage and build cost reject it first.

For compact lists, `namespace_entries` would remain authoritative, but safe mutation needs a versioned immutable sidecar and atomic manifest replacement, or a mutable block store with equivalent recovery. It must participate in build staging and never trust a sidecar from another `namespace_entries` generation. That is new durability/maintenance architecture, not an M17 read-path change. No concurrency test could change this conclusion.

## Verification

- `dotnet build spikes/m17-s/Quail.M17.ShortQuerySpike.csproj --configuration Release` — PASS, zero warnings/errors.
- One physical C: dense build, full-recall checks, and two-sample shape measurements — PASS as recorded above.
- Deterministic late-exact guards for the one- and two-character M17 shapes — PASS.
- `git diff --check` — PASS before handoff.

No manual UI, installer/package, Quail-Lab, Everything, or full M16 8x3 campaign ran; no changed boundary required them.

## Required M17 decision

Do not amend M17 to ship either measured short-query structure. Keep PR #10 open and blocked.

The recommended Quail 0.3 execution decision is to amend M17's one- and two-character product contract rather than claim its `<= 150 ms` target remains credible on the current ranking/storage model. A bounded follow-up may define explicit minimum-query behavior or a different product target, then resume only the remaining M17 bottlenecks under that amended contract. Revisit compact postings only with explicit approval for their storage and durable incremental-maintenance architecture.
