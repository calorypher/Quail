# Post-0.3 Frontend and Interface Direction

## Status

**Approved cross-version architectural direction.**

This document records the approved post-Quail-0.3 architectural invariant. Quail 0.3 is complete; the current roadmap schedules the smallest needed extraction in Quail 0.4 without reopening historical 0.3 scope.

## Frontend interchangeability

The long-term architecture should keep `Quail.App` replaceable as a frontend rather than allowing it to become the permanent owner of source-neutral application/search behavior.

`Quail.App` should eventually contain primarily frontend-specific responsibilities such as:

- WinUI rendering and presentation;
- input handling and focus/window behavior;
- UI-specific timing or debounce policy that exists because of UX requirements;
- desktop integration such as hotkeys, tray/lifecycle, shell-facing behavior, and other Windows frontend concerns;
- composition-root responsibilities needed to assemble the application.

Source-neutral application/search orchestration that would need to be shared by another frontend or search surface should not remain permanently in `Quail.App`.

In particular, responsibilities currently represented by the `LatestSearchCoordinator` class family should move after 0.3 into `Quail.Core` or an equally narrow source-neutral application layer when that work is scheduled. This includes behavior such as:

- latest-request / supersession coordination;
- stale-result protection;
- duplicate-query coalescing;
- source-neutral scheduling and execution coordination;
- other search-session behavior that should remain identical regardless of which frontend invokes the search.

UI-specific policy remains frontend-specific. For example, `QuickSearchInputPolicy` and similar behavior that exists specifically because of the Quick Search interaction model may remain in `Quail.App`.

The architectural test is:

> Replacing WinUI with another frontend should not require reimplementing Quail's source-neutral search-session semantics.

The post-M15 separation was accepted through Quail 0.3. The approved 0.4 plan performs the smallest behavior-preserving separation needed before history-aware behavior would otherwise diverge across interfaces; broader extraction should still wait for demonstrated reuse.

## CLI as a first-class interface

`Quail.Cli` should be treated as a first-class interface to the same Core/application behavior rather than as a diagnostic sidecar with a separate or reduced search engine.

Long-term interface parity means that non-visual Quail capabilities should be available through the CLI wherever they are meaningful and safe to script. This includes, as those capabilities exist in the product:

- the same source-neutral search semantics and ranking behavior;
- the same supported query/filter capabilities;
- the same source selection and source-neutral result/action semantics where a CLI representation is meaningful;
- status and diagnostics needed for automation;
- administrative/index/source operations that are intentionally exposed by the product and can be represented safely in a command-line workflow.

GUI-only presentation behavior does not need a CLI analogue. Window layout, animations, focus handling, visual result presentation, and similar frontend concerns remain frontend-specific.

The CLI must not grow a second independent search implementation or separate product semantics. Both GUI and CLI should invoke the same shared application/search layer so that fixes to correctness, ranking, source aggregation, cancellation/supersession, and future heterogeneous-source behavior do not need to be implemented twice.

For scripting, CLI behavior should remain automation-friendly: deterministic exit semantics, machine-readable output where useful, and no requirement to automate the GUI for a capability that is fundamentally non-visual.

## Intended dependency shape

The long-term conceptual dependency shape is:

```text
Quail.App -----------+
                     |
Quail.Cli -----------+----> Quail.Core / source-neutral application layer
                     |               ^
future frontend -----+               |
                                     |
Quail.FileSystem --------------------+
future sources ----------------------+
```

This diagram is conceptual rather than a requirement to introduce another project immediately. If `Quail.Core` remains the appropriate home for shared application orchestration, prefer that over creating a new layer solely for architectural symmetry. Introduce a distinct application-layer project only when concrete dependencies or reuse make it simpler than keeping the responsibility in Core.

## Relationship to source modularity

Frontend interchangeability and source modularity are separate but complementary boundaries.

- Frontends depend on source-neutral application/search behavior.
- Concrete sources implement Core-owned source contracts and remain independent of a particular frontend.
- A frontend replacement must not require moving filesystem, browser, mail, cloud, or other source logic.
- Omitting or replacing one source must not require replacing the frontend.

Do not use this direction to create a public plugin SDK, generalized frontend framework, speculative cross-platform UI abstraction, or duplicate command/query framework before real interfaces demonstrate the need.

## Timing

- Quail 0.3 is complete; no historical 0.3 scope is reopened for this refactor.
- The approved Quail 0.4 planning baseline schedules the smallest behavior-preserving source-neutral orchestration extraction as M28, before history-aware search expands GUI/CLI behavior.
- Quail 0.4 execution is currently paused pending the explicit workflow migration to Consensus, so M28 is planned but not active.
- Preserve CLI/Core parity incrementally as new non-visual product capabilities are added, rather than allowing GUI-only implementations to accumulate and planning a large parity rewrite later.
