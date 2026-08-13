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
- a bounded `Ctrl+K` workspace switcher plus `Ctrl+1` through `Ctrl+9` direct
  routes, with Escape dismissal and focus restoration;
- Quiet Instrument Deck surfaces with graphite work areas, a glacier-blue
  interaction signal, Segoe UI Variable typography, small-radius hairline
  geometry, and a 2–3 px selected-workspace rail;
- compact command headers, local modes, and bounded work areas that put direct
  controls before commentary;
- preview labels and concise first-view copy so unfinished modules do not
  masquerade as shipped tools;
- progressive disclosure retained inside the owning work area for provider,
  event, and overflow detail.

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

The current branch also adds a local Benchmark mode inside Performance. Its
Quick profile measures named SHA-256, managed buffer-copy, and 32 MiB temporary
storage workloads sequentially; it is cancelable, cleans scratch on every exit,
suspends live telemetry while visible, and saves at most one bounded path-free
result. It deliberately has no synthetic score, cross-machine rank, GPU load, or
stability verdict. The production profile and UI/storage recovery paths pass
focused owner-host tests; commit-bound complete acceptance is pending.

No executable paths, packet contents, destination history, elevation bypass, or generic process-tree termination are introduced.

### Audio mixer

Owner-host exact-source verified on `3511ba92bde450ffac4e3fad145d9a13b8986e72` and hosted Windows/package accepted on PR head `abf1dc5e58ca7a23ef57a975c7fdeb041ea4d183`: bounded Windows Core Audio endpoint enumeration plus active shared-mode render sessions. Soltex inspects at most 128 session slots, exposes at most 24 path-free session rows, and keeps system-sounds, multi-process/transferred, ended, or process-unverifiable sessions read-only. Eligible volume/mute requests revalidate endpoint/session identities plus process ID/start time immediately before the write and require immediate Windows read-back before success. The default UI keeps app controls primary and complete device inventory behind explicit disclosure.

The accepted session-control gate passed Release/identity/design checks; standard Audio was 20/20, the opt-in task-owned silent-session live-write gate was 21/21, and hosted Windows/package artifacts re-hashed exactly. The current branch adds bounded direction-scoped endpoint fingerprints, preference schema 3 migration/recovery, explicit playback/recording fallback reminders, a fixed Windows Sound settings handoff, and a sixteenth native evidence state. Owner-host production source `1d15071a14472dce199796a57966bddbf7be2fc4` is accepted at Audio 21/21, App/control 29/29, 16/16 native states, exact runtime lifecycle, and self-contained package renders; the hosted gate for the new published head remains pending. Not implemented: direct Windows default assignment, automatic failover, app routing, virtual devices, EQ, DSP, noise suppression, microphone processing, profiles, or Sonar parity.

The current productization working tree adds one bounded, atomic mix snapshot.
Only sanitized application/endpoint labels plus volume and mute are retained;
raw Core Audio identities and process metadata are not. Apply re-observes live
sessions, admits exact unique matches only, reuses guarded read-back-backed
writes, and reports missing, ambiguous, rejected, canceled, or failed entries.
The strengthened canonical verifier passes App/control 31/31 and every repository
suite locally; commit-bound package and hosted acceptance remain pending.

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

The complete ordered implementation map, prerequisites, owner decisions,
agent-owned slices, exit gates, and nonclaims now lives in
[`REMAINING_WORK.md`](REMAINING_WORK.md). The immediate gate is owner review plus
keyboard, UI Automation, high-contrast, reduced-motion, scaling, and
representative-viewport evidence for the Quiet Instrument Deck candidate. The
next independent engineering slice is richer process detail and bounded local
history; release signing, benchmark limits, Privacy Tool source, device policy,
and connector accounts each require the named owner inputs before activation.

## Nonclaims

Soltex is not yet production-ready, a full AppControl replacement, a SteelSeries GG/Sonar replacement, an antivirus engine, a remote-management platform, or an unattended correction system.
