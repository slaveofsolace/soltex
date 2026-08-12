# Soltex master project blueprint

Snapshot: 2026-08-12
Repository target: `slaveofsolace/soltex` (private)  
Current isolated source at snapshot: `C:\Users\suhai\Documents\soltex-immersive-workspace`

Product identity: **Soltex**. The solution, projects, assemblies, namespaces, current documentation, and handoff use that identity. Installed state remains compatible through the focused, fail-closed rules in `NAMING_AND_COMPATIBILITY.md`; historical evidence is not rewritten.

## 1. Product vision

Soltex is a proprietary personal-computing control surface: one immersive desktop workspace for security visibility, process and performance control, application management, audio, clips, privacy, remote assistance, device orchestration, unified search, and approved personal/work storage connectors.

The product should feel unified without becoming one privileged monolith. Each subsystem has a narrow contract, independent failure behavior, and an honest capability label. Native or privileged work is introduced only when the feature truly requires it.

## 2. Evidence vocabulary

Every status statement uses one of these meanings:

- **Windows-verified:** compiled and exercised on the documented Windows host with retained results.
- **Current source, verification pending:** implementation exists, but the latest source has not passed the complete current build/test/render gate.
- **Designed:** architecture and boundaries exist in documentation; no working feature is claimed.
- **Not implemented:** navigation, mock presentation, or an interface may exist, but the real pipeline does not.

Passing a narrow integration test proves only that integration. It does not prove production readiness, detection efficacy, visual acceptance, accessibility conformance, cross-device security, or service availability.

## 3. What is built

### 3.1 Windows-verified baseline

The current frozen implementation is commit `2a0699b2ca77b30fa636279b1d5ecab603a8bde9`. GitHub Actions run `30925606488` passed the Release build with zero warnings/errors, all seven focused suites, six native renders, and render verification. It includes:

- Windows Security Center aggregate antivirus health;
- Defender health and operating-mode details;
- quick/custom Defender scan and intelligence-update requests using fixed supported commands;
- AMSI scanning at Soltex's bounded content-intake boundary;
- SHA-256 assessment and exact-hash allow decisions;
- Authenticode and detached RSA-PSS/SHA-256 manifest verification;
- authenticated, recoverable quarantine;
- a bounded imports watcher;
- an HMAC-chained local audit log with hashed paths;
- bounded, path-redacted Defender Operational event parsing;
- prior performance measurements for WSC, Defender status/events, and a 4 KiB AMSI call;
- native WPF Home, Monitoring, Devices, Security, Remote Assist, and Updates renders;
- bounded CPU, physical-memory, process, and fixed-volume observation;
- copied finite telemetry histories and explicit current/partial/stale/unavailable/recovered state;
- a sanitized local-device profile that is explicitly `NotEnrolled`;
- one shared Quiet Instrument Deck WPF design system with graphite work surfaces,
  a glacier-blue interaction signal, bounded work areas, and direct controls.

Current source also includes:

- polling-wait cancellation that does not abandon channel readers;
- byte-bounded PowerShell stdout/stderr capture while streams are read;
- Windows Security Center precedence over Defender-specific fallback details;
- System32-only P/Invoke resolution for the security assembly;
- direct `$PSHOME` system-module manifest imports and module-qualified security cmdlets;
- provider and event-subscriber exception isolation so one nonfatal fault cannot stop monitoring;
- a PowerShell working directory anchored to its executable directory;
- audit-facing redaction of raw child-process stderr;
- Defender event queries that distinguish an empty log result from provider/access failure;
- enforcement of the requested event-count ceiling after JSON crosses the child boundary;
- three additional focused regressions for those later monitoring/process/event boundaries;
- `Soltex.RemoteAssist`, a narrow adapter for a separately installed RustDesk client;
- executable discovery/selection, reparse rejection, SHA-256 approval and revalidation;
- Authenticode checks at selection and immediately before launch;
- a constrained peer-ID type and fixed shell-free `--connect` launch plan;
- explicit local confirmation and peer-ID-free audit events;
- a Zen-informed, independently authored visual refresh and Remote Assist page;
- six-panel native render-smoke support.

Current evidence totals are Security 31/31, supply chain 18/18, hardening 12/12, update 17/17, Device Fabric 24/24, monitoring 13/13, and WPF controls 5/5. The optional in-memory EICAR run is 31/32 because the hosted AMSI provider returned native result `1`; owner-host interoperability remains open. Exact evidence is in `VALIDATION.md` and `evidence/2026-08-04-immersive-workspace/`.

## 4. Systems and principles being followed

### 4.1 Windows security cooperation

