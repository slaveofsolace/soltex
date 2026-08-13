# Soltex design system

Version: 2.0 — **Quiet Instrument Deck**

Soltex is a native Windows control surface. It should feel closer to a well-made
piece of system instrumentation than a marketing dashboard: calm at rest,
precise under load, and immediately operable.

The implementation lives in
[`Tokens.xaml`](../src/Soltex.App/Themes/Tokens.xaml) and
[`SoltexTheme.xaml`](../src/Soltex.App/Themes/SoltexTheme.xaml). The Windows
design gate rejects raw colour literals outside `Tokens.xaml`.

## Product principles

1. **Controls before commentary.** The action, value, or table a person came to
   use occupies the strongest position. Explanations move to an information
   flyout, contextual detail pane, or terse footer.
2. **One workspace, one job.** A default view has one dominant work area, one
   secondary rail or command band, and one accent. It does not stack a parade of
   equally weighted cards.
3. **Position carries state.** Selection rails, slider position, table rows,
   tabs, and master-detail relationships communicate structure before colour.
4. **Progressive depth.** Default views expose the common path. Advanced detail
   opens in a local pane or tab without turning the entire page into a document.
5. **Honest capability.** Working, preview, unavailable, and degraded states are
   explicit. No visual treatment implies protection or control the runtime has
   not demonstrated.
6. **Quiet by default.** Status colour is reserved for confirmed state; it is
   never decorative. Motion is short, functional, and reduced-motion aware.

## Signature language

The signature element is a thin **signal rail**: a 2–3 px line on the leading
edge of the selected navigation item, active tab, or current detail row. It gives
Soltex an identity without gradients, glowing borders, glass, or oversized
branding.

- Graphite surfaces form a shallow three-level stack: canvas, work surface,
  focused/inset surface.
- Glacier blue is the only interaction accent.
- Green, amber, and red are semantic state colours only.
- Borders are hairlines. Corners are small. Shadows are not used for hierarchy.
- Segoe UI Variable is deliberate Windows-native typography. Cascadia Mono is
  reserved for measurements, IDs, paths, ports, and timestamps.

## Page anatomy

Every primary workspace follows the same vertical contract:

1. **Command header (52 px target):** title, at most one short supporting
   sentence, then the primary command(s) aligned right.
2. **Local switcher (optional):** tabs or a compact segmented row for sibling
   modes. Do not add another global navigation tier.
3. **Work area:** table, master-detail grid, channel lanes, chart, or focused
   control surface. This area owns any necessary scrolling.
4. **Context footer (optional):** one short provenance/state line and an info
   control. It is not a second explanation panel.

The application shell owns the window. Pages do not use whole-page scrolling at
the 1280 × 820 reference viewport. A data list may scroll internally; a detail
pane may scroll independently.

## Information budget

- Default workspace explanatory copy: **75 words maximum**, excluding live data,
  labels, table cells, error text, and accessible names.
- Page heading: title plus zero or one sentence. Eyebrows are exceptional, not a
  template requirement.
- Visible cards/panels: no more than three peer regions before a local tab or
  master-detail split is required.
- A warning is shown only when it changes the next decision. Persistent legal,
  provenance, privacy, and nonclaim detail belongs behind an info disclosure.
- Empty states state what is empty and provide one next action. They do not teach
  the whole subsystem.

## Control selection

Use the control that matches the state, not the control that makes the screen
look interactive.

| State | Control |
|---|---|
| Continuous or relative value with immediate feedback | Slider plus numeric value |
| Boolean state | Toggle/switch or checkbox |
| One choice from a short peer set | Segmented tabs or radio group |
| One choice from a long set | Combo box |
| Dense comparable records | Sortable table with row actions |
| Record plus advanced properties | Master-detail split |
| Destructive action | Explicit button with confirmation |
| Read-only percentage/health | Meter or progress bar, never a disabled slider |

Sliders are appropriate for audio volume and user-controlled refresh cadence.
They are not used for binary settings, arbitrary security strength, or
read-only telemetry.

## Palette

### Surfaces

| Token | Value | Role |
|---|---|---|
| `CanvasColor` | `#0B0E12` | Window ground |
| `SidebarColor` | `#0D1116` | Global rail |
| `FieldColor` | `#0F141A` | Inputs |
| `InsetColor` | `#11171E` | Recessed data regions |
| `QuietColor` | `#131A21` | Secondary bands |
| `SurfaceColor` | `#171E26` | Main work surface |
| `RaisedColor` | `#1D2630` | Hover/table header |
| `ElevatedColor` | `#24303C` | Focused detail |
| `TrackColor` | `#2B3743` | Progress and slider track |

### Text

| Token | Value | Use |
|---|---|---|
| `TextBrightColor` | `#FFFFFF` | Emphasized values |
| `TextColor` | `#EEF3F7` | Primary copy |
| `TextSecondaryColor` | `#C5CFD8` | Secondary detail |
| `MutedColor` | `#92A0AD` | Labels and descriptions |
| `QuietTextColor` | `#687684` | Optional metadata only |

`QuietTextColor` never carries information needed to decide or act.

### Interaction and status

