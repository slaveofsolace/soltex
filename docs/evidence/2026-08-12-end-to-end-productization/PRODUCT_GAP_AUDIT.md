# Product gap audit

## Verdict

`REVISE`. Soltex is technically real and visually coherent, but it still presents like a careful engineering console rather than a finished daily-use Windows product.

## First impression

The application earns trust through live data and honest limitations. It loses product confidence through passive space, tiny provenance-first copy, hidden task depth, and weak cross-module flow.

## Cold-eye findings

| Priority | Evidence | Location | Finding | Disposition |
|---|---|---|---|---|
| P1 | VERIFIED | Security / Soltex activity | Long entries previously rendered as one horizontal line and hid useful text. | REPLACED with wrapped, bounded rows; exact native Security capture inspected. |
| P1 | VERIFIED | Shell footer | `v1 preview` read like scaffold residue even though multiple modules are implemented and tested. | REPLACED with the assembly version plus a real command affordance. |
| P1 | VERIFIED | Cross-module flow | Sidebar-only routing made every task begin with pointer travel. | REPLACED with a bounded Ctrl+K switcher and Ctrl+1 through Ctrl+9 routes. |
| P1 | VERIFIED | Audio | Per-app changes were verified, but there was no user-owned snapshot or recall path. | REPLACED with bounded capture/apply/clear recovery controls and explicit partial results. |
| P2 | OBSERVED | Settings, Activity, Remote Assist | Large empty canvases make the product feel unfinished when the state is sparse. | KEEP the restraint; improve task framing and contextual next actions rather than add filler. |
| P2 | OBSERVED | Performance and Audio | Provider names and provenance are visually competitive with the user's job. | KEEP the evidence but demote it to secondary disclosure/footer treatment. |
| P2 | OBSERVED | Several status pills | Repeated all-caps micro labels can feel machine-generated when overused. | KEEP only when they encode a real state; remove decorative uses. |

No P0 AI-design tell was observed. The automated source scan also reported zero catalog matches; the important issues are rendered hierarchy and product behavior, not stock web classes.

## Reference learning

SteelSeries GG is not a visual template for Soltex. Its transferable product lessons are:

1. A module should expose its main job immediately, not after a diagnostic disclosure.
2. Controls need persistent state, a visible owner, and a clear result.
3. Dense information is acceptable inside a focused tool; global navigation should remain quiet.
4. Device and capability absence should produce a useful next step, not a dead panel.
5. Distinctive product maturity comes from completed states, not from decorative gaming graphics.

## Chosen direction

- Type: Segoe UI Variable for working text; Cascadia Mono only for exact IDs, timing, and evidence.
- Palette: near-black canvas, one cyan interaction accent, green/amber/red reserved for state.
- Layout: strict task grid, compact command rail, fewer passive cards.
- Motion: one 140-180 ms state transition; respect Windows animation settings.
- Signature behavior: every meaningful write ends with an explicit verified, partial, or failed outcome and a recovery path.

## Human decision

The user remains the acceptance owner for whether the final installed build feels sufficiently polished. Automated and agent review cannot grant that subjective acceptance.

## Current verification

- canonical Release verifier: 0 warnings, 0 errors;
- Security 32/32, supply-chain 18/18, hardening 12/12, Updates 17/17,
  Device Fabric 24/24, Monitoring 16/16, Audio 21/21, App/control 31/31;
- design-token and identity policies: passed;
- exact native 1280x820 command, Mixer, and Security states: captured and
  directly inspected;
- production signing, owner visual acceptance, and hosted acceptance: not yet
  established.
