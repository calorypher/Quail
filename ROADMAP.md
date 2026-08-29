# Quail Roadmap

This roadmap defines the current product direction. Only the active release should be treated as a committed implementation plan; later releases are directional and should be revised from evidence produced by earlier milestones.

The long-term product direction is described in `docs/product-vision.md`.

## 0.1 — File Engine / CLI — COMPLETE

Quail 0.1 established the Windows 11 x64 local file-search foundation.

Released as:

- version `0.1.0`;
- tag `v0.1.0`;
- GitHub Release `Quail 0.1.0`;
- installer `Quail-0.1.0-Setup.exe`.

The release provides a persistent per-volume SQLite index built from official NTFS/MFT/USN mechanisms, restart-safe incremental synchronization, filename search with SQLite FTS5 trigram plus a persistent short-query fallback, metadata filters, multi-index aggregation, status diagnostics, shell open, a usable CLI, and Inno Setup packaging.

Milestones M01 through M07 are complete. Detailed implementation history and verification evidence remain in the milestone documents under `docs/milestones/`.

Important 0.1 limitations intentionally carried forward include:

- CLI/core only; no GUI;
- no Windows Service or continuous background index maintenance;
- no no-admin filesystem backend;
- no implicit index discovery/catalog;
- full-volume build/sync may require elevation;
- Windows 11 x64 only;
- no additional search sources/providers;
- no code signing or public-release hardening.

Do not refactor the completed 0.1 core merely to resemble a speculative future provider architecture. Reuse it unless a later concrete vertical slice demonstrates a need to change it.

## 0.2 — Launcher Shell — ACTIVE PLAN

### Goal

Turn the verified 0.1 File Engine into a lightweight, keyboard-first Windows desktop application that can be installed and used for normal file search without requiring a terminal.

0.2 remains deliberately file-first. The broader universal-search vision influences UI and architectural boundaries, but does not expand 0.2 into mail, browser, cloud, content, history, or other future sources.

The primary interaction surface is **Quick Search**: a transient global-hotkey overlay with live results from the real local file index. The approved visual direction is the current Power User 3.1 / universal-search UI direction stored with the project design materials.

A future **Full Search / Object Explorer** is part of the product direction, but its advanced browse/query-builder/history/details capabilities are not required for 0.2. Do not add empty or misleading controls for functionality that does not yet exist.

### M08 — UI framework spike: WinUI 3 vs Avalonia — COMPLETE

Implement the same minimal launcher shell in both frameworks and choose the production UI stack from measured evidence rather than preference alone.

Both candidates should implement an intentionally small equivalent slice:

- frameless Quick Search window;
- search textbox;
- approximately 20 controlled dummy results;
- keyboard navigation;
- global hotkey;
- repeated show/hide lifecycle;
- tray integration;
- light/dark behavior;
- representative Windows 11 styling consistent with the approved Quail direction.

Measure and document at minimum:

- cold startup;
- hotkey-to-visible latency;
- hidden-idle working set/private memory;
- idle CPU;
- focus and activation reliability;
- repeated summon/dismiss behavior;
- DPI and multi-monitor behavior;
- frameless/window lifecycle behavior;
- tray lifecycle;
- implementation complexity and maintainability;
- packaging/deployment constraints;
- ability to reproduce the approved Power User 3.1 visual direction without disproportionate custom work;
- use of Windows Shell icons for files/folders and system/Fluent-style icons for normal UI actions;
- practical cost/benefit of preserving a possible later Linux UI path.

WinUI 3 remains the preferred Windows-native candidate, but the spike must give Avalonia a fair test. Do not select Avalonia solely for hypothetical Linux portability, and do not select WinUI 3 merely by inertia.

If one framework encounters a fundamental blocker for the required transient launcher lifecycle, document the blocker and stop that branch instead of completing artificial benchmark parity.

The milestone ends with a durable report and explicit framework recommendation.

### M09 — Deployment spike — COMPLETE

