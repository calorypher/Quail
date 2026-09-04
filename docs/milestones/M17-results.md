# M17 Results — Search Engine Performance

## Status

**COMPLETE — FINAL ACCEPTANCE PASS; PR #10 MERGED.**

The final production implementation was tested at
`5515ef59c051a50ee14d3d9e49961154aafeb4c5`. Its final physical D: lifecycle,
comparable frozen-C plus current-D canonical M16 8x3 campaign, and manual
Quick Search smoke all passed. The whole PR then passed final independent QA and
PR #10 was merged to `main` as
`8491aa2e196f70276345928eb9bd4b107fec4ef6` on 2026-09-04. The historical M17
performance campaign below remains valid only as a measurement of commit
`fa66270461f8f4461fd574c490a82ad214dcad39`; it is not acceptance evidence
because that commit weakened short-query candidate recall.

## Current production implementation evidence

The merged M17 implementation contains the amended compact short-query path:

- `namespace_entries` remains authoritative; schema v4 adds filesystem-owned
  SQLite rank and posting chunk tables built inside staging before publication;
- postings are chunked at 1,024 labels and delta-varint encoded, so ordinary
  create/rename/delete changes rewrite only affected chunks rather than a
  complete term list;
- 64-bit sparse labels preserve static ordering; a compact label-only chunked
  static-order map allocates insertion gaps without persisting every full sort
  key. Static keys are reconstructed only for explicit sync/order maintenance;
- runtime current-user and system-location precedence is derived from indexed
  parent topology and the searching context. No builder-process user identity
  is persisted as ranking state;
- posting terms use one ASCII-only canonical representation under SQLite
  `BINARY` collation. This deliberately matches SQLite built-in `NOCASE` and
  `lower` behavior without folding non-ASCII literal substrings;
- a matching derived generation is required for search and sync. A missing or
  mismatched structure is reported as rebuild-required rather than trusted;
- the one-second short-query defer is removed. The retained duplicate-query
  coalescing remains unchanged.

Focused automated evidence on the implementation branch:

- `FileSearchRankingTests`: permanent late exact `a` and `ks` guards, compact
  location/text/static ordering, ASCII mixed-case posting order, non-ASCII
  literal-substring preservation, and a 1,100-entry cross-chunk recall guard;
- `IncrementalIndexStoreTests`: generation mismatch, create/rename/delete,
  directory-rename descendant search, mixed-case posting rename, transactional
  checkpoint behavior, and the 1,024-posting chunk bound;
- the full `Quail.Core.Tests` Release suite and the affected `Quail.App` Release
  build pass locally.

## Footprint correction after Windows reinstall (not acceptance evidence)

The preserved pre-reinstall C: schema-v4/v2 index at commit
`f81780f39a11aa485ac4306a4a4ece7f5a32eccb` was measured only through
disposable copies under `artifacts/m17/footprint-f81780f/`. It contains 850,688
records and 35,050,071 postings. The frozen source database was never changed.

The original helper figure of 223.206 ms / 317,221,198 B was not a production
search-load measure: it read both the search-time `short_query_rank_chunks`
and the sync-only `short_query_rank_order_chunks`. The corrected breakdown is:

| Component | v2 logical payload | v3 logical payload | Finding |
| --- | ---: | ---: | --- |
| Delta-varint postings | 182,615,968 B | 79,648,593 B | Reducing direct-build label spacing from 2^32 to 2^12 reduces the frozen-corpus posting stream from 5.210 to 2.272 B/posting. v2 used 28,087,225 five-byte, 6,590,218 six-byte, 342,489 seven-byte, and 30,139 eight-byte varints. |
| Search-time rank map | 23,819,264 B | 23,819,264 B | The actual search load is about 14.7 ms / 23.8 MB, not 223 ms / 317 MB. |
| Sync-only order maintenance | 293,401,934 B | 6,805,504 B | v2 persisted full static sort keys for every entry; v3 persists ordered labels and only chunk boundary keys. |
| Total logical compact payload | 499,837,166 B | 110,273,361 B | v3 is 3.146 B/posting, materially back in the M17-S bounded class rather than the v2 ~14.3 B/posting class. |

`dbstat` is unavailable in the bundled SQLite runtime. SQLite page-state
decomposition therefore uses a disposable copy: removing the v2 derived tables
released 514,277,376 B of pages (the source had zero freelist pages), while the
same operation on v3 released 172,699,648 B. The v3 direct-build copy is
411,058,176 B versus 762,478,592 B for v2. Its direct compact build contribution
was 14,244.904 ms; this is evidence for M17.5, not a rebuild optimization claim.

Focused direct `ShortQueryIndex.Search` measurements on the v3 frozen-C copy
before the runtime-ranking correction were one-character 642.338 and 443.723 ms
and two-character 364.640 and 371.657 ms. These figures triggered the bounded
runtime-ranking continuation recorded below; they are retained as the direct
before baseline.

## Bounded runtime-ranking correction

The repeatable `profile-search` helper decomposed the unchanged pre-correction
path on the disposable frozen-C v3 database. It disproved the initial assumption
that repeated parent walks were the primary cost. One representative isolated
pass produced:

