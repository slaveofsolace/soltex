# Soltex

Soltex is a proprietary personal-computing workspace. **Soltex** is the canonical product, repository, solution, assembly, and namespace identity. A small, tested compatibility boundary preserves existing authenticated local state and historical evidence without presenting the retired working name as current product copy.

The current desktop solution exposes nine normal-navigation workspaces plus two
explicit preview surfaces used by the native evidence matrix:

- **Home** — a real local summary with explicit partial/unavailable signals.
- **Monitoring** — bounded CPU, physical-memory, process, and fixed-volume observation from supported Windows interfaces.
- **Devices (preview)** — a sanitized local profile and exact non-executing capability model, explicitly not enrolled.
- **Applications** — bounded, searchable, read-only installed-software, sign-in, and Win32 service inventories.
- **Audio** — bounded Windows Core Audio endpoint and active app-session state, guarded per-session volume/mute with immediate read-back, one recoverable local mix snapshot, local fallback reminders, and a user-mediated Windows Sound handoff.
- **Security** — the implemented focus: a lightweight companion that cooperates with the antivirus provider registered with Windows, plus bounded local supply-chain verification primitives.
- **Remote Assist** — a consent-first launcher for a separately installed, Windows-trusted RustDesk client; Soltex does not own or embed the remote-session transport.
- **Activity** — a bounded local timeline of meaningful Soltex actions and recovery transitions, session-only unless the user explicitly selects retention.
- **Updates** — a non-installing signed planner that authenticates bounded release artifacts and stops at an exact human-readable preview.
- **Settings** — working local preferences for telemetry cadence, workspace restoration, detail disclosure, Activity retention, and explicit close behavior.
- **Capture (preview)** — a visible non-installed boundary for a future opt-in Windows Graphics Capture pipeline.

This is a clean-room product. It is not affiliated with, endorsed by, or derived from SteelSeries, Malwarebytes, Zen Browser, AppControl, NZXT, or RustDesk. It contains no copied binaries, signatures, detection models, private protocols, branding, or UI assets from those products. RustDesk remains a separately licensed external program.

## What works now

Home is the default WPF surface. The workspaces share the **Quiet Instrument
Deck** design system: graphite work surfaces, a glacier-blue interaction signal,
small-radius hairline geometry, compact command headers, local mode tabs, bounded
work areas, and direct controls before commentary. Primary default pages do not
own whole-page scroll viewers at the 1280×820 reference viewport; the Audio
device-detail list scrolls inside its own work area. One sequential sampler
publishes immutable snapshots and copied 48/72-sample histories, surfaces
provenance and unsupported signals, retains last confirmed values briefly as
stale, and cancels on shutdown. GPU telemetry is not implemented and remains
visibly unavailable.

`Ctrl+K` opens a bounded, searchable workspace switcher without adding another
persistent navigation tier. `Ctrl+1` through `Ctrl+9` route to the same nine
working workspaces, and Escape returns focus to the prior control.

Activity records only bounded Soltex-owned events such as an explicit process action, scan request/result, quarantine change, Remote Assist launch result, or a telemetry failure/recovery transition. It does not record clicks, browsing, packet contents, command lines, or executable/file paths. The default is memory-only for the current session; 7-day and 30-day local retention require an explicit Settings choice, shortening retention requires confirmation, and Clear Activity requires confirmation before removing visible and saved history. See [`docs/ACTIVITY.md`](docs/ACTIVITY.md).

Audio observes at most 128 active-render session slots and exposes at most 24 active shared-mode sessions. System-sounds, multi-process/transferred, and process-unverifiable sessions stay read-only. A volume or mute request revalidates the endpoint, session instance, process ID, and process start time, then reports success only after immediate Windows read-back. Raw endpoint/session identifiers, executable paths, command lines, and icon paths are never exposed or persisted. See [`docs/AUDIO.md`](docs/AUDIO.md).

