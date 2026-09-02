# M17-S Results — Short-Query Search Structure Spike

## Status and decision

**COMPLETE: bounded compact-postings direction is credible.**

The first M17-S conclusion was amended after independent QA correctly rejected
its claimed four-byte-per-posting lower bound. This result measures actual
delta-varint posting payloads instead.

**Preferred direction:** a derived, SQLite-hosted one/two-character compact
posting structure with sparse static-rank labels and chunked compressed posting
rows. It is a candidate for an explicit M17 contract amendment; it is not a
production implementation and this spike does not unblock or modify PR #10.

The earlier dense SQLite table remains rejected. The strongest runner-up is the
same compact format with dense rank ordinals: it is smaller, but does not have
a credible incremental-order-maintenance path.

## Method and physical-host corpus

M17-S reused settled M16/M17 evidence: the correctness-safe one-character
no-exact path is about 1.46 s on physical C:, FTS5 trigram cannot answer one-
or two-character terms, and the PR #10 rowid window hid later better results.
No M16 8x3, UI, installer, Quail-Lab, or Everything campaign was rerun.

`namespace_entries` remains the authoritative state. The current short-query
path uses `instr(lower(name), lower(query))`; the final order is location, text
class, path depth, path length, and deterministic name/path/identity ties.
The prototype therefore stores every candidate and orders its posting streams
by the static portion of that exact comparator. It merges streams by location,
text class, then static rank; it never applies an arbitrary candidate cutoff.

The separate prototype in `spikes/m17-s/` read a complete C: index and wrote
only ignored local artifacts. Committed evidence contains no paths, names, or
query text.

| Metric | Value |
| --- | ---: |
| Indexed entries | 851,841 |
| Existing database bytes | 271,421,440 |
| Distinct 1/2-character postings | 35,093,907 |
| Postings per entry | 41.2 |

## Distribution and candidate selection

The distribution justified sorted delta/varint streams rather than a raw
rowid assumption.

| Terms | Distinct terms | Postings | P50 list | P90 list | P99 list | Max list | Top 10 share |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| One character | 79 | 12,192,848 | 10,048 | 486,428 | 661,287 | 661,287 | 43.659% |
| Two characters | 2,337 | 22,901,059 | 364 | 24,910 | 118,315 | 223,704 | 7.319% |

Only these bounded classes were investigated:

1. SQLite FTS/B-tree alternatives — rejected early. FTS5 trigram has no one-
   or two-character terms; FTS prefix and a normal `name` B-tree cannot provide
   literal arbitrary substring recall.
2. Dense persistent SQLite postings — measured and rejected for footprint and
   build cost.
3. Compact delta-varint postings — measured as the finalist, first with dense
   ordinals and then with sparse labels needed for credible maintenance.

## Dense table rejection

The direct table held `term + text class + rowid + name ordering` for every
distinct short substring. It had full candidate recall, but its 50-per-text-
class shortcut was not final-ranking-equivalent because it lacked the static
location/path keys.

| Dense-table metric | Result |
| --- | ---: |
| Auxiliary bytes | 4,359,073,792 |
| Growth over source DB | +1,506.0% (16.06x total) |
| Bytes per posting | 124.2 |
| Isolated construction time | 482,801.505 ms (8.05 min) |
| Prototype build peak working set | 97,161,216 bytes |

It is not a candidate for M17.

## Compact finalist

The compact representation stores one delta-varint stream per `(term, text
class)`, ordered by the current ranker's static keys. It has a compact mapping
from rank label to source rowid/location. The sparse-label version spaces
initial labels by 1,024, preserving insertion room between current neighbors.

| Variant | Bytes | Growth over source DB | Bytes/posting | Bytes/indexed entry |
| --- | ---: | ---: | ---: | ---: |
| Dense ordinal stream (runner-up) | 44,580,864 | 16.4% | 1.270 | 52.335 |
| Sparse-label stream (preferred) | 84,164,608 | 31.0% | 2.398 | 98.803 |

The preferred 84.2 MB persistent increase is material but proportionate to
removing a seconds-scale interactive failure from an 851k-entry index. It is
far below the rejected 4.36 GB dense form. Search need not load all streams:
the measured rank-map load is 4,259,205 bytes and took 14.187 ms; individual
posting BLOBs are read only for the requested term.

The measured compact transformation from the already generated postings took
15,076.696 ms and reached 816,058,368 bytes peak working set. The dense
intermediate is a prototype measurement aid, not a production proposal. A
direct staged build still needs a production implementation and M17.5 remains
the owner of full-rebuild optimization; this spike records the compact
materialization contribution instead of claiming a finished rebuild design.

