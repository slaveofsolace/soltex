# Soltex continuation handoff

This is the only current continuation handoff for Soltex. Files under `docs/archive/prompts/` are historical inputs, not active instructions.

## Repository anchor

- Repository: `https://github.com/slaveofsolace/soltex`
- Protected owner checkout: `C:\Users\suhai\Documents\SOL Tools`
- Task-owned worktree: `C:\Users\suhai\Documents\soltex-identity-migration`
- Branch: `refactor/soltex-identity-and-repo-coherence`
- Draft PR: `https://github.com/slaveofsolace/soltex/pull/4`
- Stacked base: `feat/soltex-update-planner-v1`
- Exact base commit: `40c7e73452dc6c11fcd1f5711ec6360210595a44`
- Identity implementation checkpoint: `d0d0f770255338117c80651b5dab3bd4a292e5a9`

Do not reset, clean, stash, merge, rebase, or overwrite the protected owner checkout. Continue in the task-owned worktree and re-check branch, HEAD, status, remotes, PR base, and running-process ownership before changing anything.

## Product and implementation state

Soltex is a proprietary, unelevated .NET 10 WPF workspace. Current source has five production projects and five focused executable test projects under the `Soltex.*` identity:

- `Soltex.App`: the WPF shell and native render-smoke entrypoint;
- `Soltex.Security`: supported Windows Security Center observation, bounded Defender requests, AMSI intake checks, authenticated allow-list/quarantine/audit state, and signed-manifest verification;
- `Soltex.RemoteAssist`: a narrow, visible, shell-free adapter for a separately installed RustDesk client;
- `Soltex.Update`: signed descriptor/release verification and a non-installing update preview planner;
- `Soltex.DeviceFabric`: immutable device manifests, inventory snapshots, and a non-executing six-capability authorization model.

Audio and Clips remain visible product foundations rather than working DSP/capture engines. There is no embedded RustDesk transport, generic command executor, public listener, cloud connector, installer, autonomous repair agent, antivirus engine, or production update activator.

## Identity and installed-state contract

Current UI, solution, projects, assemblies, namespaces, scripts, workflow paths, and canonical docs use `Soltex`. `Directory.Build.props` and `ProductIdentity.cs` hold current metadata.

Installed authenticated state is preserved through lookup rather than an unreviewed copy:

- a fresh profile creates `%LocalAppData%\Soltex`;
- a sole existing legacy product root is selected in place and reported as compatibility mode;
- two product roots, a non-directory collision, or a reparse product root fail closed;
- state is never automatically merged, copied, deleted, or selected by timestamp;
- the legacy DPAPI description remains stable so existing protected keys remain decryptable;
- schema-1 import manifests accept only the current product or the one versioned legacy product value;
- schema-2 signed release manifests remain strictly `Soltex`.

The full contract and removal gates are in `docs/NAMING_AND_COMPATIBILITY.md`. `eng/verify-identity.ps1` rejects legacy identity outside a reasoned allowlist covering compatibility code, append-only evidence, archived prompts, and the factual external RustDesk research-cache path.

## Security invariants

- Do not disable or weaken Defender, Malwarebytes, Windows Security Center, SmartScreen, firewall policy, or tamper protection.
- Do not add exclusions, register Soltex as an antivirus provider, or claim that an AMSI result proves file safety.
- Do not copy, link, bundle, or disguise RustDesk AGPL code in the proprietary executable.
- Do not enable unattended RustDesk access, pass credentials, install its service, hide its UI, or treat a remote session as authorization for an AI job.
- Keep remote jobs typed, capability-scoped, replay-resistant, and bound to a validated target manifest. There is no executor in the current Device Fabric.
- Keep Google Drive and work Box credentials, indices, audit streams, and transfer policies separate. No connector is implemented yet.
- Do not introduce a generic shell, arbitrary script runner, public ingress, hidden persistence, silent elevation, or destructive autonomous repair.

## Verification

The stacked base passed Windows run `30918555568` with a Release build at 0 warnings/0 errors, all required suites, and native Security/Remote Assist/Updates renders. Its artifact is `soltex-windows-evidence-30918555568-1`, digest `sha256:01a8d3923249fad5cf9c48605fa3cf95c119579da4a89686a977ac74089f8384`. The hosted EICAR interop check recorded the available AMSI provider response and did not justify an independent antivirus claim.

Run the current gate from the worktree with:

```powershell
.\eng\verify-identity.ps1
dotnet build .\Soltex.sln --configuration Release
.\eng\verify.ps1 -RunEicar
```

Then run every focused suite and the three native render-smoke panels exactly as listed in `docs/VALIDATION.md`. Retain logs, screenshots, commit/merge-preview identities, toolchain version, elapsed measurements, and artifact SHA-256. A green render command is runtime evidence, not owner visual acceptance.

## Ordered continuation

1. Make PR #4 fully green on its exact merge preview and reconcile its evidence into `IMPLEMENTATION_STATUS.md`, `VALIDATION.md`, and the PR body.
2. Confirm identity policy rejects an intentionally injected current-source legacy token, then remove the fixture and rerun green.
3. Review naming/compatibility code for conflict, reparse, torn-state, tampering, and downgrade behavior. Do not add automatic file moves in this wave.
4. Finish canonical-doc consistency and keep historical evidence unchanged.
5. Only after PR #4 is green, create stacked branch `feat/soltex-immersive-workspace-v1` targeting this identity branch.
6. In that UI branch, implement the first real Home, Monitoring, and Devices slice using a shared WPF design system and real bounded providers. Never display invented telemetry; surface loading, stale, partial, unavailable, denied, and recovery states with provenance.

Later product slices may add supported audio inventory/control, app inventory, storage/search, clips capability probing, privacy adapters, Google Drive, separately governed Box, Tailscale-local status, and additional peripherals. Each requires its own typed boundary, threat model, tests, failure behavior, performance evidence, and honest nonclaims.