| Component | M16 one-character | M16 two-character |
| --- | ---: | ---: |
| Rank-map payload decode/load | 42.620 ms | 42.620 ms |
| Existing current-user/system context resolution | 343.412 ms | 343.412 ms |
| Posting SQLite read | 0.515 ms | 0.039 ms |
| Posting decode | 3.215 ms | 0.139 ms |
| Rank-label lookup | 1.070 ms | 0.091 ms |
| Repeated parent-walk location classification | 16.681 ms | 0.986 ms |
| Selection and result reconstruction | 10.180 ms | 12.222 ms |
| Unchanged production Search total | 436.839 ms | 365.703 ms |

The context lookup was dominant because finding the namespace root scanned for
`file_id=parent_file_id`, while child lookup used `COLLATE NOCASE` against the
existing binary `(parent_file_id,name)` index. The bounded correction instead
uses the rank map's known root rowid, enumerates only the indexed children of
each path parent and compares those few names case-insensitively in managed
code, then resolves all requested context rowids in one rank-map scan. This
reduced context resolution to 11.5-11.7 ms without adding a persistent index.

The production search now derives a transient one-byte-per-rank location map.
Its topology-memoized construction took 12.8-13.2 ms for 850,688 entries. The
final map is 850,688 bytes; construction allocated 4,254,096 bytes including
temporary topology arrays. The complete managed rank array retained by a
hypothetical cache is about 27,222,232 bytes. A preloaded rank/map experiment
searched the representative streams in 13.7-19.6 ms, but production deliberately
does not retain that state: the uncached path already has sufficient margin and
avoids adding about 28 MB to steady process memory. Measured production calls
allocated 56.4-57.2 MB transiently and retained only about 18-25 KB after a
forced collection.

The derived map is runtime-only and uses the searching process's
`FileSearchRankingContext`; no user identity is persisted. It is rebuilt for
every short-query call, so index generation, rebuild, sync, and context changes
cannot reuse stale classification. The persistent format remains
`compact-short-query-v3` and the v3 footprint is unchanged.

The helper verifies every one of the 850,688 map entries against the original
parent-walk classifier before comparing exact result identities and order. The
production result sequence matched that authoritative path for the M16 one-
and two-character shapes and a 588,955-posting broad one-character stream.
Permanent tests also compare both paths for `a` and `ks` across current-user,
other-user, internal, default-system, and runtime explicit-system contexts.

Focused direct production samples on fresh helper processes were:

| Shape | Postings | Samples (ms) |
| --- | ---: | ---: |
| M16 one-character | 128,256 | 85.973; 47.749; 44.047 |
| M16 two-character | 7,567 | 41.512; 41.738; 42.901 |
| Broad one-character | 588,955 | 103.531; 63.100; 59.776 |

The first broad sample is slightly above the approximate 100 ms spike
preference but remains far from the 250 ms end-to-end guardrail; the contracted
M16 one/two-character shapes retain direct-search margin for Core/App/UI work.
Ranking-aware early termination and new chunk summaries were not attempted:
the correctness-preserving complete scan already meets the feasibility goal,
so adding persistent metadata or skip logic would be unjustified scope.

At this runtime-ranking checkpoint the final canonical M16 8x3 was deliberately
unrun. That provisional statement is superseded by the accepted final campaign
recorded below.

Verification for the runtime-ranking candidate:

- diagnostic helper Release build: PASS, zero warnings/errors;
- focused short-query/ranking/mutation/multi-index suite: 49 PASS;
- full `Quail.Core.Tests` Release suite: 204 PASS;
- `Quail.App` Release build: PASS, zero warnings/errors;
- `git diff --check`: PASS;
- ignored detailed evidence:
  `artifacts/m17/footprint-f81780f/runtime-profile-v3-c-production-k-ks.json`,
  `runtime-profile-v3-c-production-broad-one.json`,
  `runtime-profile-v3-c-memory.json`,
  `runtime-search-v3-c-final-k-ks.json`, and
  `runtime-search-v3-c-final-broad-one.json`.

**Decision A for this continuation:** the bounded M17 runtime blocker is solved.
The branch is ready for execution-thread QA and later final user-owned physical
acceptance, but M17 is not complete and PR #10 remains DO NOT MERGE.

## Interrupted physical acceptance on `1f53faa` (not acceptance evidence)

The initial user-owned final acceptance attempt started from
`1f53faad774fd85e0786278477115fc01d9ce58a`. Fresh production rebuilds completed
successfully before mutation testing:

| Index | Records | State immediately after rebuild | v3 bytes/posting |
| --- | ---: | --- | ---: |
| Current C: | 462,868 | complete | 3.095 |
| Current D: | 20,304 | complete | 3.491 |

The first ordinary D: mutation created
`D:\quail-m17-mutation-probe.txt`. The normal **Quail Indexes** D: **Refresh**
then returned `Rebuild required` with
`journal-read-or-parse-failed: Short-query rank label gap is exhausted; rebuild is required.`
The D: CLI status is consequently `rebuild-required` with 20,304 records. D:
was not rebuilt after this failure, and the final canonical M16 8x3 was not
started. Any partial evidence under `artifacts/m17/final-1f53faa/` is failed,
interrupted acceptance evidence and must be retained without overwrite.

