# Implementation status

Snapshot: 2026-08-09
Product: **Soltex**
Repository: `slaveofsolace/soltex`
Merged baseline: `main` at `61639bfe7ea3a7191fcb6c70ea1f2a699aa77248` (PR #9)
Continuation branch: `sol/soltex-ui-evidence-matrix`
Evidence: [`docs/evidence/2026-08-09-ui-focus`](evidence/2026-08-09-ui-focus/README.md) and [`docs/evidence/2026-08-09-ui-evidence-matrix`](evidence/2026-08-09-ui-evidence-matrix/README.md)

## Evidence vocabulary

- **Windows-verified:** compiled and exercised by the stated Windows gate.
- **Owner-host verified:** exercised on the owner's separately identified Windows machine.
- **Implemented, verification pending:** source exists but the complete current gate has not passed.
- **Designed:** architecture exists without a working end-to-end capability.
- **Not implemented:** no working capability exists.

A narrow pass proves only its named boundary.

## Owner-host evidence

The UI-focus candidate merged through PR #9. The evidence-matrix continuation was then built and exercised locally on the owner-controlled Windows host with .NET SDK 10.0.302. GitHub CI remains a separate required gate on the exact published continuation head.

| Gate | Result |
|---|---:|
| Release build | 0 warnings, 0 errors |
| Security/Remote focused suite | 32/32, including opt-in in-memory AMSI/EICAR |
| Supply-chain suite | 18/18 |
| Security hardening suite | 12/12 |
| Update-planner suite | 17/17 |
| Device Fabric suite | 24/24 |
| Monitoring suite | 15/15 |
| Core Audio suite | 10/10 |
| WPF application suite | 10/10 |
| Identity/design guards | Passed under Windows PowerShell 5.1 |
| Native renders | Eight defaults and three expanded states at 1280x820 |
| Evidence fail-closed paths | Stale targets and mismatched tested commits rejected before capture |

The complete local evidence boundary and nonclaims are recorded in the linked packets. CI/package evidence from earlier stack commits remains historical; it does not substitute for a run on the published continuation head.

## Implemented

### Desktop workspace

- shared warm dark/coral WPF theme;
- reusable cards, buttons, progress controls, sliders, focus states, and sparklines;
- Home, Monitoring, Devices, Mixer, Clips, Security, Remote Assist, and Updates workspaces;
- native render-smoke selection for all eight defaults plus Monitoring, Mixer, and Security disclosure states;
- work-area-independent 1280x820 popup render-smoke path with a constrained-viewport regression;
- workspace-column clipping and explicit navigation-label ownership so local content cannot corrupt global navigation evidence;
- quiet default hierarchy with secondary operational detail behind explicit controls.

### Monitoring

- CPU utilization from `GetSystemTimes`;
- physical-memory status from `GlobalMemoryStatusEx`;
- fixed-volume capacity from `DriveInfo`;
- bounded process CPU/memory/thread observations;
- active-interface receive/send throughput from `NetworkInterface` statistics;
- bounded CPU, memory, and network histories;
- fixed volumes, provider coverage, and process tables disclosed only on request;
- explicit provider provenance, partial, stale, and unavailable states;
- no executable-path, packet-payload, destination, or connection-history collection;
- sampling suspended when Home/Monitoring is hidden or the window is minimized.

GPU, clocks, temperatures, fans, and power remain unavailable until a supported provider is selected.

### Audio observation

- read-only Windows Core Audio endpoint enumeration;
- render/capture classification, state, default assignment, current volume, and mute observation;
- bounded primary lists of at most six active endpoints per direction;
- overflow active and inactive endpoints behind one explicit disclosure control;
- inaccessible/unavailable states clear stale endpoint values;
- live capture overhead and sanitized-name tests.

Soltex does not set volume, route signal, create virtual devices, equalize, suppress noise, or replace SteelSeries Sonar.

### Security companion

- provider-neutral Windows Security Center health with bounded fallback;
- Defender status and supported scan/intelligence-update requests when available;
- bounded, path-redacted Defender Operational events;
- Defender Operational activity collapsed by default and cleared on query failure;
- AMSI inspection for content Soltex ingests;
- hashing, exact-hash allow decisions, Authenticode verification;
- authenticated quarantine and local audit state;
- bounded import-folder observation.

Soltex is not registered as an antivirus provider and does not disable or replace one.

### Supply-chain and update planning

- immutable publisher snapshot verification;
- explicit publisher pin policy;
- detached signed manifests;
- authenticated current/last-known-good state;
- monotonic sequence and equivocation checks;
- bounded ZIP central-directory preflight and staging;
- signed acquisition descriptors and trust rotation;
- HTTPS origin/redirect/length/hash bounds;
- deterministic non-installing preview and exact confirmation;
- authenticated planning/recovery journal.

No production release identity, trust root, source, installer activation, rollback, or uninstall transaction is configured.

### Device Fabric

- typed local device observation;
- capability catalog and target-manifest enforcement;
- enrollment/revocation/policy models and focused tests.

No remote executor, generic shell, public listener, unattended access, or Tailscale authorization integration exists.

### Remote Assist

- separately installed RustDesk selection;
- SHA-256 and Authenticode revalidation;
- constrained peer IDs and shell-free launch;
- local confirmation and path/peer-ID-free audit summaries.

RustDesk remains an external program.

### Packaging

- self-contained `win-x64` executable;
- per-user Inno Setup definition;
- package-smoke workflow that launches, renders, hashes, and records Authenticode status;
- schema-versioned package identity that distinguishes the source branch head from the commit actually tested;
- package identity that binds the retained Home render by dimensions, length, and SHA-256;
- Windows evidence workflow covering all eight default workspaces and three progressive-disclosure states with dimensions and per-file SHA-256 provenance.

The CI package at this snapshot is unsigned.

## Not implemented or not claimed

- trusted signed public distribution;
- GitHub Release automation;
- true antivirus engine, minifilter, ELAM, PPL, MVI participation, cloud reputation, or efficacy claim;
- direct proprietary Malwarebytes integration;
- installer activation/repair/rollback/uninstall evidence;
- GPU/thermal/fan telemetry;
- complete App Control, Clips capture, Privacy, Drive, Box, NAS, or unified-search features;
- audio routing, virtual devices, DSP, equalization, noise suppression, or per-app session control;
- generic remote command execution;
- full high-contrast, reduced-motion, accessibility, or viewport/scaling conformance;
- production readiness.

## Exact next slice

Publish the evidence-matrix continuation, require Windows and package-smoke CI on its exact head, inspect both manifests, and merge it to `main` with history preserved. After that verification-only slice lands, the next product slice should add bounded historical monitoring storage and a read-only Applications inventory; trusted signed distribution remains the highest-priority release blocker.