- Windows Security Center is the provider-neutral source of aggregate antivirus health.
- Microsoft Defender supplies details and supported operations when available.
- AMSI protects Soltex's own bounded content-intake boundary.
- Soltex never disables a provider, adds Defender exclusions, changes Malwarebytes registration, or claims to be a registered antivirus provider.
- Defender/Malwarebytes lessons are interoperability and UX lessons only: visible layer health, explicit scans, bounded history, recovery, quarantine, and deliberate exceptions.
- A true antivirus engine, minifilter, ELAM/PPL program, WSC registration, cloud reputation service, and measured detection organization remain a separate product program.

### 4.2 Clean-room compatibility

- Public behavior and first-party documentation may inform independently written requirements.
- Do not copy SteelSeries, NZXT, Zen Browser, AppControl, Malwarebytes, RustDesk, or other products' proprietary source, private protocols, assets, branding, signatures, or layouts.
- RustDesk is AGPL-3.0 and remains a separate installed program. Soltex's current boundary is an external process, not embedded or linked code.
- Similarity goals mean capability coverage and interaction quality, never a deceptive 1:1 visual clone.

The verified public-source comparison, rights disposition, derived AppControl requirements, and ordered clean-room implementation stages are maintained in `REFERENCE_SYSTEMS.md`. AppControl's proprietary desktop application remains `REFERENCE ONLY`; its separate MIT read-only MCP repository was reviewed as architecture metadata but was not cloned or imported.

### 4.3 Isolation and least authority

- Keep the WPF shell unelevated for ordinary use.
- Use typed interfaces and bounded queues between subsystems.
- Do not place security, indexing, or benchmark work on future audio/capture real-time threads.
- Add a service, driver, listener, or elevation broker only with a threat model, narrow protocol, signing/update plan, and direct evidence that the feature cannot be delivered safely without it.
- AI planning is advisory. It never mints authority or bypasses local confirmation.

### 4.4 Evidence-led delivery

- Build, focused tests, negative-path tests, native renders, measurements, and human review are distinct gates.
- Source inspection is not runtime proof.
- A generated screenshot is not native WPF evidence.
- A clean automated visual check is not owner acceptance.
- Every handoff records exact nonclaims and one exact resume step.

## 5. Conversation-derived product ideas

The owner's cross-device conversation resolves into these concrete decisions:

1. **RustDesk is remote hands, not the brain.** It is used when a person needs to see or control a screen. Automation goes through narrow Soltex device agents.
2. **Each computer has a small capability agent.** Voice/text intent becomes a typed, previewed, short-lived job for one target and one capability. There is no generic remote shell.
3. **Tailscale is the optional private mesh.** It provides separately managed private reachability, normally direct peer-to-peer with relay fallback where needed. Tailnet membership is not Soltex authorization, and Soltex does not open public ingress or weaken ACLs.
4. **The NAS holds data and evidence, not permissions.** It may store shared files, indexes, backups, and permitted redacted receipts. It does not hold device private keys, OAuth refresh tokens, approval policy, or the authoritative command queue.
5. **Unified search spans approved domains.** Local files, NAS files, notes, and Google Drive can appear in one search experience while provenance and account boundaries stay visible.
6. **Google Drive is personal and read-write.** Planned operations include search, upload, download, folder creation, move, rename, trash/restore, a `Solace Inbox`, and bounded directional sync rules.
7. **Box is an isolated work profile.** Credentials, indexes, audit, UI identity, and policy stay separate. Personal Drive/NAS/AI transfer is off by default and requires explicit cross-domain authorization consistent with employer policy.
8. **Per-application audio routing belongs in the same workspace.** It remains a distinct WASAPI/APO/driver product track rather than a cosmetic mixer.

The full device-fabric protocol, failure model, delivery sequence, and nonclaims live in `docs/PERSONAL_DEVICE_FABRIC.md`.

## 6. Product pillars

### 6.1 Home / command workspace

- actionable overview rather than a marketing dashboard;
- global command/search surface;
- active device, profile, privacy boundary, and connection state;
- recent meaningful events with recovery actions;
- no invented health score or false “all systems protected” summary.

### 6.2 Performance, Task Manager, and App Control

Clean-room behavior references: Windows Task Manager, NZXT CAM, and public AppControl workflows.

The differentiator to retain from AppControl is historical causality: a shared time range that correlates bounded resource samples with process, security, install, and privacy events while clearly separating temporal overlap from proven cause. The differentiator is not a 1:1 layout or a broad kill/disable feature.

Planned capabilities:

- CPU/GPU/RAM/network/storage utilization and history;
- temperatures, clocks, fans, and power only through licensed/supported providers;
- a dense top-process table with CPU, memory, GPU, disk, network, efficiency, publisher, and path trust context where supported;
- process details, search, sorting, suspend/end confirmation, priority/affinity controls with explicit permissions and recovery guidance;
- installed applications, startup entries, update/uninstall entry points, signatures, install source, disk footprint, and last-use data where Windows exposes it;
- allow/block policies only through supported Windows mechanisms and only after scope/rollback are explicit;
- no “optimizer” action that disables security, services, or scheduler behavior without a precise reversible contract.

### 6.3 Monitoring and benchmarks

- CAM-like information clarity without copying its visual identity;
- timeline views and process correlation rather than a wall of decorative gauges;
- bounded CPU, memory, disk, graphics, network, and optional render/encode benchmarks;
- user opt-in, countdown, cancellation, thermal/power guardrails, cooldown, and reproducible result metadata;
- no hidden stress testing or leaderboard claim from incomparable machines.

### 6.4 Security and Privacy Center

- retain the current supported-interface Security companion;
- integrate the future Privacy Tool work as a separate page/profile only after its actual repository/chat requirements are imported and reviewed;
- show Windows privacy permissions, startup exposure, trusted publishers, connector permissions, local audit integrity, quarantine, and account boundaries;
- keep remediation reversible and separate informational state from mutating actions;
- never present privacy toggles or Defender/Malwarebytes state that Soltex did not actually query.

### 6.5 Remote Assist and personal device fabric

- current consent-first RustDesk launcher;
- future registered device inventory and typed capability manifest;
- replay-resistant, expiring jobs with device-local authorization;
- macOS parity through a platform-specific agent and Keychain;
- optional Tailscale reachability;
- NAS/Drive/Box connectors with strict trust-domain boundaries;
- voice/AI intent translation only after each executable capability works deterministically without AI.

### 6.6 Audio workspace

Clean-room behavior reference: public SteelSeries Sonar workflows.

- Current implementation: bounded Core Audio endpoint/session observation,
  guarded per-app volume and mute, immediate Windows read-back, fallback
  reminders, and a user-mediated Windows Sound handoff.
- Still absent: direct default-endpoint assignment, per-application routing,
  virtual devices, profiles, EQ, DSP, noise processing, and microphone signal
  flow.
- per-application routing and endpoint selection;
- parametric EQ, presets, limiter/compressor/noise processing, microphone monitoring, and stream/personal mixes;
- clear signal flow and A/B/bypass states;
- WASAPI/MMDevice first; a signed virtual endpoint/APO/driver only when required;
- no claim of live routing or Sonar parity until those paths are implemented and
  measured.

### 6.7 Clips and overlay

- Windows Graphics Capture, D3D11, Media Foundation hardware encoding;
- bounded rolling segments, storage budget, recovery after encoder/device loss;
- separate no-activate/click-through overlay and hotkey handling;
- no injection into games or anti-cheat-sensitive behavior.

### 6.8 Unified search and storage

- one query surface with source/account badges and filters;
- local metadata index, bounded NAS connector, personal Drive read-write profile, later isolated Box work profile;
- explicit destination preview for moves/uploads and deterministic conflict handling;
- no hidden exfiltration to AI or cross-domain sync.

## 7. Visual direction: Quiet Instrument Deck

The implemented direction is a Swiss-industrial, Windows-native control surface:
calm at rest, precise under load, and direct before explanatory. It translates
general focus and progressive-disclosure lessons from Zen Browser, SteelSeries
Sonar, NZXT CAM, AppControl, and official Microsoft guidance without copying
their assets, copy, code, dimensions, or distinctive expression.

Defining moves:

- **Type:** Segoe UI Variable for product text and Cascadia Mono only for
  measurements, identifiers, paths, ports, and timestamps.
- **Palette:** graphite near-black surfaces with one glacier-blue interaction
  signal; green, amber, and red are reserved for semantic state.
- **Layout:** one 52 px-class command header, an optional local mode strip, then
  one bounded table, lane deck, chart, or master-detail work area. Dense lists
  scroll internally; primary default pages do not scroll as documents.
- **Geometry:** hairline borders, shallow surface depth, small radii, no hero
  gradients, glass, glow, purple selection pills, or equal-weight card carpets.
- **Signature detail:** a 2–3 px leading signal rail identifies the selected
  global workspace; the same grammar appears under selected local modes.
- **Copy:** title plus at most one sentence; default-view explanatory copy stays
  below 75 words outside data, labels, errors, and accessible names.