After M08 selects the production UI framework, compare deployment models for the actual chosen stack:

1. self-contained .NET deployment;
2. framework-dependent deployment with reliable runtime detection and a sensible prerequisite-install path.

Compare at minimum:

- installed/application footprint;
- installer footprint;
- cold startup impact;
- first-install behavior on a machine without the required runtime;
- reinstall and upgrade behavior;
- servicing/security/update implications;
- support and maintenance burden;
- any framework-specific deployment constraints introduced by the GUI stack.

The self-contained choice used by 0.1 is not automatically permanent.

Keep this spike separate from M08 so framework choice and deployment choice are not evaluated as two changing variables at once.

M09 originally selected the fully self-contained variant because its clean-baseline first install transferred fewer total bytes and had no prerequisite-bootstrap network failure mode. That historical result remains documented in `docs/milestones/M09-results.md`, but the 0.2 production deployment direction was later updated after the production WinUI payload exposed a stronger product priority: minimize Quail's own installer and installed application footprint and prefer shared, centrally serviced Microsoft runtimes where supported. M13 must validate the framework-dependent production path against the real application before the release candidate is frozen.

### M10 — Desktop shell / Quick Search lifecycle — COMPLETE

Build the first production Quail GUI shell using the framework selected by M08 and deployment direction selected by M09.

Scope:

- production `Quail.exe`;
- single-instance application lifecycle;
- global hotkey;
- reliable Quick Search summon/dismiss behavior;
- correct keyboard focus and activation;
- frameless transient overlay matching the approved UI direction;
- keyboard navigation over controlled results;
- tray integration and Exit;
- minimal settings needed for the shell;
- configurable hotkey;
- system/light/dark theme behavior as supported by the chosen design;
- optional run-at-login setting if it can be implemented cleanly in this slice;
- persistent per-user GUI configuration;
- basic diagnostics/logging appropriate for QA.

M10 may use controlled dummy results. It should validate shell/window behavior before real search complexity is introduced.

Use as little custom graphics as practical:

- Quail branding/feather remains a Quail-owned asset;
- file/folder imagery should use Windows Shell-provided icons where practical;
- ordinary UI actions should prefer system or suitably licensed Fluent-style icons;
- third-party static assets are allowed only when their source and redistribution license are clear and documented.

### M11 — Real File Search integration — COMPLETE

Connect the production Quick Search shell to the verified 0.1 file-search core without unnecessary refactoring.

Scope:

- live queries against real persistent SQLite indexes;
- multi-index search;
- responsive UI while typing;
- cancellation/debounce only where measurements show they are needed;
- keyboard selection and Enter-to-open;
- zero-result, unavailable-index, and error states;
- deterministic ordering consistent with core semantics;
- real Release-build latency/resource measurements.

Introduce a small UI-facing presentation model rather than binding Views directly to SQLite/NTFS structures. It should be capable of representing a generic search result such as:

`icon/type + primary identity + contextual secondary line + optional type-specific metadata + exceptional state/history`

In 0.2 the actual results remain files/directories. Do not build a speculative complete `QuailObject` or plugin/provider framework solely for future source types.

File/folder icons should normally come from the Windows Shell. Icon retrieval must not block the live-search path; cache and/or asynchronous loading should be used where needed based on measurement.

### M12 — Index configuration and management — COMPLETE

Remove the ordinary GUI user's dependency on manual `--index` arguments and terminal-driven routine index management.

Provide the smallest durable configuration needed for a usable desktop application:

- persistent list/catalog of configured local indexes or indexed volumes;
- enable/add and remove/disable from search as appropriate;
- visible index status;
- initial build/rebuild;
- explicit sync/refresh;
- clear communication when a privileged filesystem operation requires elevation;
- progress/error states appropriate for long-running index operations.

Do **not** implement the production Windows Service in 0.2 merely to hide elevation. The normal GUI must remain unelevated.