The deterministic root cause has two parts. Before this correction, `ApplyBatch`
processed every raw record for a file sequentially and performed compact
remove/reinsert maintenance even for metadata-only state. Separately, the
midpoint allocator could exhaust a freshly built 2^12 label gap after fourteen
distinct leaf creates ordered into that one gap; the focused regression failed
at `ShortQueryIndex.AllocateLabel` with the same rebuild-required message before
the fix. Repeated metadata records alone did not need a new static rank label,
but were still doing unnecessary compact mutation work. The physical failed
Refresh did not retain its complete raw USN stream, so this evidence does not
claim which of those two conditions dominated that one batch; both are removed
as independent, deterministic lifecycle risks.

The bounded remediation keeps `compact-short-query-v3` unchanged:

- coalesce a journal batch to one effective transition per `FileId`, preserving
  standalone rename-old behavior and using the final actionable record;
- update ordinary namespace metadata without compact remove/reinsert when
  parent, name, directory state, and hidden/system ranking attributes are
  unchanged;
- on a real exhausted leaf gap, locally relabel at most 128 adjacent leaf
  entries from one static-order chunk, transactionally rewriting only their
  rank/order/posting entries. Directories are deliberately excluded, so no
  external `ParentLabel` references change; a case outside this bounded recovery
  remains the existing explicit rebuild-required path.

Focused automated evidence for the correction is 49 PASS across
`IncrementalIndexStoreTests`, `MetadataIndexTests`, `FileSearchTests`, and
`FileSearchRankingTests`. The new deterministic regressions cover clustered
creates, repeated create/metadata records with rank-label preservation, rename,
and delete. Final automated verification for this correction: full
`Quail.Core.Tests` Release 206 PASS and `Quail.App` Release build PASS with zero
warnings and zero errors. The measurement helper and frozen-C direct-search
evidence are unchanged because neither helper nor production search execution
changed.

## Second interrupted physical acceptance on `0b8c28b` (not acceptance evidence)

Independent delta QA passed the first bounded maintenance correction at
`0b8c28b9d74bac5585edc88b70fa32261777b0ff`. The user then rebuilt the current
D: production index exactly once through **Quail Indexes**; the rebuild
completed with 20,544 records and CLI state `complete`. The first physical
CREATE Refresh for `D:\quail-m17-0b8c28b-probe.txt` passed and left the index
complete. The next Refresh, after renaming that probe to
`D:\quail-m17-0b8c28b-probe-renamed.txt`, failed with:

`journal-read-or-parse-failed: Short-query mutation found a missing parent.`

The DELETE stage was not executed. D: was deliberately left rebuild-required,
and neither the frozen-C preparation nor the final M16 8x3 campaign was
started. The create-only result is not full mutation acceptance. Non-sensitive
snapshots and comparisons from the interrupted run are preserved in the
ignored `artifacts/m17/failure-0b8c28b-physical-d-rename/` directory. The
rename comparison reports zero changed posting chunks because the failed batch
did not commit; this is rollback evidence, not a successful rename result.

The deterministic root cause was cross-`FileId` reordering inside one journal
batch. `ApplyBatch` grouped records by `FileId`; LINQ `GroupBy` ordered each
group by that identifier's first occurrence, while `CoalesceJournalRecords`
selected the final actionable state. In the focused reproduction, the source
order was parent metadata, child delete, parent delete. Coalescing moved the
parent's final delete into its first-occurrence position, removed the parent
before the child transition, and then `ShortQueryIndex.RemoveCurrentEntry`
failed in `ResolveNodePath` with the exact physical error. The new regression
failed with that exception before the correction.

The bounded correction annotates canonical records with their source position.
After per-file coalescing, delete/rename/update transitions execute at the
position of the selected final actionable record. A group containing
`FileCreate` is deliberately anchored at its first actionable position so an
interleaved new child cannot execute before its new parent. This retains one
effective transition per `FileId`, the metadata-only compact-index bypass, and
the protection against artificial sparse-gap consumption without introducing
a generic scheduler or changing transaction/generation behavior.

The leaf-gap recovery from `0b8c28b` is not the source of this failure. A test
now forces the fourteen-create local recovery, validates all namespace/rank
parent relations plus rank-order and posting-label references, then renames a
recovered leaf, deletes another, and inserts nearby with the same integrity
checks after every batch. Directories remain excluded from recovery, so no
external child `ParentLabel` is invalidated. The persistent representation
remains `compact-short-query-v3`; production Search is unchanged.

Final automated evidence for this second correction:

- the exact interleaved parent-delete regression: FAIL before correction with
  `Short-query mutation found a missing parent`, PASS after correction;
- focused `IncrementalIndexStoreTests`, `MetadataIndexTests`,
  `FileSearchTests`, and `FileSearchRankingTests`: 53 PASS, including
  interleaved parent create/delete, interleaved directory rename/subtree,
  cross-batch rename, metadata-only label preservation, fourteen-create local
  recovery plus later mutations, runtime-location-map coverage, and permanent
  late-exact `a` / `ks` guards;
- full `Quail.Core.Tests` Release: 210 PASS. The first full invocation had one
  timeout in the unchanged M13B scheduling test after 209 passes; that test
  passed in isolation and the complete suite then passed 210/210;
- `Quail.App` Release build: PASS, zero warnings and zero errors;
- `git diff --check`: PASS.

The measurement helper, persistent footprint, and frozen-C direct Search
evidence remain unchanged and were not rerun.

