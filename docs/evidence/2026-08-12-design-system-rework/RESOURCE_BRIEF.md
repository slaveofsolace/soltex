# Soltex UI rework resource brief

Date: 2026-08-12

## Objective

Replace the scroll-heavy, copy-heavy card dashboard with an independently
authored native WPF workbench that is easier to operate at 1280 × 820 and scales
without hiding core controls.

## Functional requirements

- Keep every existing working capability and honest preview boundary.
- Put direct controls, tables, charts, and live state before explanations.
- Use local tabs, master-detail panes, and internal scrolling instead of
  whole-page scrolling.
- Use sliders only for genuine continuous/relative values such as audio volume
  and user-controlled refresh cadence.
- Preserve keyboard access, automation names, status text, lifecycle behavior,
  and current security boundaries.

## Aesthetic direction

Quiet Instrument Deck: Windows-native graphite surfaces, glacier-blue interaction
signal, shallow hierarchy, precise hairlines, small radii, restrained density,
and one dominant work surface per module. Avoid gradient heroes, glass, violet
SaaS styling, repeated all-caps eyebrows, excessive pills, equal-weight card
stacks, and explanatory prose blocks.

## Delivery constraints

- Runtime: WPF/.NET on Windows; no framework migration.
- Reference viewport: 1280 × 820.
- Dependencies: no new runtime package, font, image, icon pack, or web asset.
- Performance: no decorative bitmap/video payload; preserve bounded telemetry
  and existing startup/lifecycle budgets.
- Accessibility: retain visible focus and text-backed status; test keyboard,
  100–200% scaling, high contrast, and reduced motion before conformance claims.
- Rights: reference-only clean-room translation; no proprietary payload or
  distinctive UI reproduction.

## Acceptance evidence

- Before/after native capture matrix for all primary workspaces.
- Design-token guard, Release build, affected suites, and render smoke.
- Word/scroll/control audit against the design-system budgets.
- Human owner visual acceptance remains required.

## Existing registry reused

The AppControl record in
`../2026-08-04-reference-systems/CANDIDATE_REGISTRY.json` remains the canonical
candidate record. It was rechecked on 2026-08-12; its **REFERENCE ONLY**
disposition is unchanged. No acquisition stage was opened.
