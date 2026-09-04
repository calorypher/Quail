# Quail Product Vision

## North star

Quail should evolve from fast local file search into a local-first universal search system for the user's digital information.

Working internal description:

> Find literally anything, literally anywhere.

This is a product-direction shorthand, not a final public slogan or branding decision.

The long-term goal is one fast search surface that can find useful things regardless of where they live: local files, historical file locations, mail, cloud documents, browser history and bookmarks, calendars, notes, contacts, removable media, network sources, and other sources that can be accessed safely and reliably.

Quail should not become a generic launcher whose primary value is a large collection of unrelated commands or plugins. Launching applications and small utility providers may still be useful supporting features, but the core product identity is search across the user's own information.

## Objects rather than storage locations

Quail should treat the thing being searched for as the primary concept. The source and physical representation are properties of that thing, not the product's main abstraction.

Examples of searchable objects include:

- a local file or directory;
- an email message or thread;
- a file or native document on Google Drive, OneDrive, or another cloud source;
- a browser page visit or bookmark;
- a calendar event;
- a contact;
- a note;
- an application;
- a removable-volume item;
- another source-specific entity that can be represented safely and usefully.

Searchable content fragments are not necessarily first-class objects. A matching paragraph in a DOCX file, page in a PDF, or section of an email body may be stored as a search fragment that points back to its owning object.

A useful conceptual hierarchy is therefore:

```text
Source
  -> Object
      -> Searchable fragments
```

The source explains where an object comes from. The object carries identity, metadata, state, history, and actions where available. Search fragments explain why an object matched a content query.

## Identity, history, and relationships

Quail can provide more value when it models durable identity and change history instead of only the current display name and location.

For local NTFS items this may include stable volume/file identity, previous names and locations, rename/move history, deletion state, and temporal queries over filesystem activity.

The same principle should be used for other sources when they provide durable identifiers or change feeds. For example, cloud-drive item IDs and mail message/thread IDs can become source-native identities rather than being reduced to transient display strings.

Provider/source identity, content identity, and cross-source relationships are separate concepts. A shared Core model should not force unrelated sources into one physical identifier shape.

Relationships may later be useful, for example an email attachment corresponding to a downloaded local file, but Quail should not begin by building a general knowledge graph. Relationships should be added only when a concrete source and user workflow justify them.

## Unified local index

Normal interactive search should primarily query Quail's local index rather than fan out live queries to every external service on every keystroke.

Each source adapter should, where practical:

1. perform an initial enumeration or import;
2. maintain a local synchronization cursor/checkpoint;
3. consume incremental changes when the source provides a reliable mechanism;
4. normalize searchable metadata and optional content into Quail's local model;
5. expose source-specific actions such as open, reveal, navigate, or launch.

Provider-side search may still be used as an explicit fallback or capability where local indexing is incomplete, but it should not define the latency model of the main search experience.

This local-first model is intended to preserve instant search, partial offline usefulness, one coordinated ranking experience, and predictable behavior across heterogeneous sources.

## Search experience

Quail should support two complementary search surfaces rather than forcing every workflow into a transient launcher.

**Quick Search** is the global-hotkey path for fast type/select/open interactions. It should remain compact, keyboard-first, and optimized for perceived immediacy.

**Full Search / Object Explorer** is the persistent result-browser direction. Its first implementation may be filesystem-only, with larger result sets, sorting, filtering, and richer metadata. As identity/history and heterogeneous sources arrive, the same surface can evolve into a broader object explorer rather than being replaced by a second unrelated search product.

A future query should be able to mix results from multiple sources in one ranked result set.

For example, a query for `invoice` could eventually return:

- a PDF currently in Downloads;
- the same local file found through an old name or previous path after a rename/move;
- recent Gmail messages about invoices;
- a file stored on Google Drive;
- a Google Doc whose content contains the term;
- a browser visit from the previous week;
- a Firefox bookmark to an invoicing site.

The source should be visible as useful context, but the user should not have to decide which search engine or data silo to query before typing.

Temporal and source-aware filters may later be supported, for example:

```text
invoice source:mail
invoice type:pdf
invoice from:last-week
invoice moved:yesterday
```

Exact query syntax is not frozen by this document. UI filters and textual query syntax should evolve from real workflows rather than from a speculative complete query language.

## Ranking

A unified index makes ranking across unlike objects a central product problem.

Potential ranking signals include:

- text-match quality;
- field importance, such as filename/title/subject versus body-only matches;
- recency;
- user interaction history;
- filesystem activity where relevant;
- browser visit count;
- object/source type;
- current availability;
- exact-name or exact-title matches.

Ranking should be measured against real workflows rather than expanded through speculative heuristics. Before cross-source ranking exists, each source should first provide useful and measured source-specific relevance.

## Privacy and trust