## Independent QA topology blocker on `0d5d029` and source-order correction

Independent review of `0d5d0291f134cdd1ff41ed63aa95789cd2c70333` found that
position-aware structural coalescing still projected one file's final state
onto a different source position. The confirmed deterministic batch was:

1. create a child under the root;
2. create a new parent under the root;
3. move the child into that new parent with `RenameNewName`.

The child group contained `FileCreate`, so `CoalesceJournalRecords` carried its
final `NamespaceRecord` with `ParentFileId=new-parent` but anchored execution at
the first child-create position. Before correction, the focused regression
failed in `ShortQueryIndex.InsertCurrentEntry` / `ResolveNodePath` with the
exact `Short-query mutation found a missing parent` error because the new
parent had not yet been inserted. This is the inverse of the earlier
parent-delete reordering defect and invalidates another positioning exception
as a robust lifecycle model.

The final design therefore removes per-`FileId` structural coalescing. Every
canonical journal record now updates namespace and compact state in original
source order. `RenameOldName` retains its established no-op behavior until
`RenameNewName`; real create, move/rename, and delete transitions are no longer
projected onto first/final group positions. Metadata acquisition remains
bounded and non-authoritative: the pre-scan may acquire current filesystem
metadata at most once per `FileId`, but it does not reorder lifecycle records.

This simplification is sufficient because the two protections added in
`0b8c28b` remain active. Repeated metadata-only records whose parent, name,
directory state, and hidden/system ranking attributes are unchanged bypass
compact removal/reinsertion, while genuine clustered insertions retain the
transactional maximum-128-leaf local recovery. Directory labels remain fixed;
the compact format, generation model, posting-chunk bounds, Search execution,
and footprint remain unchanged.

Deterministic coverage now includes:

- the exact child-create / new-parent-create / child-move QA reproduction and
  its directory/subtree equivalent;
- interleaved parent create before child create, and child delete before final
  parent delete;
- child creation under a new directory with interleaved metadata;
- create plus rename and create plus delete for one `FileId` in one batch;
- an existing entry moved between parents with unrelated interleaved records;
- interleaved directory rename/subtree maintenance and cross-batch
  `RenameOldName` / `RenameNewName`;
- metadata-only rank-label preservation, fourteen clustered creates, bounded
  recovery, then post-recovery rename, delete, and nearby insertion;
- namespace/rank/`ParentLabel`/order/posting integrity after the relevant
  batches, plus the existing short-query recall and ranking guards.

Final automated evidence for the source-order candidate:

- exact QA reproduction: FAIL before the correction with
  `Short-query mutation found a missing parent`, PASS after it;
- focused `IncrementalIndexStoreTests`, `MetadataIndexTests`,
  `FileSearchTests`, and `FileSearchRankingTests`: 58 PASS;
- full `Quail.Core.Tests` Release: 215 PASS;
- `Quail.App` Release build: PASS, zero warnings and zero errors;
- `git diff --check`: PASS.

No physical D: rebuild, frozen-C Search rerun, footprint campaign, or M16 8x3
was performed. The next physical rebuild remains blocked until this new delta
passes independent QA.

## Third interrupted physical acceptance on `b36999c` and delete durability correction

Independent delta QA passed the source-order candidate at
`b36999c277e30db54890077ee18c6325ac685030`. The user then built the Release
application and rebuilt D: exactly once through **Quail Indexes**. The rebuild
completed at 20,905 records with CLI state `complete`. Creating
`D:\quail-m17-b36999c-probe.txt` and refreshing D: passed, producing 20,906
records with state `complete`. After renaming the probe to
`D:\quail-m17-b36999c-probe-renamed.txt`, the next normal Refresh failed with:

`journal-read-or-parse-failed: Short-query mutation found a missing parent.`

D: is again rebuild-required. The DELETE stage and final M16 8x3 were not run.
The successful create snapshots and comparison are preserved without overwrite
under the ignored `artifacts/m17/failure-b36999c-physical-d-rename/` directory;
they are partial evidence, not full mutation acceptance.

The deterministic physical-failure-class reproduction uses two separately
committed journal batches. Batch N deletes an indexed parent directory while
its child remains known. Before correction that commit returned successfully
but removed only the parent's namespace/rank/posting/order entry, leaving the
child namespace entry and rank `ParentLabel` pointing at the removed parent.
Batch N+1 then delivered the child `FileDelete` and failed in
`ShortQueryIndex.RemoveCurrentEntry` / `ResolveNodePath` with the exact
`Short-query mutation found a missing parent` exception. This can legally cross
the implementation's batching boundary: each record set returned by the 1 MiB
`FSCTL_READ_USN_JOURNAL` buffer is passed to `ApplyBatch` and committed
independently.

A dangling committed rank topology is not safe. The runtime location map cannot
recover the missing ancestry and may classify such an entry outside its real
location; the authoritative parent walk can fail on the missing rank label;
and result path reconstruction reports a missing parent. The final durability
rule is therefore: when `FileDelete` targets a directory currently known by the
index, that transaction removes its entire currently known subtree from compact
and namespace state before committing. Entries are removed child-first. Later
descendant delete records are idempotent no-ops, regardless of later journal
batch boundaries.

