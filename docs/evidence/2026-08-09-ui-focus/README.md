# UI-focus candidate evidence packet

Date: 2026-08-09

Repository: `slaveofsolace/soltex`

Candidate branch: `soltex-ui-focus-v1`

Stack base: `feat/soltex-monitoring-and-audio` at `a0daa2163529d8f813e79ea7e342ddfbd5c61f50`

Publication status: merged to `main` by PR #9 at `61639bfe7ea3a7191fcb6c70ea1f2a699aa77248`. The branch and stack-base fields above identify the historical candidate that produced this packet.

This packet records the bounded quiet-hierarchy and progressive-disclosure pass for the native WPF workspace. It also records two Windows PowerShell 5.1 verification-script repairs and deterministic native-render initialization.

- [`WINDOWS_GATE.md`](WINDOWS_GATE.md): exact owner-host build, test, interoperability, and recovery evidence.
- [`VISUAL_REVIEW.md`](VISUAL_REVIEW.md): current-capture review of the eight default workspaces and three expanded states.
- [`EVIDENCE_MANIFEST.json`](EVIDENCE_MANIFEST.json): byte sizes and SHA-256 digests for all eleven inspected 1280x820 PNGs.

The local PNGs are generated evidence under `artifacts/visual/ui-focus-v1/final/` and remain outside version control. The manifest makes the reviewed bytes identifiable without committing machine-specific telemetry captures.

This is a merge candidate, not a production-readiness, antivirus-efficacy, accessibility-conformance, multi-viewport, or owner-acceptance claim.
