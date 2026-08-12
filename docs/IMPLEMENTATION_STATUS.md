# Implementation status

Snapshot: 2026-08-12
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

- compact module-owned navigation: Overview, Performance, Applications, Audio, Security, Remote Assist, Activity, Updates, and a separate bottom-rail Settings entry;
- deep neutral surfaces, restrained electric-iris accent, Segoe UI Variable typography, tighter spacing/radii, Fluent glyphs, and clearer focus hierarchy;
- preview labels and concise first-view copy so unfinished modules do not masquerade as shipped tools;
- progressive disclosure retained for provider, event, and overflow detail.

Exact native matrices through the Activity checkpoint were agent-inspected at 1280×820 on both the owner host and hosted Windows runner. Human owner visual acceptance remains open, so PR #11 remains draft.

### Applications

Hosted Windows-verified on `04da86e6e38f6a1de99b5de8edd2427792f4bf36`: bounded, searchable installed-software and sign-in inventories from documented Windows uninstall, Run/RunOnce, and Startup-folder sources. The page is read-only; command lines, executable paths, uninstall strings, and disable/remove actions are absent. Its exact native capture was inspected after a task-first visual refinement.

The current draft adds a third progressive Services view through query-only Service Control Manager access. It excludes drivers and binary paths, exposes state/start mode for at most 512 path-free Win32 service rows, and provides no start, stop, enable, disable, configuration, delete, or install action. Owner-host development capture observed 296/296 services with 0 inaccessible and 0 omitted in about 65 ms. Exact-source and hosted acceptance are pending.

### Settings and local preferences

Hosted Windows-verified on `5c4a16de85b0b63bff4779769193a1e94411c77c`: Settings owns a working 1/2/5-second telemetry cadence, optional restoration of the last workspace after a normal close, and the default disclosure state for Performance detail. The bounded non-secret document remains on the current Windows account, recovers explicitly after invalid/oversized input, and does not add analytics or a background service.

The current draft migrates preferences to schema 2 and adds an explicit close policy. Exit remains the default. Notification-area mode is opt-in, exposes only Open and Exit, fails closed when unavailable, and keeps the same desktop process open without installing a service. Performance sampling stops while hidden or minimized.

### Activity and local privacy

Hosted Windows/package verified on PR head `4437db80b844dcfe33cd8265e41cb5fcf00bfd76`: one searchable timeline retains at most 120 sanitized meaningful events. Session-only is the default and creates no Activity file. Seven-day or 30-day retention is an explicit local preference; shortening retention and clearing history are confirmation-owned by the main window. The JSON document is capped at 256 KiB, rejects unknown or invalid schema data, prunes expired/future/overflow entries, uses atomic replacement, and surfaces storage failure instead of pretending persistence succeeded. File- and path-like text is replaced with a generic local-item summary. Owner visual acceptance remains open.

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

### Audio mixer

Owner-host exact-source verified on `3511ba92bde450ffac4e3fad145d9a13b8986e72` and hosted Windows/package accepted on PR head `abf1dc5e58ca7a23ef57a975c7fdeb041ea4d183`: bounded Windows Core Audio endpoint enumeration plus active shared-mode render sessions. Soltex inspects at most 128 session slots, exposes at most 24 path-free session rows, and keeps system-sounds, multi-process/transferred, ended, or process-unverifiable sessions read-only. Eligible volume/mute requests revalidate endpoint/session identities plus process ID/start time immediately before the write and require immediate Windows read-back before success. The default UI keeps app controls primary and complete device inventory behind explicit disclosure.

The full Release/identity/design test gate passed; standard Audio is 20/20, the opt-in task-owned silent-session live-write gate is 21/21, App/control is 19/19, the exact native matrix is 14/14, and self-contained package launch/identity passed. Hosted Windows and package runs also passed, downloaded artifacts re-hashed exactly, and hosted Audio/package pixels were inspected. Not implemented: endpoint switching, app routing, virtual devices, EQ, DSP, noise suppression, microphone processing, profiles, or Sonar parity.

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

All eleven native 1280×820 captures from that exact Windows run were inspected. The later Applications candidate at `04da86e6e38f6a1de99b5de8edd2427792f4bf36` passed Windows run `31550273188` and package run `31550273340`; its 12-state native matrix included the inspected Applications surface.

The Settings checkpoint `5c4a16de85b0b63bff4779769193a1e94411c77c` passed Windows run `31551365746` and package-smoke run `31551365787`. Windows artifact `9124417870` has digest `sha256:54c1e6bd62180db525e1dfe9fd0c8ac30f97c56ade3baf27ea0d7181137bd031`; its exact native Settings capture was agent-inspected.

The Activity checkpoint `4437db80b844dcfe33cd8265e41cb5fcf00bfd76` passed Windows run `31560242046` and package-smoke run `31560242019`. Windows artifact `9127496208` has digest `sha256:0046bc2b99c0e10accf58b3c030025c4802ceee782553a06f7c3881599e969c9`; package artifact `9127485263` has digest `sha256:dfd3b7aed215a2646bac1ad745e431511f5e4de4958b479b9a8d3865865ab30b`. The 14-state manifest and retained PNG identities were independently revalidated, and hosted Activity/Settings captures were inspected. Optional hosted AMSI/EICAR remained 31/32 because that runner's installed provider returned native result `1`; required gates passed.

The Audio reconciliation head `abf1dc5e58ca7a23ef57a975c7fdeb041ea4d183` passed Windows run `31562540695` and package-smoke run `31562540694`. Downloaded artifacts `9128308988` and `9128281441` matched their recorded GitHub digests. All 14 native PNG identities revalidated; default/expanded Audio and packaged Home were directly inspected. The packaged executable launched with exit code 0 and remained unsigned.

## Next stages

1. Publish and accept the read-only Services/notification-area/runtime-cost slice through exact-head Windows, package, native-render, and owner-review gates.
2. Add bounded local historical telemetry only after its storage, retention, migration, and measured-idle-cost contract is independently proven.
3. Add supported default/fallback audio endpoint selection without claiming routing or DSP.
4. Add mTLS/Tailscale device enrollment, capability-scoped agents, revocation, emergency stop, and NAS audit transport. Keep personal Google Drive and work Box in separate permission domains.
5. Add Windows Graphics Capture, benchmark profiles/provenance, signing, installer lifecycle evidence, accessibility/scaling verification, and owner acceptance.

## Nonclaims

Soltex is not yet production-ready, a full AppControl replacement, a SteelSeries GG/Sonar replacement, an antivirus engine, a remote-management platform, or an unattended correction system.