The removal path is also explicitly independent of the removed entry's parent
path. `ShortQueryIndex.RemoveCurrentEntry` needs only the node name, rowid, and
rank label to remove posting, order, and rank references; its unused
`ResolveNodePath` and `CreateStaticSortKey` calls were removed. A focused test
first removes a parent's compact and namespace state, then removes the orphaned
child compact entry successfully and rolls the diagnostic transaction back.
Ordinary rename/move tests prove that insertion and reranking still compute the
full path/static key where they are actually required.

The multi-level regression deletes a directory containing a file, a nested
directory, and a nested file. Immediately after the first committed batch only
the root remains, Search exposes no removed labels, and complete
namespace/rank/`ParentLabel`/order/posting integrity passes. Later child,
nested-directory, and nested-file delete batches each commit as no-ops and pass
the same integrity check. The normal child-first then parent-delete control also
passes after each commit.

Final automated evidence for the committed-state durability candidate:

- exact cross-batch parent-delete then child-delete reproduction: FAIL before
  correction with `Short-query mutation found a missing parent`, PASS after;
- focused `IncrementalIndexStoreTests`, `MetadataIndexTests`,
  `FileSearchTests`, and `FileSearchRankingTests`: 62 PASS;
- full `Quail.Core.Tests` Release: 219 PASS;
- `Quail.App` Release build: PASS, zero warnings and zero errors;
- `git diff --check`: PASS.

The bounded leaf relabel, persistent `compact-short-query-v3` format, production
Search execution, footprint, and focused latency evidence are unchanged. No
physical D: rebuild, frozen-C benchmark, footprint campaign, or M16 8x3 was
performed for this correction.

## Fourth interrupted physical acceptance on `dde2315` and rooted-namespace correction

Independent delta QA passed the delete-durability candidate at
`dde2315cff6d3e4edd84f68c53969b9cf58bce1a`. The user built Release and rebuilt
D: exactly once through **Quail Indexes**. The rebuild completed with 20,927
records at `2026-09-04T08:42:36.8209086+00:00`, journal
`01DD39FE7448A7C2`, and checkpoint `nextUsn=34398488`. A completely fresh
mutation attempt then created `D:\quail-m17-dde2315-probe.txt`. The very first
normal D: Refresh failed with:

`journal-read-or-parse-failed: Short-query mutation found a missing parent.`

Rename and delete were not attempted. D: remains deliberately
rebuild-required, and the final M16 8x3 was not run. The sole available
pre-create posting snapshot is preserved separately under the ignored
`artifacts/m17/failure-dde2315-physical-d-create/` directory; it is failed-run
evidence, not mutation acceptance.

Read-only inspection of the preserved production database established that no
journal batch had committed after publication: its checkpoint was still
`34398488`, record count was still 20,927, and `lastRefreshedUtc` equalled the
build-time value (11.7 microseconds after `completedUtc`). The published
`complete` namespace nevertheless already
contained exactly one orphan root:

- `FileId=1B000000000001000000000000000000`;
- `ParentFileId=0B00000000000B000000000000000000`;
- name `$RmMetadata`, attributes `0x00000016`, record version 2;
- no namespace entry existed for the parent record number 11 (`$Extend`);
- the compact builder had assigned `$RmMetadata` rank label 53,248 and the same
  value as its `ParentLabel`, masking the missing namespace parent.

The orphan rooted a seven-entry `$RmMetadata` / `$TxfLog` system subtree. Every
namespace row still had a rank row, and rank/order/posting label sets were
otherwise internally consistent. This explains why the old integrity checks
did not detect the defect: `ShortQueryIndex.Build` converted a missing parent
to a synthetic `<unresolved-...>` path and defaulted its `ParentLabel` to its
own label instead of rejecting or removing an unresolvable namespace subtree.

Raw FSCTL journal replay was not required for the correction. A non-elevated
attempt stopped at read-only volume open with `CreateFile(D:)` access denied;
it did not read records or mutate the production index. The preserved database
plus deterministic tests supplied a stronger direct proof. On the unmodified
`dde2315` behavior, a build containing the exact observed `$RmMetadata`
topology followed by an ordinary root-level `FileCreate` failed with the exact
physical exception in:

`InsertCurrentEntry -> FindOrderInsertion -> ReadStaticSortKey -> ResolveNodePath`.

An order-relevant update of `$RmMetadata` independently failed through
`RemoveOrderEntry -> SaveOrderChunk -> ReadStaticSortKey -> ResolveNodePath`.
Consequently, the failing raw record did not have to reference the orphan: an
ordinary mutation of another entry whose order chunk consulted an unresolved
boundary was sufficient. This reproduces the fourth run's first-CREATE failure
class without another physical rebuild and corrects the earlier assumption
that this literal could arise only from the direct-parent check in the final
insert step.

The bounded durability rule is now: a published namespace and every committed
sync state contain only entries whose parent chain reaches an indexed
self-parent root. After build handoff replay and before compact construction,
`IndexStore` removes every still-unrooted namespace subtree transactionally.
An incremental insert or move whose parent chain is not rooted is ignored when
new, or removes any formerly reachable known subtree; later records remain
idempotent until a rooted parent exists. A parent absent during enumeration but
created by handoff replay retains its child, proving that pruning occurs only
after the continuous handoff window. A valid mutation immediately after the
final handoff cursor also remains accepted. `ShortQueryIndex.Build` now fails
closed if an unresolved parent reaches rank construction rather than producing
a synthetic path.

