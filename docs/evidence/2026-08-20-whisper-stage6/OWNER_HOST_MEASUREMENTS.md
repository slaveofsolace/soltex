# Whisper Stage 6 owner-host evidence

This checkpoint records bounded, content-free evidence from the owner-controlled
Windows host. It does not contain microphone samples, transcript text, clipboard
content, target text, model bytes, local paths from application state, or credentials.

## Source identities

- UI matrix source and tested commit: `e3dbbdbcc6d36f1d8a27a6302eccf1b72ce6cc49`
- Runtime, capture, hook, and installer source: `9ab5543656b4eba455ecf47a1c25df053cc13d12`
- SDK: .NET `10.0.302`
- Configuration: `Release`

## Deterministic UI matrix

The direct-process evidence runner captured 22 states: light, dark, high contrast,
standard and minimum-size page profiles, deterministic 100/150/200-percent raster
profiles, all 13 visible presenter states, and representative overlay variants.

- Manifest status: `canonical-clean-tree`
- Manifest SHA-256: `b1d5c22d26e6dbb1c4a643d8e22dc1fb04eb98048c0a8b402d01df94e4262bb8`
- Exact-head full-gate log SHA-256: `edb6b727b09142472decf0d28753ab577e20aff65774bb4f81a44f7cf6180030`
- App manifest declaration: `PerMonitorV2,PerMonitor`, compatibility `true/pm`

The 150- and 200-percent artifacts prove deterministic high-density WPF
rasterization. They do not prove a live Windows per-monitor DPI transition.

## Runtime snapshot

The content-free lifecycle probe at `9ab5543` recorded:

- Startup: `673.0762 ms`
- Navigation: `20` transitions, `13.48316 ms` mean, `147.7046 ms` maximum
- Visible idle: `0.531524704723994%` normalized CPU, `164024320` bytes working set,
  `116936704` private bytes
- Minimized steady: `0%` normalized CPU; performance sampling inactive
- Hidden notification area: `0%` normalized CPU; performance sampling inactive
- Runtime report SHA-256: `0b174b23583bcbaacd083c23dfda8e4ba755ab42f2614ce52994edc0f74af153`

These are one-host observations, not resource guarantees.

## Safe live adapter proof

The owner-host Windows adapter run passed `61/61` cases, including the 59
deterministic cases plus two explicitly enabled live checks:

- WASAPI: `5` devices observed; one disposable `950.0 ms` clip; `30400` PCM bytes;
  `1008.6 ms` wall time; no fallback
- Shortcut hook: `6` callbacks; p50/p95/p99 `3.30/70.80/70.80 microseconds`;
  injected signals observed by the action handler: `0`
- Log SHA-256: `600de1799568715e1241b50e25a7184228336294b14a5bbbc99e9bad08fcfc78`

The shortcut run proves registration, callback timing, injected-input rejection, and
clean unregistration. It does not replace a human physical-key or mouse-button pass.
The capture run proves one current device path and buffer disposal. It does not prove
disconnect recovery across a representative hardware matrix.

## Installer compile checkpoint

The per-user Inno installer compiled locally with `Soltex.exe` and the exact four
Whisper CPU runtime DLLs in its payload.

- Version: `0.0.1`
- Bytes: `77248201`
- SHA-256: `d594f4eeeb16a92ffb8efaf59ec3a5debe72d4be98b4696795f46c037e60c54a`
- Authenticode: not signed; SmartScreen trust is not claimed

Install, upgrade, and uninstall were intentionally not executed on the owner profile
because it contains existing Soltex state. The destructive lifecycle proof is confined
to a fresh ephemeral Windows runner and remains pending until that exact-head job
finishes successfully.

## Still not proven

- The 547 MiB production model was not downloaded or opened.
- Model-backed accuracy, latency, memory, unload, and restart remain unmeasured.
- Release-to-transcription and full capture-to-verified-submit latency remain pending.
- Physical shortcuts, live per-monitor DPI transitions, keyboard-only navigation, and
  a screen-reader walkthrough remain owner-proof gates.
- WinUI, Electron, Windows Terminal, and elevated-editor target proof remains pending.
- The navigation `SCAFFOLD` marker remains until a real model-backed capture,
  transcription, insertion, verification, and safe cancellation complete on the owner
  host.
