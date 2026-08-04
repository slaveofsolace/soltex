# Soltex continuation handoff

This is the only current continuation handoff for Soltex. Files under `docs/archive/prompts/` are historical inputs, not active instructions.

## Repository anchor

- Repository: `https://github.com/slaveofsolace/soltex`
- Protected owner checkout: `C:\Users\suhai\Documents\SOL Tools`
- Task-owned worktree: `C:\Users\suhai\Documents\soltex-immersive-workspace`
- Current branch: `feat/soltex-immersive-workspace-v1`
- Draft stacked PR: `https://github.com/slaveofsolace/soltex/pull/5`
- Stacked base branch: `refactor/soltex-identity-and-repo-coherence`
- Identity base head: `57607a35da14968c0d729795a857fd250b6566d9`
- Identity draft PR: `https://github.com/slaveofsolace/soltex/pull/4`
- Frozen immersive implementation checkpoint: `2a0699b2ca77b30fa636279b1d5ecab603a8bde9`

The protected owner checkout was re-observed at `main`, commit `bf2662de80992cfed761625642f084d3caaa0f04`, with extensive pre-existing legacy-named dirty and untracked work owned outside this task. Do not reset, clean, stash, merge, rebase, overwrite, or use it as a build-output target. Continue in the task-owned worktree and re-check branch, HEAD, status, remotes, PR base, and process ownership before changing anything.

## What is built

Soltex is a proprietary, unelevated .NET 10 WPF workspace with six production projects and seven focused executable test projects:

- `Soltex.App`: shared WPF shell, Home/Monitoring/Devices workspaces, existing Security/Remote Assist/Updates surfaces, and six-panel native render-smoke;
- `Soltex.Monitoring`: bounded Windows CPU, physical-memory, process, and fixed-volume observation plus immutable finite histories;
- `Soltex.Security`: supported Windows Security Center observation, bounded Defender requests, AMSI intake checks, authenticated allow-list/quarantine/audit state, and signed-manifest verification;
- `Soltex.RemoteAssist`: narrow visible shell-free adapter for a separately installed RustDesk client;
- `Soltex.Update`: signed descriptor/release verification and a non-installing update-preview planner;
- `Soltex.DeviceFabric`: immutable manifests/inventory, exact non-executing six-capability policy, and sanitized local observation that is explicitly `NotEnrolled`.

Home is the default page. Home, Monitoring, and Devices use one warm graphite/parchment/coral design system with shared cards, controls, focus, progress, table, scrollbar, and bounded sparkline primitives. One sequential sampler publishes immutable snapshots and copied 48/72-sample histories, cancels and awaits shutdown, preserves last confirmed values briefly as stale, then becomes unavailable rather than fabricating data. GPU and network telemetry remain explicitly unavailable.

Audio and Clips remain visible product foundations rather than working DSP/capture engines. There is no embedded RustDesk transport, generic command executor, enrolled device agent, public listener, cloud connector, installer, autonomous repair agent, antivirus engine, or production update activator.

## Clean-room and trust boundaries

- Zen Browser is `REFERENCE ONLY`: general calm-workspace, compact-navigation, focus, and balance principles were observed; no Zen source, asset, screenshot, layout measurement, icon, trademark, or design token was copied.
- RustDesk remains a separately installed AGPL-3.0 external program. No repository payload or runtime is linked, bundled, disguised, or imported into Soltex.
- Soltex never passes RustDesk passwords, enables unattended access, installs its service, requests elevation, hides the client, or treats a remote session as authorization for an AI job.
- Do not disable or weaken Defender, Malwarebytes, Windows Security Center, SmartScreen, firewall policy, or tamper protection. Do not add exclusions or register Soltex as an antivirus provider.
- Keep future remote jobs typed, capability-scoped, replay-resistant, and bound to a validated target manifest. There is no executor in the current Device Fabric.
- Keep personal Google Drive and work Box credentials, indices, audit streams, and transfer policies separate. Neither connector is implemented.
- Do not introduce a generic shell, arbitrary script runner, public ingress, hidden persistence, silent elevation, or destructive autonomous correction.

## Frozen verification

Exact implementation commit `2a0699b2ca77b30fa636279b1d5ecab603a8bde9` passed GitHub Actions run `30925606488`, job `92046999983`:

- identity policy passed;
- Release build passed in 41.59 seconds with 0 warnings and 0 errors;
- Security 31/31, supply chain 18/18, hardening 12/12, update 17/17, Device Fabric 24/24, monitoring 13/13, and WPF controls 5/5 passed;
- native Home, Monitoring, Devices, Security, Remote Assist, and Updates renders passed at 1044×788;
- render verification found all six PNGs and no render-error file;
- opt-in hosted EICAR was 31/32 because the installed AMSI provider returned native result `1`; this is an interoperability gap, not an independent antivirus result.

Artifact `soltex-windows-evidence-30925606488-1`, ID `8899014386`, is 626,093 bytes. GitHub and an independent downloaded-byte check agree on SHA-256 `71905016B3A67F8CE340D90C2E404DEBFB185C3DAE8A973F21B80F1B8A94515`.

A Human Eye current-capture review found no gross hierarchy, clipping, or identity blocker after the final visual corrections. It was not source-naive and does not grant owner visual acceptance, accessibility conformance, or broader viewport/scaling acceptance. The exact hashes, measurements, provenance, and nonclaims are in `docs/evidence/2026-08-04-immersive-workspace/` and `docs/VALIDATION.md`.

## Exact resume gate

From the task worktree:

```powershell
Set-Location 'C:\Users\suhai\Documents\soltex-immersive-workspace'
git branch --show-current
git rev-parse HEAD
git status --short --branch
git remote -v
Get-Process Soltex,dotnet -ErrorAction SilentlyContinue |
  Select-Object Id, ProcessName, Path, StartTime
```

Read `docs/IMPLEMENTATION_STATUS.md`, `docs/VALIDATION.md`, and the current evidence packet before mutation. Reuse the frozen artifact instead of repeating the full runtime gate unless code, build policy, or an assumption covered by that gate changes.

## Remaining ordered product stages

1. Owner visual review plus a representative viewport/scaling, keyboard, contrast, and screen-reader pass for the current UI.
2. Device Fabric Stage 2: loopback-only signed envelope, target/issuer binding, expiry, nonce, idempotency, policy version, replay store, cancellation, approval level, and redacted receipts. Keep it non-networked and non-executing until hostile parser/state tests pass.
3. Read-only Applications inventory and a separately authorized, bounded process-action model; no arbitrary shell or silent task termination.
4. Supported audio endpoint/session inventory before routing, EQ, microphone processing, or virtual-device claims.
5. Privacy adapters and local/NAS search with explicit roots, provenance, quotas, cancellation, and recovery.
6. Personal Google Drive read-write connector, then a separately governed work Box connector with explicit cross-domain transfer policy.
7. Optional Tailscale-local status and enrolled multi-device transport only after signed job/receipt semantics are proven.
8. Production signing identities, authenticated update source, installer, atomic activation, rollback, recovery, and uninstall as a separate release program.

Do not begin the next stage with provider registration, a driver, a service, public ingress, Defender mutations, unattended remote control, or automatic execution of staged content.