Where existing full-volume NTFS operations require elevation, use a narrow explicit elevated operation or another simple boundary that reuses existing core/CLI behavior rather than duplicating privileged indexing logic inside the GUI.

A key accepted limitation of 0.2 is that continuous background USN maintenance is not yet required. Users should be able to build and refresh indexes from the GUI, while always-on maintenance remains a later service-backed capability.

M12 completed on `main` through PR #14. Its final implementation includes persistent per-user index configuration, protected ProgramData index storage, a narrow same-executable elevated Build/Rebuild/Refresh worker, freshness reporting, operation coordination, fail-closed protected-storage checks, and the verified protected SQLite quiescent read lifecycle. Durable verification remains under `docs/milestones/M12-results.md` and `docs/milestones/evidence/M12/`.

### Pre-M13 QA automation foundation — DEFERRED

The feasibility spike is complete and deferred as an implementation dependency. WinApp CLI/UIA is workable in an interactive Quail-Lab VMConnect session, but unattended host/SSH -> interactive-desktop execution was not made reliable; the transient `InteractiveToken`/`Limited` Task Scheduler proof did not satisfy its fail-closed contract. Further inter-session bridge investment is deferred and is not required for 0.2.

For 0.2, use normal unit/integration tests plus manual GUI smoke on VMConnect as needed. A user-triggered WinApp smoke may be used selectively later when it provides real QA savings. Do not assume a reusable GUI-regression automation layer exists.

Deferred direction:

- retain WinApp/UIA as an option for same-session, user-triggered checks;
- retain manual gates for secure-desktop UAC approval, visual/UX judgment, physical multi-monitor/mixed-DPI behavior, and other cases that automation cannot represent faithfully;
- do not turn a future check into a general-purpose Windows automation framework.

Existing Quail-Lab safety rules and checkpoint gates remain in force.

### Pre-M13 File Search relevance/ranking — COMPLETE

Current literal filename matching is technically correct but alphabetic ordering is not useful enough for the 0.2 launcher experience. Add the smallest deterministic ranking model that makes normal user files and folders surface ahead of deep hidden/system matches while preserving fast indexed search.

Initial ranking direction:

- location/visibility is a first-class signal: visible objects in the current user's profile should normally outrank hidden/internal profile data and system areas;
- other visible user-space objects should normally remain above hidden/internal user data;
- hidden/internal user-space should normally outrank unrelated hidden/system infrastructure only when text relevance and other bounded signals justify it;
- known system-heavy areas such as `Windows`, `Program Files`, `ProgramData`, `$Recycle.Bin`, and `System Volume Information` should be strongly de-prioritized rather than excluded;
- within the location/visibility model, text relevance should distinguish at least exact name, prefix, token/word prefix, and substring matches;
- shallower/shorter paths may be deterministic tie-breakers, followed by stable name/path/file-identity ordering;
- avoid hard-coded magic boosts for individual user folders such as Downloads or Documents when a general current-user/visibility/depth rule explains the preference;
- do not add fuzzy matching, interaction-history learning, content relevance, recency scoring, provider-level ranking, or a speculative general ranking framework in this slice.

The ranking logic belongs outside WinUI and must be directly testable. Real workflow regressions should include at least these classes of examples: a current-user `Downloads` result above deep AppData `download*` matches; a real Desktop file above a corresponding hidden/internal Recent-link match; and useful user-space `desktop` results above `C:\Windows\WinSxS\FileMaps` infrastructure. Measure search responsiveness before accepting materially more expensive ranking work.

Completed for 0.2 with a bounded Core candidate window, location-first deterministic ranking, explicit exact/prefix/token-prefix/substring tiers, visible non-profile/current-user/internal/system-heavy path classification, and stable path/source tie-breaks. Full path-aware candidate recall across an over-limit same-text-tier set is deferred and does not block 0.2. See [Search-Ranking results](docs/milestones/Search-Ranking-results.md).

### Pre-M13 Development naming cleanup — COMPLETE

