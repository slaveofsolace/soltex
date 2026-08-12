# Caffeine checkpoint

Snapshot: 2026-08-12 01:05 America/Chicago

```text
objective: continue the full Soltex product rebuild; close the read-only Services and runtime-lifecycle slice, then continue product maturity
protected root: C:\Users\suhai\Documents\SOL Tools
protected branch/head: main / bf2662de80992cfed761625642f084d3caaa0f04
protected dirty ownership: pre-existing user/mixed legacy-identity work; preserved untouched
implementation worktree: C:\Users\suhai\Documents\soltex-product-rebuild
implementation branch: sol/soltex-product-rebuild
accepted published evidence head: 6e54fb509ba332191107aa64733db0880e3cac78
exact owner-host source: 4207ecb70ef30c09203cb4f0f2b3efedf1ef2bd6
active task-owned process: none
reboot-safe: YES
```

## Accepted gate ledger

- Activity PR head `4437db80b844dcfe33cd8265e41cb5fcf00bfd76`: owner-host and hosted Windows/package accepted.
- Audio owner source `3511ba92bde450ffac4e3fad145d9a13b8986e72`: complete owner-host Release/identity/design/test, 20/20 standard Audio, 21/21 controlled task-owned read-back, 14/14 native, and package gates passed.
- Audio reconciliation head `abf1dc5e58ca7a23ef57a975c7fdeb041ea4d183`: Windows run `31562540695` and package run `31562540694` passed; downloaded artifacts re-hashed exactly; hosted Audio/package pixels inspected.
- Optional hosted AMSI/EICAR remained 31/32 with provider result `1`; required gates passed and no efficacy claim is made.

## Current exact-source ledger

- Added query-only Win32 service inventory, drivers excluded, 512-row exposure bound, no binary paths, no mutation rights or controls.
- Added explicit Exit/default and Notification-area/opt-in close behavior, direct `Shell_NotifyIconW` resource ownership, fail-closed recovery, and no installed service.
- Performance sampling now stops while hidden/minimized/closing; navigation clocks are released and cleared before minimize.
- Runtime evidence schema 2 records startup, four lifecycle states, memory/resources, Performance-sampler state, per-thread CPU attribution, and 18 navigation transitions.
- Accepted shutdown now drains active operation, startup, and telemetry ownership before disposing security resources; controlled evidence requires confirmed disposal.
- Exact Release verification passed: build 0 warnings/errors, Security/EICAR 32/32, Monitoring 16/16, Audio 20/20, App/control 26/26.
- Live read-only Services was Current: 296/296, 0 inaccessible, 0 omitted, 92.8 ms during the exact verifier.
- Exact runtime probe: startup 2997.4 ms; navigation 39.1 ms mean/379.2 ms max; visible 0.643%, transition 2.531%, minimized steady 0%, hidden 0% normalized CPU.
- The transition was attributed to a worker/runtime thread, not the dispatcher. This identifies the lifecycle phase, not the worker implementation.
- Exact native matrix passed 15/15; Overview, Services, Security, and Settings were inspected; no error sidecars or exception dialogs were present.
- Exact self-contained package is 71,583,560 bytes, SHA-256 `761822A48D3FD9A56A99E91C9368FA4AD513777BB2C44A6E0563C4BE4A445459`; packaged Security render exited 0; package remains unsigned.
- Complete diff-focused security scan: zero surviving findings; corrected/rejected audit candidate `SOLTEX-SHUTDOWN-001`.
- Published Windows run `31568771869` and package run `31568771855` passed at source head `6e54fb509ba332191107aa64733db0880e3cac78`; tested merge `ebd163553b3229099c371cd79b8967ace2b1ab55` is exactly one commit ahead with the source as merge base.
- Downloaded artifacts `9130547242` and `9130511337` matched GitHub digests exactly; all 15 retained PNG identities and dimensions revalidated; Services, Security, Settings, and packaged Home were directly inspected without an exception dialog.
- Hosted package launch exited 0 and matched SHA-256 `236959aae3abe37a35130b68515c1472730118a6e6c8f60c9315f1ca0107e8b9`; package remains unsigned. Optional hosted EICAR/AMSI remained 31/32 with provider result `1`, so no efficacy claim is made.
- Docs-only reconciliation head `5e03e60179c4d5d08f246903808edbe9aabd663d` passed Windows run `31569439185` and package run `31569439187`.
- Audio fallback/device source `1d15071a14472dce199796a57966bddbf7be2fc4` adds direction-scoped endpoint fingerprints, schema-3 fallback reminders, a fixed `ms-settings:sound` handoff, and a separate `mixer-devices` evidence state. Owner-host exact verification, Audio 21/21, App/control 29/29, 16/16 native states, runtime lifecycle, package identity, and direct expanded-Mixer inspection are green; hosted acceptance remains pending.
- No task-owned runtime remained after package-render PID 44620 exited successfully.

## Exact resume step

Audio fallback/device source `1d15071a14472dce199796a57966bddbf7be2fc4` is owner-host accepted after exact full verification, a recovered disposed-token failure, 16/16 native states, runtime lifecycle, package identity, and direct Mixer inspection. Strengthened test head `41c8c37622ef666436024138e6d82f14e1e418da` proves duplicate-stop and queued-restart serialization. Commit the evidence ledger, publish by fast-forward, then reconcile exact hosted Windows/package artifacts. Preserve the protected owner checkout, Windows-owned default selection, and all routing/DSP/product nonclaims.
