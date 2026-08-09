# Soltex design system

The visual language of the Soltex workspace, defined once and enforced. Every
colour, radius, spacing, type, and motion value lives in
[`src/Soltex.App/Themes/Tokens.xaml`](../src/Soltex.App/Themes/Tokens.xaml).
No other XAML file may contain a raw colour literal —
[`eng/verify-design-tokens.ps1`](../eng/verify-design-tokens.ps1) fails the
Windows gate if one appears. New features tie to these tokens so the product
stays consistent as it grows.

## Principles

- **A quiet technical workspace.** Warm near-black ground, parchment text, one
  coral interaction accent. Colour carries meaning, not decoration.
- **Green means confirmed-good, never brand.** Green is reserved for healthy or
  supported state; amber for attention; red for danger. A neutral state stays
  neutral rather than borrowing a status colour.
- **Tokens first.** Add a primitive to `Tokens.xaml`, consume it by name. A value
  that is worth using twice is worth naming once.
- **Lightweight.** No control library, no theming framework, no runtime colour
  computation. Flat `ResourceDictionary` merges only.

## Layers

`Tokens.xaml` is merged before `SoltexTheme.xaml` in
[`App.xaml`](../src/Soltex.App/App.xaml):

1. **Colour primitives** — the only raw hex in the app.
2. **Semantic brushes** — roles (`SurfaceBrush`, `AccentBrush`, …) that reference
   the primitives. UI references brushes, never primitives directly.
3. **Scales** — radius, spacing, type, motion.

`SoltexTheme.xaml` holds component styles only; it references tokens and contains
no literals.

## Colour

Warm graphite ground, darkest to lightest. All contrast ratios are against
`CanvasColor` (`#1F1F1F`) unless noted.

### Surfaces

| Token | Hex | Role |
|---|---|---|
| `SidebarColor` | `#181816` | Navigation rail |
| `FieldColor` | `#1B1B19` | Text input wells |
| `InsetColor` | `#1D1D1A` | Inset pills and code chips |
| `CanvasColor` | `#1F1F1F` | Window ground |
| `QuietColor` | `#21211E` | Quiet cards and inset rows |
| `SurfaceColor` | `#252522` | Default card |
| `RaisedColor` | `#2B2B27` | Raised controls, table headers |
| `ElevatedColor` | `#302B27` | Keyboard-focus surface |
| `HeroBaseColor` | `#292622` | Hero panels |
| `TrackColor` | `#34332E` | Progress / slider track |

Elevation is expressed by stepping up this ramp and adding a border — not by
drop shadows, which are avoided for performance.

### Text (parchment)

| Token | Hex | Contrast | Use |
|---|---|---:|---|
| `TextBrightColor` | `#E7E3D6` | ~12:1 | Emphasised values, input text |
| `TextColor` | `#D8D5C8` | ~11:1 | Primary body and headings |
| `TextSecondaryColor` | `#BDBAAF` | ~8:1 | Secondary detail, table headers |
| `MutedColor` | `#96938A` | ~5.3:1 | Labels, descriptions (AA) |
| `QuietTextColor` | `#716F68` | ~3.2:1 | Non-essential mono metadata only |

`QuietTextColor` is below the 4.5:1 threshold for body text and is reserved for
non-essential metadata (provenance strings, timestamps, sample counts) rendered
in the mono face. Do not use it for information a user must read to act.

### Accent (coral)

| Token | Hex | Use |
|---|---|---|
| `AccentColor` | `#F76F53` | Primary action, eyebrows, active nav (~5.9:1) |
| `AccentFocusColor` | `#F9A08D` | Keyboard-focus ring on accent controls |
| `AccentDimColor` | `#8E5A4E` | Active nav index numeral |
| `AccentQuietColor` | `#3A2925` | Accent-tinted quiet fill |
| `OnAccentColor` | `#211714` | Text on an accent fill (~6.5:1 on coral) |

### Status

| Token | Hex | Meaning |
|---|---|---|
| `SignalColor` | `#7ED0A7` | Confirmed-good / supported (green) |
| `SignalTextColor` | `#B8C4B9` | Body text inside a green surface |
| `SignalSurfaceColor` | `#20251F` | Green-tinted card ground |
| `SignalQuietColor` | `#202B25` | Green pill fill |
| `SignalBorderColor` | `#3D4C42` | Green surface border |
| `WarningColor` | `#E6B866` | Attention / waiting (amber) |
| `DangerColor` | `#F18279` | Danger (red) |
| `DangerTextColor` | `#FF9BA2` | Destructive control label |

Status is never encoded by colour alone. Every status pairs a colour with a text
label or mono tag (`LIVE`, `PARTIAL`, `NOT ENROLLED`, `UNAVAILABLE`) so it reads
without colour perception.

### Edges, navigation, and translucent fills

