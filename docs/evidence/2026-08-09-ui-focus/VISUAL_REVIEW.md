# UI-focus current-capture review

Verdict: **KEEP** for the bounded 1280x820 candidate. The default surfaces are calmer, coherent, and free of a gross clipping or hierarchy blocker in the inspected captures. This is an agent current-capture review, not owner acceptance.

## Direction

The implementation follows a quiet instrument-panel direction: warm graphite and parchment, coral for selection/action, mint and amber for state, strict left alignment, restrained motion, and a serif/neutral/mono type hierarchy. It borrows focus and immersion principles from the stated references without cloning their assets or layouts.

Default pages prioritize a small working set. Secondary telemetry or long operational lists require an explicit disclosure action.

## Observed defaults

- **Home:** one dominant live metric, a concise machine card, two secondary signal groups, and explicit unavailable/not-enrolled states.
- **Monitoring:** CPU, memory, and network are the only first-view facts. Volumes, provider coverage, and process rows move behind `Show system detail`.
- **Mixer:** each primary column shows at most six active endpoints. The header reports the total, and `Show N more` contains overflow active plus inactive endpoints.
- **Security:** current provider health and supported actions remain visible. The bounded 24-event table is collapsed behind `Show activity`.
- **Clips:** the empty state explicitly says capture is off and avoids a decorative empty dashboard.
- **Devices, Remote Assist, Updates:** their local-only, consent-first, external-runtime, and non-installing boundaries remain visually primary.
- **Sidebar:** the protection status is compressed to one low-emphasis boundary card instead of competing with the active workspace.

## Observed expanded states

- Monitoring details scrolls to fixed volumes, provider coverage, and the bounded process table.
- Mixer more-endpoints scrolls to the complete classified inventory; density is permitted only after the user asks for it.
- Security activity reveals the fixed-height, internally scrolling, path-redacted event table and a visible `Hide activity` control.

## Measured

- Eleven PNGs were inspected at exactly 1280x820.
- Eight default states and three expanded states are bound by [`EVIDENCE_MANIFEST.json`](EVIDENCE_MANIFEST.json).
- Three independent native Clips captures were byte-identical.
- Nine WPF application checks include default-collapsed and open/close behavior for Monitoring, plus bounded Mixer primary lists and endpoint conservation across the disclosure boundary.
- The design-token guard scanned all seven tracked XAML files successfully.

## Generic-design audit

No P0 generic-AI tell was observed in the current captures: there is no neon gradient soup, glass-card grid, indiscriminate pill treatment, centered marketing composition, or excessive animation. The earlier equal-weight card/list density was materially reduced on Monitoring, Mixer, Security, Clips, and the sidebar.

Intentional uppercase is limited to status/provenance microcopy. Rounded cards remain part of the established Soltex native theme, but hierarchy now comes primarily from spacing, scale, typography, and disclosure rather than card count.

## Unknown and open

- Owner taste and final `KEEP`, `REVISE`, or `REJECT` decision.
- 1366x768, 1440p, 4K, portrait, snapped-window, and 125/150/200-percent scaling behavior.
- Keyboard traversal order, screen-reader reading order/names beyond current automation labels, and formal contrast measurements.
- High-contrast, reduced-motion, long localization strings, and extreme live-data states.
- Extended-state usability with hundreds of endpoints or events beyond the enforced provider bounds.

## Five-axis boundary

| Axis | Status | Evidence boundary |
|---|---|---|
| Functional implementation | PASS | Progressive-disclosure handlers and failure clearing |
| Runtime integration | PASS | Eleven native owner-host WPF captures |
| Visual presentation | PASS for one viewport | Agent `KEEP`; not a viewport matrix |
| Evidence/regression coverage | PASS for this slice | Tests, hashes, repeatability, factual ledger |
| Human acceptance | UNKNOWN | Reserved for the owner |