The optional Audio mix snapshot stores at most 64 normalized app/endpoint label
pairs with volume and mute state in a 64 KiB atomic local document. Applying it
re-observes live sessions, skips missing or ambiguous pairs, reuses the guarded
Core Audio writes, and reports verified, partial, canceled, or failed outcomes.

Applications can also observe bounded Win32 service state and start mode through query-only Service Control Manager access. Drivers and binary paths are excluded, and no service action is exposed. Closing Soltex exits by default; keeping the desktop process in the notification area is an explicit, reversible preference that suspends Performance sampling while hidden. Soltex installs no background service. See [`docs/RUNTIME_AND_SERVICES.md`](docs/RUNTIME_AND_SERVICES.md).

The .NET 10 WPF solution builds and executes on Windows. The Security companion currently provides:

- provider-neutral antivirus health through Windows Security Center;
- Windows Security Center change notification with bounded polling fallback;
- detailed Microsoft Defender status when Defender is available;
- Defender operating-mode visibility and a bounded, path-redacted Operational event timeline;
- supported Defender quick, file, and folder scan requests;
- security-intelligence update requests;
- AMSI inspection before Soltex consumes bounded untrusted content;
- SHA-256 assessment and exact-hash allow decisions;
- Authenticode verification through `WinVerifyTrust`;
- authenticated, recoverable quarantine with restore and delete;
- a bounded watcher limited to Soltex's import folder;
- privacy-preserving, HMAC-chained local audit events.

The current supply-chain foundation adds:

- an explicit publisher policy that combines Windows Authenticode trust, a single requested embedded signature, zero secondary signatures, code-signing EKU, exact subject-name SHA-256, and SubjectPublicKeyInfo SHA-256;
- strict schema-2 `Soltex` release manifests signed with detached RSA-PSS/SHA-256 signatures;
- canonical Windows-relative manifest paths, explicit UTC publication time, length and SHA-256 validation, and reparse-point rejection;
- authenticated per-user release-sequence state that rejects lower sequences and same-sequence/different-manifest equivocation;
- a bounded, cancellation-aware cross-process lock around release-sequence reads and writes;
- bounded ZIP staging that rejects traversal, Windows device names, alternate-data-stream syntax, case collisions, reparse/symbolic links, unsupported entry types, excessive entry counts, expanded sizes, and compression ratios;
- private staging roots with failure, cancellation, and owner-disposal cleanup;
- strict signed acquisition descriptors, metadata-signature quorum, signed trust-policy rotation, TLS SPKI pins, authorized redirect origins, bounded HTTPS acquisition, and exact byte/hash verification;
- a composed non-installing planner that verifies manifest, archive, executable publisher, file-change, disk-impact, warning, recovery, deterministic plan-hash, and exact expiring confirmation evidence;
- an authenticated current/last-known-good planning journal plus a real Soltex Updates page that displays bounded sanitized state and fails closed.

This is not an installer or release service. No production Soltex signing identity, TLS pin, or signed descriptor source is configured. The planner can authenticate, download, stage, and preview inert bytes, but it cannot execute, install, elevate, activate, repair, roll back, or uninstall them.

The current Remote Assist source provides explicit RustDesk executable selection, Authenticode and SHA-256 revalidation, fixed shell-free launch arguments, constrained peer IDs, local confirmation, and peer-ID-free auditing. It does not bundle RustDesk, store passwords, enable unattended access, request elevation, install a service, or hide the external client.

System-wide real-time scanning, behavior monitoring, cloud intelligence, and remediation come from Microsoft Defender or another antivirus provider registered with Windows. Soltex does not disable or replace that provider.

## Install

`Soltex-1.0.0-win-x64-setup.exe` installs the workspace for the current user only. It writes to `%LocalAppData%\Programs\Soltex`, adds Start Menu entries and an Add/Remove Programs record, and never requests administrator rights — the application manifest requests `asInvoker` and the workspace is designed to run unelevated.

