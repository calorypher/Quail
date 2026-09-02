# Quail Roadmap

This roadmap records the current product direction. An approved release plan defines the intended milestone sequence, but only the currently approved active milestone authorizes implementation. Later milestones may be revised when earlier evidence changes the technical picture.

The long-term product direction is described in `docs/product-vision.md`. Repository workflow and engineering guardrails are defined in `AGENTS.md`.

## 0.1 — File Engine / CLI — COMPLETE

Quail 0.1 established the Windows 11 x64 local file-search foundation.

Released as:

- version `0.1.0`;
- tag `v0.1.0`;
- installer `Quail-0.1.0-Setup.exe`.

The release established the persistent per-volume SQLite index, official NTFS/MFT/USN enumeration and incremental synchronization, filename search, metadata filters, multi-index aggregation, status diagnostics, shell open, CLI, and initial Inno Setup packaging.

Milestones M01 through M07 are complete. Detailed history and verification evidence remain under `docs/milestones/`.

## 0.2 — Launcher Shell — COMPLETE

Quail 0.2 turned the file engine into the first public desktop application release.

Released as:

- version `0.2.0`;
- tag `v0.2.0`;
- exact release source commit `551fb1dcf9b90b53a95399b45955ca30dc59c494`;
- installer `Quail-0.2.0-Setup.exe`;
- public prerelease on GitHub.

The release provides:

- production WinUI 3 `Quail.exe`;
- global Quick Search hotkey and tray lifecycle;
- persistent GUI-managed local NTFS indexes;
- protected ProgramData index storage and narrow elevated Build/Rebuild/Refresh operations;
- real indexed file search with deterministic ranking;
- configurable hotkey and theme;
- diagnostic/administrative `Quail.Cli.exe`;
- framework-dependent Windows deployment with prerequisite detection;
- Inno Setup packaging under fixed `C:\Program Files\Quail`;
- public MIT-licensed source and third-party notices.

M08 through M14 and the bounded pre-M13 supporting work are complete. Detailed milestone specifications, results, QA evidence, release notes, and security/publication history remain under `docs/milestones/` and `docs/releases/`.

Known 0.2 product limitations intentionally carried forward include:

- search performance is materially behind Everything in some normal workflows;
- ranking/relevance needs another pass;
- indexes require explicit manual refresh/rebuild rather than continuous background maintenance;
- no launch-on-startup option in the finished product workflow;
- no Full Search result browser;
- Settings and Index Manager remain separate surfaces;
- filesystem is the only search source;
- no automatic updater;
- no Windows Service;
- no file identity/history UX;
- Windows 11 x64 only.

The `v0.2.0` tag is an immutable release baseline. Post-release documentation and future development continue on newer `main` commits and must not move the tag.

## 0.3 — Daily-usable Filesystem Search / Everything replacement — ACTIVE PLAN

### Product goal

Quail 0.3 should be able to replace Everything for ordinary daily local-file search on the developer's Windows PC.

This does **not** mean feature parity with Everything. The target workflow is:

```text
Windows starts
  -> Quail starts automatically
  -> the filesystem index stays current without manual refresh
  -> the global hotkey opens Quick Search
  -> normal searches feel immediate
  -> useful results rank near the top
  -> Enter opens the intended item
```

For workflows that need more than a transient launcher:

```text
Quick Search
  -> Full Search
  -> inspect, sort and filter a larger result set
  -> open, reveal, or copy the path of the selected result
```

The detailed product and architectural direction is recorded in `docs/0.3-direction.md`.

The approved release plan is M15 through M24 with a bounded M17.5 investigation inserted after M16 evidence exposed a separate full build/rebuild performance gap. The sequence remains intentionally mostly linear so each milestone validates the assumptions used by the next one. If M17.5 or M19 produces evidence that materially changes the planned implementation path, return to roadmap planning before expanding scope.

### M15 — Core / FileSystem Boundary Extraction

