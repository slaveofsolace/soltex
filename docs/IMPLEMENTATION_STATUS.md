# Implementation status

Snapshot: 2026-08-11  
Product: **Soltex**  
Repository: `slaveofsolace/soltex`  
Canonical baseline: `main` at `9e424a47f1cf23d8a174162d3561f5b27dbb7551`  
Draft rebuild: `sol/soltex-product-rebuild` / [PR #11](https://github.com/slaveofsolace/soltex/pull/11)

## Evidence vocabulary

- **Hosted Windows-verified:** compiled and exercised by the stated Windows workflow on the exact commit.
- **Owner-host verified:** exercised on the owner's separately identified Windows machine.
- **Implemented, verification pending:** source exists but the complete current gate has not passed.
- **Designed:** an architecture or typed boundary exists without an end-to-end user capability.
- **Not implemented:** no working capability exists.

A passing test proves only its named boundary. A rendered image proves layout execution, not human visual acceptance.

## Current candidate

### Product shell

Implemented on the draft branch:

- compact module-owned navigation: Overview, Performance, Audio, Security, Remote Assist, Device mesh preview, Capture preview, and Updates;
- deep neutral surfaces, restrained electric-iris accent, Segoe UI Variable typography, tighter spacing/radii, Fluent glyphs, and clearer focus hierarchy;
- preview labels and concise first-view copy so unfinished modules do not masquerade as shipped tools;
- progressive disclosure retained for provider, event, and overflow detail.

The pre-rebuild exact native matrix was human-inspected. Fresh images from the current candidate still require human review before PR #11 can leave draft.

### Applications

Implemented on the draft branch: bounded, searchable installed-software and sign-in inventories from documented Windows uninstall, Run/RunOnce, and Startup-folder sources. The page is read-only; command lines, executable paths, uninstall strings, and disable/remove actions are absent.

### Performance and bounded task action

Implemented on the draft branch:

- live CPU, memory/commit, storage, network, and bounded process observation;
- history charts and explicit provider/limitation states;
- process selection with graceful End task first;
- a second explicit force-stop confirmation only after the target does not close;
- PID, name, Windows session, and start-time revalidation;
- hard rejection of Soltex, PID 0–4, Session 0, cross-session targets, and named critical Windows/security processes;
- force stop targets only the chosen process, never its descendants.

No executable paths, packet contents, destination history, elevation bypass, or generic process-tree termination are introduced.

### Audio observation

Implemented: read-only Windows Core Audio endpoint enumeration, render/capture grouping, state, default assignment, current volume, mute observation, and bounded primary lists.

Not yet implemented: per-app sessions, volume/mute writes, app routing, virtual devices, EQ, DSP, or noise suppression.

### Security companion

Implemented: provider-neutral Windows Security Center health, Defender status and supported requests, path-redacted Defender events, AMSI for ingested content, hashing, Authenticode observation, authenticated quarantine/audit state, and bounded import observation.

Soltex is not an antivirus provider and does not disable or replace Defender or Malwarebytes.

### Device fabric and Remote Assist

Implemented/designed: typed local observation, capability manifests, enrollment/revocation/policy models, and a trusted separately installed RustDesk launcher with identity revalidation and local confirmation.

Not yet implemented: remote executor, unattended correction, public listener, Tailscale authorization, mTLS enrollment, emergency stop, or fleet/NAS log transport. RustDesk remains external remote hands.

### Updates and packaging

Implemented: signed-manifest/update-planning primitives, monotonic/equivocation checks, bounded staging, deterministic preview, recovery journal, self-contained `win-x64` packaging, Inno Setup definition, package smoke, hashes, and retained render identity.

Not yet implemented: production publisher identity/trust root, signed public release, activation/repair/rollback/uninstall evidence, or GitHub Release automation.

## Hosted verification history

The process-control candidate at `6466fa58971e154dc487070d4bdf873e2cdf3175` passed Windows run `31548417109` and package run `31548417244`.

- Windows artifact `9123371405`: SHA-256 `59426b8cec52e343a2e56789822641d926ab7e1511a5c39eb6ffa5cf99747995`
- package artifact `9123351960`: SHA-256 `15fd5d67c26122b914ebb5a113e27d7d0d61d0698b4c94a0891f564c4b08579a`

All eleven native 1280×820 captures from that exact Windows run were inspected. The follow-up visual-honesty correction is not covered by those historical runs and requires a new exact-head gate.

## Next stages

1. Pass exact-head Windows/package workflows; inspect fresh default and expanded native renders.
2. Add a real Settings/permissions surface and local historical telemetry with explicit retention.
3. Add Core Audio session observation and documented volume/mute writes with read-back. Routing remains a separate signed virtual-audio component.
4. Add mTLS/Tailscale device enrollment, capability-scoped agents, revocation, emergency stop, and NAS audit transport. Keep personal Google Drive and work Box in separate permission domains.
5. Add Windows Graphics Capture, benchmark profiles/provenance, signing, installer lifecycle evidence, accessibility/scaling verification, and owner acceptance.

## Nonclaims

Soltex is not yet production-ready, a full AppControl replacement, a SteelSeries GG/Sonar replacement, an antivirus engine, a remote-management platform, or an unattended correction system.
