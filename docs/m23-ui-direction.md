# Quail — M23 UI implementation spec

## Status

Approved UI/UX direction for M23 — Quick Search & UI Polish. This is an implementation-oriented design reference, not a pixel-perfect contract. Functional scope and architecture remain defined by the repository, roadmap, and active M23 milestone.

## References

Primary visual reference: `quail-m23-ui-direction-current.png`

Earlier product/UI direction: `Quail — UI visual direction`

Branding reference: `Quail — branding working spec`

## Implementation principle

Preserve the existing M22 functionality. M23 should polish and restructure presentation without introducing new providers, new search semantics, or architectural scope. Prefer the running application and real data over matching generated mockup pixels exactly.

## Shared visual language

Quick Search and Full Search are two modes of the same product. They should share typography, Quail blue selection/accent language, search-field styling, result-row hierarchy, icon family, spacing rhythm, and light/dark behavior.

Use Segoe UI Variable / Segoe UI as the Windows-native reference. Keep surfaces restrained and Fluent-adjacent rather than decorative. Avoid oversized cards, excessive borders, strong shadows, or dense form-like layouts.

## Quick Search

Quick Search remains a transient, keyboard-first overlay with no native title bar. The primary flow is invoke → type → select → Enter.

Header row: Quail feather at left, dominant search field, clear-query affordance when relevant, Expand to Full Search icon, and Settings icon. Expand should use an outward-arrows icon. Settings remains secondary.

Results: compact object rows with icon, primary name, contextual second line (for M23 primarily path), and optional right-side type/size metadata. Selection uses a clear Quail-blue state. Hover must be quieter than selection.

Default density should show roughly 5–6 results without feeling cramped.

Footer hints should be compact and optional: navigation, Enter/Open, Alt+Enter/Open in Full Search, Esc/Close. Avoid covering the surface with shortcut badges.

## Quick Search context menu

Right-click or the equivalent context action on a result opens a compact menu anchored to the row. Current M23 actions should be represented directly and only if supported by the existing implementation.

Preferred order:

1. Open
2. Open file location
3. Copy path
4. Open in Full Search

Keep the menu visually lightweight, use familiar Windows/Fluent iconography where available, and show keyboard shortcuts only where they materially help.

## Full Search — window chrome and header

Full Search is a normal persistent, resizable Windows window and should use native minimize, maximize/restore, and close controls.

Inside the app header, place the Quail feather + Quail identity at left. At the right side of the application-owned header area, keep Settings and a Collapse to Quick control represented by inward-facing arrows. Do not label the mode switch with a large text button.

The search field sits immediately below the app header and remains the dominant control.

## Full Search — filters

Avoid the current M22 wall of text boxes and checkboxes. The default view should expose only a small set of common filter chips/compact controls in one row, with the rest discoverable through + Add filter.

Recommended initial visible filters for the file-first M23 UI:

- Type
- Extension
- Modified
- Location, if already supported or feasible within existing scope; otherwise omit rather than invent behavior
- + Add filter

Clear all appears as a quiet action at the far right when filters are active.

Advanced/existing filters such as minimum/maximum size and file attributes should live behind Add filter or an expandable filter surface rather than permanently occupying the main layout.

Filter controls should read as friendly search refinement, not as an enterprise ticket-query form. Prefer compact chips/dropdowns and context-specific editors over a grid of labels and input boxes.

## Full Search — results and sorting

Use the available width primarily for results.

The result table/list should retain the existing useful columns: Name, Path, Kind, Size, Modified, subject to what the repository currently supports.

Remove the separate Sort control. Column headers are the sorting controls. Clicking a sortable header changes sort; the active column shows a subtle direction indicator. The active sort should be obvious without adding a separate form control.

Selection language should match Quick Search as closely as the denser Full Search table permits.

The result count belongs in a low-emphasis status/footer area.

## Settings access

Settings must remain directly accessible from Full Search, not only from Quick Search or tray. Use a restrained gear action consistent with the Quick Search settings icon.

Do not redesign the Settings information architecture in this spec; M23 Settings visual polish can be handled as a follow-on pass using the same shared visual language.

## States and feedback

Quick Search — empty: show the search field and a restrained prompt such as “Start typing to search”. Do not use a large decorative empty card.

Quick Search — no results: clear “No results found” message with one short helpful line.

Full Search — empty query: preserve the normal window and filter structure; show a calm prompt in the results area rather than a blank table that looks broken.

Full Search — no results: retain query and filters and show a clear no-results state in the results area.

Index unavailable / error: state what is unavailable and provide one useful recovery action when the current functionality supports it, such as opening Indexing settings. Avoid alarming styling except where action is required.

Loading or refresh transitions should not cause large layout shifts.

## Expand / Collapse behavior

Quick → Full: outward-facing arrows.

Full → Quick: visually matching inward-facing arrows.

The icons should be from one coherent icon family and have equivalent size, stroke weight, hit area, and placement.

Switching modes should preserve the current query and selection/context where the existing implementation supports it. The visual transition may be subtle, but correctness and responsiveness take precedence over animation.

## Transitions

Use motion only where it improves continuity: opening/closing Quick Search, opening a context menu, filter popover expansion, or mode transition. Prefer short Windows-like fades/scale/position transitions. Respect reduced-motion preferences. No ornamental animation is required for M23.

## Light and dark

Both themes must remain first-class. Preserve the same hierarchy, spacing, density, and selection semantics. Dark mode should use soft dark surfaces rather than pure black; light mode should rely on spacing/surfaces more than heavy borders.

Exact color tokens, corner radii, and spacing values may be tuned against the running application instead of copied from the generated mockup.

## Icons

The mockup iconography is directional only. M23 should use a coherent Fluent/Windows-compatible icon family where practical. The approved Quail feather assets remain canonical for product identity. Exact utility icons, including Expand/Collapse and context-menu glyphs, should be validated in the running UI rather than copied from the AI-generated image.

## Out of scope for this design pass

- No new search providers.
- No new indexing architecture.
- No new file-history semantics.
- No content-search expansion unless already part of the active milestone.
- No redesign of product branding.
- No pixel-perfect recreation of generated screenshots.
- No speculative provider-specific UI.

## M23 visual acceptance checklist

- Quick Search reads as a fast overlay, not a mini desktop application.
- Full Search reads as the expanded version of the same product.
- Full Search header uses native Windows window controls plus Quail-owned Settings and Collapse-to-Quick affordances.
- Default Full Search filter area is compact and friendly; advanced filters are discoverable without dominating the screen.
- Sorting is performed through clickable column headers.
- Quick Search context menu is clear, compact, and consistent with supported actions.
- Empty, no-results, and unavailable/error states are intentional and understandable.
- Light and dark themes preserve equivalent hierarchy.
- The UI remains usable with real long file names and paths, common Windows scaling, and keyboard navigation.
- No functional or architecture scope is added solely to match the mockup.

## Implementation handoff

Before implementation, Codex should read AGENTS.md, README, ROADMAP.md, the active M23 milestone, and any current architecture/verification documents in the repository. Repository rules and active milestone scope take precedence over this document.

For implementation, copy or adapt this spec into the repository (for example docs/m23-ui-direction.md) together with the approved mockup if repository rules allow. Once copied, the repository version becomes the canonical implementation reference for M23.