**Goal:** turn `Quail.Core` into the source-neutral application/search core and move the existing NTFS/filesystem implementation into a concrete source module without changing product behavior.

The approved compile-time dependency direction is:

```text
Quail.App ---------> Quail.Core
                         ^
                         |
Quail.FileSystem -------+

later concrete sources also depend on Quail.Core
```

`Quail.Core` must not reference `Quail.FileSystem` or another concrete source. A minimal internal Core-owned source/search contract such as `ISearchSource`, or an equivalent dependency-inversion seam, is allowed when required for search, Core-level result/action projection, and aggregation. This does not authorize a provider framework.

Scope:

- create a filesystem-specific production module/project, expected to be named `Quail.FileSystem` unless the concrete dependency graph provides a better simple name;
- move NTFS/MFT/USN access, filesystem-specific SQLite persistence, build/sync/status behavior, filesystem identity, filesystem search/ranking internals, and filesystem actions out of generic Core;
- introduce only the minimal source-neutral Core query/result/action/source contracts required by current Quick Search and the already-approved future Full Search direction;
- remove direct filesystem implementation details such as `IndexStore`, native NTFS identifiers, filesystem result wrappers, and mandatory file-shaped result assumptions from the normal App/Core search path;
- use source-neutral names for normal search orchestration/coordinator/presentation types unless a type genuinely handles only filesystem behavior;
- preserve CLI and filesystem-administration behavior; direct `Quail.App -> Quail.FileSystem` references may remain only for static composition and explicitly filesystem-specific administration/UAC paths;
- isolate such App-to-FileSystem references so future optional source loading is primarily a composition change rather than a redesign of Quick Search/Core/presentation;
- preserve existing index behavior and 0.2 user-visible behavior unless a narrowly justified compatibility change is unavoidable.

Long-term invariant established by M15:

- first-party source modules are intended to become physically optional;
- omitting a source such as `Quail.FileSystem.dll` should eventually remove only that source's results, actions, indexing/synchronization behavior, and source-specific settings;
- source-neutral Core, Quick Search, Full Search, presentation, and other installed sources should continue to work;
- M15 does not implement runtime module loading or require a deleted-DLL runtime test.

Out of scope:

- public provider/plugin SDK or dynamic provider loading;
- runtime source discovery/loading as an M15 deliverable;
- provider discovery/versioning, lifecycle, or generalized capability framework;
- browser/cloud sources;
- universal object/storage model;
- final heterogeneous/cross-source ranking;
- performance or ranking redesign;
- service/background maintenance;
- storage migration merely to make the refactor look cleaner.

Acceptance boundary:

- existing 0.2 user-visible behavior remains functional;
- NTFS/index implementation no longer lives in generic Core;
- `Quail.Core` has no dependency on `Quail.FileSystem` or another concrete source;
- `Quail.FileSystem` implements Core-owned minimal source/search contracts;
- Core search can be exercised with a non-filesystem fake source without constructing filesystem objects;
- the normal App/Core search/coordinator/presentation path uses source-neutral contracts and naming and does not leak NTFS/SQLite/file-only implementation assumptions;
- direct App-to-FileSystem dependencies, if retained, are isolated to static composition and explicitly filesystem-specific administration;
- Quick Search, CLI, and Build/Rebuild/Refresh remain usable;
- existing tests and Release builds pass;
- no speculative provider/plugin/capability/runtime-loading framework is introduced.

Stop if completing the split would require changing the privilege/security model, performing a material index-schema migration, introducing a broad provider/runtime-loading architecture, or materially redesigning search/ranking. The detailed executable contract is in `docs/milestones/M15.md`.

### M16 — Search Performance Investigation & Target

**Goal:** measure why Quail is slower than Everything and define evidence-based performance targets before changing the search engine.

Scope:

- compare Quail and Everything on the same machine, representative corpus, and representative query set;
- measure cold and warm behavior, one/two-character queries where supported, normal filename queries, broad-result queries, and repeated typing/query changes;
- measure end-to-end Quick Search latency rather than database-only latency;
- separate database/candidate/ranking/Core/UI/icon costs only where that helps identify a real bottleneck;
- add the minimum repeatable instrumentation needed for diagnosis;
- record bottlenecks, likely fixes, expected benefit/cost, and release targets for M17.

Do not perform a large optimization or engine rewrite during this milestone.

Acceptance boundary:

- repeatable benchmark procedure and durable baseline exist;
- the practical Quail-vs-Everything gap is quantified;
- primary bottlenecks are supported by measurements;
- M17 has explicit evidence-based performance targets and recommended work.

Stop if the current search architecture appears fundamentally incapable of meeting the product goal without a large redesign; return a recommendation instead of beginning that redesign implicitly.

### M17 — Search Engine Performance

**Goal:** remove the measured interactive-search bottlenecks from M16 and make normal filesystem search feel effectively immediate.

Scope is driven by M16 evidence and may include query/candidate strategy, FTS/direct lookup behavior, unnecessary materialization/allocation, work performed before top results are available, interactive query-update behavior, and UI/icon work only where profiling shows those areas are on the critical path.

Do not add complexity for unmeasured theoretical performance issues. Full index build/rebuild performance is explicitly outside M17 and is owned by M17.5.

Acceptance boundary:

- M16 release targets are met, or a simpler result with practically equivalent user experience is explicitly justified by measurements;
- normal search feels effectively immediate in representative use;
- the practical gap to Everything is materially reduced;
- search correctness remains intact;
- build/sync/storage costs do not regress materially without a documented reason.

Stop if meeting the target requires replacement of the overall storage/search architecture or another material roadmap-level redesign.

### M17.5 — Index Build/Rebuild Performance Investigation & Target

**Goal:** determine why a full Quail filesystem index build/rebuild takes on the order of minutes at the current corpus scale, quantify the dominant costs, and decide whether a separate bounded production optimization belongs in 0.3 before ranking and continuous-maintenance work proceed.

M16 comparison work exposed a separate high-signal gap: on the physical host, Quail currently rebuilds a roughly 0.86-million-record corpus in about three minutes, while a 30 FPS Everything recording showed its roughly 0.93-million-object rebuild completing in about 2.6-2.7 seconds. The datasets, stored metadata, durability model, and internal architectures are not identical, so Everything is a reference point rather than a parity requirement. The magnitude of the gap is nevertheless large enough to justify measurement before 0.3 freezes its filesystem lifecycle.

Scope:

- establish a repeatable full build/rebuild benchmark on a representative Quail corpus and preserve the current end-to-end baseline;
- attribute elapsed time across the major existing stages, including MFT/enumeration, metadata acquisition, SQLite base-row writes, FTS/search-index maintenance, and WAL/checkpoint/finalization;
- measure the cost of the current staging/durability path where it materially contributes;
- determine whether per-row FTS triggers, transaction/bulk-insert strategy, metadata acquisition, serial work, CPU utilization, or I/O are dominant bottlenecks;
- evaluate bounded parallelism or a staged reader/worker/writer pipeline only as measured hypotheses; do not assume that more threads or concurrent SQLite writers are beneficial;
- record Quail database size, indexed object count, approximate bytes/object, and stabilized memory footprint; collect the comparable Everything database/object figures where available and interpret differences in light of Quail's broader metadata and SQLite/FTS representation;
- optionally record cold-load/startup implications only if they are inexpensive to measure and materially relevant;
- produce an evidence-based target/range and a recommendation on whether 0.3 needs a separate production build/rebuild optimization milestone before M18/M19, or whether the work should be deferred with an explicit rationale.

Out of scope:

- implementing the production optimization itself;
- weakening durability, protected-storage, or integrity guarantees for benchmark results;
- changing search/ranking semantics;
- schema/storage redesign without a separate roadmap decision;
- continuous-maintenance/service architecture;
- speculative multicore or multi-writer redesign not supported by measurements.