The production-measure helper gained a read-only `inspect-index` command for
checkpoint and namespace/rank/order/posting integrity evidence. Its existing
`report --work-copy` path now applies the same rooted-namespace normalization as
production before direct compact construction. On a disposable copy of the
preserved failed D: database, the corrected path produced 20,920 namespace and
rank entries, zero namespace or rank-parent orphans, 20,920 unique order
labels, and 660,545 posting labels with zero dangling labels. It removed only
the seven unresolvable system-subtree rows and 130 associated posting labels;
rooted `$RECYCLE.BIN` remained indexed. Direct compact construction took
767.375 ms on that focused copy.

The two exact regressions failed before the correction with `Short-query
mutation found a missing parent` and pass after it. Additional coverage proves
late-parent handoff convergence, post-handoff insertion, and removal of an
existing directory subtree moved beneath an unrooted parent. The persistent
format remains `compact-short-query-v3`, Search execution is unchanged, and no
physical D: rebuild, Refresh, frozen-C performance rerun, footprint campaign,
or M16 8x3 was performed during this correction. Final automated evidence:

- the two exact physical-failure-class regressions: FAIL before correction
  with `Short-query mutation found a missing parent`, PASS after correction;
- focused `IncrementalIndexStoreTests`, `MetadataIndexTests`,
  `FileSearchTests`, and `FileSearchRankingTests`: 66 PASS;
- full `Quail.Core.Tests` Release: 223 PASS;
- `Quail.App` Release build: PASS, zero warnings and zero errors;
- diagnostic helper Release build: PASS, zero warnings and zero errors;
- `git diff --check`: PASS.

## Final accepted physical-host evidence on `5515ef5`

The accepted raw evidence is preserved, ignored, and was not modified during
this consolidation:

`artifacts/m17/final-5515ef5/`

It contains the post-rebuild and after-each-operation integrity inspections,
before/after posting snapshots and comparisons for create/rename/delete,
current C:/D: reports, the immutable-source and disposable-final-v3 frozen-C
reports, the disposable final-v3 DB, and the complete canonical M16 traces,
drivers, diagnostics, `results.json`, and `summary.txt`.

### D: rebuild, integrity, and ordinary mutation lifecycle

One normal **Quail Indexes** D: Rebuild at `5515ef5` completed with 22,639
records, `nextUsn=35871032`, journal `01DD39FE7448A7C2`, namespace and
short-query generation 1, and `compact-short-query-v3`. The post-rebuild
inspection found 22,639 namespace rows, rank rows, and order labels; 721,652
posting labels; and zero unrooted namespace rows, namespace/rank orphans,
missing or mismatched parent labels, duplicate/missing order labels, or dangling
posting labels.

| Operation | Completed state | Generation | Posting labels | Changed chunks | Removed chunks | Changed payload lower bound | Largest chunk | Result |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| CREATE `quail-m17-5515ef5-probe.txt` | 22,640 records; `nextUsn=35871672` | 2 / 2 | 721,697 | 68 | 0 | 73,714 B | 2,393 B | PASS |
| RENAME to `quail-m17-5515ef5-probe-renamed.txt` | 22,640 records; `nextUsn=35872064` | 3 / 3 | 721,707 | 22 | 0 | 28,821 B | 2,257 B | PASS |
| DELETE renamed probe | 22,639 records; `nextUsn=35872200` | 4 / 4 | 721,652 | 55 | 0 | 61,711 B | 2,391 B | PASS |

Every after-operation inspection remained `complete` with all integrity counters
at zero. After DELETE both the record count and posting-label count returned
exactly to their post-rebuild values. The comparison payloads are focused lower
bounds for changed posting chunks, not total SQLite transaction I/O.

### Production footprint and comparable frozen corpus

`dbstat` was unavailable in the bundled SQLite runtime, so
`compactDerivedBytes` and `compactGrowthPercent` are `null` in all final reports.
Logical payload totals remain the accepted comparable lower-bound evidence; no
substitute page estimate is claimed.

| Index/report | Namespace rows | Postings | Database bytes | Bytes/posting | Posting payload | Search rank-map payload / load | Order-maintenance payload / load | Logical compact payload |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Current C: | 462,868 | 20,192,875 | 236,150,784 B | 3.095 | 45,835,279 B | 12,960,304 B / 23.015 ms | 3,702,944 B / 6.887 ms | 62,498,527 B |
| Current D: | 22,639 | 721,652 | 9,216,000 B | 3.477 | 1,694,220 B | 633,892 B / 1.222 ms | 181,112 B / 0.207 ms | 2,509,224 B |
| Frozen pre-reinstall C final v3 | 850,675 | 35,049,776 | 411,049,984 B | 3.146 | 79,647,777 B | 23,818,900 B / 16.041 ms | 6,805,400 B / 4.796 ms | 110,272,077 B |

The immutable frozen v2 source report records 850,688 rows, 35,050,071
postings, 762,478,592 B, 14.261 B/posting, and a 499,837,166 B logical compact
payload. Its `--work-copy` compact construction created the final-v3 disposable
DB in 14,745.821 ms at 411,049,984 B. This is compact-representation evidence,
not a claim to optimize the full rebuild pipeline; M17.5 remains out of scope.