### Focused physical-host latency

Each sample includes compact candidate retrieval, deterministic bounded merge,
source-row reconstruction, and the existing ranker for the selected top 50.
It is not an input-to-render UI number.

| Shape | Samples (ms) |
| --- | ---: |
| One character, exact | 20.535; 5.680 |
| One character, no-exact | 7.273; 5.763 |
| Two characters, exact | 7.416; 5.753 |
| Two characters, no-exact | 12.278; 5.031 |
| Common/broad one character | 6.867; 6.003 |

These results leave substantial margin below the 150 ms product target for
surrounding Core/UI work. They do not use a pre-ranking rowid window.

### Correctness

For all five measured shapes, compact posting counts exactly equalled the
authoritative `instr(lower(name), lower(query))` candidate counts. This
includes broad one-character shapes with 661,287 entries and two-character
no-exact shapes with 135,581 entries. The bounded merge's result identities
were re-ranked with the existing `FileSearchRanking` and matched exactly.

The deterministic one- and two-character late-exact guards retain all five
synthetic candidates, select the later exact match first, and exercise the
compact stream merge itself. They also assert location-before-text and
text-before-static-rank ordering. Thus the `a` and `ks` regression shape is
covered without reusing or modifying PR #10.

Unfiltered Quick Search is the measured target. A filtered request can retain
current semantics by continuing the same complete ordered streams while
testing existing `namespace_entries` predicates; it may decode more postings,
but no candidate is silently removed. Per-index compact results retain the
existing deterministic `MultiIndexSearch` merge. No Core-to-FileSystem
dependency was introduced.

## Maintenance, consistency, and recovery

The compact structure is derived state; `namespace_entries` remains
authoritative. The appropriate production container is SQLite, not a custom
durable store:

- compact term chunks, rank-label mapping, and a source generation marker can
  live in the same SQLite database and transaction as namespace mutations;
- build staging can generate the derived tables before publishing the complete
  staged index; a generation mismatch marks compact state unusable and permits
  deterministic rebuild from `namespace_entries`;
- create/delete/rename changes only affected term/class streams and the moving
  entry's rank label. Sparse labels permit normal insertions without renumbering
  every existing entry; an exhausted gap can use bounded local relabeling or an
  explicit rebuild/recovery path.

The prototype deliberately stored one BLOB per complete term/class stream to
measure the unsafe simplest mutation form. Across 100 deterministic existing-
entry delete samples it affected 37 posting lists at P50 and 120 at P95, with
whole-payload rewrites of 9,918,653 bytes at P50 and 25,691,056 bytes at P95
(SQLite page/transaction overhead excluded). Whole-list BLOB replacement is
therefore rejected for M20 maintenance.

The credible bounded follow-up is fixed-size compressed chunks per `(term,
class)` inside SQLite. It changes the measured write amplification from whole-
list payloads to changed chunks plus occasional split/merge metadata, while
retaining ordinary SQLite transactions, staging, generation detection, and
deterministic recovery. This is a small internal storage structure, not a new
service, custom file store, privilege model, or generic framework.

No concurrency work is justified: it would not alter the measured footprint,
ranking, or transaction conclusion.

## Recommended M17 amendment

Amend M17 before resuming PR #10 work:

1. authorize a filesystem-owned, derived, SQLite-hosted compact one/two-
   character structure using sparse static-rank labels and chunked delta-varint
   posting streams;
2. preserve literal case-insensitive substring semantics, full recall,
   deterministic ranking, filters, result limits, and existing multi-index
   semantics;
3. treat whole-list BLOB replacement as explicitly out of scope; the later
   M20 maintenance implementation must use bounded chunks and source-
   generation validation;
4. retain M17.5 ownership of direct full-build/rebuild optimization.

With that amendment, `<=150 ms` for one- and two-character search remains
credible. Do not merge PR #10 until the amended M17 implementation has its
own correctness and physical-host evidence.

## Verification

- `dotnet build spikes/m17-s/Quail.M17.ShortQuerySpike.csproj --configuration Release` — PASS, zero warnings/errors.
- Physical-host distribution, compact build, compact full-recall/ranking
  verification, focused retrieval/ranking samples, and mutation measurement —
  PASS as recorded above.
- Deterministic late-exact compact guards, including location/text ordering —
  PASS.
- Focused `FileSearchRankingTests` — PASS (7 tests).
- `git diff --check` — PASS before handoff.