Requirements: Windows 10 version 2004 (build 19041) or newer, x64. The .NET runtime is bundled, so no separate runtime install is needed.

The installer is **not code-signed**. No production Soltex code-signing identity has been selected, so Windows SmartScreen will warn on first run and the publisher will show as unknown. Verify the download against its published SHA-256 before running it. Code signing is tracked with the rest of the release program in [`docs/VALIDATION.md`](docs/VALIDATION.md).

Uninstalling removes the installed program files and shortcuts. Local Soltex state — preferences, optional Activity history, quarantine, the HMAC-chained security audit log, release-sequence state, and planning journals — lives under your user profile and is deliberately left in place so an accidental uninstall cannot destroy authenticated security history. Activity can be cleared from its own confirmed control before uninstalling; the remaining security/update state requires a separate deliberate cleanup procedure.

To build the installer from source, publish the app and compile the script with [Inno Setup](https://jrsoftware.org/isinfo.php) 6:

```powershell
.\eng\publish-release.ps1
```

## Build and verify

Requirements:

- Windows 10 version 2004 or newer, or Windows 11;
- .NET 10 SDK;
- PowerShell 5.1 and the Defender module for Defender-specific controls.

```powershell
dotnet build .\Soltex.sln --configuration Release

dotnet run `
  --project .\tests\Soltex.Security.Tests\Soltex.Security.Tests.csproj `
  --configuration Release `
  --no-build

dotnet run `
  --project .\tests\Soltex.Security.SupplyChain.Tests\Soltex.Security.SupplyChain.Tests.csproj `
  --configuration Release `
  --no-build

dotnet run `
  --project .\tests\Soltex.Security.Hardening.Tests\Soltex.Security.Hardening.Tests.csproj `
  --configuration Release `
  --no-build

dotnet run `
  --project .\tests\Soltex.Update.Tests\Soltex.Update.Tests.csproj `
  --configuration Release `
  --no-build

dotnet run `
  --project .\tests\Soltex.DeviceFabric.Tests\Soltex.DeviceFabric.Tests.csproj `
  --configuration Release `
  --no-build

dotnet run `
  --project .\tests\Soltex.Monitoring.Tests\Soltex.Monitoring.Tests.csproj `
  --configuration Release `
  --no-build

dotnet run `
  --project .\tests\Soltex.Audio.Tests\Soltex.Audio.Tests.csproj `
  --configuration Release `
  --no-build

dotnet run `
  --project .\tests\Soltex.App.Tests\Soltex.App.Tests.csproj `
  --configuration Release `
  --no-build
```

The safe EICAR interoperability check is opt-in and submits the harmless marker to AMSI in memory only:

```powershell
.\eng\verify.ps1 -RunEicar
```

Run the desktop app:

```powershell
dotnet run --project .\src\Soltex.App\Soltex.App.csproj --configuration Release
```

## Evidence

The immersive-workspace implementation at exact branch commit `2a0699b2ca77b30fa636279b1d5ecab603a8bde9` was exercised by GitHub Actions run `30925606488`, job `92046999983`, on a hosted Windows runner:

- Release build: **passed** in 41.59 seconds, with 0 warnings and 0 errors across all 13 projects;
- identity policy: **passed**;
- Security companion suite: **31/31 passed**;
- supply-chain suite: **18/18 passed**;
- hostile hardening suite: **12/12 passed**;
- update-planner suite: **17/17 passed** in 2,895.6 ms on that runner;
- Device Fabric suite: **24/24 passed** in 47.7 ms;
- monitoring suite: **13/13 passed** in 765.9 ms, including a representative 166.6 ms provider / 167.1 ms wall-clock bounded capture;
- WPF control/render suite: **5/5 passed** in 1,704.3 ms;
- opt-in hosted EICAR suite: **31/32** because the installed hosted-runner AMSI provider returned native result `1` for the in-memory marker;
- native Home, Monitoring, Devices, Security, Remote Assist, and Updates renders: **passed** at 1044×788;
- render artifact verification: **passed**, with no render-error files.

The retained Actions artifact is `soltex-windows-evidence-30925606488-1` (artifact ID `8899014386`, 626,093-byte ZIP, SHA-256 `71905016B3A67F8CE340D90C2E404DEBFB185C3DAE8A973F21B80F1B8A94515`). Its downloaded bytes were independently re-hashed to the same digest. The hosted server did not expose a usable live `wscapi.dll` provider boundary, so this run proves bounded fallback behavior rather than successful live provider enumeration. A Human Eye cold-eye review of the six frozen captures found no gross clipping, hierarchy, or identity blocker after the final correction; this remains agent inspection, not owner visual acceptance. Broader viewport/scaling, keyboard/screen-reader, accessibility, and owner-review matrices remain pending.

Documentation:

- Current continuation handoff: [`HANDOFF.md`](HANDOFF.md).
- Documentation map: [`docs/INDEX.md`](docs/INDEX.md).
- Current status and nonclaims: [`docs/IMPLEMENTATION_STATUS.md`](docs/IMPLEMENTATION_STATUS.md).
- Activity privacy, retention, recovery, and deletion contract: [`docs/ACTIVITY.md`](docs/ACTIVITY.md).
- Core Audio session observation, write admission, read-back, and nonclaims: [`docs/AUDIO.md`](docs/AUDIO.md).
- Runtime lifecycle, service-inventory bounds, and measured-cost contract: [`docs/RUNTIME_AND_SERVICES.md`](docs/RUNTIME_AND_SERVICES.md).
- Immersive workspace behavior and visual/data boundaries: [`docs/IMMERSIVE_WORKSPACE.md`](docs/IMMERSIVE_WORKSPACE.md).
- Exact commands and evidence ledger: [`docs/VALIDATION.md`](docs/VALIDATION.md).
- Frozen Wave B evidence and decision ledger: [`docs/evidence/2026-08-04-immersive-workspace/`](docs/evidence/2026-08-04-immersive-workspace/).
- Publisher, signed-release, anti-rollback, and archive boundaries: [`docs/SUPPLY_CHAIN_SECURITY.md`](docs/SUPPLY_CHAIN_SECURITY.md).
- Security architecture: [`docs/SECURITY_ENGINEERING_HANDOFF.md`](docs/SECURITY_ENGINEERING_HANDOFF.md).
- Threat model: [`docs/THREAT_MODEL.md`](docs/THREAT_MODEL.md).
- Remote Assist license and trust boundary: [`docs/REMOTE_ASSIST.md`](docs/REMOTE_ASSIST.md).
- Implemented Device Fabric policy foundation plus proposed NAS, Drive, and isolated Box boundaries: [`docs/PERSONAL_DEVICE_FABRIC.md`](docs/PERSONAL_DEVICE_FABRIC.md).
- Complete built/current/planned systems and visual/coding roadmap: [`docs/MASTER_PROJECT_BLUEPRINT.md`](docs/MASTER_PROJECT_BLUEPRINT.md).
- Product identity and installed-state compatibility contract: [`docs/NAMING_AND_COMPATIBILITY.md`](docs/NAMING_AND_COMPATIBILITY.md).

## Important boundary

Soltex Security is not a registered third-party antivirus product. It has no production detection organization, cloud reputation service, Windows Security Center provider registration, file-system minifilter, protected anti-malware service, ELAM component, MVI participation, independent efficacy certification, or supported detection-rate claim.

The supply-chain and update-planning code does not constitute a production signed release system. Production certificate/key procurement and custody, real public pins and signed trust-policy rollout, timestamping/revocation policy, a production descriptor source, signed installer selection, transactional activation, rollback/recovery, uninstall, and retained release-candidate evidence remain separate work.

Copyright © 2026. All rights reserved. See [`LICENSE.txt`](LICENSE.txt).