Acceptance boundary:

- the representative rebuild baseline is reproducible and its major phase costs account for the practical majority of elapsed time;
- dominant bottlenecks are identified with evidence rather than inferred from total runtime alone;
- Quail storage/record-count/bytes-per-object baseline is recorded, with an appropriately qualified Everything comparison where available;
- realistic optimization opportunities are ranked by expected gain, implementation risk, and maintenance cost;
- the milestone ends with an explicit roadmap recommendation: add a bounded production optimization before M18/M19, or continue to M18 with rebuild optimization deferred.

If the evidence recommends material schema, durability, privilege/security, or broad architecture changes, stop and return to roadmap planning rather than implementing them inside M17.5.

### M18 — Ranking / Relevance v2

**Goal:** make the intended result appear high enough that fast search is also useful search.

Scope:

- fix the known candidate-recall limitation where an over-limit same-text-tier candidate set can hide better path/context matches;
- improve text relevance and useful exact/prefix/token-prefix/substring behavior where measurements support it;
- preserve useful user-space results above hidden/internal/system-heavy matches where appropriate;
- improve behavior for many duplicate or near-duplicate names;
- keep deterministic, stable ordering and bounded tie-breakers;
- build a representative relevance regression set from real file-search workflows;
- keep ranking within the latency budget established by M16/M17.

Out of scope unless evidence proves necessity:

- ML/LLM ranking;
- interaction-history learning;
- fuzzy matching;
- generalized cross-provider ranking.

Acceptance boundary:

- known 0.2 recall/ranking problems are addressed;
- representative top-N results improve measurably;
- ordering remains deterministic;
- performance remains within the accepted search budget.

### M19 — Continuous Maintenance Boundary Spike

**Goal:** determine the smallest safe architecture that can maintain NTFS indexes automatically without routine manual UAC/Refresh.

This is a bounded design/feasibility milestone because the likely boundary is privileged and security-sensitive.

Scope:

- determine whether a Windows Service is actually required and, if so, define its smallest responsibility;
- validate service account/privilege requirements, IPC, input validation, protected index access, and interaction with the current elevated worker;
- define persistent USN-consumption and checkpoint lifecycle;
- define restart/catch-up, continuity-loss, and recovery semantics;
- identify installer/service-registration implications;
- perform a focused threat analysis of the actual privileged boundary;
- use small PoCs only where evidence is needed to resolve a real unknown.

Do not design a generic background agent, updater service, plugin host, remote API, or enterprise service-management layer.

Acceptance boundary:

- one recommended minimal architecture is selected;
- key privilege/integrity unknowns are resolved with evidence;
- the trust boundary and supported recovery semantics are explicit;
- M20 receives a closed implementation contract;
- the future of the current elevated worker is clear.

Stop on an unresolved privilege-escalation/data-integrity concern, a requirement for a substantially broader privileged service, or a conflict with the protected index-storage model.

### M20 — Continuous Filesystem Maintenance

**Goal:** make manual Refresh/Rebuild unnecessary for normal index freshness.

Scope:

- implement the architecture selected by M19;
- continuously or effectively continuously consume incremental USN changes;
- persist checkpoints safely;
- catch up after Quail or Windows restarts;
- handle sleep/resume where real behavior requires explicit treatment;
- validate journal identity/range/continuity;
- recover automatically only where correctness is clear;
- enter explicit `Rebuild required` or equivalent state where correctness can no longer be guaranteed;
- expose a simple filesystem index-health/status model for the UI;
- keep querying and maintenance compatible;
- add only the installer/service work required by the selected boundary;
- perform security/integrity verification proportional to the privileged boundary.

This milestone updates current filesystem state only. It does not introduce durable rename/move/delete history or deleted-item retention.

Acceptance boundary:

- create/rename/move/delete changes reach the index without manual Refresh;
- restarts do not silently lose pending changes;
- checkpoint and continuity handling are correct;
- loss of continuity cannot silently produce a trusted-but-stale index;
- the interactive application remains unelevated;
- background work is practically idle when there are no changes;
- independent security/integrity review passes for the implemented boundary.

### M21 — Unified Settings & Launch on Startup

**Goal:** remove routine development-tool friction from startup and index administration.

Scope:

- replace the split ordinary Settings + Index Manager workflow with one coherent Settings window;
- include real 0.3 sections only, expected to be `General`, `Indexing`, and `About`;
- General includes launch with Windows, global hotkey, and theme;
- Indexing includes indexed volumes, health/status, rebuild/recovery actions, and useful diagnostics;
- use a straightforward supported Windows startup mechanism appropriate to the deployment model;
- preserve existing user configuration where practical;
- keep filesystem-specific settings sufficiently isolated that future omission of the FileSystem module can remove its settings without breaking ordinary Settings.

Do not add empty Browser/Cloud/Provider/Plugin pages or a generalized settings-extension/deployment subsystem.

Acceptance boundary:

- launch-on-startup can be enabled/disabled and survives real sign-out/reboot verification;
- ordinary settings and index management are available in one coherent surface;
- index health from M20 is understandable without exposing unnecessary service internals;
- recovery actions exist but are not the normal freshness workflow.

### M22 — Full Search v1

**Goal:** add the first persistent result-browser surface for workflows that exceed the transient Quick Search list.

In 0.3 this is filesystem-only **Full Search v1**, not yet the full multi-source Object Explorer.

Scope:

- normal persistent resizable window;
- transition from Quick Search to Full Search while preserving the query;
- one source-neutral internal GUI-to-Core search-request/orchestration path shared by Quick Search and Full Search;
- larger scrollable/list-table result set;
- filesystem fields needed by this 0.3 surface: name, path/context, file/folder kind, size, modified date/time, and created date/time when the indexed model can provide it correctly;
- keyboard navigation;
- open;
- open containing folder/reveal;
- copy path;
- useful sorting;
- practical filters for file/folder, extension/type, size range, modified range, and created range where supported correctly;
- hidden/system/read-only as advanced filesystem filters where useful;
- sensible empty/error/index-unavailable states.

Prefer compact filter controls/chips/dropdowns over a full visual query builder. Do not re-establish filesystem fields as mandatory semantics for every future Core result merely because Full Search v1 is filesystem-only.

Out of scope:

- boolean AND/OR query trees;
- saved searches;
- tabs;
- preview subsystem;
- rename/delete/copy/move file management;
- multi-pane file manager behavior;
- history/relationship panes;
- provider/source navigation for sources that do not yet exist.

Acceptance boundary:

- a user can practically inspect and narrow a large filesystem result set without the CLI;
- sort/filter behavior is correct;
- Quick Search and Full Search share Core search semantics instead of implementing two engines;
- representative filtered queries such as type/size/date combinations work end to end.

### M23 — Quick Search & UI Polish

**Goal:** polish the integrated 0.3 UI after the functional surfaces are in place, without sacrificing perceived search latency.

Scope:

- smoother Quick Search result-list expansion/collapse;
- restrained transition animation;
- summon/dismiss/focus lifecycle polish;
- delayed searching/busy indication only when latency is actually perceptible;
- visual and keyboard consistency across Quick Search, Full Search, and Settings;
- loading/error/health presentation where needed;
- final layout/DPI polish appropriate to the existing visual direction;
- measure perceived/end-to-end latency after animation and presentation changes.

Animation must not make a fast search feel slower. A spinner must not flash for normal fast queries.

Acceptance boundary:

- normal searches look and feel immediate;
- animation does not block input/result updates;
- no material focus/lifecycle regressions remain;
- the three primary UI surfaces are visually and behaviorally coherent;
- manual visual/interaction QA passes.

### M24 — 0.3 Stabilization / Release Candidate

