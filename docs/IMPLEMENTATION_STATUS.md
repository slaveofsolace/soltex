# Implementation status

Snapshot: 2026-08-05  
Product: **Soltex**  
Repository: `slaveofsolace/soltex`  
Current branch: `audit/soltex-v1-quality-pass`  
Current commit: `a2ac7718fd207364c9a74199585a9720ae53821f`  
Pull request: [#7](https://github.com/slaveofsolace/soltex/pull/7)

## Evidence vocabulary

- **Windows-verified:** compiled and exercised by the stated Windows gate.
- **Owner-host verified:** exercised on the owner's separately identified Windows machine.
- **Implemented, verification pending:** source exists but the complete current gate has not passed.
- **Designed:** architecture exists without a working end-to-end capability.
- **Not implemented:** no working capability exists.

A narrow pass proves only its named boundary.

## Windows-verified branch head

GitHub Actions run `31065075683` exercised the source branch head on Windows Server 2025 with .NET 10:

| Gate | Result |
|---|---:|
| Release build | 0 warnings, 0 errors |
| Security/Remote focused suite | 31/31 |
| Supply-chain suite | 18/18 |
| Security hardening suite | 12/12 |
| Update-planner suite | 17/17 |
| Device Fabric suite | 24/24 |
| Monitoring suite | 15/15 |
| WPF application suite | 7/7 |
| Hosted EICAR/AMSI | 31/32 |
| Native renders | Home, Monitoring, Devices, Security, Remote Assist, Updates passed |

Run `31065075715` independently published and launched the self-contained `win-x64` package and produced a native Home render.

## Implemented

### Desktop workspace

- shared warm dark/coral WPF theme;
- reusable cards, buttons, progress controls, sliders, focus states, and sparklines;
- Home, Monitoring, Devices, Security, Remote Assist, and Updates workspaces;
- native render-smoke selection for those six workspaces.

### Monitoring

- CPU utilization from `GetSystemTimes`;
- physical-memory status from `GlobalMemoryStatusEx`;
- fixed-volume capacity from `DriveInfo`;
- bounded process CPU/memory/thread observations;
- active-interface receive/send throughput from `NetworkInterface` statistics;
- bounded CPU, memory, and network histories;
- explicit provider provenance, partial, stale, and unavailable states;
- no executable-path, packet-payload, destination, or connection-history collection;
- sampling suspended when Home/Monitoring is hidden or the window is minimized.

GPU, clocks, temperatures, fans, and power remain unavailable until a supported provider is selected.

### Security companion

- provider-neutral Windows Security Center health with bounded fallback;
- Defender status and supported scan/intelligence-update requests when available;
- bounded, path-redacted Defender Operational events;
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
- package-smoke workflow that launches, renders, hashes, and records Authenticode status.

The CI package at this snapshot is unsigned.

## Not implemented or not claimed

- trusted signed public distribution;
- GitHub Release automation;
- true antivirus engine, minifilter, ELAM, PPL, MVI participation, cloud reputation, or efficacy claim;
- direct proprietary Malwarebytes integration;
- installer activation/repair/rollback/uninstall evidence;
- GPU/thermal/fan telemetry;
- complete App Control, Audio, Clips, Privacy, Drive, Box, NAS, or unified-search features;
- generic remote command execution;
- full high-contrast, reduced-motion, accessibility, or viewport/scaling conformance;
- production readiness.

## Exact next slice

Trusted distribution is the highest-priority release blocker. Select a production code-signing identity, define custody/revocation/timestamping, and add a signed installer/package verification lane. In parallel, the next low-privilege product feature should be read-only installed-application/startup inventory using documented Windows locations.
