# Quail Roadmap

This roadmap records the current product direction. Only an approved active milestone is a committed implementation plan. Later releases are directional and should be revised from evidence produced by earlier work.

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

## 0.3 — Daily-usable Filesystem Search / Everything replacement — DIRECTION APPROVED

### Product goal

Quail 0.3 should be able to replace Everything for ordinary daily local-file search on the developer's Windows PC.

This does **not** mean feature parity with Everything. The target user workflow is:

```text
Windows starts
  -> Quail starts automatically
  -> the filesystem index stays current without manual refresh
  -> the global hotkey opens Quick Search
  -> search feels immediate
  -> useful results rank near the top
  -> Enter opens the intended item
```

For workflows that need more than a transient launcher:

```text
Quick Search
  -> Full Search
  -> inspect, sort and filter a larger result set
```

The detailed directional requirements for 0.3 are recorded in `docs/0.3-direction.md`. Concrete milestone numbers and specifications are intentionally not frozen until the first 0.3 milestone is designed and approved.

### Directional scope

The expected 0.3 work includes the following product slices.

#### Core / filesystem boundary

Separate the current filesystem-heavy `Quail.Core` into a real application/search core and a filesystem-specific internal implementation.

The direction is:

```text
Quail.App
    -> Quail.Core
        -> internal source/provider implementations
            -> FileSystem
            -> later Browser / Drive / Mail / Calendar / ...
```

The extraction must be behavior-preserving first. It should remove direct filesystem implementation details from the GUI/Core boundary without creating a public provider SDK, dynamic plugin framework, speculative capability matrix, universal object database, or generalized provider lifecycle.

A second real heterogeneous source should later validate which abstractions are genuinely common.

#### Search performance

Search performance is a release-level requirement for 0.3.

Before changing algorithms, measure Quail and Everything against the same representative corpus and query set. Include cold/warm behavior, short queries, longer queries, broad-result queries, and normal interactive typing. Profile the measured bottlenecks and optimize those bottlenecks rather than targeting an arbitrary synthetic number.

Normal successful searches should feel effectively immediate. Approximately 0.5 seconds is an upper-bound degraded experience, not the desired steady-state latency.

#### Ranking / relevance v2

Improve practical result quality beyond the bounded 0.2 ranking model.

The work should address at least:

- text relevance;
- candidate recall, including the known over-limit same-tier/path-aware recall limitation;
- useful user-space results versus hidden/internal/system-heavy results;
- deterministic stable ordering;
- representative real file-search workflows.

Do not add ML ranking, interaction-history learning, fuzzy matching, or large heuristic frameworks without evidence that the simpler ranking model is insufficient.

#### Automatic filesystem maintenance

Normal use should no longer require explicit Refresh/Rebuild merely to keep search current.

The filesystem source should support continuous or effectively continuous maintenance using the existing NTFS/USN foundation, including:

- incremental change consumption;
- restart catch-up;
- sleep/resume behavior as required by real testing;
- detection of journal/checkpoint continuity loss;
- automatic safe recovery where possible;
- explicit `Rebuild required` or equivalent when correctness can no longer be guaranteed;
- simple index-health/status states.

A Windows Service is a plausible privileged/background boundary, but its exact responsibility must remain the smallest design justified by the milestone. Security and integrity of that boundary require strong review; unrelated service responsibilities do not.

#### Launch on startup

Provide a normal user-facing option to launch Quail with Windows so daily use does not require manual startup.

#### Quick Search UI polish

Polish the existing transient Quick Search without sacrificing perceived latency.

Expected areas include:

- smoother result-list expand/collapse behavior;
- refined transition/lifecycle behavior;
- a delayed searching/busy indicator only for queries that are actually taking long enough to be noticeable.

Do not flash a spinner for normal fast queries. The UI should assume search is normally immediate and surface progress only when latency becomes perceptible.

#### Unified Settings window

Replace the separate ordinary Settings and Index Manager experience with one coherent settings window.

A minimal useful structure is expected to resemble:

```text
Settings
  -> General
      -> Launch at startup
      -> Global hotkey
      -> Theme
  -> Indexing
      -> indexed volumes
      -> status / health
      -> rebuild / recovery actions
      -> useful diagnostics
  -> About
```

Do not create empty categories for future providers merely to reserve navigation space.

#### Full Search v1

Introduce the first functional version of the mockup's Full Search / Object Explorer surface. In 0.3 it is a filesystem-only **Full Search v1**, not yet the full multi-source Object Explorer.

Expected capabilities:

- normal resizable persistent window;
- same Core search semantics and ranking as Quick Search;
- larger scrollable result set;
- name and path/context;
- file/folder type;
- size;
- modified date/time;
- created date/time if the indexed data can provide it correctly;
- keyboard navigation;
- open;
- open containing folder;
- copy path;
- sorting where useful;
- basic filtering.

Initial filtering should cover practical filesystem properties such as:

- file versus folder;
- extension/file type;
- size range;
- modified-before/after;
- created-before/after if supported correctly;
- hidden/system/read-only as advanced options where useful.

Prefer compact filter controls/chips or equivalent focused controls over a large visual query-builder. A full boolean query-builder, saved searches, tabs, relationship panes, or file-manager behavior are not 0.3 requirements.

Quick Search and Full Search should use one internal GUI-to-Core search-request model rather than two unrelated search implementations. That model may contain text and the filters needed by actual 0.3 surfaces, but it is not a public provider API and should not be generalized for hypothetical future sources beyond demonstrated need.

### 0.3 quality boundary

The release should be judged against the real daily-use result, not feature count.

A successful 0.3 should make it reasonable to leave Quail running from Windows startup, trust that the local NTFS index is current, invoke search without administrative maintenance, receive fast and useful results, and use Full Search when a transient Quick Search list is insufficient.

Security, index correctness, change-tracking continuity, and measured search performance warrant strong verification. Routine UI/settings/install scenarios require representative supported-path testing rather than combinatorial hardening. See `AGENTS.md` for the risk-based engineering proportionality rules.

### Explicitly outside 0.3

Unless an approved milestone finds a narrow prerequisite, 0.3 does not implement:

- browser history/bookmarks provider;
- Google Drive, Gmail, OneDrive, calendar, contacts, or other heterogeneous sources;
- content indexing;
- full NTFS file identity/history/lineage product features;
- deleted-item retention/history UX;
- a public/dynamic plugin framework;
- a generalized provider SDK;
- a large visual query builder;
- a multi-pane file manager;
- automatic update installation;
- installer replacement solely for technology churn;
- Linux implementation;
- AI features;
- enterprise deployment machinery without a concrete requirement;
- exhaustive compatibility machinery for arbitrary historical development-release installation variants.

## 0.4 — File Identity & History — DIRECTIONAL

After 0.3 establishes a clean filesystem source boundary and reliable continuous change tracking, stable file identity/history is the preferred next deeper filesystem capability.

Likely areas include:

- stable volume/file identity as the basis of object lineage;
- previous names and paths;
- rename/move history;
- deletion state where explicitly retained;
- retention semantics and privacy controls;
- temporal filesystem queries;
- richer Full Search/Object Explorer details and history presentation;
- offline/removable-volume identity and catalog behavior if the concrete design supports it naturally.

0.3 should preserve the technical information needed for this direction, but should not silently begin retaining deleted/history data merely because it may be useful later.

## First heterogeneous source — later, likely after 0.4

Browser history/bookmarks remain the preferred low-friction candidate for the first genuinely different source because they can validate heterogeneous unified search without immediately requiring OAuth/cloud account infrastructure.

That source should be used to test the actual Core/source boundary. Only after multiple real implementations exist should Quail extract broader shared provider contracts from demonstrated common requirements.

A later cloud source such as Google Drive or Gmail can then validate account handling, remote incremental synchronization, credential storage, retention semantics, and offline behavior.

## Other future directions

### Content search

Optional content indexing remains a later vertical slice. Searchable content fragments should point back to their owning object rather than automatically becoming durable first-class objects.

### Network file search

Selected SMB/network shares may later become a distinct filesystem-like source with explicit stale/unavailable behavior and controlled refresh. NTFS/USN guarantees must not be assumed for remote shares.

### Update automation

Quail 0.2 has no automatic updater. A later update milestone may compare the current Inno-based deployment with other mature mechanisms against an explicit requirement for a low-friction or background update experience.

Do not turn the filesystem service into a generic privileged updater for convenience. A no-prompt privileged update mechanism requires its own narrowly scoped security design and review.

### Linux

Linux remains a plausible later platform. Preserve natural portable boundaries in Core/source logic, but do not compromise the Windows implementation or introduce speculative cross-platform abstractions merely to reserve a future port.

### Extensibility

Quail should remain internally modular. A public/dynamically loaded plugin system is not committed. Evaluate extension loading, isolation, permissions, API versioning, compatibility, and update behavior only if real third-party-extension use cases appear.

## Product constraints across releases

- Keep architecture simple and dependencies limited.
- Build small validated vertical slices.
- Keep the GUI independent from source-specific storage and platform internals.
- Extract internal boundaries when current code demonstrates the need; avoid speculative frameworks.
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
