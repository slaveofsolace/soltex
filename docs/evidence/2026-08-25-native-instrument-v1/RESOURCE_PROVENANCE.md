# Native instrument design references

Acquired: 2026-08-25 12:17 America/Chicago

Disposition: **REFERENCE ONLY**

## Apple Human Interface Guidelines

- Source: Apple Inc.; <https://developer.apple.com/design/human-interface-guidelines/>.
- Supporting pages:
  <https://developer.apple.com/design/human-interface-guidelines/design-principles>,
  <https://developer.apple.com/design/human-interface-guidelines/typography>, and
  <https://developer.apple.com/design/human-interface-guidelines/layout>.
- Version: live public documentation as inspected on the acquisition date; no
  source revision identifier was published on the inspected pages.
- Rights/entitlement: public documentation viewed for general design guidance.
  No entitlement to reuse Apple design resources, assets, fonts, code, or trade
  dress is asserted.
- Acquisition and archive scan: web documentation was inspected in place. No
  archive, installer, binary, design resource, or payload was downloaded, so an
  archive malware scan and payload SHA-256 are not applicable.
- Inspection: retained only general principles of purpose, agency,
  responsibility, familiarity, flexibility, simplicity, craft, delight,
  hierarchy, readable typography, alignment, and adaptive layout.
- Clean-room adaptation: Soltex independently translates these principles into
  one dominant task per workspace, calm spacing, concise copy, progressive
  disclosure, clear feedback, and recoverable actions. It does not reproduce
  Apple component geometry, materials, platform layouts, wording, or visual
  identity.
- Runtime proof: the onboarding renders referenced by
  `SHELL_ONBOARDING_SLICE.md` prove the current independent hierarchy and
  compact-shell behavior only. They do not prove Apple parity or human
  acceptance.
- Release ledger: admitted as **REFERENCE ONLY**; no redistributable item enters
  the package or SBOM.

## Microsoft Mica guidance

- Source: Microsoft; <https://learn.microsoft.com/windows/apps/design/style/mica>.
- Version: live Windows application design documentation inspected on the
  acquisition date.
- Rights/entitlement: public product documentation used as the authoritative
  behavior reference for a future Windows-native backdrop.
- Acquisition and archive scan: inspected in place; no payload was downloaded,
  so archive scan and payload SHA-256 are not applicable.
- Inspection: Mica is intended as the base layer of a long-lived app window,
  with a restrained content layer and automatic solid/high-contrast fallbacks.
- Adaptation: the next native-chrome slice may use supported Windows APIs for
  the shell base layer while retaining Soltex's own tokens and identity.
- Runtime proof: none in this slice. Mica/AppWindow activation remains an
  explicit nonclaim until native runtime and fallback evidence exists.
- Release ledger: documentation-only input; no external runtime or asset has
  been admitted.

## Boundary

The Apple reference defines the desired emotional qualities; Microsoft guidance
defines the Windows platform behavior. Soltex's code, layout, copy, tokens, and
interaction design remain independently authored.
