# Soltex design system

Soltex uses one lightweight WPF design system defined by
[`Tokens.xaml`](../src/Soltex.App/Themes/Tokens.xaml) and
[`SoltexTheme.xaml`](../src/Soltex.App/Themes/SoltexTheme.xaml).
The Windows gate rejects raw colour literals outside `Tokens.xaml`.

## Product intent

Soltex is a native system workspace, not a marketing dashboard or terminal skin.

- information density should feel useful, not crowded;
- direct controls appear before implementation commentary;
- preview modules are visibly different from working modules;
- typography is sans-serif throughout the product surface;
- one electric-iris accent identifies interaction;
- green, amber, and red are reserved for confirmed state;
- colour never carries status without a text label;
- borders and stepped surfaces express hierarchy; decorative shadows are avoided;
- motion is brief and disabled when Windows requests reduced animation.

## Palette

### Surfaces

| Token | Value | Role |
|---|---|---|
| `CanvasColor` | `#0B0D12` | Window ground |
| `SidebarColor` | `#0F1117` | Navigation rail |
| `FieldColor` | `#101218` | Inputs |
| `InsetColor` | `#111319` | Recessed regions |
| `QuietColor` | `#13161D` | Low-emphasis containers |
| `SurfaceColor` | `#171A22` | Default card |
| `RaisedColor` | `#1D212A` | Hover and table headers |
| `ElevatedColor` | `#242936` | Focused surface |
| `TrackColor` | `#2B303B` | Progress and slider tracks |

### Text

| Token | Value | Use |
|---|---|---|
| `TextBrightColor` | `#FFFFFF` | Emphasized values |
| `TextColor` | `#F1F3F7` | Primary copy |
| `TextSecondaryColor` | `#C7CBD4` | Secondary detail |
| `MutedColor` | `#9197A4` | Labels and descriptions |
| `QuietTextColor` | `#646B78` | Nonessential metadata only |

`QuietTextColor` must not carry information required to make a decision.

### Interaction and status

| Token | Value | Meaning |
|---|---|---|
| `AccentColor` | `#8B7CFF` | Primary action and selected workspace |
| `AccentFocusColor` | `#B2A8FF` | Keyboard focus |
| `AccentQuietColor` | `#211E3D` | Selected or branded quiet surface |
| `SignalColor` | `#5DD39E` | Confirmed healthy or current |
| `WarningColor` | `#F4BE63` | Attention, partial, or preview |
| `DangerColor` | `#FF6B7A` | Failure or destructive action |

Contrast must be remeasured whenever a palette token changes. The current
palette is not a formal WCAG conformance claim.

## Geometry and spacing

The surface language is deliberately compact.

| Radius token | Value |
|---|---:|
| `RadiusXs` | 4 |
| `RadiusSm` | 6 |
| `RadiusMd` | 8 |
| `RadiusLg` | 10 |
| `RadiusCard` | 10 |
| `RadiusHero` | 12 |
| `RadiusHeroXl` | 14 |

| Padding token | Value |
|---|---:|
| `PadCard` | 18 |
| `PadHero` | 20 |
| `PadCompact` | 14 |
| `PadPill` | 9,5 |
| `PadField` | 12,10 |
| `PadButton` | 14,9 |

Avoid adding new gaps or radii until the existing scale is proven insufficient.

## Type

- `UiFont`: Segoe UI Variable Text with Segoe UI fallback.
- `DisplayFont`: Segoe UI Variable Display with Segoe UI fallback.
- `IconFont`: Segoe Fluent Icons with Segoe MDL2 Assets fallback.
- `MonoFont`: Cascadia Mono with Consolas fallback, reserved for identifiers,
  measurements, and compact provenance.

| Token | Size | Role |
|---|---:|---|
| `FontMicro` | 10 | Compact tags |
| `FontMono` | 11 | Measurements |
| `FontCaption` | 12 | Supporting copy |
| `FontSmall` | 12 | Muted descriptions |
| `FontLabel` | 13 | Navigation |
| `FontBody` | 14 | Body |
| `FontSection` | 17 | Section headings |
| `FontTitle` | 20 | Card titles |
| `FontDisplaySm` | 22 | Compact hero title |
| `FontMetric` | 34 | Primary metric |
| `FontDisplay` | 30 | Page title |

The old Georgia display face and oversized 38–64 px editorial hierarchy are
retired from normal product UI.

## Navigation

Navigation is grouped by user intent:

- System: Overview and Performance.
- Control: Audio, Security, and Remote Assist.
- Connect and maintain: Device Mesh, Capture, and Updates.

A working module and a preview module must never look equivalent. Preview badges
are explicit and exposed through UI Automation.

## Components

All component styles live in `SoltexTheme.xaml`:

- `CardStyle`, `QuietCardStyle`, and `HeroCardStyle`;
- `ActionButton`, `SecondaryButton`, and `NavButton`;
- `StatusPillStyle`, `FieldStyle`, `MetricProgressStyle`;
- `SoltexSliderStyle`;
- implicit scrollbar and data-grid styles.

Every interactive control needs rest, hover, pressed, keyboard-focus, disabled,
busy, success, and failure behavior where applicable.

## Motion

| Token | Duration |
|---|---:|
| `MotionFast` | 120 ms |
| `MotionBase` | 160 ms |
| `MotionSlow` | 200 ms |

Motion must clarify state change. It must not loop decoratively.

## Accessibility status

Implemented boundaries include visible focus, text-backed status, named charts,
and automation names on primary controls.

Still required before conformance claims:

- complete keyboard traversal;
- UI Automation and screen-reader review;
- high-contrast behavior;
- reduced-motion verification;
- 100%, 125%, 150%, and 200% scaling;
- small-window and multi-monitor coverage;
- owner visual acceptance.

## Extension rules

1. Add raw colours only to `Tokens.xaml`.
2. Add reusable components only to `SoltexTheme.xaml`.
3. Prefer an existing spacing, type, and radius token.
4. Keep preview capability visibly labeled.
5. Run the design-token guard, Release build, application tests, and complete
   native render matrix before merging.
