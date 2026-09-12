# Quail — M23 Settings UI implementation spec

Status: working implementation spec for Codex
Scope: UI/UX direction only for the existing Settings window in M23
Out of scope: new product scope, new backend capabilities, settings architecture redesign, pixel-perfect fidelity

## 1. Goal

Modernize the Settings window so it visually matches the approved M23 UI direction for Quick Search and Full Search.

Target feeling:

- Windows-first, modern, calm, polished
- Closer to Fluent / Windows 11 than to early Windows 10 settings
- Clear information hierarchy
- Sparse but not empty
- Consistent with Quail dark theme and brand accent blue

This is a UI polish pass for existing functionality, not a feature expansion.

## 2. Canonical visual references

Primary reference image:

- `quail-m23-settings-ui-direction-current.png`

Use the approved M23 Quick Search / Full Search direction as the shared visual language.

Important rule:

- The mockup is a direction, not a pixel-perfect contract.
- Real running UI, control constraints, DPI scaling, and WinUI behavior take precedence over exact reproduction.

## 3. Window structure

Settings is a normal resizable app window.

Use:

- Standard app title bar
- Quail app icon in title bar
- Native Windows caption controls (minimize, maximize/restore, close)

Recommended high-level layout:

- Left navigation rail / pane
- Main content area on the right

No decorative slogan or footer branding inside the actual app window.

Specifically do NOT include:

- `Quail M23` label in the lower-left corner
- any slogan text under it

## 4. Navigation

Left pane should contain only the current sections:

- General
- Indexing
- About

Visual behavior:

- Compact vertical navigation
- Current item highlighted with a filled or softly elevated blue-accent selection state
- Inactive items are subtle and calm
- Icons should be simple, outline-style, Windows-like
- Navigation should feel modern, not like legacy side menu chrome

Recommended navigation characteristics:

- Width roughly in the "compact but comfortable" range
- Enough padding to avoid cramped appearance
- Section icon + label on one line
- No unnecessary nesting or accordion behavior for M23

## 5. Shared visual language

Settings should visually align with Quick Search and Full Search.

Use the same language for:

- dark surfaces
- blue accent color
- rounded corners
- soft borders / separators
- restrained glow or depth
- typography hierarchy

Desired theme characteristics:

- dark, deep-blue-tinted surfaces
- subtle contrast between window background, navigation pane, content cards, and inputs
- avoid harsh black/white contrast
- avoid flat "enterprise form" look

## 6. Typography and spacing

Typography should feel closer to the approved M23 mockups than to stock legacy settings.

Guidance:

- Clear page title at top of content area
- Short descriptive subtitle under the page title
- Card titles medium weight
- Explanatory text smaller and lower emphasis
- Labels readable without looking heavy

Spacing:

- Prefer fewer, larger cards rather than many tiny separated widgets
- Use comfortable vertical spacing between sections
- Use consistent padding inside cards
- Avoid the current crowded / mechanically stacked feeling

## 7. Main page: General

General should be the "hero" settings page.

Recommended content grouping:

### 7.1 Launch at startup

Display as a feature card with:

- leading icon
- title: `Launch at startup`
- short explanation
- toggle on the right side
- explicit text state allowed (`On` / `Off`) if useful

### 7.2 Quick Search hotkey

Display as a separate card with:

- leading icon
- title: `Quick Search hotkey`
- short explanation
- current shortcut shown in an input-like field or pill field
- secondary action button, e.g. `Change...`

This should feel like a deliberate setting control, not a raw text box.

### 7.3 Theme

Display as its own card with:

- leading icon
- title: `Theme`
- short explanation
- compact dropdown / combo box aligned to the right or lower row
- values like `System`, `Light`, `Dark`

### 7.4 Tray behavior

Display as a feature card with:

- leading icon
- title: `Tray behavior`
- short explanation
- at least one checkbox/toggle such as `Show Quail in the system tray`
- supporting explanatory text under the checkbox if helpful