- **Controls:** real values use real controls. Audio volume uses sliders;
  booleans use toggles/buttons; read-only telemetry uses meters; sliders are not
  decorative.
- **Motion:** 120–200 ms functional transitions only, with the Windows reduced-
  motion preference respected.

The canonical component, token, page-anatomy, copy-budget, and clean-room rules
are in [`DESIGN_SYSTEM.md`](DESIGN_SYSTEM.md). The 2026-08-12 implementation
replaces page-level scrolling in the primary workspaces with bounded work areas
and has a complete 16-state 1280×820 native preview pass. Owner acceptance,
keyboard/screen-reader review, high contrast, reduced motion, multi-scale, and
representative small/large-window gates remain open.

## 8. Coding direction

- Keep .NET 10 WPF as the Windows shell for the current product track.
- Preserve `Soltex.Security` and `Soltex.RemoteAssist` boundaries.
- Introduce projects by capability, for example `Soltex.Monitoring`, `Soltex.AppControl`, `Soltex.Benchmarks`, `Soltex.Audio`, `Soltex.Clips`, `Soltex.Privacy`, `Soltex.DeviceFabric`, and connector-specific assemblies only when real code justifies them.
- Define small provider interfaces and typed immutable models before binding UI.
- Prefer supported Windows APIs: WSC, AMSI, Authenticode/WinTrust, DPAPI, Event Log, ETW/PDH/Performance Counters, WMI/CIM where appropriate, WASAPI/MMDevice, Windows Graphics Capture, D3D11, and Media Foundation.
- Treat every external executable, plugin, archive, update, and connector as an explicit trust boundary.
- Bound queues, payloads, output, retry, timeout, retention, and concurrency.
- Cancellation and recovery are part of every asynchronous contract.
- Do not add dependencies until their license, maintenance, privilege, binary provenance, and performance cost are documented.
- Keep mock/sample providers visibly labeled and impossible to confuse with live telemetry.
- Avoid a wholesale MVVM/framework rewrite merely for style; refactor incrementally around tested interfaces.

## 9. Delivery plan

### Gate 0 — establish the current truth

Completed for implementation commit `2a0699b`: Release build, identity policy, seven focused suites, six native renders, artifact verification, independent artifact re-hash, and current-capture evidence reconciliation. Hosted EICAR and owner visual acceptance remain explicitly separate open gates.

### Wave 1 — immersive shell and read-only observability

- **Completed bounded slice:** shared navigation/design tokens/component states, real Home/Monitoring/Devices surfaces, typed read-only CPU/memory/process/volume providers, bounded histories, sanitized local observation, and an exact non-executing capability model.
- **Still open in Wave 1:** command/search surface, broader responsive/scaling/accessibility evidence, and owner visual acceptance.
- No mutating process action, network listener, cloud OAuth, or stress benchmark was added.

### Wave 2 — controlled local actions

- process end/suspend/priority/affinity with confirmation, permission reporting, and audit;
- startup/application inventory and supported uninstall/update entry points;
- bounded local benchmark runner with cancellation and thermal guardrails;
- Privacy Center read-only inventory.

### Wave 3 — native media pillars

- WASAPI/MMDevice audio graph, then DSP and virtual-endpoint decision;
- WGC/D3D11/Media Foundation clips pipeline and overlay;
- performance isolation evidence between security, audio, capture, and benchmarks.

### Wave 4 — personal device fabric

- typed loopback job protocol and device-local policy;
- Windows/macOS agents;
- optional external Tailscale reachability;
- registered-device RustDesk handoff;
- NAS, then personal Drive, then isolated Box;
- voice/AI intent translation last.

## 10. Completion gates

For each wave retain:

- requirement-to-evidence matrix;
- threat model and license/provenance record;
- default, deny, timeout, cancellation, partial-failure, restart, and recovery tests;
- Release build output and static analyzer results;
- idle/active CPU, memory, I/O, network, and latency measurements appropriate to the feature;
- native UI captures and explicit human visual status;
- exact nonclaims and rollback/recovery instructions;
- a copy-ready handoff with branch, commit, dirty status, running-process ownership, evidence paths, and one exact resume step.

## 11. Immediate next action

Run the bounded owner UI gate at 100 and 150 percent scaling with one keyboard/high-contrast/screen-reader pass, then record `KEEP`, `REVISE`, or `REJECT`. If accepted, begin Device Fabric Stage 2 with a loopback-only signed envelope/replay/receipt model that remains non-networked and non-executing until hostile state/parser tests pass. Do not begin with OAuth, a generic remote command executor, a driver, public ingress, unattended control, or decorative dashboard expansion.