This technical cleanup removed milestone-specific names from the active application runtime and verification tooling without changing product behavior, the protected-index boundary, or historical evidence.

### Pre-M13 UI polish — COMPLETE

Completed the bounded production UI polish pass. The final implementation provides:

- compact Quick Search at 700 × 56 as a single surface;
- expanded Quick Search at 700 × 370 with a subtle 140 ms content transition;
- refined result hierarchy;
- Settings at 700 × 500 as a local themed `SettingsSurface`;
- coherent Light, Dark, and System presentation;
- Index Manager presentation and theme cleanup;
- fixed local hotkey capture/restore regression;
- automated, manual, and independent QA PASS;
- search performance finding explicitly deferred to M13.

See [Pre-M13 UI Polish results](docs/milestones/Pre-M13-UI-Polish-results.md).

### M13 — Packaging, upgrade, performance and technical release candidate

Integrate the completed 0.2 product slices into one installable, verified technical release candidate. M13 proves that the application itself is release-quality; public-repository and publication-specific readiness belongs to M14.

Scope:

- validate the updated deployment direction against the actual production application rather than blindly carrying forward the original M09 self-contained recommendation;
- prefer a framework-dependent unpackaged WinUI 3 deployment if the production validation remains clean;
- keep supported Microsoft runtime components shared where practical, including the required .NET Desktop Runtime, Windows App Runtime, and VC++ x64 runtime, rather than copying complete private runtime sets into the Quail application directory solely to avoid first-install network transfer;
- retain mature Inno Setup packaging unless evidence requires a change;
- detect required shared prerequisites precisely and install only missing/insufficient prerequisites from pinned official Microsoft sources with integrity verification before Quail files are installed;
- accept that a genuinely missing prerequisite may increase one-time first-install network transfer; prioritize Quail's own installer size and installed application footprint over minimizing total transfer on an unusually clean machine;
- keep `C:\Program Files\Quail` limited to Quail binaries/resources and dependencies that genuinely must remain app-local under the supported deployment model; do not manually delete arbitrary publish files to manufacture a smaller footprint;
- measure and record production publish size/file count, installer size, installed-directory size/file count, prerequisite downloads on representative states, reinstall behavior, and upgrade behavior;
- do not introduce trimming, Native AOT, unsupported single-file tricks, or comparable deployment complexity unless the normal supported framework-dependent path leaves a demonstrated problem that justifies a separate decision;
- upgrade correctly from an installed 0.1 build;
- make `Quail.exe` the normal user-facing application;
- keep `Quail.Cli.exe` available for diagnostics/administrative workflows where still useful;
- preserve user configuration and index data across reinstall/upgrade/uninstall unless an explicit user choice says otherwise;
- verify autostart/tray/lifecycle behavior if those features are included;
- run core regression tests;
- run a documented GUI smoke-test matrix appropriate to the release, using manual VMConnect smoke by default and optional user-triggered WinApp/UIA checks where they provide real value; retain manual gates where required;
- verify the installed build on Quail-Lab and a physical Windows host;
- measure the complete real application in Release configuration, including startup, hidden-idle memory, idle CPU, hotkey-to-visible latency, search responsiveness, focus behavior, and repeated lifecycle stability;
- for Quail 0.2, assess physical hidden-idle working set against an approximately 200 MiB release criterion together with practically idle CPU and stable resources; retain approximately 100 MiB as the long-term lightweight aspiration rather than treating a stable framework-level excess as an automatic 0.2 blocker;
- document known limitations and technical release evidence in the repository.

Successful compilation or installer generation is not sufficient completion evidence.

M13 ends when an exact 0.2 release candidate is installable, regression-verified, physically smoke-tested, performance-measured, footprint-audited, and documented. It does not change repository visibility, perform the final public dependency/history/trademark audit, create release tags, publish a GitHub Release, or replace published assets.

### M14 — Public-release readiness and first public release