| Token | Value | Meaning |
|---|---|---|
| `AccentColor` | `#67B7FF` | Action, focus-adjacent selection, signal rail |
| `AccentFocusColor` | `#A8D6FF` | Keyboard focus |
| `AccentQuietColor` | `#10263A` | Selected quiet surface |
| `OnAccentColor` | `#06131C` | Text/icons on accent |
| `SignalColor` | `#5DD39E` | Confirmed healthy/current |
| `WarningColor` | `#F4BE63` | Attention, partial, or preview |
| `DangerColor` | `#FF6B7A` | Failure or destructive action |

Colour never carries status without a text label. Contrast must be measured after
token changes; this document is not a WCAG conformance claim.

## Geometry and spacing

| Radius token | Value |
|---|---:|
| `RadiusXs` | 2 |
| `RadiusSm` | 4 |
| `RadiusMd` | 6 |
| `RadiusLg` | 8 |
| `RadiusCard` | 6 |
| `RadiusHero` | 8 |

| Padding token | Value |
|---|---:|
| `PadCard` | 14 |
| `PadHero` | 16 |
| `PadCompact` | 10 |
| `PadPill` | 8,4 |
| `PadField` | 10,8 |
| `PadButton` | 12,8 |

Use the 4/6/8/12/16/20/24 spacing scale. Repeated sections use a 12 px gap;
unrelated regions use 16–20 px. Do not add one-off padding to repair a hierarchy
problem.

## Type

- `UiFont`: Segoe UI Variable Text with Segoe UI fallback.
- `DisplayFont`: Segoe UI Variable Display with Segoe UI fallback.
- `IconFont`: Segoe Fluent Icons with Segoe MDL2 Assets fallback.
- `MonoFont`: Cascadia Mono with Consolas fallback.

| Token | Size | Role |
|---|---:|---|
| `FontMicro` | 10 | Rare compact tag |
| `FontMono` | 11 | Measurements and IDs |
| `FontCaption` | 12 | Supporting copy |
| `FontLabel` | 13 | Navigation and control labels |
| `FontBody` | 14 | Body/data |
| `FontSection` | 16 | Local section heading |
| `FontTitle` | 18 | Work-area title |
| `FontDisplaySm` | 21 | Compact hero value |
| `FontMetric` | 32 | Primary metric |
| `FontDisplay` | 26 | Page title |

All-caps mono eyebrows are not a default page pattern. Sentence case is the
default for navigation, tabs, headings, and buttons.

## Navigation and commands

- Global navigation remains shallow and grouped by user intent.
- The selected item uses a signal rail plus quiet fill, never a purple pill.
- `Ctrl+K` opens one transient workspace command surface; `Ctrl+1` through
  `Ctrl+9` use the same route table, and Escape restores focus.
- Command results remain bounded to the nine working workspaces and use the
  shared theme for hover, selection, focus, and modal scrim treatment.
- Primary commands remain visible and consistently aligned in the command header.
- Secondary/rare commands move into overflow or a local detail surface.
- Working and preview modules remain visibly distinct and are exposed through UI
  Automation.

## Workspace migration contract

| Workspace | Default structure |
|---|---|
| Home | Brief system posture + three direct next actions + recent signal row |
| Performance | Live telemetry grid; optional internal details pane |
| Applications | Dense process/app table + row detail/action pane |
| Audio | Repeated channel lanes with volume sliders; Devices/More as local tabs |
| Security | Posture/status band + scan command deck + Activity/Quarantine local tabs |
| Remote Assist | Device/session master-detail deck + compact permission disclosure |
| Activity | Filter command band + bounded event table |
| Updates | Prepared update detail + History/Recovery local tabs |
| Settings | Category rail/tabs + one bounded settings panel at a time |

## Motion

| Token | Duration |
|---|---:|
| `MotionFast` | 120 ms |
| `MotionBase` | 160 ms |
| `MotionSlow` | 200 ms |

Motion confirms hover, selection, expansion, and completion. It does not loop or
animate decorative surfaces. Reduced-motion mode removes nonessential transitions.

## Reference translation and clean-room boundary

Reference systems are studied for general interaction principles only:

- SteelSeries Sonar: repeated channel grammar, direct controls first, local tabs.
- Zen Browser: low-chrome focus, shallow workspaces, transient detail layers.
- NZXT CAM: telemetry grouped around a small number of legible live instruments.
- AppControl: process/history clarity and focused utilities.
- Microsoft Windows guidance: shallow NavigationView hierarchy, stable command
  placement, appropriate slider use, and immediate settings changes.

No proprietary code, assets, copy, presets, measurements, brand treatment, or
distinctive layout is copied. See the dated provenance record under
`docs/evidence/2026-08-12-design-system-rework/`.

## Verification and human gate

Before merge:

1. Run the design-token guard and all affected application tests.
2. Build Release and capture every primary workspace at 1280 × 820.
3. Verify 100%, 125%, 150%, and 200% scaling, keyboard traversal, focus visibility,
   high contrast, reduced motion, empty/error/busy states, and small-window behavior.
4. Confirm default primary workspaces do not require whole-page scrolling.
5. Record performance and lifecycle non-regression evidence.
6. Reserve subjective visual acceptance for the owner.

Green builds and screenshots are implementation evidence; they are not owner
visual approval or a WCAG conformance claim.

## Extension rules

1. Add raw colours only to `Tokens.xaml`.
2. Add reusable components only to `SoltexTheme.xaml`.
3. Reuse the spacing, type, and radius scales before adding values.
4. Put working controls before explanatory copy.
5. Keep preview capability visibly labeled.
6. Re-run the native evidence matrix after any layout or theme change.
