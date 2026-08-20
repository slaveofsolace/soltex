# Whisper Stage 6 owner-host evidence

This checkpoint records bounded, content-free evidence from the owner-controlled
Windows host. It does not contain microphone samples, transcript text, clipboard
content, target text, model bytes, local paths from application state, or credentials.

## Source identities

- UI matrix source and tested commit: `e3dbbdbcc6d36f1d8a27a6302eccf1b72ce6cc49`
- Runtime, capture, hook, and installer source: `9ab5543656b4eba455ecf47a1c25df053cc13d12`
- Hosted installer lifecycle source and tested commit: `2215247e257a69e0c8c6317ffb347a1c244e69f2`
- Extended target-matrix source and tested commit: `0c59d0fd186791c5bb591c7e1f5252af6dc7bf30`
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

## Controlled target and latency proof

The current owner-host target run passed `62/62` cases. Soltex created and owned every
mutated field; the evidence log contains no target or transcript content.

- WinForms clipboard insertion with ownership-checked restoration: `296.27 ms`
- WinForms direct UI Automation replacement: `13.34 ms`
- WinForms verified submit: `90.55 ms`, exactly one observed Enter
- Chromium input insertion: `269.17 ms`
- Chromium verified submit: `331.53 ms`, two controlled targets and two observed Enter events
- WPF TextBox: `AutomationValue`
- WPF RichTextBox: `ClipboardPaste`
- PasswordBox: `ProtectedField` fallback
- Read-only field: `TargetNotEditable` fallback
- Focus-changing field: `TargetChanged` fallback
- Log SHA-256: `d91f142e719ae9f00d15f2f6c199ea8e7e29802a2254a5b29cfd547ed3273a02`

These timings are one-host observations. They do not prove WinUI or production-model behavior.

## Extended owner target matrix

The extended exact-head run adds three content-free owner-host rows:

- Electron 42.7.1 executable: `232794112` bytes; SHA-256
  `6482758560e64f4e99a62dd244223a238ff26a378bbe813790f1efbcec2bccc8`
- Electron: `Browser`, `ClipboardPaste`, insertion `393.49 ms`, verified submit
  `162.69 ms`, exactly one Enter; log SHA-256
  `4abd0bdab12d1fe88c48484573520df625d327c66bf2f4971dbfc6f804d95dc9`
- Windows Terminal: `Terminal`, `Medium`; submission-disabled policy `InsertText`,
  separately opted-in policy `InsertAndSubmit`, zero dispatches; log SHA-256
  `fc6ed42231391751efe231abac771d1ec82533916317ea742c253d2b676368ac`
- Elevated child: exact process token `High`; standard-integrity inspection
  `UnknownTarget`; decision `CopyText`; zero mutation and submit dispatches; log
  SHA-256 `9788ceddbad22437fe484dc98fd7a5f8903c7318f064455e61552e6132bfa990`

The Electron runtime was a pinned repo-external proof dependency and is not shipped or committed.
The host policy rejected automatic recursive deletion of that temporary runtime root, so its local
cache remains outside the repository. No Electron fixture process or profile remains active.

A dedicated WinUI 3 sample built and opened, but its `TextBox` did not appear in UI Automation by
exact HWND or desktop-root PID/automation-ID lookup. That experimental sample was removed rather
than counted as proof. WinUI remains pending.

## Installer compile checkpoint

The per-user Inno installer compiled locally with `Soltex.exe` and the exact four
Whisper CPU runtime DLLs in its payload.

- Version: `0.0.1`
- Bytes: `77248201`
- SHA-256: `d594f4eeeb16a92ffb8efaf59ec3a5debe72d4be98b4696795f46c037e60c54a`
- Authenticode: not signed; SmartScreen trust is not claimed

Install, upgrade, and uninstall were intentionally not executed on the owner profile
because it contains existing Soltex state. The destructive lifecycle proof ran only on
a fresh ephemeral Windows runner and passed at exact source `2215247`:

- Workflow run: [32356853289](https://github.com/slaveofsolace/soltex/actions/runs/32356853289)
- Initial 0.0.1 installer: `77269992` bytes,
  SHA-256 `72ad4a4185e656b852aec21889585675389ef42da4b2156399d7b4b92ed30268`
- Upgrade 0.0.2 installer: `77269994` bytes,
  SHA-256 `d23feec3302b5be4b819ccb0d5a2f43f7b99409694a80d0ec4104e2afdb8e4c9`
- Install/update/uninstall lifecycle manifest SHA-256:
  `5ad4a2eabdbf61f35135ae0ae9369ba864958284243f218520035c0dd34cb9c1`
- Packaged executable/render identity manifest SHA-256:
  `b4bd10eef4d87cdc464b99a5cd2d425175f587fbb14f44ce017f2920e3f6bf53`
- Exact-owned cleanup report SHA-256:
  `4ee8858033c1c39d26ebf5e6dc6340e3e9b3869de4a33af85ad6b8e76005aa56`
- Model and settings survived update; uninstall removed their exact-owned fixtures.
- An unrelated model-directory file and the shared authenticated-state key survived.
- Inno second-phase self-removal completed after a bounded `561.1 ms` observation.
- The packaged CPU runtime loaded with no model or microphone access.

The installers and executable remain unsigned. This is functional lifecycle evidence,
not publisher reputation, SmartScreen acceptance, or production release authorization.

## Still not proven

- The 547 MiB production model was not downloaded or opened.
- Model-backed accuracy, latency, memory, unload, and restart remain unmeasured.
- Release-to-transcription and full capture-to-verified-submit latency remain pending.
- Physical shortcuts, live per-monitor DPI transitions, keyboard-only navigation, and
  a screen-reader walkthrough remain owner-proof gates.
- WinUI editable-target proof remains pending.
- The navigation `SCAFFOLD` marker remains until a real model-backed capture,
  transcription, insertion, verification, and safe cancellation complete on the owner
  host.
