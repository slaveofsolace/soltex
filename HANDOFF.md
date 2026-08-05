# Soltex continuation handoff

This is the only current continuation handoff for Soltex. Files under `docs/archive/prompts/` are historical inputs, not active instructions.

## Repository anchor

- Repository: `https://github.com/slaveofsolace/soltex`
- Current branch: `main`
- Integration merge commit: `ac7eef304b956012ba21929d8b7afba9c9b9dd1c`
- Protected owner checkout: `C:\Users\suhai\Documents\SOL Tools`
- Release build checkout: `C:\Users\suhai\Documents\soltex-release-build`

The V1 stack has landed. PR #6 merged `release/soltex-v1-foundation` into `main` as a merge commit, so every evidence-linked ancestor tip stays reachable:

| Source | Tip | Reachable from `main` |
|---|---|---|
| PR #2 supply-chain hardening | `39eaf628f6add6c89963a81b7c1971c2f74f02a1` | yes |
| PR #3 update planner | `40c7e73452dc6c11fcd1f5711ec6360210595a44` | yes |
| PR #4 identity coherence | `57607a35da14968c0d729795a857fd250b6566d9` | yes |
| PR #5 immersive workspace | `767a64abd7e1a9c0a3c73bbc8d2b4cb510539a20` | yes |
| Frozen implementation checkpoint | `2a0699b2ca77b30fa636279b1d5ecab603a8bde9` | yes |

PRs #2 through #6 are closed. The stacked branches remain on the remote as recovery references and can be deleted once their commits are no longer needed for evidence lookup; nothing is lost when they go, because all five tips are ancestors of `main`.

The protected owner checkout was last observed at `main`, commit `bf2662de80992cfed761625642f084d3caaa0f04`, with extensive pre-existing legacy-named dirty and untracked work owned outside this task. Do not reset, clean, stash, merge, rebase, overwrite, or use it as a build-output target. Re-check branch, HEAD, status, remotes, and process ownership before changing anything.

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

- AppControl desktop is `REFERENCE ONLY`: its public history, event, process-context, privacy, alert, and range-selection behaviors inform independent requirements. The proprietary installer was not downloaded or executed, and no 1:1 UI, binary, insight, asset, protocol, or implementation was copied. See `docs/REFERENCE_SYSTEMS.md` and `docs/evidence/2026-08-04-reference-systems/`.
- AppControl's separate MCP repository is public MIT metadata for a local read-only query boundary; it was not cloned or imported. Any Soltex AI evidence interface stays off by default, read-only, consented, bounded, and separate from action authority.
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

## Owner-host verification

Integration head `767a64abd7e1a9c0a3c73bbc8d2b4cb510539a20` was exercised on the owner Windows host (Windows 10.0.26200, .NET SDK 10.0.302) before the merge:

- Release build: 0 warnings, 0 errors;
- Security 31/31, supply chain 18/18, hardening 12/12, update 17/17, Device Fabric 24/24, monitoring 13/13, WPF controls 5/5;
- opt-in EICAR: **32/32** — the owner host's registered AMSI provider blocked the in-memory marker, closing the hosted-runner interoperability gap that had capped that lane at 31/32;
- all six native renders produced PNGs with no render-error file.

The 32/32 result is provider-interoperability evidence for this one machine. It is not a detection-rate, efficacy, or antivirus-product claim, and it does not transfer to hosts with a different registered provider.

## Desktop release

`eng\publish-release.ps1` produces the self-contained `win-x64` build and the per-user installer from a single command. Version 1.0.0 was built from the merge commit and verified on the owner host:

- installed silently to `%LocalAppData%\Programs\Soltex` with no elevation prompt;
- Start Menu and uninstall shortcuts created with correct targets;
- installed binary rendered the Security panel and launched a real WPF window titled `Soltex`;
- uninstall removed the program directory and the Add/Remove Programs record with no leftovers.

The installer is not code-signed, so SmartScreen warns on first run and the publisher shows as unknown. Uninstall deliberately preserves per-user Soltex state so authenticated quarantine, audit, release-sequence, and journal history survive an accidental removal.

## Exact resume gate

From the task worktree:

```powershell
Set-Location 'C:\Users\suhai\Documents\soltex-release-build'
git fetch origin main
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
3. Durable bounded local monitoring/event history with retention, corruption recovery, redaction, measured idle/storage cost, and a shared selected-time range.
4. Read-only Applications inventory, then a separately authorized and bounded process-action model; no arbitrary shell, publisher-wide block in the first slice, or silent task termination.
5. Supported audio endpoint/session inventory before routing, EQ, microphone processing, or virtual-device claims.
6. Privacy adapters and local/NAS search with explicit roots, provenance, quotas, cancellation, and recovery.
7. Personal Google Drive read-write connector, then a separately governed work Box connector with explicit cross-domain transfer policy.
8. Optional Tailscale-local status and enrolled multi-device transport only after signed job/receipt semantics are proven.
9. Production signing identities and an authenticated update source, then code signing for the shipped installer, atomic activation, rollback, and recovery as a separate release program. The unsigned per-user installer and its deterministic uninstall already exist; what remains is the signed, self-updating path.

Do not begin the next stage with provider registration, a driver, a service, public ingress, Defender mutations, unattended remote control, or automatic execution of staged content.