**Goal:** prove that the integrated 0.3 product satisfies the Everything-replacement workflow and freeze a verified release candidate.

This is a stabilization milestone, not a feature milestone.

Scope:

- full automated regression and clean Release builds;
- installer/payload validation;
- service install/start/stop/uninstall validation if M19/M20 selected a service;
- Quail-Lab verification and physical Windows-host smoke testing;
- launch-on-startup and lifecycle verification;
- continuous-maintenance restart/sleep/resume/continuity checks appropriate to the implemented design;
- Quick Search, Full Search, Settings, and index-health workflows;
- final performance comparison against M16 baseline and Everything;
- relevance regression set;
- resource/idle CPU checks;
- final focused security review of the privileged boundaries actually present;
- representative upgrade from the normal supported 0.2 deployment, without building exhaustive compatibility machinery for unusual development-install variants;
- release notes, changelog, known limitations, and durable QA evidence.

The decisive product smoke is ordinary real-world use without Everything: leave Quail running from Windows startup, create/rename/move/delete files, search them through the hotkey, use Full Search filters, and restart the system without needing manual Refresh or falling back to Everything because Quail is too slow or poorly ranked.

Acceptance boundary:

- exact 0.3 release candidate is identified and reproducible;
- automated, VM, physical-host, performance, relevance, maintenance, and focused security checks pass;
- supported 0.2 -> 0.3 transition is documented and verified at representative scope;
- no release-blocking known defect remains;
- the branch/PR is ready for final independent QA.

Creating/moving `v0.3.0`, publishing a GitHub Release, or replacing release assets remains a separate explicit approval gate.

### Explicitly outside 0.3

Unless an approved milestone finds a narrow prerequisite, 0.3 does not implement:

- browser history/bookmarks source;
- Google Drive, Gmail, OneDrive, calendar, contacts, or other heterogeneous sources;
- content indexing;
- full NTFS file identity/history/lineage product features;
- deleted-item retention/history UX;
- application-provider/launcher expansion as a release goal;
- public/dynamic plugin framework;
- runtime source/module discovery/loading as an M15 deliverable;
- generalized provider SDK/lifecycle/capability/version-negotiation machinery;
- universal source/object storage model;
- full visual query builder or saved searches;
- preview/file-manager operations;
- automatic update installation;
- installer replacement solely for technology churn;
- Linux implementation;
- AI features;
- enterprise deployment machinery without a concrete requirement;
- exhaustive compatibility machinery for arbitrary historical development-release installation variants.

## 0.4 — File Identity & History — DIRECTIONAL

After 0.3 establishes a clean filesystem source boundary and reliable continuous change stream, stable file identity/history is the preferred next deeper filesystem capability.

Working product intent:

> Quail should remember what happened to a file, not only where it is now.

The release should remain search-oriented rather than becoming a filesystem-forensics or auditing product.

Expected areas, not yet numbered into approved milestones:

1. **Identity/history feasibility and semantics** — validate volume/file identity, hardlinks, file-ID reuse, rename/move/delete correlation, journal gaps, and the exact guarantees Quail can safely promise.
2. **History persistence model** — add the smallest event/path-lineage store that supports the product workflows, with storage-growth measurements and explicit retention semantics.
3. **Rename/move lineage** — previous names/paths and search by historical identity/location where useful.
4. **Deletion/history retention** — retain deleted/history data only through an explicit product/privacy decision with clear controls and clearing behavior.
5. **Full Search / Object Explorer v2** — richer history/details presentation and temporal filters built on the existing Full Search surface.
6. **0.4 stabilization/release** — verify identity correctness, retention/privacy behavior, storage growth, performance, and release readiness.

0.3 should preserve stable source-native identity and the change stream needed for this direction, but must not silently retain deleted/history data merely because maintenance observes those events.

## 0.5 — First Heterogeneous Source — DIRECTIONAL

Browser history/bookmarks remain the preferred low-friction candidate for the first genuinely different source because they can validate heterogeneous unified search without immediately requiring OAuth/cloud account infrastructure.

