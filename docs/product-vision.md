# Quail Product Vision

## North star

Quail should evolve from a fast local file-search backend into a local-first universal search system for the user's digital information.

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

The file-engine research suggests that Quail can provide more value when it models durable identity and change history instead of only the current filename and path.

For local NTFS items this may include stable volume/file identity, previous names and locations, rename/move history, deletion state, and temporal queries over filesystem activity.

The same principle should be used for other sources when they provide durable identifiers or change feeds. For example, cloud-drive item IDs and mail message/thread IDs can become source-native identities rather than being reduced to transient display strings.

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

This local-first model is intended to preserve instant search, partial offline usefulness, one ranking model, and predictable behavior across heterogeneous sources.

## Search experience

A query should be able to mix results from multiple sources in one ranked result set.

For example, a query for `invoice` could return:

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

Exact query syntax is not frozen by this document.

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

Ranking should be measured against real workflows rather than expanded through speculative heuristics.

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

## Source diversity

Not every source will provide the same capabilities. Quail should not assume that every adapter has a REST API, a stable ID, a change feed, or content access.

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

The shared model should represent missing capabilities explicitly rather than pretending all sources have identical guarantees.

## Platform direction

Quail remains Windows-first. The current NTFS/MFT/USN engine and likely WinUI 3 desktop shell are intentionally optimized for Windows rather than constrained by speculative cross-platform requirements.

Linux is a plausible later platform because the long-term product is no longer merely another general-purpose launcher. The universal object/index model, query/ranking logic, cloud-source adapters, and other platform-neutral capabilities should therefore avoid unnecessary coupling to WinUI or NTFS where natural boundaries already exist.

This does not mean building a cross-platform abstraction layer in advance. Windows-specific filesystem, service, hotkey, tray, shell, and UI integration should remain explicit platform components.

If Linux support is eventually pursued, the expected model is to reuse the portable core and source logic while providing a Linux-specific local filesystem backend and platform integration. A WinUI 3 frontend would not itself be portable and may require a separate Linux frontend; this is acceptable if the rest of the application remains cleanly separated. Cross-platform UI frameworks should not be selected solely to preserve a hypothetical future port if they materially reduce Windows UX, performance, simplicity, or maintainability.

## Incremental development

The north-star vision is intentionally much broader than the scope of any single release.

Quail should continue to be built as small validated vertical slices. Do not attempt to implement all sources at once and do not build a speculative public plugin framework around imagined future adapters.

The current NTFS engine is the first source implementation and remains valuable regardless of later sources. The next major product step remains a usable file-first GUI. Stable file identity/history is a strong candidate for the first deeper object-model capability.

After the file-first experience is usable, a second genuinely different source should be added to validate the shared model. Browser history/bookmarks are a plausible low-friction candidate because they test heterogeneous unified search without requiring a cloud account. A later cloud source such as Google Drive or Gmail can then validate incremental remote synchronization and OAuth/account handling.

Only after multiple real source implementations exist should Quail extract a more general provider/source contract from demonstrated common requirements.

## Relationship to launcher features

Application launching, calculator/conversion helpers, web actions, and similar launcher capabilities may still be added when they are cheap and useful. They are supporting conveniences, not the reason Quail exists.

The product should not measure success by matching Flow Launcher, Raycast, Command Palette, or another launcher feature-for-feature. The differentiating direction is one fast, local-first index of the user's own searchable digital world.

## Conceptual relationship to WinFS

The object-centric direction resembles a narrow part of the old WinFS vision: identity and searchable objects matter more than one current filesystem path.

Quail is not intended to replace NTFS, become an operating-system storage platform, or implement a universal relational object store. Existing systems remain authoritative sources. Quail observes or synchronizes them and builds a local search-oriented model above them.

## Current decision boundary

This document records the strategic north star, not a committed implementation sequence for all future versions.

The existing 0.1 file-engine work remains the foundation. The file-first GUI remains the next product milestone. The roadmap after that should be revised incrementally as identity/history and additional-source experiments provide evidence.