| Token | Hex | Role |
|---|---|---|
| `BorderColor` | `#3B3A34` | Default hairline border |
| `BorderStrongColor` | `#4A4840` | Emphasised / secondary-button border |
| `HeroBorderColor` | `#514239` | Hero panel border |
| `NavSelectedColor` | `#372925` | Selected navigation entry |
| `ScrollTrackColor` | `#242421` | Scrollbar rail |
| `ScrollThumbColor` | `#5B5850` | Scrollbar thumb |
| `AccentAreaColor` | `#183F302A` | Coral chart area fill (≈9% alpha) |
| `SignalAreaColor` | `#1422381F` | Green chart area fill (≈8% alpha) |
| `SelectionColor` | `#8058453F` | Text selection highlight |

Each colour primitive has a matching `…Brush` (for example `SurfaceColor` →
`SurfaceBrush`). UI binds to the brush.

## Radius

| Token | Value | Use |
|---|---:|---|
| `RadiusXs` | 4 | Scrollbar, track caps |
| `RadiusSm` | 8 | Buttons, nav entries |
| `RadiusMd` | 10 | Inset chips |
| `RadiusLg` | 12 | Pills, small cards |
| `RadiusCard` | 14 | Default card |
| `RadiusHero` | 18 | Hero card |
| `RadiusHeroXl` | 24 | Large hero panel |

## Spacing

An 8-based scale with a 4/6 fine step for dense controls. Use these for new gaps
and padding rather than fresh numbers.

| Token | Value | | Padding token | Value |
|---|---:|---|---|---|
| `Space2xs` | 4 | | `PadCard` | 22 |
| `SpaceXs` | 6 | | `PadHero` | 26 |
| `SpaceSm` | 8 | | `PadCompact` | 15 |
| `SpaceMd` | 12 | | `PadPill` | 10,6 |
| `SpaceLg` | 16 | | `PadField` | 14,11 |
| `SpaceXl` | 20 | | `PadButton` | 16,10 |
| `Space2xl` | 24 | | | |
| `Space3xl` | 28 | | | |

## Type

Three roles: **Georgia** for display moments, **Segoe UI** for controls and body,
**Cascadia Mono** for measurements and identifiers.

| Token | Size | Role |
|---|---:|---|
| `FontMicro` | 9 | Mono tags |
| `FontMono` | 10 | Eyebrows, mono captions |
| `FontCaption` | 11 | Fine print |
| `FontSmall` | 12 | Muted descriptions |
| `FontLabel` | 13 | Navigation labels |
| `FontBody` | 14 | Body |
| `FontSection` | 17 | Section titles |
| `FontTitle` | 22 | Card titles (display) |
| `FontDisplaySm` | 24 | Hero subtitles (display) |
| `FontMetric` | 36 | Metric values (display) |
| `FontDisplay` | 38 | Page titles (display) |

Named text styles in `SoltexTheme.xaml` — `PageEyebrowStyle`, `PageTitleStyle`,
`SectionTitleStyle`, `MutedTextStyle`, `MetricValueStyle` — apply these roles.
Prefer a named style over an inline `FontSize`.

## Motion

Swiss-quiet transitions, 140–220 ms. Respect the system reduced-motion setting
before animating.

| Token | Duration |
|---|---:|
| `MotionFast` | 140 ms |
| `MotionBase` | 180 ms |
| `MotionSlow` | 220 ms |

## Components

Styles in `SoltexTheme.xaml`, all token-driven:

| Style | Applies to |
|---|---|
| `CardStyle`, `QuietCardStyle`, `HeroCardStyle` | Surface containers |
| `StatusPillStyle` | Status pills |
| `ActionButton`, `SecondaryButton`, `NavButton` | Buttons and nav entries |
| `FieldStyle` | Text inputs |
| `MetricProgressStyle` | Metric bars |
| `SoltexSliderStyle` | Sliders |
| implicit `ScrollBar`, `DataGrid`, `DataGridColumnHeader`, `DataGridCell` | Lists and tables |

### Interaction states

Every interactive control defines rest, hover, pressed, keyboard-focus, and
disabled. Focus is always visible: accent controls thicken to a 2px
`AccentFocusBrush` ring; nav entries raise to `ElevatedBrush`. Disabled controls
drop to 0.42 opacity rather than changing hue.

## Accessibility status

Implemented: visible focus on every control, non-colour status labels, AA body
contrast, `AutomationProperties.Name` on charts and key controls.

Not yet verified (tracked in [`VALIDATION.md`](VALIDATION.md) and `HANDOFF.md`):
high-contrast theme behaviour, reduced-motion wiring, a full keyboard/UI
Automation traversal, the 100/125/150/200% scaling matrix, and owner visual
acceptance. No accessibility-conformance claim is made.

## Extending the system

1. **New colour** → add a `Color` primitive and its `…Brush` to `Tokens.xaml`,
   then reference the brush. The guard rejects a raw hex used anywhere else.
2. **New component** → add a keyed `Style` to `SoltexTheme.xaml` that references
   tokens; do not inline values.
3. **New size or gap** → reuse a scale token; add a new scale step only when a
   genuinely new rhythm is needed, and document it here.
4. Run `pwsh eng/verify-design-tokens.ps1` (also runs in CI) before committing.
