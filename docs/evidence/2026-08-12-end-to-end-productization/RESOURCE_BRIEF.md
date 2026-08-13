# Reference brief

## Job

Make Soltex feel like a coherent Windows product rather than a collection of engineering panels, while keeping its controls truthful, local, and reversible.

## Direction

- Swiss/industrial native utility: strict grid, direct labels, compact task controls, and functional status color.
- Preserve the current near-black canvas and cyan signal accent.
- Use typography and density to establish hierarchy; do not add decorative gradients, glass, fake telemetry, promotional art, or noisy card grids.
- Add depth through real state, recovery, shortcuts, focus behavior, and verified outcomes.

## Runtime and delivery

- Windows 10/11, WPF, .NET 10 pinned by `global.json`.
- Per-user installer and installed-app upgrade path.
- No kernel drivers, audio virtual devices, game injection, hidden remote-control runtime, or replacement-antivirus claims.

## Budgets

- Preserve the existing bounded telemetry cadence and collectors.
- No additional always-on background service.
- New local documents must be bounded, atomically replaced, and recover safely from malformed or oversized input.
- Keep the primary module view understandable at 1280 x 820 and at Windows scaling targets covered by the native render matrix.

## Evidence contract

- Focused unit tests for new state transitions and recovery behavior.
- Release build and existing project verification suites.
- Exact native captures of normal, empty, failure, keyboard, and confirmation states.
- Installed upgrade/recovery check that preserves existing local security data.
- Human visual acceptance remains a separate owner decision.