A universal personal index can become one of the most sensitive data stores on the machine. Privacy behavior is therefore part of the architecture, not later UI polish.

Preferred principles:

- local-first by default;
- no Quail-operated cloud service required for normal indexing/search;
- external sources are opt-in;
- source credentials/tokens are stored through appropriate OS credential facilities rather than in the searchable index;
- metadata-only versus content indexing should be separable where practical;
- users should be able to exclude paths, accounts, source categories, or other scopes;
- users should be able to clear local indexed data;
- retention semantics must be explicit when a source item is deleted or source history is cleared.

Quail must not silently retain data that a reasonable user would believe was deleted from the source. A future source may support both mirror-style deletion and an explicit history-retention mode, but retaining deleted source data must be a deliberate user choice with clear limits.

This principle also applies to future NTFS history: the fact that rename/move/delete events can be observed does not by itself authorize indefinite historical retention.

## Source diversity

Not every source will provide the same capabilities. Quail should not assume that every adapter has a REST API, a stable ID, a change feed, content access, historical events, or a privileged background component.

A source may support some subset of:

- stable identity;
- initial enumeration;
- incremental synchronization;
- content extraction;
- historical events;
- offline availability;
- relationships;
- actions;
- source-native search.

The shared model should represent missing capabilities where real sources require that distinction rather than pretending all sources have identical guarantees.

Do not design a complete capability framework from the filesystem source alone. Add broader contracts only after multiple real source implementations demonstrate common requirements.

## Core and source boundaries

The intended compile-time architecture is source-neutral and dependency-inverted:

```text
Quail.App ---------> Quail.Core
                         ^
                         |
Quail.FileSystem -------+
Quail.Browser ----------+
Quail.Drive ------------+
Quail.Mail -------------+
Quail.Calendar ---------+
```

`Quail.Core` owns application/search orchestration and the minimal query/result/action semantics genuinely shared by normal search surfaces. It must not depend on a concrete source. Concrete source modules implement Core-owned contracts and keep source-specific indexing, persistence, synchronization, identity, ranking details, and actions in the source module.

At runtime, the application may register source implementations with Core and Core may aggregate their results. That runtime flow does not imply a compile-time `Core -> source` dependency.

A small internal contract such as `ISearchSource`, or an equivalent dependency-inversion seam, is acceptable when it exists only to support demonstrated search, result projection/action routing, and aggregation needs. This modularity does not imply a public plugin framework, dynamic provider loading, an extension SDK, capability matrix, provider lifecycle framework, version negotiation, or one universal storage schema.

Normal App/Core search-flow types should not be named or shaped around filesystem merely because FileSystem is the first source. The common result model should not require every result to expose a filesystem path, file/directory shape, NTFS attributes, or filesystem timestamps. Source-specific metadata remains source-specific until multiple real sources demonstrate a useful common abstraction.

### Physical source optionality

First-party source modules are intended to become physically optional.

A future deployment should be able to omit a source module such as `Quail.FileSystem.dll`. Doing so should remove only that source's results, source-specific actions, indexing/synchronization behavior, and source-specific settings. Quail's source-neutral Core, Quick Search, Full Search, normal result presentation, and other installed sources should continue to work.

This does not require a runtime loader today. During early development, `Quail.App` may statically compose first-party source implementations and may directly reference a source for explicitly source-specific administration. Those references should remain isolated so later optional module loading primarily changes the composition root rather than forcing another broad search-stack refactor.

Dynamic loading should be introduced only when a concrete product/deployment need exists. Physical optionality of first-party modules does not by itself commit Quail to third-party plugins or a public SDK.

## Frontend and interface boundaries

The long-term architecture also requires **frontend interchangeability**.

`Quail.App` should eventually contain primarily frontend-specific responsibilities: WinUI rendering/presentation, input and window/focus handling, UI-specific debounce/timing policy, Windows desktop integration, and composition-root work. Source-neutral application/search orchestration that another frontend or search surface would need must not remain permanently owned by `Quail.App`.

Responsibilities such as latest-request/supersession coordination, stale-result protection, duplicate-query coalescing, and source-neutral scheduling/execution coordination should live in `Quail.Core` or an equally narrow source-neutral application layer before a second frontend or shared search surface would otherwise need to duplicate them. UI-specific policy such as `QuickSearchInputPolicy` may remain frontend-local.

The intended architectural test is simple: replacing WinUI must not require reimplementing Quail's source-neutral search-session semantics.

The current post-M15 split is acceptable through Quail 0.3. This is a post-0.3 direction and must not broaden the active 0.3 scope merely to move existing coordinator code.

`Quail.Cli` should likewise be a first-class interface to the same Core/application behavior, not a diagnostic sidecar with separate search semantics. Non-visual product capabilities should be exposed through the CLI wherever they are meaningful and safe to script, including the same search/ranking semantics, supported filters, source-neutral actions, status/diagnostics, and intentionally exposed administrative/source operations. GUI-only presentation behavior does not require a CLI equivalent.