Take the exact M13 technical release candidate through the dedicated audit required before Quail becomes a public FOSS project. The intended outcome, if the audit passes and the required approvals are given, is for `v0.2.0` to become Quail's first public release.

Scope:

- final dependency/license audit against the actual `main` and exact release payload;
- final publish/installer payload audit, including both genuinely app-local components and the shared Microsoft prerequisites referenced by the installer;
- final third-party asset/icon/font/license and attribution review;
- repository-history audit for secrets, private data, credentials, machine-specific material, or accidentally committed artifacts;
- add the standard MIT `LICENSE` for the public code release;
- add `THIRD-PARTY-NOTICES` or an equivalent attribution mechanism where required by the actual dependency/asset set;
- add a minimal `CONTRIBUTING.md` only if contributor guidance is useful for the first public state;
- review README, CHANGELOG, release notes, About text, topics, description, and other public-facing GitHub metadata for accuracy without advertising unimplemented future capabilities as current features;
- document the code-signing/package-authenticity status and decide explicitly whether an unsigned first public installer is acceptable;
- perform an independent final security review focused especially on the privileged indexing/storage boundary and installer/update surface actually present in 0.2;
- perform the final manual `QUAIL` / `Q.QUAIL` / materially similar software-mark check in the relevant EU/Poland sources, including TMview/EUIPO and UPRP;
- verify the exact final source commit and release artifact hashes after all audit fixes.

M14 is a readiness/publication milestone, not a feature milestone. Fix defects required to make the audited 0.2 release safe and publishable, but do not add post-0.2 product capabilities merely because the repository is becoming public.

The milestone acceptance boundary before publication is: the repository, exact source commit, installer/payload, licensing/notices, public metadata, security findings, and trademark check are all in a documented state ready for public release.

Publication-sensitive actions remain separate approval gates. Do not change repository visibility from private to public, create/move `v0.2.0`, publish the GitHub Release, replace release assets, or perform equivalent publication actions without the user's explicit approval for the specific action.

### Explicitly outside 0.2

Unless later evidence forces a narrowly scoped prerequisite, 0.2 does not implement:

- Windows Service;
- continuous background index maintenance;
- no-admin/user-profile crawler backend;
- application search/provider;
- browser history/bookmarks;
- Gmail, Drive, OneDrive, mail, calendar, contacts, or other cloud sources;
- file-content indexing;
- file identity/history/lineage UI beyond what the existing 0.1 data already exposes;
- Full Search/Object Explorer advanced browse mode;
- visual query builder;
- details/history/relationship panes for future object types;
- calculator/conversions/web utility providers;
- dynamic/public plugin framework or extension SDK;
- automatic update installation;
- Linux implementation;
- AI features.

An update-available indicator is not a release requirement for 0.2 unless it becomes a trivial consequence of another approved slice.

## Post-0.2 roadmap decision gate

Do not automatically continue the old sequence of application search, utility providers, and browser integration simply because those items appeared in earlier roadmap drafts.

After 0.2 is released and its real UI/search behavior has been measured, return to roadmap planning and choose the next vertical slice from evidence and the universal-search product vision.

Strong candidates include:

- stable local-file identity, rename/move lineage, temporal filesystem history, and related ranking/search behavior;
- the first genuinely heterogeneous non-filesystem source, with browser history/bookmarks being a plausible low-friction candidate;
- a later cloud source such as Google Drive or Gmail to validate OAuth/account handling and remote incremental synchronization;
- application search as a supporting convenience when it provides enough user value to justify prioritization.

Only after multiple real source implementations exist should Quail extract a general source/provider contract from demonstrated common requirements.

## Directional deployment and indexing modes — later

Quail should eventually remain usable without administrator rights while retaining a service-backed full-volume NTFS index as the recommended capability where elevation is available.

Preferred future product model:

- **Full mode / per-machine** — administrator-approved installation, privileged Quail Windows Service, full local NTFS MFT/USN indexing, normal interactive application unelevated.
- **User mode / per-user** — no service and no admin requirement; indexing limited to data available under the user's normal permissions, such as profile/user-selected locations and potentially network shares.