### Comparable-corpus count diagnostic

The final benchmark trace reports `recordCount=873327`, while the actual
namespace rows of its two search indexes are 850,675 (frozen v3) plus 22,639
(current D:) = **873,314**. The 13-row difference is understood and does not
affect Search:

- `report --work-copy` invokes `RemoveUnrootedNamespaceEntries` before direct
  compact construction, removing 13 unrooted frozen rows, but that helper path
  does not rewrite metadata `record_count`;
- the work-copy therefore retains its frozen-source status metadata count of
  850,688, while its `namespace_entries` and compact state contain 850,675 rows;
- `FileSystemSearchSource.GetSearchIndexScale()` reads `IndexStore.GetStatus()`
  and is used only to write the trace `session-start` scale, yielding
  850,688 + 22,639 = 873,327;
- production Search invokes `IndexStore.Search` / `ShortQueryIndex.Search` over
  the actual SQLite namespace and derived tables, not this diagnostic status
  count.

The benchmark database size cross-checks exactly:
411,049,984 B + 9,216,000 B = 420,265,984 B. The baseline records both the
actual comparable corpus and the separate diagnostic trace/status count; it
does not represent 873,327 as a namespace-row count.

### One final canonical M16 8x3 campaign

The only final campaign used the disposable frozen pre-reinstall C final-v3 DB
and the rebuilt current D: DB; it did not substitute the post-reinstall current
C: corpus. The resident Quail process was closed. `results.json` records
`gitHead=5515ef59c051a50ee14d3d9e49961154aafeb4c5`, `sourceDirty=false`,
.NET 10.0.400, Windows 10.0.26200.0, three repetitions, two indexes, and
420,265,984 database bytes. All 24 input-to-text samples and the three rapid
burst samples satisfy their M17 targets and guardrails.

| Scenario | Samples (ms) | Median | Target | Worst | Guardrail | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `ordinary-name` | 14.500, 16.998, 16.675 | 16.675 | <= 50 | 16.998 | <= 100 | PASS |
| `strong-prefix` | 14.810, 17.658, 17.910 | 17.658 | <= 50 | 17.910 | <= 100 | PASS |
| `broad-result` | 123.633, 124.152, 129.879 | 124.152 | <= 150 | 129.879 | <= 250 | PASS |
| `one-character` | 97.766, 98.287, 96.501 | 97.766 | <= 150 | 98.287 | <= 250 | PASS |
| `two-character` | 90.291, 91.568, 96.652 | 91.568 | <= 150 | 96.652 | <= 250 | PASS |
| `warm-repeated` | 60.024, 60.174, 61.500 | 60.174 | <= 100 | 61.500 | <= 150 | PASS |
| `fresh-process-first-search` | 81.134, 81.017, 81.295 | 81.134 | <= 125 | 81.295 | <= 150 | PASS |
| `rapid-typing` final input | 48.730, 49.501, 50.648 | 49.501 | <= 75 | 50.648 | <= 125 | PASS |
| `rapid-typing` prescribed burst | 548.662, 550.434, 556.827 | 550.434 | <= 600 | 556.827 | <= 700 | PASS |

### Manual product smoke and acceptance assessment

After the campaign, a normal Release Quail process used the current production
C: and D: indexes. A normal query, one-character query, two-character query,
and opening the intended result all passed; the user observed immediate results.
The frozen C DB was benchmark evidence only and was never configured as a user
production index.

| M17 acceptance area | Final evidence | Status |
| --- | --- | --- |
| M16 timing targets and guardrails | One canonical 8x3 above | PASS |
| Full short-query recall and ranking | Permanent late-exact, ranking/order, ASCII/non-ASCII, mutation, and runtime-map tests; 223-test final Release suite | PASS |
| Derived-state lifecycle and generation integrity | Staged v3 implementation, deterministic regressions, final D: rebuild plus create/rename/delete inspections | PASS |
| Proportionate footprint/load/build/mutation cost | Current C:/D: and frozen comparable reports; bounded chunk comparisons | PASS |
| Broad, warm, fresh, and rapid interaction | Final canonical campaign | PASS |
| Affected Release builds | `Quail.App` Release PASS, zero warnings/errors; helper Release PASS | PASS |
| Manual product smoke | Current production C:/D: normal/one/two/open | PASS |
| Out-of-scope boundaries | No M17.5 rebuild optimization, M18 ranking redesign, M19/M20 service work, generic scheduler, or persistent-format change beyond v3 | PASS |

`LatestSearchCoordinator` remains in `Quail.App`. That is compatible with the
accepted M15 source-neutral boundary and is not an M17 blocker. The post-0.3
direction may move reusable source-neutral request execution coordination out
of the frontend, but no such refactor is part of this milestone.

Consolidation verified all final JSON reports, 24 trace JSONL files, summary
medians against `results.json`, every timing guardrail, report/lifecycle
cross-checks, the record-count diagnosis, and an unchanged raw-evidence
inventory. No physical operation, benchmark rerun, manual smoke rerun, build,
or test suite was run for this documentation-only consolidation.

Final independent QA of the whole PR passed after consolidation. No production
code changed after the accepted implementation commit; the post-acceptance
branch changes were evidence consolidation and independent post-0.3
documentation. The user then explicitly approved merge.