Both GUI and CLI should invoke the same shared application/search layer. The CLI must not grow a second independent search engine. Automation-friendly behavior such as deterministic exit semantics and machine-readable output should be preferred where useful.

The detailed approved direction is recorded in `docs/post-0.3-interface-direction.md`.

## Platform direction

Quail remains Windows-first. The current NTFS/MFT/USN engine and WinUI 3 desktop application are intentionally optimized for Windows rather than constrained by speculative cross-platform requirements.

Linux remains a plausible later platform because the long-term product is broader than a Windows launcher. Platform-neutral Core logic and remote/source adapters should therefore avoid unnecessary coupling to WinUI or NTFS where natural boundaries already exist.

This does not mean building a cross-platform abstraction layer in advance. Windows-specific filesystem, service, hotkey, tray, shell, installer, and UI integration should remain explicit platform components.

If Linux support is eventually pursued, the expected model is to reuse portable Core/source contracts and source logic where appropriate while providing Linux-specific local filesystem/platform integration. A WinUI 3 frontend is not portable and may require a separate Linux frontend; this is acceptable if the rest of the application remains cleanly separated.

## Incremental development

The north-star vision is intentionally much broader than the scope of any single release.

Quail should continue to be built as small validated vertical slices. Do not attempt to implement all sources at once and do not build a speculative public plugin framework around imagined future adapters.

Quail 0.2 established the first public file-search desktop baseline: Quick Search, real local NTFS indexes, GUI-managed index configuration, ranking, packaging, and a diagnostic CLI.

The approved Quail 0.3 release plan makes filesystem search good enough for ordinary daily use and aims to replace Everything in the developer's normal local-file-search workflow. Its M15 architecture milestone establishes a source-neutral Core/FileSystem dependency boundary; later 0.3 milestones cover measured performance, ranking/relevance, automatic filesystem maintenance, launch-on-startup, UI/settings integration, Full Search v1, polish, and stabilization.

Stable NTFS file identity/history is the preferred deeper filesystem direction after that foundation. 0.3 should preserve stable identity and a reliable change stream but should not silently introduce historical/deleted-item retention.

After the filesystem experience and identity/history foundation are mature enough, a genuinely different source should validate and refine the shared model. Browser history/bookmarks remain a plausible low-friction first candidate because they test heterogeneous unified search without requiring a cloud account. A later cloud source such as Google Drive or Gmail can then validate incremental remote synchronization, OAuth/account handling, credential storage, and retention semantics.

Before introducing another frontend or shared search surface, move source-neutral search-session orchestration out of the WinUI-specific application layer so the new interface can reuse the same semantics. Preserve CLI/Core parity incrementally as non-visual capabilities are added instead of accumulating GUI-only product behavior and planning a later parity rewrite.

Only after multiple real source implementations exist should Quail generalize broader source/provider contracts from demonstrated common requirements. The M15 dependency-inversion seam is intentionally much narrower than that future work.

## Relationship to launcher features

Application launching, calculator/conversion helpers, web actions, and similar launcher capabilities may still be added when they are cheap and useful. They are supporting conveniences, not the reason Quail exists.

The product should not measure success by matching Flow Launcher, Raycast, Command Palette, Everything, or another product feature-for-feature. Comparisons are useful for concrete workflows and performance, but Quail's differentiating direction is one fast, local-first index of the user's own searchable digital world.

## Conceptual relationship to WinFS

The object-centric direction resembles a narrow part of the old WinFS vision: identity and searchable objects matter more than one current filesystem path.

Quail is not intended to replace NTFS, become an operating-system storage platform, or implement a universal relational object store. Existing systems remain authoritative sources. Quail observes or synchronizes them and builds a local search-oriented model above them.

## Current decision boundary

This document records the strategic north star, not a committed implementation sequence for all future versions.

Quail 0.2 is the current public baseline. Quail 0.3 has an approved M15-M24 release plan focused on a daily-usable, fast, automatically maintained filesystem-search product with Quick Search and Full Search. M15 additionally establishes the source-neutral dependency direction needed for future heterogeneous and physically optional first-party sources without implementing a runtime plugin/loading framework.

Frontend interchangeability and CLI/Core parity are approved post-0.3 architectural directions. They do not add scope to Quail 0.3, but the first suitable post-0.3 planning cycle should schedule source-neutral orchestration extraction before a second frontend/shared search surface would otherwise duplicate it.

File identity/history is directional work after the 0.3 filesystem-usability foundation. Browser history/bookmarks remain the likely first heterogeneous source after the filesystem-focused releases. Later sequencing should continue to change when measured behavior and real usage provide better evidence.