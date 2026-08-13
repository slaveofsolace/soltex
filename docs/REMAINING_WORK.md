# Soltex remaining work

Snapshot: 2026-08-12

This is the ordered implementation map after the Quiet Instrument Deck rework.
“Built” means present and locally verified in the current branch; it does not
mean production-certified or owner-accepted.

## Current product truth

Built and working:

- native WPF shell and shared design system;
- live Home and bounded Performance telemetry;
- a short, cancelable local CPU/memory/storage benchmark with named measurements,
  strict scratch limits, one bounded saved result, and no synthetic score;
- virtualized installed-app, startup, and read-only service inventory;
- guarded process termination workflow;
- Core Audio endpoint/session observation plus per-app volume and mute with
  immediate read-back, one bounded recoverable mix snapshot, and supported
  Windows Sound handoff;
- provider-neutral Windows protection health, Defender scan orchestration,
  intelligence update request, redacted events, import guard, AMSI intake,
  authenticated quarantine, and security audit;
- local meaningful Activity timeline with explicit retention and deletion;
- consent-first, separately installed RustDesk launcher;
- authenticated non-installing update planner and recovery journal;
- local preferences, close-to-tray lifecycle, packaging definition, and test
  infrastructure.
- bounded searchable workspace commands through `Ctrl+K` and direct
  `Ctrl+1`-`Ctrl+9` routes.

Explicit previews or absent systems:

- Device Mesh has policy models and a local profile, but no enrollment or agent;
- Capture has no recorder or overlay;
- Updates has no production release identity/source or activation path;
- Soltex is a security companion, not an antivirus engine;
- there is no historical telemetry database, unified search, NAS/Drive/Box
  connector, Privacy Center, DSP/audio routing engine, generic
  remote executor, or unattended correction engine.

## Stage 0 — owner UI and accessibility gate

Dependency: current Quiet Instrument Deck candidate.

Agent work:

- run keyboard traversal/focus restoration and UI Automation checks;
- capture 100%, 125%, 150%, and 200% scaling plus 1366×768, 1440p, and 4K;
- inspect high contrast, reduced motion, busy, error, recovery, and confirmation
  states;
- fix any clipping or state ambiguity and retain exact native evidence.

Owner work:

- review representative Overview, Performance, Applications, Audio, Security,
  Remote Assist, Updates, and Settings captures;
- record `KEEP`, `REVISE`, or `REJECT`, with any page-specific notes.

Exit gate: exact-source native matrix, accessibility disposition, and explicit
owner visual decision. This is the next recommended slice.

## Stage 1 — release and installer program

Dependency: Stage 0 accepted.

Owner decisions:

- select code-signing and metadata-signing identities;
- define key custody, rotation, revocation, timestamp, and incident ownership;
- choose the authenticated release origin and update channel policy.

Agent work:

- bind production public pins/keys and descriptor fixtures;
- implement the smallest privileged activator around an exact confirmed plan and
  immutable verified handles;
- prove locked-file, disk-full, reboot, cancellation, repair, rollback, and
  deterministic uninstall behavior;
- sign and revalidate application/installer/package evidence.

Exit gate: signed release-candidate installation and recovery evidence. Until
then, Updates remains preview-only and the installer remains unsigned.

## Stage 2 — system manager and AppControl-class history

Dependency: retention/migration contract approved before persistent telemetry.

Implementation order:

1. sortable process detail with publisher/signature and supported disk/network
   telemetry;
2. guarded suspend, priority, and affinity controls with explicit permission and
   recovery behavior;
3. supported startup enable/disable and uninstall/update handoffs;
4. bounded local telemetry history with expiry, size cap, corruption recovery,
   deletion, and measured idle cost;
5. shared time-range correlation among resource, process, install, security, and
   Activity events, explicitly labeled as correlation rather than causation;
6. GPU/temperature/fan/power providers only through licensed supported APIs.

Exit gate: hostile tests, retention/deletion tests, idle/active measurements,
and no “optimizer” action that weakens Windows security or services.

## Stage 3 — benchmark lab

Built now: Quick local profile version 1 measures named CPU, memory, and 32 MiB
temporary-storage workloads in about four seconds. It pauses live sampling,
supports cancellation, removes scratch on cancel/failure/success, stores one
bounded path-free result, and discloses profile/version and environmental limits.

Remaining expansion depends on reliable thermal/power signals: optional longer
profiles, opt-in countdown and cooldown, thermal/power guardrails, GPU/render
workloads, richer background-load disclosure, repeatability studies, and export.
Do not publish a composite score or cross-machine ranking until comparability is
independently validated.

Owner decisions for expansion: allowed duration, temperature/power ceilings, and
whether GPU or render workloads are permitted.

## Stage 4 — Privacy Center