## Invalidated short-query candidate

For one- and two-character literal substring queries, `IndexStore` now reads a
bounded four-times-result-limit candidate set in stable SQLite `rowid` order and
applies the existing final ranking and result limit. This avoids repeated full
table scans and candidate-expansion sorts on the short-query path. The existing
trigram path and its candidate-expansion behavior remain unchanged for queries
of three or more characters. This strategy is invalid: a later exact or prefix
candidate can be excluded before the unchanged ranker sees it.

The focused regression uses `Limit=1` and inserts `ba`, `ca`, `da`, `ea`, then
`a` (and the analogous `bks` through `eks`, then `ks`). The invalid candidate
returns the first substring instead of the later exact match for both query
lengths. The test failed on `a70ff8fd86694609e35e5aaa2de0495d3a31fc5c` and is
retained as the correctness guard for a future replacement.

The unsafe short-query strategy and zero defer were reverted after QA. The
coordinator's duplicate identical-query coalescing remains, because it does not
change candidate recall or ranking behavior and independently removed measured
queue work.

## Invalidated physical-host campaign

The canonical M16 harness ran once with all eight scenarios and three
repetitions on commit `fa66270461f8f4461fd574c490a82ad214dcad39` with a clean
worktree. The host had .NET SDK `10.0.400`, Windows `10.0.26200.0`, two active
indexes, 858,879 records, and 273,358,848 database bytes. The compact,
non-sensitive samples are committed in `docs/milestones/M17-baseline.json`.
The ignored raw traces remain under `artifacts/m17/final-fa66270/`.

| Scenario | Samples (ms) | Median (ms) | Target / guardrail | Result |
| --- | ---: | ---: | ---: | --- |
| `ordinary-name` | 21.865, 16.439, 21.628 | 21.628 | <= 50 / <= 100 | PASS |
| `strong-prefix` | 18.427, 17.149, 15.426 | 17.149 | <= 50 / <= 100 | PASS |
| `broad-result` | 135.741, 136.219, 139.119 | 136.219 | <= 150 / <= 250 | PASS |
| `one-character` | 39.891, 41.092, 40.867 | 40.867 | <= 150 / <= 250 | PASS |
| `two-character` | 43.410, 41.082, 47.259 | 43.410 | <= 150 / <= 250 | PASS |
| `warm-repeated` | 60.807, 60.667, 60.177 | 60.667 | <= 100 / <= 150 | PASS |
| `fresh-process-first-search` | 89.961, 88.056, 87.328 | 88.056 | <= 125 / <= 150 | PASS |
| `rapid-typing` final | 56.422, 58.381, 55.676 | 56.422 | <= 75 / <= 125 | PASS |
| `rapid-typing` burst | 538.225, 538.309, 525.437 | 538.225 | <= 600 / <= 700 | PASS |

`broad-result` Core median was 126.547 ms with zero measured queue wait.
`warm-repeated` Core median was 47.755 ms with zero measured queue wait. The
short-query Core medians were 33.528 ms (`one-character`) and 33.573 ms
(`two-character`), with zero measured queue wait.

## Verification before the blocker

| Area | Result | Evidence |
| --- | --- | --- |
| Short-query correctness | INVALIDATED | Post-candidate QA showed the `rowid` window could omit later exact matches. The final performance candidate therefore does not satisfy M17 correctness constraints. |
| Ranking and multi-index ordering | PASS | Existing focused ranking and multi-index tests passed; final ranking and merge comparers were not changed. |
| Duplicate-query scheduling | PASS | The focused coordinator test proves one running identical Core search serves the latest UI generation, while the existing stale-result, invalidation, bounded-pending, lane, and disposal tests remain green. |
| Focused automated suite | PASS | `dotnet test tests\\Quail.Core.Tests\\Quail.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~FileSearchTests|FullyQualifiedName~FileSearchRankingTests|FullyQualifiedName~MultiIndexSearchTests|FullyQualifiedName~M13BSearchSchedulingTests|FullyQualifiedName~M11SearchCoordinatorTests|FullyQualifiedName~M12IndexCatalogTests|FullyQualifiedName~SearchPerformanceTraceTests|FullyQualifiedName~M11ShortQueryDeferrerTests"` — 57 passed. |
| Affected Release build | PASS | The final canonical harness rebuilt `Quail.App`, `Quail.Core`, and `Quail.FileSystem` in Release with zero warnings and zero errors before the 8x3 campaign. |

No bounded multi-index concurrency was added: the local candidate and duplicate
query changes met every target, so the measured evidence did not justify extra
parallelism or a scheduler framework.

## Final scope and closure

The historical safe-path measurement of approximately 1.46 seconds for a
one-character no-exact-match query established why M17-S was needed; it is not a
current M17 blocker. M17-S outcome A and the amended M17 contract approved the
filesystem-owned compact derived structure merged through PR #10.

No M17.5 rebuild optimization, ranking v2, maintenance/service work, package
work, generic scheduler, or unrelated architecture change was introduced.
Search actions and path resolution remain unchanged. The user-owned rebuild,
compact lifecycle evidence, one final canonical 8x3 campaign, manual smoke, and
final independent QA are complete. PR #10 was merged to `main` as
`8491aa2e196f70276345928eb9bd4b107fec4ef6`. M17 is complete.