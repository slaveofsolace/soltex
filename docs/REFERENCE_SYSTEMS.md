# Reference systems and clean-room product lessons

Snapshot: 2026-08-04

Disposition: **REFERENCE ONLY** unless a later, separately evidenced review says otherwise

This document converts public, first-party product descriptions into independently authored Soltex requirements. It is not a parity claim and is not permission to copy a product's source, assets, branding, measurements, private protocols, detection logic, driver behavior, or interface.

No proprietary installer was downloaded or executed for the original public-research pass. AppControl's public pages were sufficient to answer the current design questions, and its terms reserve the desktop application's proprietary rights. The separate AppControl MCP repository is MIT-licensed, but no code or binary from it was acquired because Soltex does not need that dependency for the current slice.

On 2026-08-09, the user separately authorized a bounded visual observation of their already-installed, already-running SteelSeries GG/Sonar window. That observation remains **REFERENCE ONLY**: no binary, asset, screenshot, preset, text, setting, private protocol, or product measurement entered the repository. Its clean-room record is [`evidence/2026-08-09-ui-evidence-matrix/RESOURCE_PROVENANCE.md`](evidence/2026-08-09-ui-evidence-matrix/RESOURCE_PROVENANCE.md).

## Reference matrix

| System | Public lesson retained | Soltex boundary | Planned application |
|---|---|---|---|
| [AppControl](https://www.appcontrol.com/) | Historical CPU/GPU/memory/disk/temperature timelines, process/event correlation, publisher/signature/elevation context, privacy-access events, alerts, range selection, and plain-language explanations | Desktop app is proprietary and **REFERENCE ONLY**. Do not download, decompile, mimic its layout, reuse insights, or infer that a process is safe from a description. Process-kill/disable rules require a separate threat model, confirmation, recovery, and protected-process policy. | Bounded local history, event timeline, read-only Applications inventory, then separately authorized process actions |
| [AppControl MCP](https://github.com/AppControlLabs/appcontrol-mcp-go/) | A local, optional, off-by-default, read-only query surface can make historical system evidence easier to inspect | The MIT MCP bridge and proprietary desktop are separate. Soltex may adopt the architectural principle, not vendor-specific schemas or code. Any future AI query layer remains read-only and consented; action planning is a different trust boundary. | Later local evidence-query adapter after durable monitoring storage exists |
| [Zen Browser](https://zen-browser.app/) | Calm chrome, compact navigation, workspaces, focus, and a deliberate balance among beauty, performance, and privacy | No Zen assets, source, screenshots, exact dimensions, trademarks, or layout reproduction | Existing shared Soltex shell and future contextual workspace rail |
| [NZXT CAM](https://nzxt.com/pages/cam) | At-a-glance CPU/GPU/memory/storage presentation, temperature history, and a compact overlay mode | Hardware controls and sensor availability are provider-specific. Never invent temperature, fan, clock, or GPU data and never copy CAM's dashboard | Monitoring detail, optional mini view, licensed/supported sensor-provider track |
| [SteelSeries Sonar](https://steelseries.com/en-gb/gg/sonar) | Clear game/chat/media/aux/mic lanes, repeated channel grammar, restrained identity accents, layered navigation, and progressive disclosure | Capability inspiration only. Use supported Windows audio-session and endpoint APIs; do not copy UI, dimensions, text, presets, DSP, virtual-device behavior, branding, or assets | Read-only endpoint/session inventory first; carry only the general hierarchy lessons into independently authored Soltex work |
| [RustDesk](https://github.com/rustdesk/rustdesk) | Visible remote hands, cross-platform screen/input/file capabilities, self-hosted rendezvous/relay option | AGPL-3.0 external client. Soltex does not embed, disguise, silently install, or treat it as the command brain. Unattended access is outside the current contract. | Existing consent-first external-client handoff; later bind a visible handoff to an enrolled device record |
| [Tailscale](https://tailscale.com/docs/concepts/wireguard) | Private mesh reachability built on WireGuard with NAT traversal, identity, and access-control services | Separately installed network dependency. Reachability or tailnet membership is not Soltex authorization. No automatic ACL weakening, public ingress, or fallback exposure. | Later local status and approved-target reachability after signed job semantics are proven |
| [Microsoft Defender](https://learn.microsoft.com/en-us/defender-endpoint/microsoft-defender-security-center-antivirus) and [Malwarebytes](https://help.malwarebytes.com/hc/en-us/article_attachments/49551606875547) | Provider coexistence, provider-neutral health, explicit scans, detection history, and visible protection state | Soltex remains a companion. It does not register as antivirus, suppress or replace providers, add exclusions, copy signatures/models, or claim detection efficacy. | Existing Windows Security Center/Defender/AMSI cooperation and factual interoperability reporting |
| [Google Drive](https://developers.google.com/workspace/drive/api/guides/about-sdk) | OAuth-backed search, upload/download, file operations, shared-drive provenance, app-data folders, and change events | Personal connector with least implemented scope, OS secret storage, explicit account/destination, deterministic conflicts, and no hidden AI or NAS export | Personal read-write connector after local/NAS search foundations |
| [Box](https://developer.box.com/guides/api-calls/permissions-and-errors/scopes) | OAuth scopes and user permissions jointly constrain every action; read-only/read-write and downscoped access are distinct | Isolated work trust domain with separate credentials, index, audit, UI identity, and employer policy. No default Box-to-personal/NAS/AI transfer. | Work connector only after explicit enterprise and cross-domain policy review |

## AppControl-derived requirements

The useful product idea is historical causality, not a cosmetic Task Manager clone.

1. Retain bounded, timestamped resource samples with explicit retention, storage cost, clock provenance, schema version, and gap markers.
2. Correlate a selected time range with bounded process and system events without claiming causation when only temporal overlap is known.
3. Make process identity inspectable: sanitized name, PID/start identity, publisher/signature state, elevation state, first/last observed time, and exact provider provenance where Windows supports them.
4. Distinguish new, changed, unsigned, unusual, and privacy-access events. "Unusual" must have an explainable local rule; it must not be a vague AI safety verdict.
5. Keep descriptions informational and visibly fallible. A description never authorizes kill, quarantine, blocking, deletion, or publisher-wide policy.
6. Keep all process actions out of the monitoring provider. Each action needs explicit target revalidation, permission reporting, confirmation, timeout, audit, protected-process safeguards, and recovery guidance.
7. If Soltex later exposes system history to an AI client, make it local, read-only, off by default, payload-bounded, path/redaction aware, and consented per data class. It must not expose action tools through the same interface.

## Visual and interaction direction

The product should feel immersive because context persists while tools change, not because every panel glows.

- Use the existing quiet graphite/parchment/coral system and compact workspace rail.
- Add a shared time horizon so Monitoring, Applications, Security, and Privacy can inspect the same selected interval.
- Prefer one strong graph plus a correlated event/process ledger over a carpet of gauges.
- Allow dense tables to use the canvas edge-to-edge; keep details in a contextual inspector.
- Preserve explicit waiting, partial, stale, unavailable, recovered, permission-required, and confirmation states.
- Add compact/overlay modes only after keyboard access, scaling, provenance, and data legibility are demonstrated.

## Ordered implementation stages

1. **Durable local history:** bounded append-only sample/event storage, retention, migration, corruption recovery, redaction, and measured idle/storage cost.
2. **Read-only Applications:** installed/startup/running inventory with supported publisher, signature, version, source, and permission provenance.
3. **Correlated event timeline:** process start/stop and supported security/privacy events, with explicit gaps and non-causality wording.
4. **Controlled local actions:** end/suspend/priority/affinity behind target revalidation, protected-process rules, consent, audit, and recovery. No publisher-wide blocking in the first action slice.
5. **Benchmark area:** opt-in, cancelable, thermally guarded, reproducible bounded workloads with cooldown and comparable metadata.
6. **Audio foundation:** supported Windows endpoint/session inventory and per-session volume/mute before routing, EQ, processing, or virtual-device claims. Microsoft's [Core Audio APIs](https://learn.microsoft.com/en-us/windows/win32/coreaudio/about-the-windows-core-audio-apis) are the implementation reference.
7. **Device and connector expansion:** signed/replay-resistant local jobs before mesh transport; NAS and personal Drive before the separately governed Box work profile.

## Explicit nonclaims

This research does not demonstrate AppControl parity, multi-day history, GPU/network/temperature monitoring, privacy-access attribution, process-action safety, audio routing, remote execution, Tailscale integration, Drive/Box access, benchmark comparability, antivirus efficacy, accessibility conformance, or owner visual acceptance. Those remain staged product work with independent evidence gates.