Dependency: import and review the actual Privacy Tool requirements/repository;
do not reconstruct them from a chat title.

Implementation order:

1. read-only Windows privacy permission and exposure inventory;
2. connector/account permission inventory;
3. publisher/startup/privacy history correlation;
4. reversible supported controls with before/after state and rollback;
5. separate personal and work profiles.

Owner work: provide/approve the canonical Privacy Tool source and decide which
Windows privacy changes Soltex may offer.

## Stage 5 — native media

### Audio

Current per-app volume/mute remains the safe base. Next: explicit app-to-endpoint
routing where supported, signal-flow view, presets, EQ, compressor/limiter, noise
processing, microphone monitoring, and stream/personal mixes. A virtual endpoint,
APO, or driver requires a separate signing, latency, coexistence, crash-isolation,
and uninstall program; do not infer SteelSeries Sonar parity from the current
mixer.

### Capture

Implement opt-in Windows Graphics Capture, D3D11 surfaces, Media Foundation
hardware encoding, bounded rolling segments, storage budgets, encoder/device-loss
recovery, hotkeys, and a no-activate/click-through overlay. No game injection.

Exit gate: sustained latency/CPU/GPU/memory/I/O evidence and isolation from
security, telemetry, and benchmark work.

## Stage 6 — Device Fabric and bounded unattended correction

Dependency: deterministic typed capabilities must work locally without AI.

Implementation order:

1. loopback-only signed job envelopes, expiry, nonce/replay defense, receipts,
   cancellation, and emergency stop;
2. device enrollment, mTLS identity, revocation, capability manifest, local
   authorization, and audit;
3. optional externally managed Tailscale reachability—tailnet membership is not
   Soltex authorization;
4. Windows agent, then a separately implemented macOS agent and Keychain custody;
5. registered-device RustDesk handoff for visible remote hands;
6. unattended correction only for predeclared, reversible runbooks with bounded
   inputs, simulation/preview, health checks, rollback, rate limits, local policy,
   emergency stop, and complete receipts;
7. AI/voice intent translation last; it chooses among authorized typed jobs and
   never produces a generic shell command or mints authority.

Owner decisions: devices, Tailscale administration, unattended-policy scope,
maintenance windows, emergency-stop owner, and which exact corrective runbooks
may execute without per-run confirmation.

Exit gate: hostile replay/identity tests, offline/partition recovery, compromise
revocation drill, rollback drill, and explicit owner acceptance. “Full
unsupervised control” remains forbidden as an unbounded generic executor.

## Stage 7 — NAS, search, Google Drive, and isolated Box

Dependency: Device Fabric identity and audit, plus connector-specific OAuth
threat models.

Implementation order:

1. local search and provenance badges;
2. bounded NAS search/storage/receipt transport—the NAS stores data, not
   permissions or device/OAuth keys;
3. personal Google Drive read-write: search, upload/download, folder creation,
   move, rename, trash/restore, Solace Inbox, and directional sync rules;
4. isolated work Box profile with separate credentials, index, UI identity,
   audit, policy, and no default transfer to personal Drive, NAS, or AI;
5. explicit source/destination/conflict previews and recoverable job receipts.

Owner decisions: OAuth accounts, NAS folders/quotas/backups, work-policy approval,
and every allowed cross-domain transfer.

## Stage 8 — true antivirus product program (optional, separate)

The current companion should continue to coexist with Defender/Malwarebytes. A
replacement antivirus requires a separate company-scale program: production
minifilter and service architecture, process/memory/exploit/web/ransomware/EDR
sensors, ELAM/PPL, Windows Security Center registration, signature/reputation and
sample systems, tamper resistance, MVI/Trusted Signing/WHQL/HLK, independent
certification, and measured detection/remediation/false-positive rates.

Do not begin kernel or provider-registration work by “pilfering” another product,
disabling Windows protection, or adding exclusions. Keep every protection claim
proportional to independently demonstrated evidence.

## Recommended split for the next handoff

The owner can take:

- Stage 0 visual verdict and page-specific preferences;
- signing/release-provider choices in Stage 1;
- benchmark limits in Stage 3;
- canonical Privacy Tool source and allowed settings in Stage 4;
- device/account/Tailscale/NAS/work-policy decisions in Stages 6–7.

The implementation agent can take next:

1. Stage 0 scaling/keyboard/accessibility evidence and corrections;
2. Stage 2 process-detail provider contract and UI slice while signing choices
   are pending;
3. Stage 3 benchmark expansion after the owner sets thermal and GPU safety limits;
4. later stages only when their named prerequisites and authority exist.

Every stage must retain an exact branch/commit, focused tests, failure/recovery
evidence, measurements, native captures, nonclaims, and one exact resume step.