These are capability levels of one product, not separate applications. User mode may have weaker performance, change tracking, and catch-up guarantees; those differences must be explicit.

Implement the production service/application boundary and the user-mode filesystem backend as later explicit milestones. Do not hide them inside unrelated GUI or installer work.

Switching deployment/indexing mode should eventually be installer-driven and preserve configuration/user data where practical. Rebuilding indexes during a mode switch is acceptable when simpler and safer than migration.

## Future / Wishlist

### Network File Search

- selected SMB/network shares or mapped drives as searchable sources;
- persistent local metadata/name cache;
- controlled refresh rather than aggressive rescanning;
- explicit unavailable/stale state;
- measure and limit SMB/server load;
- treat network indexing as a distinct backend because NTFS/USN guarantees do not automatically apply.

Network content indexing is a separate later decision because it may impose substantial network and server I/O.

### Content Search

- optional text-content indexing for selected formats and scopes;
- separate extraction/full-text pipeline from the filesystem namespace index;
- controlled background resource use and exclusions;
- re-extract only changed content where practical;
- start with predictable formats and evaluate mature parsers or system `IFilter` support for formats such as PDF, DOCX, XLSX and PPTX;
- benchmark the storage/search engine before freezing it; SQLite FTS5 is a candidate, not a commitment.

Searchable content fragments should point back to their owning object rather than automatically becoming durable first-class objects.

### Update automation

Preferred baseline later:

- check an authoritative release source;
- show a non-blocking update indication;
- download the normal installer;
- verify authenticity/integrity;
- invoke the normal installer for in-place upgrade;
- accept normal UAC for per-machine updates unless a separately designed and security-reviewed privileged mechanism later justifies removing it.

Do not turn the NTFS service into a generic privileged updater merely for convenience.

### Extensibility decision

Quail should remain internally modular, but a public/dynamically loaded plugin system is not committed.

If real third-party-extension use cases appear, evaluate extension loading/isolation, permissions, API versioning, compatibility, installation/update behavior, and security as a separate product decision.

## Public-release readiness

The first public Quail release is now planned as `v0.2.0`, subject to successful completion of the M13 technical release candidate and the dedicated M14 public-release readiness audit.

The approved project name for public release remains **Quail**. The approved public code license is **MIT**. The code license does not define rights to the Quail name, logo, or other branding assets; branding is handled separately.

M14 owns the final dependency/payload/asset/history/security/trademark audit and the addition of publication-specific repository material such as the MIT `LICENSE`, required notices, and final public GitHub metadata. Do not add that infrastructure earlier merely because the decisions are already known.

If M14 finds a material blocker, do not publish merely to preserve the planned version number or schedule. Fix the blocker within the bounded public-release scope or explicitly defer publication.

## Product constraints across releases

- Keep architecture simple and dependencies limited.
- Build small validated vertical slices.
- Keep the GUI independent from the file-index schema and NTFS implementation details.
- Prefer clean internal boundaries when a concrete slice needs them; avoid speculative frameworks.
- Avoid regular full rescans and aggressive polling.
- Do not elevate the launcher UI merely to access NTFS internals.
- Keep the Windows Service optional for running Quail at all, while requiring it for the eventual full local NTFS MFT/USN mode unless later evidence provides an equally safe alternative.
- Prefer mature packaging/update tooling over custom infrastructure when sufficient.
- Do not bypass or weaken Windows UAC for convenience.
- Windows remains the first platform. Preserve natural portable boundaries, but do not add speculative cross-platform abstractions solely for a hypothetical Linux port.
- Use as little custom UI artwork as practical; prefer Windows Shell/system icons or clearly licensed reusable icon sets outside Quail-specific branding.
- If a feature is not required for the current vertical slice, materially increases complexity, has many edge cases, and can safely wait, defer it.