Working goal:

> Prove and refine the source-neutral Core contract with a second source that has fundamentally different identity, persistence, synchronization, ranking signals, and actions.

Expected areas include browser-source feasibility, Firefox/Chromium data access as supported by evidence, bookmark/history identity and retention, local import/indexing, incremental refresh, unified result presentation/ranking, source/type filtering, URL actions, and Full Search/Object Explorer multi-source behavior.

Only after FileSystem and at least one real heterogeneous source exist should Quail generalize broader shared provider/source contracts from demonstrated common requirements. The dependency-inverted M15 seam is intentionally narrower than that future framework-level question.

The exact 0.5 scope, browser families, runtime source-loading needs, and milestone numbering remain intentionally unfrozen until 0.4 and real 0.3/0.4 usage provide better evidence.

## Later directional candidates

The ordering below is intentionally not assigned to fixed version numbers yet.

### Content search

Optional content indexing for selected formats/scopes, with searchable fragments pointing back to their owning objects rather than automatically becoming independent durable objects.

Likely early formats include predictable text-oriented sources such as plain text, PDF, DOCX, XLSX, and PPTX only where mature extraction mechanisms make the maintenance cost reasonable.

### First cloud source

A source such as Google Drive or Gmail can later validate OAuth/account handling, credential storage, remote incremental synchronization, retention semantics, and offline behavior.

### Network file search

Selected SMB/network shares may later become a distinct filesystem-like source with explicit stale/unavailable behavior and controlled refresh. NTFS/USN guarantees must not be assumed for remote shares.

### Update automation

Quail currently has no automatic updater. A later update milestone may compare the current Inno-based deployment with other mature mechanisms against an explicit low-friction/background-update requirement.

Do not turn the filesystem service into a generic privileged updater for convenience. A no-prompt privileged update mechanism requires its own narrowly scoped security design and review.

### Application search and utility actions

Application launching, calculator/conversions, and web actions remain supporting conveniences. Add them only when their value justifies prioritization over the core personal-search roadmap.

### Linux

Linux remains a plausible later platform. Preserve natural portable boundaries in Core/source logic, but do not compromise the Windows implementation or introduce speculative cross-platform abstractions merely to reserve a future port.

### Extensibility

Quail should remain internally modular. A public/dynamically loaded plugin system is not committed. Evaluate extension loading, isolation, permissions, API versioning, compatibility, and update behavior only if real third-party-extension use cases appear.

Physical optionality of first-party source modules is a separate concern from public third-party extensibility. The former may later require a small internal composition/loading mechanism without implying the latter.

## Product constraints across releases

- Keep architecture simple and dependencies limited.
- Build small validated vertical slices.
- Keep `Quail.Core` independent of concrete source modules.
- Keep normal GUI search orchestration/presentation independent from source-specific storage and platform internals.
- Concrete source modules implement Core-owned minimal contracts and retain source-specific identity, indexing, persistence, synchronization, ranking details, and actions.
- Preserve future physical optionality of first-party sources without introducing runtime loading before it is needed.
- Extract broader shared provider contracts only when multiple real sources demonstrate the need; avoid speculative frameworks.
- Preserve source-native identity where useful rather than forcing one universal identifier model.
- Avoid regular full rescans and aggressive polling during healthy operation.
- Do not elevate the interactive launcher UI merely to access NTFS internals.
- Keep privileged/background responsibilities narrow and explicitly justified.
- Measure performance before optimizing and compare against representative real workflows.
- Apply verification rigor according to consequence and likelihood; see `AGENTS.md`.
- Prefer mature packaging/update tooling over custom infrastructure when sufficient.
- Do not bypass or weaken Windows UAC for convenience.
- Windows remains the first platform; preserve natural portable boundaries without speculative cross-platform architecture.
- If a feature is not required for the current vertical slice, materially increases complexity, has many edge cases, and can safely wait, defer it.
