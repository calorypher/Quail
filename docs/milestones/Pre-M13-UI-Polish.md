# Pre-M13 UI Polish

## Goal

Polish the existing Quail 0.2 production UI before M13 without redesigning the product or adding user-facing capabilities.

## Visual direction

Keep the approved Power User 3.1 direction as a directional reference: a minimalist, frameless, transient, keyboard-first Quick Search with calm, moderately dense presentation. Results retain Windows Shell icons and communicate primary identity, contextual path, and optional metadata. The existing Quail feather remains subtle branding. Light and dark are variants of the same product rather than separate designs.

## Scope

- Refine the existing Quick Search compact and expanded surfaces, result-row hierarchy, selected and pointer-over presentation, and spacing.
- Add a short, non-blocking expanded-content transition after the existing immediate compact/expanded resize.
- Consolidate a small set of semantic visual resources in `App.xaml`.
- Refine the existing Settings presentation/surface without changing its behavior.
- Host the existing Settings presentation as a local surface in Quick Search when needed to preserve theme coherence across Light, Dark, and System.
- Refine the existing native Index Manager presentation and apply the current Quail theme when it is opened or reopened.
- Preserve the established 700 px width, 56 px compact height, 370 px expanded height, 500 px Settings host height, DPI scaling, cursor-monitor centering, focus, and lifecycle contracts.

## Out of scope

- Any redesign, new search provider, ranking change, query behavior change, index/storage/privilege change, M13 work, package/publish work, or new dependency.
- New Quick Search actions, filters, previews, recents, pins, history, source tabs, context menus, or administrative actions.
- New graphics, fonts, assets, rendering frameworks, Acrylic/Mica/backdrops, or custom animation frameworks.
- Broad changes to hotkey, tray, single-instance, keyboard, pointer, test-pipe, or deployment semantics. A local capture/restore regression fix discovered during manual QA is an allowed corrective fix.

## Acceptance

- Quick Search remains frameless, keyboard-first, and visually lighter in compact mode.
- Expanded result rows retain Shell icon, Name, Path, and Metadata while making selection, hover, hierarchy, and contrast clear but restrained.
- Entering and leaving expanded mode remains correct during rapid type/clear/type interaction. The existing immediate resize remains authoritative; the expanded content uses a subtle 100–160 ms non-blocking opacity/vertical-reveal transition and never delays searching.
- Settings and Index Manager retain their existing behavior while presenting a coherent hierarchy and theme.
- The Index Manager keeps its native Windows title bar and applies the current configured Quail theme when opened.
- Settings, Quick Search, and Index Manager client surfaces remain coherent across explicit Light, Dark, and System themes.
- Automated tests and builds pass with no warnings or errors. A manual visual approval remains required before the milestone can be considered complete.

## Automated verification

- Run the full Release test suite.
- Build `Quail.App` for `win-x64` in Release.
- Build the existing M10 harness because Quick Search layout/event code is changed.
- Review the final diff and audit that no dependencies or assets were added.

## Manual visual gate

The user must inspect a Release `Quail.exe` before the milestone is accepted. The gate covers compact alignment and chrome, the compact-to-expanded and return behavior, result readability and Shell icons, Settings presentation and behavior, and Index Manager presentation/theme coherence in Light, Dark, and System modes.

## Stop conditions

Stop if canonical preparation fails, unrelated changes exist, the fixed layout dimensions or lifecycle/focus/centering contract cannot be preserved, a behavior/storage/privilege change is needed, a new dependency or rendering framework is required, replacing the Settings surface would require a broader window-architecture refactor, or verification exposes an unrelated defect.
