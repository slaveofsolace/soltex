# Soltex handoff

Snapshot: 2026-08-09 (America/Chicago)

## Repository state

```text
repository: slaveofsolace/soltex
merged baseline: main at 61639bfe7ea3a7191fcb6c70ea1f2a699aa77248 (PR #9)
continuation branch: sol/soltex-ui-evidence-matrix
isolated worktree: C:\Users\suhai\Documents\soltex-ui-evidence-matrix
owner checkout: C:\Users\suhai\Documents\SOL Tools (dirty work preserved and untouched)
publication: pending exact-head commit, push, PR, and CI
```

No rebase, force push, reset, clean, stash, security-provider mutation, antivirus exclusion, generic shell, proprietary reverse engineering, or owner-checkout overwrite was used.

## Current bounded slice

This continuation closes the native UI evidence gap without changing the product interface:

- adds `eng/capture-ui-evidence.ps1` as the canonical 11-state renderer;
- covers all eight default workspaces and three expanded disclosure states;
- rejects stale evidence, error sidecars, empty/missing PNGs, wrong dimensions, and ambiguous commit identity;
- records per-file byte lengths and SHA-256 digests;
- separates `source_head_sha` from `tested_commit_sha` in render and package evidence;
- records package identity through a reusable fail-closed script;
- binds the retained package render by dimensions, length, and SHA-256;
- captures the published GUI process's explicit waited exit code;
- uses a nonactivating, borderless popup Window so its 1280x820 capture is not clamped to hosted work-area geometry;
- clips workspace drawing to its column and binds navigation labels directly to their owning buttons;
- gives GitHub checkouts enough ancestry depth to verify a pull-request merge checkout;
- uploads the complete visual directory instead of a six-file allowlist;
- records factual validation and clean-room SteelSeries reference boundaries.

## Local evidence

Environment: owner-controlled Windows host; .NET SDK 10.0.302; Release configuration.

```text
build: passed, 0 warnings, 0 errors
identity policy: passed
design-token policy: passed
Security/Remote: 31/31
Supply chain: 18/18
Hardening: 12/12
Update: 17/17
Device Fabric: 24/24
Monitoring: 15/15
Core Audio: 10/10
WPF application: 10/10
total required tests: 137/137
native renders: 11/11 at 1280x820
stale-output recovery: rejected before capture
mismatched tested commit: rejected before capture
self-contained package: published and rendered; process exit code 0
package identity: schema 2; Authenticode NotSigned
package stale/identity recovery: rejected before evidence write
```

The first PR run correctly rejected a 1044x788 hosted capture. The root cause was the normal-chrome window inheriting the runner work-area constraint. The recovery configures render-smoke as a nonactivating, borderless, nonresizable popup fixed to 1280x820 and captures the complete native Window visual. A focused regression starts from a 1044x788 request, exercises that real off-screen popup path, and proves the canonical bitmap plus bottom-right content.

Generated images and their manifest remain under ignored `artifacts/` paths. All eleven images were directly inspected. This is render evidence, not owner visual acceptance or accessibility conformance.

## Reference-only observation

The user authorized a visual-only observation of an already-running SteelSeries GG/Sonar window. Only general hierarchy lessons were retained. No screenshot, binary, asset, text, preset, DSP behavior, setting, or private protocol entered Soltex. An unrelated authentication overlay was left untouched.

## Required publication checks

Before merge:

1. commit only task-owned files in the isolated worktree;
2. rerun the render matrix against that exact committed HEAD into fresh evidence directories;
3. push `sol/soltex-ui-evidence-matrix` and open a PR to `main`;
4. require Windows and package-smoke success on the exact PR head;
5. inspect `render-matrix.json` and `package-smoke.json` for correct source/tested identities;
6. keep hosted EICAR/provider interoperability separate from the existing owner-host 32/32 result;
7. merge with history preserved, then verify `origin/main` contains the merge.

## Remaining product work after this slice

1. Trusted signed distribution and installer lifecycle evidence.
2. Bounded durable monitoring history and a read-only Applications/startup inventory.
3. Accessibility, scaling, keyboard, reduced-motion, high-contrast, and owner visual acceptance.
4. GPU/thermal provider selection and reproducible benchmark design.
5. Per-session audio observation before any routing, virtual-device, EQ, or DSP claim.
6. Consent-bound enrolled-device jobs before generic cross-device automation.

Soltex is not a registered antivirus, a Defender/Malwarebytes replacement, a production update service, an unattended remote-management agent, a generic remote shell, a signed public release, or production-ready.