### 7.5 Save action

Use a clear primary `Save` button.

Placement:

- bottom-right of the main content area, or bottom-right of the relevant content/card area

The button should stand out as the primary action, but without oversized emphasis.

## 8. Indexing page

Indexing should feel clearer and more structured than the current implementation.

High-level structure:

- page title: `Indexing`
- short explanatory subtitle
- list of indexed locations rendered as separate cards

Each indexed volume card should include:

- leading drive/storage icon
- drive label, e.g. `C:\`, `D:\`
- maintenance/status badge, e.g. `Maintenance unavailable`
- summary line with record count / last maintained timestamp
- indication whether enabled for Quick Search
- action row with existing actions such as `Build`, `Disable`, `Remove`

Visual guidance:

- Treat each volume as a card, not as a plain text block
- Status badge may use a subtle warning tint when needed
- Buttons should be grouped cleanly and remain easy to scan
- Do not make this page feel like an admin console

`Add local volume`:

- place as a clearly visible but not oversized action near the bottom of the page
- can be full-width or prominent inline action depending on layout constraints
- should visually read as an additive action

## 9. About page

About should be visually cleaner and more polished than the current plain-text version.

Recommended structure:

- page title: `About Quail`
- short single-line description
- brand block with Quail feather icon and product name
- version information
- links/actions for:
  - GitHub repository
  - MIT License

Guidance:

- Keep it concise
- Make it feel intentional, not empty
- External links can use subtle link styling or icon affordances
- Avoid over-decorating this page

## 10. Control styling

The main controls should visually match the rest of Quail.

### Inputs / fields

- rounded corners
- soft border
- subtle filled dark surface
- focus state with restrained blue accent

### Buttons

- primary button: blue accent fill
- secondary buttons: dark filled or outlined subtle style
- hover and pressed states should be visible but understated

### Toggles / checkboxes / dropdowns

- modern Windows-like appearance
- avoid looking like raw default controls unless styling limitations force it
- consistency matters more than heavy customization

## 11. States

At minimum, account for:

- normal state
- hover/focus states
- disabled controls
- empty / unavailable informational state when relevant
- indexing warning/unavailable state

Do not invent elaborate loading skeletons unless the current UI architecture already supports them.

Subtle state feedback is enough.

## 12. Motion / transitions

If practical and low-risk:

- subtle page transition or content fade when switching sections
- subtle hover transitions on cards and buttons
- no flashy animations

Motion should be optional and restrained.

It must not create implementation drag or visual noise.

## 13. Implementation constraints for Codex

- Do not expand the M23 feature scope.
- Do not redesign architecture to chase visuals.
- Prefer pragmatic improvements using existing WinUI capabilities.
- Reuse shared tokens/styles/components where practical.
- If a mockup detail is expensive or fragile, preserve the hierarchy and tone rather than forcing a brittle replica.
- Favor maintainable XAML over bespoke hacks.

## 14. Acceptance criteria for the UI pass

The pass is successful when:

- Settings clearly looks like part of the same product as M23 Quick Search and Full Search
- The window no longer feels like early Windows 10 settings UI
- General page has a modern card-based layout
- Indexing page presents drives in clearer, friendlier cards
- About page is concise and polished
- Navigation, spacing, typography, and controls feel more modern and cohesive
- No extra branding footer appears in the lower-left of the window
- Existing settings functionality still works

## 15. QA checklist

Check visually in:

- dark theme first
- light theme if applicable / supported
- normal DPI and at least one higher scaling setting if available

Verify:

- alignment of the left nav and content area
- consistency of card spacing
- title/subtitle hierarchy
- button prominence
- keyboard focus visibility
- long text wrapping on Indexing cards
- About links readability
- caption controls coexist cleanly with custom content

## 16. Non-goals

This spec does NOT require:

- new settings categories
- plugin/provider settings redesign
- onboarding flows
- advanced settings IA redesign
- fully bespoke design system work
- pixel-perfect parity with the mockup image
